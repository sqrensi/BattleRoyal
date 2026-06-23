using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.UI;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public sealed class MatchChallengeController : MonoBehaviour
    {
        public const float WeaponPickSeconds = 10f;
        public const float GameOverDelaySeconds = 5f;

        private readonly List<ChallengeTarget> targets = new List<ChallengeTarget>(32);

        private int destroyedTargets;
        private bool sessionStarted;
        private bool sessionEnded;
        private bool runActive;
        private bool weaponPicked;
        private float elapsedSeconds;
        private float runStartedAt;
        private Transform playerSpawn;
        private LocalPlayerMarker localPlayer;
        private GameHudController gameHud;
        private PlayerSpawnManager spawnManager;
        private FpsCharacterController fpsController;
        private PlayerWeaponController weaponController;
        private PlayerWeaponLoadoutController loadoutController;

        public static MatchChallengeController Active { get; private set; }

        public bool IsRunActive => runActive && !sessionEnded;

        public int DestroyedTargets => destroyedTargets;

        public int TotalTargets => targets.Count;

        public float ElapsedSeconds => elapsedSeconds;

        private void Awake()
        {
            Active = this;
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        private void Update()
        {
            if (!runActive || sessionEnded)
            {
                return;
            }

            elapsedSeconds = Mathf.Max(0f, Time.time - runStartedAt);
            gameHud?.SetChallengeElapsedSeconds(elapsedSeconds);
        }

        public void PrepareForNewMatch()
        {
            sessionStarted = false;
            sessionEnded = false;
            runActive = false;
            weaponPicked = false;
            destroyedTargets = 0;
            elapsedSeconds = 0f;
            targets.Clear();
        }

        public void StartChallengeSession()
        {
            StartCoroutine(StartChallengeSessionRoutine());
        }

        public IEnumerator StartChallengeSessionRoutine()
        {
            if (sessionStarted)
            {
                yield break;
            }

            sessionStarted = true;
            sessionEnded = false;
            runActive = false;
            weaponPicked = false;
            destroyedTargets = 0;
            elapsedSeconds = 0f;

            CacheReferences();
            ResolvePlayerSpawn();
            BootstrapTargets();

            LoadingScreenOverlay.Hide();
            yield return WaitForLocalPlayerRoutine();
            if (localPlayer == null)
            {
                sessionStarted = false;
                Debug.LogError("[MatchChallengeController] Challenge aborted: local player not spawned.");
                LoadingScreenOverlay.Hide();
                yield break;
            }

            BindLocalPlayer();
            DisableCombatUntilPrepEnds();
            gameHud?.SetChallengeTargetStats(0, targets.Count);
            gameHud?.SetChallengeElapsedSeconds(0f);

            yield return WeaponPickPhaseRoutine();
            yield return StartRunRoutine();
        }

        public void NotifyTargetDestroyed(ChallengeTarget target)
        {
            if (!runActive || sessionEnded || target == null)
            {
                return;
            }

            destroyedTargets++;
            gameHud?.SetChallengeTargetStats(destroyedTargets, targets.Count);

            if (destroyedTargets >= targets.Count && targets.Count > 0)
            {
                FinishChallenge();
            }
        }

        private IEnumerator WeaponPickPhaseRoutine()
        {
            fpsController?.SetMovementLocked(true);
            fpsController?.SetWeaponPickUiMode(true);
            gameHud?.ShowDuelWeaponPickPanel(HandleWeaponPicked);

            var remaining = WeaponPickSeconds;
            while (remaining > 0f)
            {
                gameHud?.SetChallengePrepCountdown(Mathf.CeilToInt(remaining));
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            gameHud?.HideDuelWeaponPickPanel();
            fpsController?.SetWeaponPickUiMode(false);

            if (!weaponPicked)
            {
                HandleWeaponPicked(WeaponKind.AssaultRifle);
            }
        }

        private IEnumerator StartRunRoutine()
        {
            gameHud?.ClearChallengePrepCountdown();
            gameHud?.ShowChallengeStartedBanner();
            yield return new WaitForSecondsRealtime(1.2f);
            gameHud?.ClearDuelRoundBanner();

            EnableLocalPlayerCombat();
            fpsController?.SetMovementLocked(false);
            runActive = true;
            runStartedAt = Time.time;
            elapsedSeconds = 0f;
            gameHud?.SetChallengeElapsedSeconds(0f);
        }

        private void HandleWeaponPicked(WeaponKind kind)
        {
            if (weaponPicked || sessionEnded)
            {
                return;
            }

            weaponPicked = true;
            gameHud?.HideDuelWeaponPickPanel();
            fpsController?.SetWeaponPickUiMode(false);
            loadoutController ??= localPlayer.GetComponent<PlayerWeaponLoadoutController>();
            if (loadoutController == null)
            {
                return;
            }

            loadoutController.ApplyDuelRoundWeaponPick(kind, PlayerWeaponLoadoutController.TrainingInfiniteReserveAmmo);
            loadoutController.ConfigureTrainingInfiniteReserveAmmo(true);
            localPlayer.GetComponent<PlayerWeaponHolsterController>()?.ForceArmedState();
            localPlayer.GetComponent<PlayerPickupController>()?.RefreshWeaponAvailability();
        }

        private void FinishChallenge()
        {
            if (sessionEnded)
            {
                return;
            }

            runActive = false;
            sessionEnded = true;
            elapsedSeconds = Mathf.Max(0f, Time.time - runStartedAt);
            fpsController?.SetMovementLocked(true);
            weaponController?.SetDuelFireBlocked(true);
            gameHud?.SetChallengeElapsedSeconds(elapsedSeconds);
            gameHud?.ScheduleChallengeGameOver(elapsedSeconds);
        }

        private void BootstrapTargets()
        {
            targets.Clear();
            var targetsRoot = GameObject.Find("Targets");
            if (targetsRoot == null)
            {
                Debug.LogWarning("[MatchChallengeController] Targets root not found in scene.");
                return;
            }

            for (var i = 0; i < targetsRoot.transform.childCount; i++)
            {
                var child = targetsRoot.transform.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var target = child.GetComponent<ChallengeTarget>();
                if (target == null)
                {
                    target = child.gameObject.AddComponent<ChallengeTarget>();
                }

                target.EnsureCollider();
                targets.Add(target);
            }
        }

        private void CacheReferences()
        {
            gameHud = FindFirstObjectByType<GameHudController>();
            spawnManager = FindFirstObjectByType<PlayerSpawnManager>();
        }

        private void ResolvePlayerSpawn()
        {
            var spawnPoints = GameObject.Find("SpawnPoints");
            if (spawnPoints != null && spawnPoints.transform.childCount > 0)
            {
                playerSpawn = spawnPoints.transform.GetChild(0);
                return;
            }

            playerSpawn = spawnManager != null ? spawnManager.transform : null;
        }

        private IEnumerator WaitForLocalPlayerRoutine()
        {
            const float timeoutSeconds = 8f;
            var deadline = Time.unscaledTime + timeoutSeconds;
            while (localPlayer == null && Time.unscaledTime < deadline)
            {
                localPlayer = FindFirstObjectByType<LocalPlayerMarker>();
                yield return null;
            }
        }

        private void BindLocalPlayer()
        {
            if (localPlayer == null)
            {
                return;
            }

            fpsController = localPlayer.GetComponent<FpsCharacterController>();
            weaponController = localPlayer.GetComponent<PlayerWeaponController>();
            loadoutController = localPlayer.GetComponent<PlayerWeaponLoadoutController>();
        }

        private void DisableCombatUntilPrepEnds()
        {
            if (fpsController != null)
            {
                fpsController.enabled = true;
                fpsController.SetMovementLocked(true);
                fpsController.SetGameOverMode(false);
                fpsController.SetServerReconciliationSuspended(true);
            }

            if (weaponController != null)
            {
                weaponController.enabled = true;
                weaponController.SetDuelFireBlocked(true);
            }

            var pickup = localPlayer.GetComponent<PlayerPickupController>();
            if (pickup != null)
            {
                pickup.enabled = true;
                pickup.RefreshWeaponAvailability();
            }
        }

        private void EnableLocalPlayerCombat()
        {
            fpsController?.SetMovementLocked(false);
            fpsController?.SetServerReconciliationSuspended(true);

            var viewPresentation = localPlayer.GetComponent<PlayerViewPresentation>();
            viewPresentation?.SetForceThirdPersonBody(false);

            if (weaponController != null)
            {
                weaponController.enabled = true;
                weaponController.SetDuelFireBlocked(false);
                weaponController.RefreshWeaponAvailability();
            }

            loadoutController?.ConfigureTrainingInfiniteReserveAmmo(true);
        }
    }
}
