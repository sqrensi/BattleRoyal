using System;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using ShooterPrototype.UI;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class MatchDuelController : MonoBehaviour
    {
        private RealtimeTransportClient transportClient;
        private GameHudController gameHud;
        private Transform localPlayer;
        private FpsCharacterController fpsController;
        private CharacterController characterController;
        private PlayerWeaponController weaponController;
        private PlayerPickupController pickupController;
        private PlayerHealth playerHealth;
        private MatchPresenceSync presenceSync;

        private RealtimeTransportClient.MatchStateMessage lastState;
        private string currentPhase = "lobby";
        private bool matchOutcomeScheduled;
        private MatchOutcomeSummary? capturedOutcome;
        private bool remotesRevealed;
        private int lastSpawnTeleportRound = int.MinValue;

        public bool ShouldSuppressPoseReconcile =>
            string.Equals(currentPhase, "ending", StringComparison.Ordinal);

        public void PrepareForNewMatch()
        {
            matchOutcomeScheduled = false;
            capturedOutcome = null;
            lastSpawnTeleportRound = int.MinValue;
            remotesRevealed = false;
            currentPhase = "lobby";
            lastState = null;
            DuelSpawnUtility.ClearCachedRoots();
            presenceSync?.SetRemoteAvatarsVisible(false);
            fpsController?.SetWeaponPickUiMode(false);
            weaponController?.SetDuelFireBlocked(true);
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            if (transportClient != null)
            {
                transportClient.MatchStateReceived += HandleMatchState;
            }
        }

        private void OnDisable()
        {
            if (transportClient != null)
            {
                transportClient.MatchStateReceived -= HandleMatchState;
            }

            fpsController?.SetWeaponPickUiMode(false);
            presenceSync?.SetRemoteAvatarsVisible(true);
            gameHud?.ClearDuelRoundBanner();
            gameHud?.ClearDuelRoundEndCountdown();
        }

        private void Update()
        {
            if (lastState == null)
            {
                return;
            }

            if (currentPhase == "round")
            {
                var countdown = Mathf.Max(0, lastState.countdownRemainingSeconds);
                if (countdown > 0 && countdown <= 5)
                {
                    gameHud?.SetDuelRoundEndCountdown(countdown);
                }
                else
                {
                    gameHud?.ClearDuelRoundEndCountdown();
                }
            }
            else
            {
                gameHud?.ClearDuelRoundEndCountdown();
            }

            EnforceLocalDeadState();
        }

        private void EnforceLocalDeadState()
        {
            if (playerHealth == null || !playerHealth.IsDead)
            {
                return;
            }

            SetMovementLocked(true);
            SetCombatEnabled(false);
            fpsController?.SetServerReconciliationSuspended(true);

            if (fpsController != null && fpsController.enabled)
            {
                fpsController.enabled = false;
            }

            if (weaponController != null && weaponController.enabled)
            {
                weaponController.enabled = false;
            }

            if (pickupController != null && pickupController.enabled)
            {
                pickupController.enabled = false;
            }
        }

        private void ResolveDependencies()
        {
            if (transportClient == null)
            {
                transportClient = FindFirstObjectByType<RealtimeTransportClient>();
            }

            if (gameHud == null)
            {
                gameHud = FindFirstObjectByType<GameHudController>();
            }

            EnsureLocalPlayerBound();
        }

        private void EnsureLocalPlayerBound()
        {
            if (localPlayer != null)
            {
                return;
            }

            var marker = FindFirstObjectByType<LocalPlayerMarker>();
            if (marker != null)
            {
                BindLocalPlayer(marker.transform);
            }
        }

        private void BindLocalPlayer(Transform player)
        {
            localPlayer = player;
            fpsController = player.GetComponent<FpsCharacterController>();
            characterController = player.GetComponent<CharacterController>();
            weaponController = player.GetComponent<PlayerWeaponController>();
            pickupController = player.GetComponent<PlayerPickupController>();
            playerHealth = player.GetComponent<PlayerHealth>();
            presenceSync = player.GetComponent<MatchPresenceSync>();

            if (playerHealth != null)
            {
                playerHealth.SetEliminationMode(true);
            }
        }

        private void HandleMatchState(RealtimeTransportClient.MatchStateMessage state)
        {
            if (state == null || !string.Equals(state.matchMode, "duel", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ResolveDependencies();
            if (localPlayer == null)
            {
                var marker = FindFirstObjectByType<LocalPlayerMarker>();
                if (marker != null)
                {
                    BindLocalPlayer(marker.transform);
                }
            }

            if (!remotesRevealed)
            {
                presenceSync?.SetRemoteAvatarsVisible(false);
            }

            lastState = state;
            RevealRemotesIfNeeded();
            ApplyPhase(state);
        }

        private void RevealRemotesIfNeeded()
        {
            if (remotesRevealed || lastState == null)
            {
                return;
            }

            var phase = lastState.phase ?? "lobby";
            if (string.Equals(phase, "lobby", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            remotesRevealed = true;
            presenceSync?.SetRemoteAvatarsVisible(true);
        }

        private void ApplyPhase(RealtimeTransportClient.MatchStateMessage state)
        {
            var previousPhase = currentPhase;
            currentPhase = state.phase ?? "lobby";
            var phaseEntered = !string.Equals(previousPhase, currentPhase, StringComparison.Ordinal);

            UpdateHud(state);
            gameHud?.SetMatchStatusMessage(string.Empty);
            UpdateDuelPhasePresentation(state, previousPhase, phaseEntered);
            TryApplySpawnTeleport(state, phaseEntered);

            switch (currentPhase)
            {
                case "round":
                    ApplyRoundPhase(state, phaseEntered);
                    break;
                case "prep":
                    SetMovementLocked(false);
                    SetCombatEnabled(false);
                    break;
                case "round_end":
                    SetMovementLocked(false);
                    SetCombatEnabled(IsLocalPlayerAlive());
                    UpdateRoundEndBanner(state);
                    break;
                case "ending":
                    ApplyLockedPhase(state);
                    ScheduleMatchOutcome(state.isLocalWinner);
                    break;
                default:
                    SetMovementLocked(state.duelMovementLocked);
                    SetCombatEnabled(false);
                    break;
            }
        }

        private void UpdateDuelPhasePresentation(
            RealtimeTransportClient.MatchStateMessage state,
            string previousPhase,
            bool phaseEntered)
        {
            if (phaseEntered && currentPhase == "round")
            {
                presenceSync?.SnapAllRemoteAvatarsToLastKnownPose();
                presenceSync?.SetRemoteAvatarsVisible(true);
                gameHud?.ClearDuelRoundBanner();
            }

            if (phaseEntered &&
                !string.Equals(previousPhase, "round_end", StringComparison.Ordinal) &&
                currentPhase != "round_end")
            {
                gameHud?.ClearDuelRoundBanner();
            }
        }

        private void UpdateRoundEndBanner(RealtimeTransportClient.MatchStateMessage state)
        {
            if (string.IsNullOrWhiteSpace(state.winnerTicketId))
            {
                gameHud?.SetDuelRoundBanner("Ничья");
            }
        }

        private bool IsLocalPlayerAlive()
        {
            return playerHealth == null || !playerHealth.IsDead;
        }

        private void TryApplySpawnTeleport(RealtimeTransportClient.MatchStateMessage state, bool phaseEntered)
        {
            if (currentPhase != "round" || !phaseEntered)
            {
                return;
            }

            if (state.duelTeamIndex < 0 || state.duelSpawnSlotIndex < 0)
            {
                return;
            }

            if (state.duelRoundNumber == lastSpawnTeleportRound)
            {
                return;
            }

            if (!TeleportToAssignedSpawn(state, reviveIfDead: false))
            {
                return;
            }

            lastSpawnTeleportRound = state.duelRoundNumber;
        }

        private void ApplyRoundPhase(RealtimeTransportClient.MatchStateMessage state, bool phaseEntered)
        {
            SetMovementLocked(false);
            SetCombatEnabled(state.duelCombatEnabled && IsLocalPlayerAlive());
            fpsController?.SetServerReconciliationSuspended(false);
            EnableCharacterControllerIfNeeded();

            if (phaseEntered)
            {
                ReviveLocalPlayerForRoundStart(state);
            }
        }

        private void ReviveLocalPlayerForRoundStart(RealtimeTransportClient.MatchStateMessage state)
        {
            if (localPlayer == null || playerHealth == null || !playerHealth.IsDead)
            {
                return;
            }

            if (!DuelSpawnUtility.TryResolveSpawnPose(
                    state.duelTeamIndex,
                    state.duelSpawnSlotIndex,
                    out var position,
                    out var rotation))
            {
                return;
            }

            ForceReviveLocalPlayer(position, rotation, reviveIfDead: true);
        }

        private void ApplyLockedPhase(RealtimeTransportClient.MatchStateMessage state)
        {
            SetMovementLocked(true);
            SetCombatEnabled(false);
            fpsController?.SetServerReconciliationSuspended(true);
        }

        private void UpdateHud(RealtimeTransportClient.MatchStateMessage state)
        {
            gameHud?.SetDuelMatchHud(
                state.duelLocalRoundWins,
                state.duelOpponentRoundWins,
                state.duelRoundNumber,
                state.duelRoundsToWin,
                state.countdownRemainingSeconds,
                BuildDuelPhaseLabel(state));
        }

        private string BuildDuelPhaseLabel(RealtimeTransportClient.MatchStateMessage state)
        {
            var countdown = Mathf.Max(0, state.countdownRemainingSeconds);
            switch (currentPhase)
            {
                case "prep":
                    return $"Подготовка — {countdown} сек.";
                case "round":
                    return $"Раунд {Mathf.Max(1, state.duelRoundNumber)} — {countdown} сек.";
                case "round_end":
                    if (string.IsNullOrWhiteSpace(state.winnerTicketId))
                    {
                        return $"Перерыв — {countdown} сек.";
                    }

                    return string.Equals(state.winnerTicketId, state.localTicketId, StringComparison.Ordinal)
                        ? $"Раунд за тобой — {countdown} сек."
                        : $"Раунд за соперником — {countdown} сек.";
                case "ending":
                    return state.isLocalWinner ? "Победа!" : "Поражение";
                default:
                    return string.Empty;
            }
        }

        private bool TeleportToAssignedSpawn(RealtimeTransportClient.MatchStateMessage state, bool reviveIfDead)
        {
            if (localPlayer == null)
            {
                return false;
            }

            if (!DuelSpawnUtility.TryResolveSpawnPose(
                    state.duelTeamIndex,
                    state.duelSpawnSlotIndex,
                    out var position,
                    out var rotation))
            {
                Debug.LogWarning(
                    $"[MatchDuel] Spawn not found for team={state.duelTeamIndex} slot={state.duelSpawnSlotIndex}");
                return false;
            }

            ForceReviveLocalPlayer(position, rotation, reviveIfDead);
            presenceSync?.ResetSelfAuthoritativeReconcileCursor();
            StartCoroutine(FlushPoseAfterTeleport());
            return true;
        }

        private System.Collections.IEnumerator FlushPoseAfterTeleport()
        {
            for (var i = 0; i < 4; i++)
            {
                yield return null;
                Physics.SyncTransforms();
                presenceSync?.FlushLocalPose();
            }
        }

        private void EnableCharacterControllerIfNeeded()
        {
            if (characterController != null && !characterController.enabled && playerHealth != null && !playerHealth.IsDead)
            {
                characterController.enabled = true;
            }
        }

        private void ForceReviveLocalPlayer(Vector3 position, Quaternion rotation, bool reviveIfDead)
        {
            if (localPlayer == null)
            {
                return;
            }

            fpsController?.ClearExternalLaunchVelocity();
            fpsController?.ApplyLookOrientation(rotation.eulerAngles.y, 0f);

            if (playerHealth != null && playerHealth.IsDead)
            {
                if (reviveIfDead)
                {
                    playerHealth.ForceReviveAt(position, rotation);
                }
                else
                {
                    playerHealth.RepositionWhileDead(position, rotation);
                }

                fpsController?.NotifyLocalRespawned(reviveIfDead ? 1.5f : 0.5f);
                return;
            }

            if (characterController != null)
            {
                characterController.enabled = false;
            }

            localPlayer.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();
            if (characterController != null)
            {
                characterController.enabled = true;
            }

            fpsController?.NotifyLocalRespawned(1.5f);
        }

        private void SetMovementLocked(bool locked)
        {
            if (playerHealth != null && playerHealth.IsDead)
            {
                locked = true;
            }

            fpsController?.SetMovementLocked(locked);
        }

        private void SetCombatEnabled(bool enabled)
        {
            if (playerHealth != null && playerHealth.IsDead)
            {
                enabled = false;
            }

            if (pickupController != null)
            {
                pickupController.enabled = enabled;
            }

            weaponController?.SetDuelFireBlocked(!enabled);
            if (weaponController != null && enabled)
            {
                weaponController.RefreshWeaponAvailability();
            }
        }

        private void ScheduleMatchOutcome(bool won)
        {
            if (matchOutcomeScheduled)
            {
                return;
            }

            matchOutcomeScheduled = true;
            var summary = BuildMatchOutcomeSummary(won);
            gameHud?.ScheduleGameOver(won, summary);
        }

        private MatchOutcomeSummary BuildMatchOutcomeSummary(bool won)
        {
            if (capturedOutcome.HasValue)
            {
                return capturedOutcome.Value;
            }

            var roundWins = lastState?.duelLocalRoundWins ?? 0;
            var summary = MatchOutcomeSummary.CreateDuel(won, roundWins, 0);
            capturedOutcome = summary;
            return summary;
        }
    }
}
