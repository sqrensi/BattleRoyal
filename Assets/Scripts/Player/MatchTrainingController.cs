using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.UI;
using UnityEngine;
using UnityEngine.AI;

namespace ShooterPrototype.Player
{
    public sealed class MatchTrainingController : MonoBehaviour
    {
        public const float SessionDurationSeconds = 600f;

        private const int DefaultBotCount = 4;
        private const float DefaultBotWanderRadius = 42f;

        [SerializeField] private int botCount = DefaultBotCount;
        [SerializeField] private float botWanderRadius = DefaultBotWanderRadius;
        [SerializeField] private float botRespawnDelaySeconds = 4f;
        [SerializeField] private Vector3 pickupZoneOffset = new Vector3(0f, 0f, 5f);
        [SerializeField] private Vector3 pickupZoneSize = new Vector3(8f, 2f, 8f);

        private readonly List<TrainingBotController> bots = new List<TrainingBotController>(DefaultBotCount);

        private float remainingSeconds = SessionDurationSeconds;
        private int killCount;
        private int aliveBots;
        private bool sessionEnded;
        private bool sessionStarted;
        private Transform playerSpawn;
        private GameHudController gameHud;
        private CombatHudController combatHud;
        private GameKillFeedController killFeed;
        private PlayerSpawnManager spawnManager;
        private PickupSpawnManager pickupSpawnManager;
        private LocalPlayerMarker localPlayer;

        public static MatchTrainingController Active { get; private set; }

        public bool IsSessionActive => sessionStarted && !sessionEnded;

        public int KillCount => killCount;

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

        public void PrepareForNewMatch()
        {
            sessionEnded = false;
            sessionStarted = false;
            killCount = 0;
            aliveBots = 0;
            remainingSeconds = SessionDurationSeconds;
        }

        public void StartTrainingSession()
        {
            StartCoroutine(StartTrainingSessionRoutine());
        }

        public IEnumerator StartTrainingSessionRoutine()
        {
            if (sessionStarted)
            {
                yield break;
            }

            sessionStarted = true;
            sessionEnded = false;
            killCount = 0;
            remainingSeconds = SessionDurationSeconds;

            CacheReferences();
            gameHud?.SetScoreboardLocalTicket("offline-local");
            ResolvePlayerSpawn();

            LoadingScreenOverlay.SetMessage("Подготовка тренировки...");
            yield return null;

            yield return EnsureNavMeshReadyRoutine();
            yield return WaitForLocalPlayerRoutine();
            if (localPlayer == null)
            {
                sessionStarted = false;
                Debug.LogError(
                    "[MatchTrainingController] Training aborted: local player was not spawned. " +
                    "Check PlayerSpawnManager scene name and SpawnPoints on the training scene.");
                LoadingScreenOverlay.Hide();
                yield break;
            }

            ResolvePlayerSpawn();
            SetupPickups();
            EnableLocalPlayerCombat();
            yield return SpawnBotsRoutine();
            gameHud?.ShowModeIntroBanner(
                MainMenuGameModeUtility.GetModeIntroDescription(MainMenuGameMode.Training),
                5f);
            RefreshHud();
            StartCoroutine(SessionTimerRoutine());

            LoadingScreenOverlay.Hide();
        }

        public void NotifyBotKilled(TrainingBotController bot)
        {
            if (sessionEnded || bot == null)
            {
                return;
            }

            killCount++;
            aliveBots = Mathf.Max(0, aliveBots - 1);

            combatHud?.ShowTrainingKillBanner(bot.KillBannerVictimLabel);
            killFeed?.PushLocalPlayerKill(ResolveLocalKillerName(), bot.KillFeedVictimLabel);
            gameHud?.SetKillCount(killCount);
            gameHud?.SetMatchCornerStats(killCount, aliveBots);
        }

        public void NotifyBotRespawned()
        {
            aliveBots++;
            gameHud?.SetMatchCornerStats(killCount, aliveBots);
        }

