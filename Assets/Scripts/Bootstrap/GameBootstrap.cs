using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using ShooterPrototype.Player;
using ShooterPrototype.UI;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Bootstrap
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        private static GameBootstrap instance;

        [SerializeField] private LaunchMode overrideLaunchMode = LaunchMode.AutoDetect;
        [SerializeField] private bool keepAliveAcrossScenes = true;
        [SerializeField] private bool runClientInBackground = true;
        [SerializeField] private PerformancePresetController performancePreset;
        [SerializeField] private NetworkConfig networkConfig;
        [SerializeField] private NetworkLauncher networkLauncher;
        [SerializeField] private bool createRuntimeQueueApiClient = true;
        [SerializeField] private bool createRuntimeRealtimeTransportClient = true;
        [SerializeField] private bool createRuntimeGameHud = true;
        [SerializeField] private bool createRuntimePlayerSpawnManager = true;
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private string gameSceneName = "Game";
        [SerializeField] private string duelSceneName = "1x1";
        [SerializeField] private string trainingSceneName = "training";
        [SerializeField] private string challengeSceneName = "challenge";
        [SerializeField] private GameObject battleRoyalePlanePrefab;

        private bool initialized;
        private Coroutine matchWarmupCoroutine;
        private QueueApiClient queueApiClient;
        private RealtimeTransportClient realtimeTransportClient;
        private GameHudController gameHudController;
        private PlayerSpawnManager playerSpawnManager;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.Log("[GameBootstrap] Duplicate bootstrap detected. Destroying newest instance.");
                Destroy(gameObject);
                return;
            }

#if UNITY_EDITOR
            if (battleRoyalePlanePrefab == null)
            {
                battleRoyalePlanePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Plane/Plane.prefab");
            }
