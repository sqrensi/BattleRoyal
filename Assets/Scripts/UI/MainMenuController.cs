using System.Collections;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private NetworkLauncher networkLauncher;
        [SerializeField] private QueueApiClient queueApiClient;
        [SerializeField] private PlayerProfileApiClient profileApiClient;
        [SerializeField] private NetworkConfig networkConfig;

        [Header("UI")]
        [SerializeField] private Button startButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text startButtonText;
        [SerializeField] private Button changeCharacterButton;
        [SerializeField] private TMP_Text selectedCharacterText;

        [Header("Characters")]
        [SerializeField] private string charactersResourcesFolder = "Characters";

        [Header("Connection State")]
        [SerializeField] private string idleStatusText = "Готов к подключению";
        [SerializeField] private string searchingStatusText = "Поиск матча...";
        [SerializeField] private string connectingStatusText = "Матч найден. Подключение к серверу...";
        [SerializeField] private string connectionFailedStatusText = "Не удалось подключиться. Проверь сервер и попробуй снова.";
        [SerializeField] private string connectedStatusText = "Подключение успешно.";
        [SerializeField] private string queueCancelledStatusText = "Поиск матча отменен.";

        [Header("Scene Flow")]
        [SerializeField] private bool autoLoadGameSceneOnSuccess = true;
        [SerializeField] private string gameSceneName = "Game";
        [SerializeField] private string duelSceneName = "1x1";

        [Header("Reliability")]
        [SerializeField] private int enqueueRetryCount = 2;
        [SerializeField] private float enqueueRetryDelaySeconds = 0.4f;

        private Coroutine queuePollingCoroutine;
        private Coroutine profileSyncCoroutine;
        private bool isQueueing;
        private string currentTicketId = string.Empty;
        private string localPlayerId;

        private MainMenuUiSoundController uiSound;
        private MainMenuServerConnectionGate connectionGate;

        public Button ChangeCharacterButton => changeCharacterButton;

        public Button StartButton => startButton;
        public TMP_Text StatusText => statusText;

        private void Awake()
        {
            EnsureDependencies();
            EnsureUiLayout();
            EnsureUiSound();

            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartPressed);
            }
            if (changeCharacterButton != null)
            {
                changeCharacterButton.onClick.AddListener(OnChangeCharacterPressed);
                if (uiSound != null)
                {
                    changeCharacterButton.onClick.AddListener(uiSound.PlayButton);
                }
            }

            localPlayerId = PlayerIdentityService.GetOrCreatePlayerId();
            SetStatus(idleStatusText);
            SetStartButtonState(isQueueing: false, interactable: true);
            RefreshSelectedCharacterLabel();
            EnsurePlayerPreview();
            EnsureAmbience();
            EnsureCameraMotion();
            EnsureSections();
            ApplyServerConnectionState(MainMenuServerConnectionState.Loading, "Подключение к серверу...");
        }

        private void Start()
        {
            profileSyncCoroutine = StartCoroutine(SyncProfileRoutine());
        }

        private void OnEnable()
        {
            EnsureDependencies();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (networkLauncher == null)
            {
                Debug.LogWarning("[MainMenuController] NetworkLauncher is not set.");
                return;
            }

            networkLauncher.StatusChanged += HandleStatusChanged;

            SetStatus(idleStatusText);
            SetStartButtonState(isQueueing: false, interactable: true);
            RefreshSelectedCharacterLabel();
            if (PlayerProfileService.IsServerSynced)
            {
                RefreshPlayerPreview(true);
            }
        }

        private void OnDisable()
        {
            if (queuePollingCoroutine != null)
            {
                StopCoroutine(queuePollingCoroutine);
                queuePollingCoroutine = null;
            }

            isQueueing = false;
            currentTicketId = string.Empty;

            if (networkLauncher == null)
            {
                return;
            }

            networkLauncher.StatusChanged -= HandleStatusChanged;
        }

        public void OnChangeCharacterPressed()
        {
            var selection = CharacterSelectionService.SelectNextModel(charactersResourcesFolder);
            var displayName = selection.ModelAsset != null ? selection.DisplayName : "Default";
            RefreshSelectedCharacterLabel(displayName);
            RefreshPlayerPreview(PlayerProfileService.IsServerSynced);
        }

        public void OnStartPressed()
        {
            EnsureDependencies();

            if (GetComponent<MainMenuSectionController>() is { IsPanelOpen: true })
            {
                return;
            }

            if (!PlayerProfileService.IsServerSynced)
            {
                SetStatus("Сервер недоступен");
                ApplyServerConnectionState(MainMenuServerConnectionState.Unavailable, "Сервер недоступен");
                return;
            }

            if (networkLauncher == null)
            {
                SetStatus("Ошибка: NetworkLauncher не привязан.");
                return;
            }

            if (queueApiClient == null)
            {
                SetStatus("Ошибка: QueueApiClient не привязан.");
                return;
            }

            if (isQueueing)
            {
                uiSound?.PlayCancel();
                CancelQueue();
                return;
            }

            if (networkLauncher.IsConnecting)
            {
                SetStatus("Подключение уже выполняется...");
                return;
            }

            uiSound?.PlayStart();
            StartQueueSearch();
        }

        private void HandleStatusChanged(string message)
        {
            SetStatus(message);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }

            Debug.Log($"[MainMenuController] {message}");
        }

        private void StartQueueSearch()
        {
            if (queuePollingCoroutine != null)
            {
                StopCoroutine(queuePollingCoroutine);
            }

            queuePollingCoroutine = StartCoroutine(EnqueueAndPollRoutine());
        }

        private void CancelQueue()
        {
            if (!isQueueing)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(currentTicketId))
            {
                StartCoroutine(SendDequeueBestEffort(currentTicketId));
            }

            if (queuePollingCoroutine != null)
            {
                StopCoroutine(queuePollingCoroutine);
                queuePollingCoroutine = null;
            }

            isQueueing = false;
            currentTicketId = string.Empty;
            SetStatus(queueCancelledStatusText);
            SetStartButtonState(isQueueing: false, interactable: true);
        }

        private IEnumerator EnqueueAndPollRoutine()
        {
            var realtimeClient = FindObjectOfType<RealtimeTransportClient>();
            realtimeClient?.Disconnect();
            networkLauncher?.DisconnectClient("Preparing queue search.");

            if (profileApiClient != null)
            {
                yield return PlayerProfileService.SyncProfile(
                    this,
                    profileApiClient,
                    localPlayerId,
                    fallbackToLocalOnFailure: false);
            }

            if (!PlayerProfileService.IsServerSynced)
            {
                SetStatus("Сервер недоступен");
                ApplyServerConnectionState(MainMenuServerConnectionState.Unavailable, "Сервер недоступен");
                SetStartButtonState(isQueueing: false, interactable: true);
                yield break;
            }

            RefreshNicknameEditor();

            if (queueApiClient != null && networkLauncher != null && !string.IsNullOrWhiteSpace(networkLauncher.CurrentTicketId))
            {
                yield return StartCoroutine(SendLeaveMatchBestEffort(networkLauncher.CurrentTicketId));
                networkLauncher.ClearMatchContext();
            }

            SetStatus(searchingStatusText);
            SetStartButtonState(isQueueing: true, interactable: false);

            var enqueueCompleted = false;
            var enqueueOk = false;
            QueueEnqueueResponse enqueueResponse = null;
            var enqueueError = string.Empty;

            ActiveMatchContext.SetMode(MainMenuGameModeSelector.SelectedMode);

            var attempts = Mathf.Max(1, enqueueRetryCount + 1);
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                enqueueCompleted = false;
                enqueueOk = false;
                enqueueResponse = null;
                enqueueError = string.Empty;

                yield return StartCoroutine(queueApiClient.Enqueue(
                    localPlayerId,
                    MainMenuGameModeUtility.ToApiValue(MainMenuGameModeSelector.SelectedMode),
                    (ok, response, error) =>
                {
                    enqueueCompleted = true;
                    enqueueOk = ok;
                    enqueueResponse = response;
                    enqueueError = error;
                }));

                if (enqueueCompleted && enqueueOk && enqueueResponse != null && !string.IsNullOrWhiteSpace(enqueueResponse.ticketId))
                {
                    break;
                }

                if (attempt < attempts)
                {
                    SetStatus($"{searchingStatusText} retry {attempt}/{attempts - 1}...");
                    yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, enqueueRetryDelaySeconds));
                }
            }

            if (!enqueueCompleted || !enqueueOk || enqueueResponse == null || string.IsNullOrWhiteSpace(enqueueResponse.ticketId))
            {
                isQueueing = false;
                queuePollingCoroutine = null;
                SetStatus($"Не удалось встать в очередь. {enqueueError}".Trim());
                SetStartButtonState(isQueueing: false, interactable: true);
                yield break;
            }

            currentTicketId = enqueueResponse.ticketId;
            isQueueing = true;
            SetStartButtonState(isQueueing: true, interactable: true);

            var pollDelay = GetQueuePollInterval();
            while (isQueueing && !string.IsNullOrWhiteSpace(currentTicketId))
            {
                var statusCompleted = false;
                var statusOk = false;
                QueueTicketStatusResponse statusResponse = null;
                var statusError = string.Empty;

                yield return StartCoroutine(queueApiClient.GetTicketStatus(currentTicketId, (ok, response, error) =>
                {
                    statusCompleted = true;
                    statusOk = ok;
                    statusResponse = response;
                    statusError = error;
                }));

                if (!statusCompleted || !statusOk || statusResponse == null)
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStatus($"Ошибка статуса очереди: {statusError}".Trim());
                    SetStartButtonState(isQueueing: false, interactable: true);
                    yield break;
                }

                var status = statusResponse.status ?? string.Empty;
                if (status.Equals("Queued", System.StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus($"{searchingStatusText} ({statusResponse.queueDurationSeconds:F1}s)");
                }
                else if (status.Equals("Matched", System.StringComparison.OrdinalIgnoreCase))
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStartButtonState(isQueueing: false, interactable: false);
                    var playerCount = Mathf.Max(1, statusResponse.matchedPlayerCount);
                    networkLauncher.SetMatchContext(statusResponse.matchId, playerCount, statusResponse.ticketId);
                    SetStatus($"{connectingStatusText} {statusResponse.serverAddress}:{statusResponse.serverPort} | players: {playerCount}");
                    yield return StartCoroutine(ConnectAndEnterGameRoutine(statusResponse.serverAddress, statusResponse.serverPort));
                    yield break;
                }
                else if (status.Equals("Cancelled", System.StringComparison.OrdinalIgnoreCase))
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStatus(queueCancelledStatusText);
                    SetStartButtonState(isQueueing: false, interactable: true);
                    yield break;
                }
                else if (status.Equals("Expired", System.StringComparison.OrdinalIgnoreCase))
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStatus("Поиск матча истек. Нажми Start, чтобы попробовать снова.");
                    SetStartButtonState(isQueueing: false, interactable: true);
                    yield break;
                }
                else if (status.Equals("Disconnected", System.StringComparison.OrdinalIgnoreCase) ||
                         status.Equals("Left", System.StringComparison.OrdinalIgnoreCase))
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStatus("Тикет больше не активен. Нажми Start, чтобы войти в новую очередь.");
                    SetStartButtonState(isQueueing: false, interactable: true);
                    yield break;
                }
                else
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStatus($"Неизвестный статус тикета: {status}. Нажми Start и попробуй снова.");
                    SetStartButtonState(isQueueing: false, interactable: true);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(pollDelay);
            }

            queuePollingCoroutine = null;
        }

        private void SetStartButtonState(bool isQueueing, bool interactable)
        {
            if (startButton != null)
            {
                startButton.interactable = interactable;
            }

            GetComponent<MainMenuGameModeSelector>()?.SetInteractable(!isQueueing && interactable);

            if (startButtonText != null)
            {
                startButtonText.text = isQueueing ? "Отмена" : "Играть";
            }
            else if (startButton != null)
            {
                var label = startButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = isQueueing ? "Отмена" : "Играть";
                }
            }
        }

        public void BindStartButtonText(TMP_Text label)
        {
            startButtonText = label;
        }

        private void EnsureEconomy()
        {
            PlayerProfileService.ApplyLocalFallback();
        }

        public void BindConnectionGate(MainMenuServerConnectionGate gate)
        {
            connectionGate = gate;
        }

        public void RetryServerConnection()
        {
            if (profileSyncCoroutine != null)
            {
                StopCoroutine(profileSyncCoroutine);
                profileSyncCoroutine = null;
            }

            ApplyServerConnectionState(MainMenuServerConnectionState.Loading, "Подключение к серверу...");
            profileSyncCoroutine = StartCoroutine(SyncProfileRoutine());
        }

        private IEnumerator SyncProfileRoutine()
        {
            if (profileApiClient == null)
            {
                ApplyServerConnectionState(MainMenuServerConnectionState.Unavailable, "Сервер недоступен");
                yield break;
            }

            ApplyServerConnectionState(MainMenuServerConnectionState.Loading, "Подключение к серверу...");
            yield return PlayerProfileService.SyncProfile(this, profileApiClient, localPlayerId, fallbackToLocalOnFailure: false);

            if (PlayerProfileService.IsServerSynced)
            {
                SetStatus(idleStatusText);
                ApplyServerConnectionState(MainMenuServerConnectionState.Connected);
                RefreshPlayerPreview(true);
                RefreshNicknameEditor();
                yield break;
            }

            SetStatus("Сервер недоступен");
            ApplyServerConnectionState(MainMenuServerConnectionState.Unavailable, "Сервер недоступен");
            RefreshPlayerPreview(false);
        }

        private void ApplyServerConnectionState(MainMenuServerConnectionState state, string message = null)
        {
            connectionGate?.SetState(state, message);
        }

        public string LocalPlayerId => localPlayerId;
        public PlayerProfileApiClient ProfileApiClient => profileApiClient;

        private void EnsureUiSound()
        {
            uiSound = GetComponent<MainMenuUiSoundController>();
            if (uiSound == null)
            {
                uiSound = gameObject.AddComponent<MainMenuUiSoundController>();
            }
        }

        private void EnsureUiLayout()
        {
            var layout = GetComponent<MainMenuUiLayout>();
            if (layout == null)
            {
                layout = gameObject.AddComponent<MainMenuUiLayout>();
            }

            layout.ApplyLayout(this);
            if (startButtonText == null && startButton != null)
            {
                startButtonText = startButton.GetComponentInChildren<TMP_Text>(true);
            }
        }

        private void RefreshSelectedCharacterLabel(string selectedName = null)
        {
            if (selectedCharacterText == null)
            {
                return;
            }

            var resolvedName = selectedName;
            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                var selected = CharacterSelectionService.ResolveSelectedModel(charactersResourcesFolder);
                resolvedName = selected.ModelAsset != null ? selected.DisplayName : "Default";
            }

            selectedCharacterText.text = $"Персонаж: {resolvedName}";
        }

        private void EnsurePlayerPreview()
        {
            var preview = GetComponent<MainMenuPlayerPreview>();
            if (preview == null)
            {
                preview = gameObject.AddComponent<MainMenuPlayerPreview>();
            }

            preview.SetAllowPreview(false);
        }

        private void RefreshNicknameEditor()
        {
            var editor = GetComponent<MainMenuNicknameEditor>();
            if (editor != null)
            {
                editor.Configure(this, profileApiClient, uiSound);
            }

            var leaderboard = GetComponent<MainMenuLeaderboardPanel>();
            if (leaderboard != null)
            {
                leaderboard.Configure(this, profileApiClient);
                leaderboard.RequestRefresh();
            }
        }

        private void RefreshPlayerPreview(bool allow)
        {
            var preview = GetComponent<MainMenuPlayerPreview>();
            if (preview != null)
            {
                preview.SetAllowPreview(allow);
            }
        }

        private void EnsureAmbience()
        {
            if (GetComponent<MainMenuAmbienceController>() == null)
            {
                gameObject.AddComponent<MainMenuAmbienceController>();
            }
        }

        private void EnsureSections()
        {
            var layout = GetComponent<MainMenuUiLayout>();
            layout?.ConfigureSections(this);
        }

        private void EnsureCameraMotion()
        {
            MainMenuCameraMotion.Resolve();
        }

        private static string BuildLocalPlayerId()
        {
            return PlayerIdentityService.GetOrCreatePlayerId();
        }

        private void EnsureDependencies()
        {
            if (networkLauncher == null)
            {
                networkLauncher = FindObjectOfType<NetworkLauncher>();
            }

            if (queueApiClient == null)
            {
                queueApiClient = FindObjectOfType<QueueApiClient>();
            }

            if (profileApiClient == null)
            {
                profileApiClient = FindObjectOfType<PlayerProfileApiClient>();
            }

            if (queueApiClient == null && networkLauncher != null && !Application.isBatchMode)
            {
                queueApiClient = networkLauncher.GetComponent<QueueApiClient>();
                if (queueApiClient == null)
                {
                    queueApiClient = networkLauncher.gameObject.AddComponent<QueueApiClient>();
                    Debug.Log("[MainMenuController] QueueApiClient auto-created on NetworkLauncher object.");
                }
            }

            if (profileApiClient == null && networkLauncher != null && !Application.isBatchMode)
            {
                profileApiClient = networkLauncher.GetComponent<PlayerProfileApiClient>();
                if (profileApiClient == null)
                {
                    profileApiClient = networkLauncher.gameObject.AddComponent<PlayerProfileApiClient>();
                }
            }

            if (profileApiClient == null && !Application.isBatchMode)
            {
                profileApiClient = GetComponent<PlayerProfileApiClient>();
                if (profileApiClient == null)
                {
                    profileApiClient = gameObject.AddComponent<PlayerProfileApiClient>();
                }
            }

            if (networkConfig == null && networkLauncher != null)
            {
                networkConfig = networkLauncher.Config;
            }

            if (queueApiClient != null)
            {
                var baseUrl = networkConfig != null ? networkConfig.QueueApiBaseUrl : "http://127.0.0.1:5050";
                var timeout = networkConfig != null ? networkConfig.QueueRequestTimeoutSeconds : 5f;
                queueApiClient.Configure(baseUrl, timeout);
                profileApiClient?.Configure(baseUrl, timeout);
            }
        }

        private float GetQueuePollInterval()
        {
            if (networkConfig != null)
            {
                return Mathf.Max(0.1f, networkConfig.QueuePollIntervalSeconds);
            }

            return 0.5f;
        }

        private IEnumerator SendDequeueBestEffort(string ticketId)
        {
            if (queueApiClient == null || string.IsNullOrWhiteSpace(ticketId))
            {
                yield break;
            }

            yield return StartCoroutine(queueApiClient.Dequeue(ticketId, (_, __, ___) => { }));
        }

        private IEnumerator SendLeaveMatchBestEffort(string ticketId)
        {
            if (queueApiClient == null || string.IsNullOrWhiteSpace(ticketId))
            {
                yield break;
            }

            yield return StartCoroutine(queueApiClient.LeaveMatch(ticketId, (_, __, ___) => { }));
        }

        private void TryLoadGameScene()
        {
            var targetScene = ActiveMatchContext.ResolveGameSceneName(gameSceneName, duelSceneName);
            if (string.IsNullOrWhiteSpace(targetScene))
            {
                SetStatus("Сцена матча не задана. Остаемся в MainMenu.");
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(targetScene))
            {
                SetStatus($"Сцена '{targetScene}' не найдена в Build Settings.");
                return;
            }

            SetStatus($"Загрузка сцены '{targetScene}'...");
            SceneManager.LoadScene(targetScene);
        }

        private IEnumerator ConnectAndEnterGameRoutine(string address, int port)
        {
            var connectTask = networkLauncher.ConnectToServerAsync(address, port);
            while (!connectTask.IsCompleted)
            {
                yield return null;
            }

            if (connectTask.IsFaulted)
            {
                Debug.LogWarning($"[MainMenuController] Connect task faulted: {connectTask.Exception?.GetBaseException().Message}");
            }

            var connected = networkLauncher != null && networkLauncher.IsClientConnected;
            if (!connected)
            {
                if (queueApiClient != null && networkLauncher != null && !string.IsNullOrWhiteSpace(networkLauncher.CurrentTicketId))
                {
                    yield return StartCoroutine(SendLeaveMatchBestEffort(networkLauncher.CurrentTicketId));
                }

                if (networkLauncher != null && !string.IsNullOrWhiteSpace(networkLauncher.LastConnectionError))
                {
                    SetStatus($"{connectionFailedStatusText} ({networkLauncher.LastConnectionError})");
                }
                else
                {
                    SetStatus(connectionFailedStatusText);
                }

                networkLauncher?.ClearMatchContext();
                SetStartButtonState(isQueueing: false, interactable: true);
                yield break;
            }

            SetStatus(connectedStatusText);
            SetStartButtonState(isQueueing: false, interactable: true);
            if (autoLoadGameSceneOnSuccess)
            {
                TryLoadGameScene();
            }
        }
    }
}
