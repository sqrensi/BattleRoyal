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
        private PlayerWeaponLoadoutController weaponLoadoutController;
        private PlayerPickupController pickupController;
        private PlayerHealth playerHealth;
        private MatchPresenceSync presenceSync;

        private RealtimeTransportClient.MatchStateMessage lastState;
        private string currentPhase = "lobby";
        private bool matchOutcomeScheduled;
        private MatchOutcomeSummary? capturedOutcome;
        private bool remotesRevealed;
        private const int DuelWeaponSpareAmmo = 60;

        private int lastSpawnTeleportRound = int.MinValue;
        private int lastTeleportTeamIndex = -1;
        private int lastTeleportSlotIndex = -1;
        private bool localWeaponPickedThisRoundPick;

        public bool ShouldSuppressPoseReconcile =>
            string.Equals(currentPhase, "ending", StringComparison.Ordinal);

        public bool IsDuelRoundResetPhase =>
            string.Equals(currentPhase, "round_pick", StringComparison.Ordinal) ||
            string.Equals(currentPhase, "round_end", StringComparison.Ordinal) ||
            string.Equals(currentPhase, "prep", StringComparison.Ordinal);

        public void PrepareForNewMatch()
        {
            matchOutcomeScheduled = false;
            capturedOutcome = null;
            lastSpawnTeleportRound = int.MinValue;
            lastTeleportTeamIndex = -1;
            lastTeleportSlotIndex = -1;
            localWeaponPickedThisRoundPick = false;
            remotesRevealed = false;
            currentPhase = "lobby";
            lastState = null;
            DuelSpawnUtility.ClearCachedRoots();
            WeaponCatalog.PrewarmAllWeaponPrefabs();
            presenceSync?.SetRemoteAvatarsVisible(false);
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
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
            weaponLoadoutController = player.GetComponent<PlayerWeaponLoadoutController>();
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

            ActiveMatchContext.SetMode(MainMenuGameMode.Duel1v1);

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

            if (phaseEntered &&
                (string.Equals(currentPhase, "round_pick", StringComparison.Ordinal) ||
                 string.Equals(currentPhase, "round", StringComparison.Ordinal)))
            {
                PrepareLocalPlayerForRoundStart(state);
            }

            TryApplySpawnTeleport(state, phaseEntered);

            switch (currentPhase)
            {
                case "round_pick":
                    ApplyRoundPickPhase(state, phaseEntered);
                    break;
                case "round":
                    ApplyRoundPhase(state, phaseEntered);
                    break;
                case "prep":
                    SetMovementLocked(false);
                    SetCombatEnabled(false);
                    fpsController?.SetWeaponPickUiMode(false);
                    gameHud?.HideDuelWeaponPickPanel();
                    break;
                case "round_end":
                    SetMovementLocked(false);
                    SetCombatEnabled(IsLocalPlayerAlive());
                    fpsController?.SetWeaponPickUiMode(false);
                    gameHud?.HideDuelWeaponPickPanel();
                    UpdateRoundEndBanner(state);
                    break;
                case "ending":
                    ApplyLockedPhase(state);
                    ScheduleMatchOutcome(state.isLocalWinner);
                    break;
                default:
                    SetMovementLocked(state.duelMovementLocked);
                    SetCombatEnabled(false);
                    fpsController?.SetWeaponPickUiMode(false);
                    gameHud?.HideDuelWeaponPickPanel();
                    break;
            }
        }

        private void UpdateDuelPhasePresentation(
            RealtimeTransportClient.MatchStateMessage state,
            string previousPhase,
            bool phaseEntered)
        {
            if (phaseEntered && currentPhase == "round_pick")
            {
                presenceSync?.SnapAllRemoteAvatarsToLastKnownPose();
                presenceSync?.SetRemoteAvatarsVisible(true);
                gameHud?.ClearDuelRoundBanner();
            }

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
            if (currentPhase != "round_pick" || !phaseEntered)
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

            if (!TeleportToAssignedSpawn(state, reviveIfDead: true))
            {
                return;
            }

            lastSpawnTeleportRound = state.duelRoundNumber;
        }

        private void ApplyRoundPickPhase(RealtimeTransportClient.MatchStateMessage state, bool phaseEntered)
        {
            SetMovementLocked(true);
            SetCombatEnabled(false);
            fpsController?.SetServerReconciliationSuspended(false);
            EnableCharacterControllerIfNeeded();

            if (phaseEntered)
            {
                localWeaponPickedThisRoundPick = false;
                ClearLocalLoadoutForRoundPick();
            }

            if (!localWeaponPickedThisRoundPick)
            {
                fpsController?.SetWeaponPickUiMode(true);
                gameHud?.ShowDuelWeaponPickPanel(HandleDuelWeaponPicked);
            }
        }

        private void ApplyRoundPhase(RealtimeTransportClient.MatchStateMessage state, bool phaseEntered)
        {
            SetMovementLocked(false);
            SetCombatEnabled(state.duelCombatEnabled && IsLocalPlayerAlive());
            fpsController?.SetServerReconciliationSuspended(false);
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
            EnableCharacterControllerIfNeeded();

            if (phaseEntered)
            {
                EnsureLocalWeaponFromServerState(state);
            }
        }

        private void HandleDuelWeaponPicked(WeaponKind kind)
        {
            if (currentPhase != "round_pick" || localWeaponPickedThisRoundPick)
            {
                return;
            }

            EnsureLocalPlayerBound();
            if (localPlayer == null)
            {
                return;
            }

            weaponLoadoutController ??= localPlayer.GetComponent<PlayerWeaponLoadoutController>();
            if (weaponLoadoutController == null)
            {
                return;
            }

            localWeaponPickedThisRoundPick = true;
            transportClient?.SendDuelWeaponPick((int)kind);
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
        }

        public void HandleWeaponPickRejected(string reason)
        {
            if (currentPhase != "round_pick")
            {
                return;
            }

            localWeaponPickedThisRoundPick = false;
            fpsController?.SetWeaponPickUiMode(true);
            gameHud?.ShowDuelWeaponPickPanel(HandleDuelWeaponPicked);

            if (!string.IsNullOrWhiteSpace(reason))
            {
                Debug.LogWarning($"[MatchDuel] Weapon pick rejected: {reason}");
            }
        }

        private void ClearLocalLoadoutForRoundPick()
        {
            if (localPlayer == null)
            {
                return;
            }

            weaponLoadoutController ??= localPlayer.GetComponent<PlayerWeaponLoadoutController>();
            weaponLoadoutController?.Loadout?.ClearForSpawn();

            var weaponMount = localPlayer.GetComponent<PlayerWeaponMount>();
            if (weaponMount != null && weaponMount.HasMountedWeapon)
            {
                weaponMount.UnequipWeapon();
            }

            localPlayer.GetComponent<PlayerWeaponHolsterController>()?.ForceHolsteredIdleState();
            pickupController?.RefreshWeaponAvailability();
        }

        private bool HasLocalWeapon()
        {
            return weaponLoadoutController != null &&
                   weaponLoadoutController.Loadout != null &&
                   weaponLoadoutController.Loadout.HasAnyWeapon;
        }

        private void EnsureLocalWeaponFromServerState(RealtimeTransportClient.MatchStateMessage state)
        {
            if (HasLocalWeapon() || state == null || state.duelPickedWeaponKind < 0)
            {
                return;
            }

            ApplyLocalWeaponKind(WeaponKindUtility.ClampKind(state.duelPickedWeaponKind));
        }

        private bool ApplyLocalWeaponKind(WeaponKind kind)
        {
            if (localPlayer == null)
            {
                return false;
            }

            weaponLoadoutController ??= localPlayer.GetComponent<PlayerWeaponLoadoutController>();
            if (weaponLoadoutController == null)
            {
                return false;
            }

            if (!weaponLoadoutController.ApplyDuelRoundWeaponPick(kind, DuelWeaponSpareAmmo))
            {
                return false;
            }

            localPlayer.GetComponent<PlayerWeaponHolsterController>()?.ForceArmedState();
            pickupController?.RefreshWeaponAvailability();
            return true;
        }

        private void PrepareLocalPlayerForRoundStart(RealtimeTransportClient.MatchStateMessage state)
        {
            EnsureLocalPlayerBound();
            if (localPlayer == null || playerHealth == null)
            {
                return;
            }

            if (playerHealth.IsDead)
            {
                if (DuelSpawnUtility.TryResolveSpawnPose(
                        state.duelTeamIndex,
                        state.duelSpawnSlotIndex,
                        out var position,
                        out var rotation))
                {
                    ForceReviveLocalPlayer(position, rotation, reviveIfDead: true);
                }
                else
                {
                    playerHealth.ForceReviveAt(localPlayer.position, localPlayer.rotation);
                    fpsController?.NotifyLocalRespawned(1.5f);
                }
            }
            else
            {
                playerHealth.RestoreFullHealthForRoundStart();
            }

            presenceSync?.ResetSelfAuthoritativeReconcileCursor();
            presenceSync?.FlushLocalPose();
        }

        private void ApplyLockedPhase(RealtimeTransportClient.MatchStateMessage state)
        {
            SetMovementLocked(true);
            SetCombatEnabled(false);
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
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
                case "round_pick":
                    return $"Выбор оружия — {countdown} сек.";
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
            lastTeleportTeamIndex = state.duelTeamIndex;
            lastTeleportSlotIndex = state.duelSpawnSlotIndex;
            presenceSync?.ResetSelfAuthoritativeReconcileCursor();
            StartCoroutine(FlushPoseAfterTeleport());
            return true;
        }

        private System.Collections.IEnumerator FlushPoseAfterTeleport()
        {
            for (var i = 0; i < 6; i++)
            {
                yield return null;

                if (localPlayer != null &&
                    lastTeleportTeamIndex >= 0 &&
                    lastTeleportSlotIndex >= 0 &&
                    DuelSpawnUtility.TryResolveSpawnPose(
                        lastTeleportTeamIndex,
                        lastTeleportSlotIndex,
                        out var groundedPosition,
                        out var groundedRotation))
                {
                    DuelSpawnUtility.TryApplyGroundedPose(
                        localPlayer.transform,
                        characterController,
                        groundedPosition,
                        groundedRotation);
                }

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