#endif

            instance = this;

            if (initialized)
            {
                return;
            }

            initialized = true;

            if (keepAliveAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            if (!Application.isBatchMode && runClientInBackground)
            {
                Application.runInBackground = true;
                Debug.Log("[GameBootstrap] Enabled Application.runInBackground for multiplayer local testing.");
            }

            if (!Application.isBatchMode)
            {
                EnsurePerformancePreset();
                EnsureNetworkPerformanceMonitor();
            }

            if (networkLauncher == null)
            {
                networkLauncher = GetComponent<NetworkLauncher>();
            }

            if (networkLauncher == null)
            {
                Debug.LogError("[GameBootstrap] NetworkLauncher component is missing.");
                return;
            }

            if (networkConfig == null)
            {
                Debug.LogError("[GameBootstrap] NetworkConfig asset is not assigned.");
                return;
            }

            networkLauncher.Initialize(networkConfig);

            if (!Application.isBatchMode && createRuntimeQueueApiClient)
            {
                queueApiClient = GetComponent<QueueApiClient>();
                if (queueApiClient == null)
                {
                    queueApiClient = gameObject.AddComponent<QueueApiClient>();
                }

                queueApiClient.Configure(networkConfig.QueueApiBaseUrl, networkConfig.QueueRequestTimeoutSeconds);
            }

            if (!Application.isBatchMode && createRuntimeRealtimeTransportClient)
            {
                realtimeTransportClient = GetComponent<RealtimeTransportClient>();
                if (realtimeTransportClient == null)
                {
                    realtimeTransportClient = gameObject.AddComponent<RealtimeTransportClient>();
                }

                realtimeTransportClient.Configure(networkConfig.ResolveRealtimeWsUrl());
            }

            var mode = ResolveLaunchMode();
            Debug.Log($"[GameBootstrap] Launch mode resolved as: {mode}");

            if (mode == LaunchMode.DedicatedServer && networkLauncher.CanAutoStartMockServer())
            {
                networkLauncher.StartDedicatedServer();
            }
            else if (mode == LaunchMode.DedicatedServer)
            {
                Debug.LogWarning("[GameBootstrap] Dedicated server mode detected, but auto-start is disabled in NetworkConfig.");
            }

            if (!Application.isBatchMode && createRuntimeGameHud)
            {
                gameHudController = gameObject.GetComponent<GameHudController>();
                if (gameHudController == null)
                {
                    gameHudController = gameObject.AddComponent<GameHudController>();
                }

                gameHudController.Initialize(networkLauncher, mainMenuSceneName, performancePreset);
                HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            }

            if (!Application.isBatchMode && createRuntimePlayerSpawnManager)
            {
                playerSpawnManager = gameObject.GetComponent<PlayerSpawnManager>();
                if (playerSpawnManager == null)
                {
                    playerSpawnManager = gameObject.AddComponent<PlayerSpawnManager>();
                }

                var activeScene = SceneManager.GetActiveScene();
                ActiveMatchContext.SyncFromScene(
                    activeScene.name,
                    gameSceneName,
                    duelSceneName,
                    trainingSceneName,
                    challengeSceneName);
                playerSpawnManager.Configure(ResolveActiveGameSceneName());
                playerSpawnManager.HandleSceneLoaded(activeScene);
            }

        }

        private string ResolveActiveGameSceneName()
        {
            return ActiveMatchContext.ResolveGameSceneName(
                gameSceneName,
                duelSceneName,
                trainingSceneName,
                challengeSceneName);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private LaunchMode ResolveLaunchMode()
        {
            if (overrideLaunchMode != LaunchMode.AutoDetect)
            {
                return overrideLaunchMode;
            }

            return Application.isBatchMode ? LaunchMode.DedicatedServer : LaunchMode.Client;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode _)
        {
            ActiveMatchContext.SyncFromScene(
                scene.name,
                gameSceneName,
                duelSceneName,
                trainingSceneName,
                challengeSceneName);

            var isMatchScene = scene.name == gameSceneName ||
                               scene.name == duelSceneName ||
                               scene.name == trainingSceneName ||
                               scene.name == challengeSceneName;
            if (gameHudController != null)
            {
                gameHudController.SetActiveForScene(isMatchScene);
            }

            if (playerSpawnManager != null)
            {
                playerSpawnManager.Configure(ResolveActiveGameSceneName());
                playerSpawnManager.HandleSceneLoaded(scene);
            }

            if (scene.name == gameSceneName && !Application.isBatchMode)
            {
                EnsureBattleRoyaleController();
            }
            else if (scene.name == trainingSceneName && !Application.isBatchMode)
            {
                EnsureTrainingController();
            }
            else if (scene.name == challengeSceneName && !Application.isBatchMode)
            {
                EnsureChallengeController();
            }
            else if (scene.name == duelSceneName && !Application.isBatchMode)
            {
                if (ActiveMatchContext.IsOfflineDuelSession)
                {
                    EnsureOfflineDuelController();
                }
                else
                {
                    EnsureDuelController();
                }
            }

            if (scene.name == mainMenuSceneName && !Application.isBatchMode)
            {
                ActiveMatchContext.SetOfflineTrainingSession(false);
                ActiveMatchContext.SetOfflineChallengeSession(false);
                ActiveMatchContext.SetOfflineDuelSession(false);
                LoadingScreenOverlay.Hide();
            }

            if (isMatchScene && !Application.isBatchMode)
            {
                if (matchWarmupCoroutine != null)
                {
                    StopCoroutine(matchWarmupCoroutine);
                }

                matchWarmupCoroutine = StartCoroutine(WarmUpMatchSceneRoutine());
            }
            else if (!isMatchScene && !Application.isBatchMode)
            {
                LoadingScreenOverlay.Hide();
            }
        }

        private IEnumerator WarmUpMatchSceneRoutine()
        {
            if (!LoadingScreenOverlay.IsVisible)
            {
                LoadingScreenOverlay.Show("Загрузка...");
            }

            LoadingScreenOverlay.SetMessage("Подгрузка интерфейса...");
            LoadingScreenOverlay.SetProgress(-1f);

            gameHudController?.WarmUpMatchUi();

            yield return null;
            yield return null;

            const float timeoutSeconds = 5f;
            var deadline = Time.unscaledTime + timeoutSeconds;
            while (FindFirstObjectByType<LocalPlayerMarker>() == null && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            if (ActiveMatchContext.IsTraining)
            {
                yield return OfflineTrainingBootstrap.StartWhenPlayerReady(this);
            }
            else if (ActiveMatchContext.IsChallenge)
            {
                LoadingScreenOverlay.Hide();
                yield return OfflineChallengeBootstrap.StartWhenPlayerReady(this);
            }
            else if (ActiveMatchContext.IsOfflineDuelSession)
            {
                LoadingScreenOverlay.Hide();
                yield return OfflineDuelBootstrap.StartWhenPlayerReady(this);
            }

            Canvas.ForceUpdateCanvases();
            yield return null;

            LoadingScreenOverlay.Hide();
            matchWarmupCoroutine = null;
        }

        private void EnsureChallengeController()
        {
            var existing = FindFirstObjectByType<MatchChallengeController>();
            if (existing != null)
            {
                existing.PrepareForNewMatch();
                return;
            }

            var controllerObject = new GameObject("MatchChallenge");
            controllerObject.AddComponent<MatchChallengeController>();
        }

        private void EnsureTrainingController()
        {
            var existing = FindFirstObjectByType<MatchTrainingController>();
            if (existing != null)
            {
                existing.PrepareForNewMatch();
                return;
            }

            var controllerObject = new GameObject("MatchTraining");
            controllerObject.AddComponent<MatchTrainingController>();
        }

        private void EnsureOfflineDuelController()
        {
            var online = FindFirstObjectByType<MatchDuelController>();
            if (online != null)
            {
                Destroy(online.gameObject);
            }

            var existing = FindFirstObjectByType<MatchOfflineDuelController>();
            if (existing != null)
            {
                existing.PrepareForNewMatch();
                return;
            }

            var controllerObject = new GameObject("MatchOfflineDuel");
            controllerObject.AddComponent<MatchOfflineDuelController>();
        }

        private void EnsureDuelController()
        {
            var offline = FindFirstObjectByType<MatchOfflineDuelController>();
            if (offline != null)
            {
                Destroy(offline.gameObject);
            }

            var existing = FindFirstObjectByType<MatchDuelController>();
            if (existing != null)
            {
                existing.PrepareForNewMatch();
                return;
            }

            var controllerObject = new GameObject("MatchDuel");
            var controller = controllerObject.AddComponent<MatchDuelController>();
            controller.PrepareForNewMatch();
        }

        private void EnsureBattleRoyaleController()
        {
            var existing = FindFirstObjectByType<MatchBattleRoyaleController>();
            if (existing != null)
            {
                if (battleRoyalePlanePrefab != null)
                {
                    existing.ConfigurePlanePrefab(battleRoyalePlanePrefab);
                }

                existing.PrepareForNewMatch();
                return;
            }

            var controllerObject = new GameObject("MatchBattleRoyale");
            var controller = controllerObject.AddComponent<MatchBattleRoyaleController>();
            if (battleRoyalePlanePrefab != null)
            {
                controller.ConfigurePlanePrefab(battleRoyalePlanePrefab);
            }
        }

        private void EnsurePerformancePreset()
        {
            if (performancePreset == null)
            {
                performancePreset = GetComponent<PerformancePresetController>();
            }

            if (performancePreset == null)
            {
                performancePreset = gameObject.AddComponent<PerformancePresetController>();
            }
        }

        private static void EnsureNetworkPerformanceMonitor()
        {
            if (NetworkPerformanceMonitor.Instance != null)
            {
                return;
            }

            var bootstrap = FindFirstObjectByType<GameBootstrap>();
            if (bootstrap == null)
            {
                return;
            }

            if (bootstrap.GetComponent<NetworkPerformanceMonitor>() == null)
            {
                bootstrap.gameObject.AddComponent<NetworkPerformanceMonitor>();
            }
        }
    }
}