        public (Vector3 Position, Quaternion Rotation) SampleBotSpawnPose()
        {
            var origin = playerSpawn != null ? playerSpawn.position : Vector3.zero;
            for (var attempt = 0; attempt < 24; attempt++)
            {
                var offset = Random.insideUnitSphere * botWanderRadius;
                offset.y = 0f;
                if (TrySampleNavMeshPosition(origin + offset, botWanderRadius, out var hit))
                {
                    return (hit, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                }
            }

            return (origin + Vector3.forward * 12f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        }

        private void CacheReferences()
        {
            gameHud = FindFirstObjectByType<GameHudController>();
            combatHud = FindFirstObjectByType<CombatHudController>();
            killFeed = FindFirstObjectByType<GameKillFeedController>();
            spawnManager = FindFirstObjectByType<PlayerSpawnManager>();
            pickupSpawnManager = FindFirstObjectByType<PickupSpawnManager>();
            localPlayer = FindFirstObjectByType<LocalPlayerMarker>();
        }

        private void ResolvePlayerSpawn()
        {
            var spawnRoot = GameObject.Find("SpawnPoints");
            if (spawnRoot != null && spawnRoot.transform.childCount > 0)
            {
                playerSpawn = spawnRoot.transform.GetChild(0);
                return;
            }

            if (localPlayer != null)
            {
                playerSpawn = localPlayer.transform;
            }
        }

        private IEnumerator EnsureNavMeshReadyRoutine()
        {
            const float timeoutSeconds = 8f;
            var deadline = Time.unscaledTime + timeoutSeconds;
            var sampleOrigin = playerSpawn != null ? playerSpawn.position : Vector3.zero;
            while (Time.unscaledTime < deadline)
            {
                if (TrySampleNavMeshPosition(sampleOrigin, 48f, out _))
                {
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning(
                "[MatchTrainingController] NavMesh not found near spawn. Bake NavMesh on the training scene " +
                "(NavMesh Surface on walkable floor) so bots can move.");
        }

        private static bool TrySampleNavMeshPosition(Vector3 position, float radius, out Vector3 result)
        {
            if (NavMesh.SamplePosition(position, out var hit, radius, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }

            result = position;
            return false;
        }

        public void OnLocalPlayerSpawned(LocalPlayerMarker marker)
        {
            if (marker == null)
            {
                return;
            }

            localPlayer = marker;
        }

        private IEnumerator WaitForLocalPlayerRoutine()
        {
            const float timeoutSeconds = 10f;
            var deadline = Time.unscaledTime + timeoutSeconds;
            while (localPlayer == null && Time.unscaledTime < deadline)
            {
                localPlayer = FindFirstObjectByType<LocalPlayerMarker>();
                if (localPlayer != null)
                {
                    yield break;
                }

                yield return null;
            }
        }

        private void SetupPickups()
        {
            if (pickupSpawnManager == null || playerSpawn == null)
            {
                return;
            }

            var zoneCenter = playerSpawn.position +
                             playerSpawn.forward * pickupZoneOffset.z +
                             playerSpawn.right * pickupZoneOffset.x +
                             Vector3.up * pickupZoneOffset.y;
            pickupSpawnManager.BootstrapTrainingPickupZone(
                zoneCenter,
                playerSpawn.forward,
                pickupZoneSize);
        }

        private void EnableLocalPlayerCombat()
        {
            if (localPlayer == null)
            {
                localPlayer = FindFirstObjectByType<LocalPlayerMarker>();
            }

            if (localPlayer == null)
            {
                Debug.LogWarning("[MatchTrainingController] Local player not found.");
                return;
            }

            var fps = localPlayer.GetComponent<FpsCharacterController>();
            if (fps != null)
            {
                fps.enabled = true;
                fps.SetMovementLocked(false);
                fps.SetGameOverMode(false);
                fps.SetServerReconciliationSuspended(true);
            }

            var viewPresentation = localPlayer.GetComponent<PlayerViewPresentation>();
            viewPresentation?.SetForceThirdPersonBody(false);

            var pickup = localPlayer.GetComponent<PlayerPickupController>();
            if (pickup != null)
            {
                pickup.enabled = true;
                pickup.RefreshWeaponAvailability();
            }

            var weapon = localPlayer.GetComponent<PlayerWeaponController>();
            if (weapon != null)
            {
                weapon.enabled = true;
                weapon.RefreshWeaponAvailability();
            }

            var loadoutController = localPlayer.GetComponent<PlayerWeaponLoadoutController>();
            loadoutController?.ConfigureTrainingInfiniteReserveAmmo(true);
        }

        private IEnumerator SpawnBotsRoutine()
        {
            ClearBots();

            var count = Mathf.Max(1, botCount);
            aliveBots = count;
            for (var i = 0; i < count; i++)
            {
                var pose = SampleBotSpawnPose();
                var bot = TrainingBotFactory.Create(pose.Position, pose.Rotation, i + 1);
                bots.Add(bot);
                yield return null;
            }
        }

        private void ClearBots()
        {
            for (var i = 0; i < bots.Count; i++)
            {
                if (bots[i] != null)
                {
                    Destroy(bots[i].gameObject);
                }
            }

            bots.Clear();
            aliveBots = 0;
        }

        private void RefreshHud()
        {
            gameHud?.SetKillCount(killCount);
            gameHud?.SetMatchCornerStats(killCount, aliveBots);
            gameHud?.SetTrainingTimerSeconds(Mathf.CeilToInt(remainingSeconds));
        }

        private IEnumerator SessionTimerRoutine()
        {
            while (remainingSeconds > 0f && !sessionEnded)
            {
                remainingSeconds -= Time.deltaTime;
                gameHud?.SetTrainingTimerSeconds(Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds)));
                yield return null;
            }

            EndSession();
        }

        private void EndSession()
        {
            if (sessionEnded)
            {
                return;
            }

            sessionEnded = true;
            var elapsedSeconds = Mathf.Clamp(
                Mathf.RoundToInt(SessionDurationSeconds - Mathf.Max(0f, remainingSeconds)),
                1,
                Mathf.RoundToInt(SessionDurationSeconds));
            gameHud?.ScheduleTrainingGameOver(killCount, elapsedSeconds);
        }

        private static string ResolveLocalKillerName()
        {
            return PlayerProfileService.LocalDisplayNickname;
        }
    }
}
