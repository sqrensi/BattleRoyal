using System;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using ShooterPrototype.UI;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class MatchDeathmatchController : MonoBehaviour
    {
        private RealtimeTransportClient transportClient;
        private NetworkLauncher networkLauncher;
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
        private bool initialWeaponPickDone;
        private bool manualWeaponPickOpen;
        private bool serverSaysAlive = true;
        private int lastAppliedSpawnSlot = -1;
        private int lastRevivedDeathSeq = -1;
        private Vector3 lastServerHitDirection = Vector3.forward;
        private int localPlacementEstimate = 1;
        private const int DmWeaponSpareAmmo = 60;

        public bool ShouldSuppressPoseReconcile =>
            string.Equals(currentPhase, "ending", StringComparison.Ordinal);

        public bool ShouldSendWeaponInNetworkPose =>
            lastState != null && lastState.duelPickedWeaponKind >= 0;

        public void PrepareForNewMatch()
        {
            matchOutcomeScheduled = false;
            capturedOutcome = null;
            initialWeaponPickDone = false;
            manualWeaponPickOpen = false;
            serverSaysAlive = true;
            lastRevivedDeathSeq = -1;
            lastAppliedSpawnSlot = -1;
            localPlacementEstimate = 1;
            remotesRevealed = false;
            currentPhase = "lobby";
            lastState = null;
            DmSpawnUtility.ClearCachedRoots();
            WeaponCatalog.PrewarmAllWeaponPrefabs();
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
            gameHud?.ClearDuelMatchHud();
            gameHud?.SetMatchStatusMessage(string.Empty);
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
                transportClient.MatchStatsReceived += HandleMatchStats;
                transportClient.RespawnReceived += HandleRespawn;
                transportClient.DamageReceived += HandleDamageReceived;
                if (transportClient.TryGetLatestMatchState(out var cachedState))
                {
                    HandleMatchState(cachedState);
                }
            }
        }

        private void OnDisable()
        {
            if (transportClient != null)
            {
                transportClient.MatchStateReceived -= HandleMatchState;
                transportClient.MatchStatsReceived -= HandleMatchStats;
                transportClient.RespawnReceived -= HandleRespawn;
                transportClient.DamageReceived -= HandleDamageReceived;
            }

            fpsController?.SetWeaponPickUiMode(false);
            presenceSync?.SetRemoteAvatarsVisible(true);
            gameHud?.ClearDuelMatchHud();
        }

        private void Update()
        {
            if (presenceSync == null)
            {
                EnsureLocalPlayerBound();
            }

            if (!remotesRevealed &&
                presenceSync != null &&
                presenceSync.RemoteAvatarCount > 0)
            {
                remotesRevealed = true;
                presenceSync.SetRemoteAvatarsVisible(true);
            }

            if (lastState == null)
            {
                return;
            }

            MaintainInitialWeaponPickUi();
            TryOpenManualWeaponPick();
            EnforceLocalDeadState();
            EnsureRemotesVisible();
        }

        private void MaintainInitialWeaponPickUi()
        {
            if (initialWeaponPickDone ||
                manualWeaponPickOpen ||
                currentPhase != "round" ||
                lastState == null ||
                matchOutcomeScheduled ||
                GameHudController.IsPauseMenuOpen)
            {
                return;
            }

            EnsureLocalPlayerBound();
            if (localPlayer == null || gameHud == null)
            {
                return;
            }

            fpsController?.SetWeaponPickUiMode(true);
            fpsController?.MaintainWeaponPickCursor();
            gameHud.ShowDuelWeaponPickPanel(HandleInitialWeaponPicked);
        }

        private void TryOpenManualWeaponPick()
        {
            if (manualWeaponPickOpen ||
                !initialWeaponPickDone ||
                currentPhase != "round" ||
                !IsLocalPlayerAlive() ||
                fpsController == null ||
                gameHud == null ||
                GameHudController.IsPauseMenuOpen ||
                matchOutcomeScheduled)
            {
                return;
            }

            if (!ReadWeaponChangeKeyDown())
            {
                return;
            }

            manualWeaponPickOpen = true;
            fpsController.SetWeaponPickUiMode(true);
            gameHud.ShowDuelWeaponPickPanel(HandleManualWeaponPicked);
        }

        private static bool ReadWeaponChangeKeyDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.B);
#endif
        }

        private void EnforceLocalDeadState()
        {
            if (playerHealth == null || !playerHealth.IsDead)
            {
                return;
            }

            LockDeadControls();
            KeepRemotesVisibleWhileDead();
        }

        private void HandleDamageReceived(RealtimeTransportClient.DamageMessage message)
        {
            if (message == null || lastState == null)
            {
                return;
            }

            if (!string.Equals(lastState.matchMode, "deathmatch", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var localTicketId = ResolveLocalTicketId(lastState);
            if (string.IsNullOrWhiteSpace(localTicketId) ||
                !string.Equals(message.targetTicketId, localTicketId, StringComparison.Ordinal))
            {
                return;
            }

            var hitDirection = new Vector3(message.dirX, message.dirY, message.dirZ);
            if (hitDirection.sqrMagnitude > 0.0001f)
            {
                lastServerHitDirection = hitDirection.normalized;
            }

            if (message.killed && message.deathSeq > 0)
            {
                serverSaysAlive = false;
            }
        }

        private void EnsureLocalDeathFromServer(int serverDeathSeq = -1)
        {
            if (playerHealth == null || playerHealth.IsDead)
            {
                return;
            }

            playerHealth.ForceDeathFromServer(lastServerHitDirection, serverDeathSeq);
        }

        private void KeepRemotesVisibleWhileDead()
        {
            remotesRevealed = true;
            presenceSync?.SetRemoteAvatarsVisible(true);
            presenceSync?.SnapAllRemoteAvatarsToLastKnownPose();
        }

        private void LockDeadControls()
        {
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
                transportClient = RealtimeTransportClient.Active ??
                                  FindFirstObjectByType<RealtimeTransportClient>();
            }

            if (networkLauncher == null)
            {
                networkLauncher = FindFirstObjectByType<NetworkLauncher>();
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

        private void HandleMatchStats(RealtimeTransportClient.MatchStatsMessage message)
        {
            if (message?.profile == null)
            {
                return;
            }

            PlayerProfileService.ApplyLiveRatings(message.profile.duelRating, message.profile.rating);
            gameHud?.NotifyServerMatchStatsApplied(message.ratingDelta);
        }

        private void HandleRespawn(RealtimeTransportClient.RespawnMessage message)
        {
            if (message == null || lastState == null)
            {
                return;
            }

            var localTicketId = ResolveLocalTicketId(lastState);
            if (string.IsNullOrWhiteSpace(localTicketId) ||
                !string.Equals(message.ticketId, localTicketId, StringComparison.Ordinal))
            {
                return;
            }

            serverSaysAlive = true;
            TryReviveAtServerSpawn(lastState, message.spawnSlotIndex, message.deathSeq);
        }

        private void HandleMatchState(RealtimeTransportClient.MatchStateMessage state)
        {
            if (state == null ||
                !string.Equals(state.matchMode, "deathmatch", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ActiveMatchContext.SetMode(MainMenuGameMode.Deathmatch);
            ResolveDependencies();
            EnsureLocalPlayerBound();

            var previousAlive = serverSaysAlive;
            serverSaysAlive = state.dmLocalAlive;
            lastState = state;
            RevealRemotesIfNeeded(state);
            ApplyPhase(state, previousAlive);
        }

        private void RevealRemotesIfNeeded(RealtimeTransportClient.MatchStateMessage state = null)
        {
            var snapshot = state ?? lastState;
            if (snapshot == null)
            {
                return;
            }

            if (presenceSync != null && presenceSync.RemoteAvatarCount > 0)
            {
                remotesRevealed = true;
                presenceSync.SetRemoteAvatarsVisible(true);
                return;
            }

            var phase = snapshot.phase ?? "lobby";
            if (string.Equals(phase, "lobby", StringComparison.OrdinalIgnoreCase) &&
                Mathf.Max(0, snapshot.connectedCount) < 2)
            {
                return;
            }

            remotesRevealed = true;
            presenceSync?.SetRemoteAvatarsVisible(true);
        }

        private void EnsureRemotesVisible()
        {
            if (presenceSync == null || lastState == null)
            {
                return;
            }

            var phase = lastState.phase ?? "lobby";
            if (string.Equals(phase, "lobby", StringComparison.OrdinalIgnoreCase) &&
                Mathf.Max(0, lastState.connectedCount) < 2)
            {
                return;
            }

            remotesRevealed = true;
            presenceSync.SetRemoteAvatarsVisible(true);
        }

        private void ApplyPhase(RealtimeTransportClient.MatchStateMessage state, bool previousAlive)
        {
            var previousPhase = currentPhase;
            currentPhase = state.phase ?? "lobby";
            var phaseEntered = !string.Equals(previousPhase, currentPhase, StringComparison.Ordinal);

            UpdateHud(state);

            if (previousAlive && !serverSaysAlive)
            {
                EnsureLocalDeathFromServer(state.dmLocalDeathSeq);
                KeepRemotesVisibleWhileDead();
            }

            switch (currentPhase)
            {
                case "round":
                    ApplyFightPhase(state, phaseEntered);
                    break;
                case "prep":
                    if (phaseEntered)
                    {
                        ClearLocalLoadoutForWeaponPick();
                        initialWeaponPickDone = false;
                        TryApplySpawnTeleport(state, force: true);
                        presenceSync?.SetRemoteAvatarsVisible(true);
                    }

                    SetMovementLocked(false);
                    SetCombatEnabled(false);
                    CloseWeaponPickUi();
                    EnsureRemotesVisible();
                    break;
                case "ending":
                    ApplyLockedPhase();
                    ScheduleMatchOutcome(state.isLocalWinner);
                    break;
                default:
                    SetMovementLocked(state.duelMovementLocked);
                    SetCombatEnabled(false);
                    CloseWeaponPickUi();
                    EnsureRemotesVisible();
                    break;
            }
        }

        private void ApplyFightPhase(RealtimeTransportClient.MatchStateMessage state, bool phaseEntered)
        {
            SetMovementLocked(!serverSaysAlive || state.duelMovementLocked);
            SetCombatEnabled(state.duelCombatEnabled && IsLocalPlayerAlive());
            if (IsLocalPlayerAlive())
            {
                fpsController?.SetServerReconciliationSuspended(false);
            }

            EnableCharacterControllerIfNeeded();
            EnsureRemotesVisible();

            if (phaseEntered)
            {
                ClearLocalLoadoutForWeaponPick();
                initialWeaponPickDone = false;
                presenceSync?.SetRemoteAvatarsVisible(true);
                presenceSync?.SnapAllRemoteAvatarsToLastKnownPose();
                presenceSync?.ResetSelfAuthoritativeReconcileCursor();
                presenceSync?.FlushLocalPose();
            }
        }

        private void ClearLocalLoadoutForWeaponPick()
        {
            EnsureLocalPlayerBound();
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

        private void HandleInitialWeaponPicked(WeaponKind kind)
        {
            if (currentPhase != "round" || initialWeaponPickDone)
            {
                return;
            }

            if (!ApplyWeaponSelection(kind, sendToServer: true))
            {
                return;
            }

            initialWeaponPickDone = true;
            HideWeaponPickUi();
            SetCombatEnabled(lastState != null && lastState.duelCombatEnabled && IsLocalPlayerAlive());
        }

        private void HandleManualWeaponPicked(WeaponKind kind)
        {
            if (!manualWeaponPickOpen)
            {
                return;
            }

            if (!ApplyWeaponSelection(kind, sendToServer: true))
            {
                return;
            }

            initialWeaponPickDone = true;
            manualWeaponPickOpen = false;
            HideWeaponPickUi();
            SetCombatEnabled(lastState != null && lastState.duelCombatEnabled && IsLocalPlayerAlive());
        }

        private bool ApplyWeaponSelection(WeaponKind kind, bool sendToServer)
        {
            EnsureLocalPlayerBound();
            if (localPlayer == null)
            {
                return false;
            }

            weaponLoadoutController ??= localPlayer.GetComponent<PlayerWeaponLoadoutController>();
            if (weaponLoadoutController == null)
            {
                return false;
            }

            if (!weaponLoadoutController.ApplyDuelRoundWeaponPick(kind, DmWeaponSpareAmmo))
            {
                return false;
            }

            localPlayer.GetComponent<PlayerWeaponHolsterController>()?.ForceArmedState();
            pickupController?.RefreshWeaponAvailability();

            if (sendToServer)
            {
                transportClient?.SendDuelWeaponPick((int)kind);
            }

            return true;
        }

        private void HideWeaponPickUi()
        {
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
        }

        private void CloseWeaponPickUi()
        {
            manualWeaponPickOpen = false;
            HideWeaponPickUi();
        }

        private void EnsureLocalWeaponFromServerState(RealtimeTransportClient.MatchStateMessage state)
        {
            if (state == null || state.duelPickedWeaponKind < 0)
            {
                return;
            }

            if (ApplyWeaponSelection(WeaponKindUtility.ClampKind(state.duelPickedWeaponKind), sendToServer: false))
            {
                initialWeaponPickDone = true;
                HideWeaponPickUi();
            }
        }

        private void ApplyLockedPhase()
        {
            SetMovementLocked(true);
            SetCombatEnabled(false);
            CloseWeaponPickUi();
            fpsController?.SetServerReconciliationSuspended(true);
        }

        private void UpdateHud(RealtimeTransportClient.MatchStateMessage state)
        {
            var countdown = Mathf.Max(0, state.countdownRemainingSeconds);
            gameHud?.SetDeathmatchHud(state.localKillCount, state.aliveCount, countdown);
            gameHud?.SetMatchStatusMessage(string.Empty);
        }

        private void TryApplySpawnTeleport(RealtimeTransportClient.MatchStateMessage state, bool force)
        {
            if (state == null || state.duelSpawnSlotIndex < 0)
            {
                return;
            }

            if (!force && state.duelSpawnSlotIndex == lastAppliedSpawnSlot)
            {
                return;
            }

            if (!DmSpawnUtility.TryResolveSpawnPose(
                    state.duelSpawnSlotIndex,
                    out var position,
                    out var rotation))
            {
                return;
            }

            ForceReviveLocalPlayer(position, rotation, reviveIfDead: true);
            lastAppliedSpawnSlot = state.duelSpawnSlotIndex;
        }

        private void TryReviveAtServerSpawn(
            RealtimeTransportClient.MatchStateMessage state,
            int spawnSlotOverride = -1,
            int serverDeathSeq = -1)
        {
            if (state == null)
            {
                return;
            }

            var slot = spawnSlotOverride >= 0 ? spawnSlotOverride : state.duelSpawnSlotIndex;
            if (slot < 0)
            {
                return;
            }

            if (serverDeathSeq >= 0)
            {
                if (serverDeathSeq <= lastRevivedDeathSeq)
                {
                    return;
                }

                lastRevivedDeathSeq = serverDeathSeq;
            }
            else if (playerHealth != null && !playerHealth.IsDead && serverSaysAlive)
            {
                return;
            }

            if (!DmSpawnUtility.TryResolveSpawnPose(slot, out var position, out var rotation))
            {
                return;
            }

            transportClient?.ResetPoseSequenceForRespawn();
            if (serverDeathSeq >= 0)
            {
                playerHealth?.ApplyServerDeathSequence(serverDeathSeq);
            }

            ForceReviveLocalPlayer(position, rotation, reviveIfDead: true);
            lastAppliedSpawnSlot = slot;
            DmRespawnTrace.Log(
                "local-respawn",
                $"slot={slot} deathSeq={serverDeathSeq} lastRevived={lastRevivedDeathSeq} " +
                $"pos=({position.x:F2},{position.y:F2},{position.z:F2}) yaw={rotation.eulerAngles.y:F1}");
            SetMovementLocked(false);
            SetCombatEnabled(state.duelCombatEnabled);
            EnableCharacterControllerIfNeeded();
            fpsController?.SetServerReconciliationSuspended(false);
            presenceSync?.SetRemoteAvatarsVisible(true);
            presenceSync?.ResetSelfAuthoritativeReconcileCursor();
            presenceSync?.FlushLocalPose();
        }

        private bool IsLocalPlayerAlive()
        {
            return serverSaysAlive && (playerHealth == null || !playerHealth.IsDead);
        }

        private void EnableCharacterControllerIfNeeded()
        {
            if (characterController != null && !characterController.enabled && IsLocalPlayerAlive())
            {
                characterController.enabled = true;
            }

            if (fpsController != null && !fpsController.enabled && IsLocalPlayerAlive())
            {
                fpsController.enabled = true;
            }

            if (weaponController != null && !weaponController.enabled && IsLocalPlayerAlive())
            {
                weaponController.enabled = true;
            }

            if (pickupController != null && !pickupController.enabled && IsLocalPlayerAlive())
            {
                pickupController.enabled = true;
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
            }
            else
            {
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

            EnsureLocalWeaponFromServerState(lastState);
        }

        private void SetMovementLocked(bool locked)
        {
            if (!IsLocalPlayerAlive())
            {
                locked = true;
            }

            fpsController?.SetMovementLocked(locked);
        }

        private void SetCombatEnabled(bool enabled)
        {
            if (!IsLocalPlayerAlive())
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

            var kills = lastState?.localKillCount ?? 0;
            var placement = EstimatePlacement(lastState, won);
            var summary = MatchOutcomeSummary.CreateDeathmatch(won, kills, placement);
            capturedOutcome = summary;
            return summary;
        }

        private static int EstimatePlacement(RealtimeTransportClient.MatchStateMessage state, bool won)
        {
            if (state == null)
            {
                return won ? 1 : 2;
            }

            if (won)
            {
                return 1;
            }

            return Mathf.Max(2, state.connectedCount);
        }

        private static string ResolveLocalTicketId(RealtimeTransportClient.MatchStateMessage state)
        {
            if (!string.IsNullOrWhiteSpace(state.localTicketId))
            {
                return state.localTicketId.Trim();
            }

            var launcher = FindFirstObjectByType<NetworkLauncher>();
            return launcher != null && !string.IsNullOrWhiteSpace(launcher.CurrentTicketId)
                ? launcher.CurrentTicketId.Trim()
                : string.Empty;
        }
    }
}
