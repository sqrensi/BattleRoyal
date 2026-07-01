using System.Collections;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using ShooterPrototype.Platform;
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
        [SerializeField] private string connectionFailedStatusText = "Не удалось подключиться к серверу.";
        [SerializeField] private string connectedStatusText = "Подключение успешно.";
        [SerializeField] private string queueCancelledStatusText = "Поиск матча отменен.";

        [Header("Scene Flow")]
        [SerializeField] private bool autoLoadGameSceneOnSuccess = true;
        [SerializeField] private string gameSceneName = "Game";
        [SerializeField] private string duelSceneName = "1x1";
        [SerializeField] private string deathmatchSceneName = "dm";
        [SerializeField] private string trainingSceneName = "training";
        [SerializeField] private string challengeSceneName = "challenge";

        [Header("Reliability")]
        [SerializeField] private int enqueueRetryCount = 2;
        [SerializeField] private float enqueueRetryDelaySeconds = 0.4f;
        [Tooltip("1v1 online: if no human match within this many seconds, start local offline bot duel.")]
        [SerializeField] private float duelBotFallbackQueueSeconds = 5f;

        private Coroutine queuePollingCoroutine;
        private Coroutine profileSyncCoroutine;
        private bool isQueueing;
        private string currentTicketId = string.Empty;
        private float queueSearchStartedAtUnscaled;
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
            RefreshServerSyncUiState();
        }

        private void Start()
        {
            if (ConnectionRecoveryState.TryConsumePending(out var recoveryMessage))
            {
                HandleServerUnavailable(recoveryMessage);
            }

            BeginProfileSync(isManualRetry: false);
        }

        private void OnEnable()
        {
            EnsureDependencies();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            PlayerProfileService.ProfileSynced += HandleProfileSynced;

            if (networkLauncher == null)
            {
                Debug.LogWarning("[MainMenuController] NetworkLauncher is not set.");
                return;
            }

            networkLauncher.StatusChanged += HandleStatusChanged;

            SetStatus(idleStatusText);
            SetStartButtonState(isQueueing: false, interactable: true);
            RefreshSelectedCharacterLabel();
            RefreshServerSyncUiState();
            if (PlayerProfileService.IsServerSynced)
            {
                RefreshPlayerPreview(true);
            }
        }

        private void OnDisable()
        {
            PlayerProfileService.ProfileSynced -= HandleProfileSynced;

            if (profileSyncCoroutine != null)
            {
                StopCoroutine(profileSyncCoroutine);
                profileSyncCoroutine = null;
            }

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

        private void HandleProfileSynced()
        {
            RefreshServerSyncUiState();
            if (!PlayerProfileService.IsServerSynced)
            {
                return;
            }

            ApplyServerConnectionState(MainMenuServerConnectionState.Connected);
            RefreshPlayerPreview(true);
            RefreshNicknameEditor();
            SetStatus(idleStatusText);
        }

        public void OnChangeCharacterPressed()
        {
            if (!PlayerProfileService.IsServerSynced)
            {
                SetStatus("Смена персонажа доступна после подключения к серверу.");
                return;
            }

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

            var selectedMode = MainMenuGameModeSelector.SelectedMode;
            if (!PlayerProfileService.IsServerSynced)
            {
                uiSound?.PlayStart();
                if (selectedMode == MainMenuGameMode.Challenge)
                {
                    StartOfflineChallenge();
                }
                else if (selectedMode == MainMenuGameMode.Training)
                {
                    StartOfflineTraining();
                }
                else if (selectedMode == MainMenuGameMode.Deathmatch)
                {
                    StartOfflineDeathmatch();
                }
                else if (selectedMode == MainMenuGameMode.Duel1v1)
                {
                    StartOfflineDuelWithBot();
                }

                return;
            }

            if (MainMenuGameModeUtility.IsOfflineSoloMode(selectedMode))
            {
                uiSound?.PlayStart();
                if (selectedMode == MainMenuGameMode.Challenge)
                {
                    StartOfflineChallenge();
                }
                else
                {
                    StartOfflineTraining();
                }

                return;
            }

            if (!PlayerProfileService.IsServerSynced)
            {
                SetStatus("Онлайн режимы доступны после подключения к серверу.");
                return;
            }

            if (selectedMode != MainMenuGameMode.Duel1v1 &&
                selectedMode != MainMenuGameMode.Deathmatch)
            {
                SetStatus("Онлайн доступны режимы «1 на 1» и «Дэзматч».");
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
            if (!string.IsNullOrWhiteSpace(message))
            {
                Debug.Log($"[MainMenuController][NetworkLauncher] {message}");
            }
        }

        private void SetStatus(string message, bool showInUi = true)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Debug.Log($"[MainMenuController] {message}");
            }

            if (statusText == null)
            {
                return;
            }

            var shouldShow = showInUi &&
                             !string.IsNullOrWhiteSpace(message) &&
                             !string.Equals(message, idleStatusText, System.StringComparison.Ordinal);

            if (!shouldShow)
            {
                statusText.text = string.Empty;
                statusText.gameObject.SetActive(false);
                return;
            }

            statusText.text = message;
            statusText.gameObject.SetActive(true);
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
            var realtimeClient = RealtimeTransportClient.Active ?? FindObjectOfType<RealtimeTransportClient>();
            realtimeClient?.EndMatchSession();
            networkLauncher?.DisconnectClient("Preparing queue search.");

            isQueueing = true;
            SetStartButtonState(isQueueing: true, interactable: true);
            SetStatus(searchingStatusText);

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
                HandleServerUnavailable("Сервер недоступен");
                SetStartButtonState(isQueueing: false, interactable: true);
                yield break;
            }

            RefreshNicknameEditor();

            if (queueApiClient != null && networkLauncher != null && !string.IsNullOrWhiteSpace(networkLauncher.CurrentTicketId))
            {
                yield return StartCoroutine(SendLeaveMatchBestEffort(networkLauncher.CurrentTicketId));
                networkLauncher.ClearMatchContext();
            }

            SetStartButtonState(isQueueing: true, interactable: false);

            var enqueueCompleted = false;
            var enqueueOk = false;
            QueueEnqueueResponse enqueueResponse = null;
            var enqueueError = string.Empty;

            ActiveMatchContext.SetMode(MainMenuGameModeSelector.SelectedMode);
            ActiveMatchContext.SetOfflineDuelSession(false);
            ActiveMatchContext.SetOfflineTrainingSession(false);
            ActiveMatchContext.SetOfflineChallengeSession(false);
            ActiveMatchContext.SetOfflineDeathmatchSession(false);

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
                    SetStatus($"{searchingStatusText} (повтор {attempt}/{attempts - 1})");
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
            queueSearchStartedAtUnscaled = Time.unscaledTime;
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

                    if (ClientSettingsService.AllowBotMatchmaking &&
                        duelBotFallbackQueueSeconds > 0f &&
                        MainMenuGameModeSelector.SelectedMode == MainMenuGameMode.Duel1v1 &&
                        Time.unscaledTime - queueSearchStartedAtUnscaled >= duelBotFallbackQueueSeconds)
                    {
                        yield return StartCoroutine(StartOfflineDuelAfterQueueTimeoutRoutine());
                        yield break;
                    }
                }
                else if (status.Equals("Matched", System.StringComparison.OrdinalIgnoreCase))
                {
                    isQueueing = false;
                    queuePollingCoroutine = null;
                    currentTicketId = string.Empty;
                    SetStartButtonState(isQueueing: false, interactable: false);
                    ActiveMatchContext.SetOfflineDuelSession(false);
                    ActiveMatchContext.SetOfflineTrainingSession(false);
                    ActiveMatchContext.SetOfflineChallengeSession(false);
                    if (!string.IsNullOrWhiteSpace(statusResponse.matchMode))
                    {
                        ActiveMatchContext.SetMode(
                            MainMenuGameModeUtility.FromApiValue(statusResponse.matchMode));
                    }
                    else
                    {
                        ActiveMatchContext.SetMode(MainMenuGameModeSelector.SelectedMode);
                    }
                    var playerCount = Mathf.Max(1, statusResponse.matchedPlayerCount);
                    networkLauncher.SetMatchContext(statusResponse.matchId, playerCount, statusResponse.ticketId);
                    SetStatus(connectingStatusText);
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
            BeginProfileSync(isManualRetry: true);
        }

        private void BeginProfileSync(bool isManualRetry)
        {
            if (profileSyncCoroutine != null)
            {
                StopCoroutine(profileSyncCoroutine);
                profileSyncCoroutine = null;
            }

            EnsureDependencies();
            localPlayerId = PlayerIdentityService.GetOrCreatePlayerId();
            RefreshReconnectButton(PlayerProfileService.IsServerSynced, interactable: false);
            ApplyServerConnectionState(
                MainMenuServerConnectionState.Loading,
                isManualRetry ? "Переподключение к серверу..." : "Подключение к серверу...");
            profileSyncCoroutine = StartCoroutine(SyncProfileRoutine(isManualRetry));
        }

        private void RefreshReconnectButton(bool serverSynced, bool? interactable = null)
        {
            var reconnect = GetComponent<MainMenuReconnectButton>();
            if (reconnect == null)
            {
                reconnect = GetComponent<MainMenuUiLayout>()?.ReconnectButton;
            }

            if (reconnect == null)
            {
                return;
            }

            reconnect.SetVisible(!serverSynced);
            reconnect.SetInteractable(interactable ?? !serverSynced);
        }

        private IEnumerator SyncProfileRoutine(bool isManualRetry)
        {
            try
            {
                EnsureDependencies();
                RefreshReconnectButton(PlayerProfileService.IsServerSynced, interactable: false);

                if (profileApiClient == null)
                {
                    HandleServerUnavailable("Сервер недоступен");
                    yield break;
                }

                ApplyServerConnectionState(MainMenuServerConnectionState.Loading, "Авторизация...");
                yield return YandexGamesIntegrationService.PrepareAccountAndBindProfile(
                    this,
                    playerId => localPlayerId = playerId);
                ApplyServerConnectionState(MainMenuServerConnectionState.Loading, "Загрузка профиля...");
                yield return SyncProfileWithRetry(isManualRetry ? 4 : 2, isManualRetry ? 2f : 1f);

                YandexGamesIntegrationService.NotifyMainMenuLoadingComplete();

                if (PlayerProfileService.IsServerSynced)
                {
                    SetStatus(idleStatusText);
                    ApplyServerConnectionState(MainMenuServerConnectionState.Connected);
                    RefreshPlayerPreview(true);
                    RefreshNicknameEditor();
                    yield break;
                }

                SetStatus("Сервер недоступен. Доступны тренировка и челлендж.");
                HandleServerUnavailable(ResolveServerUnavailableMessage());
                RefreshPlayerPreview(false);
            }
            finally
            {
                profileSyncCoroutine = null;
                RefreshServerSyncUiState();
            }
        }

        private void HandleServerUnavailable(string message)
        {
            var displayMessage = string.IsNullOrWhiteSpace(message)
                ? "Сервер недоступен"
                : message;
            SetStatus(displayMessage);
            ApplyServerConnectionState(MainMenuServerConnectionState.Unavailable, displayMessage);
        }

        private string ResolveServerUnavailableMessage()
        {
            if (networkConfig != null && networkConfig.IsQueueApiMixedContentBlocked())
            {
                return "Нужен HTTPS-адрес API в NetworkConfig (Queue Api Base Url Secure).";
            }

            if (!string.IsNullOrWhiteSpace(PlayerProfileService.LastSyncError))
            {
                return $"Сервер недоступен: {PlayerProfileService.LastSyncError}";
            }

            return "Сервер недоступен";
        }

        private IEnumerator SyncProfileWithRetry(int attempts, float retryDelaySeconds)
        {
            attempts = Mathf.Max(1, attempts);
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                EnsureDependencies();
                localPlayerId = PlayerIdentityService.GetOrCreatePlayerId();

                yield return PlayerProfileService.SyncProfile(
                    this,
                    profileApiClient,
                    localPlayerId,
                    fallbackToLocalOnFailure: false);

                if (PlayerProfileService.IsServerSynced)
                {
                    yield break;
                }

                if (attempt < attempts)
                {
                    yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, retryDelaySeconds));
                }
            }
        }

        private void ApplyServerConnectionState(MainMenuServerConnectionState state, string message = null)
        {
            connectionGate?.SetState(state, message);
        }

        private void RefreshServerSyncUiState()
        {
            var synced = PlayerProfileService.IsServerSynced;
            GetComponent<MainMenuGameModeSelector>()?.SetOnlineModesRestricted(!synced);
            GetComponent<MainMenuSectionController>()?.SetServerSyncRestrictions(synced);

            if (changeCharacterButton != null)
            {
                changeCharacterButton.interactable = synced;
            }

            RefreshReconnectButton(synced);

            var leaderboard = GetComponent<MainMenuLeaderboardPanel>();
            if (leaderboard?.RootRect != null)
            {
                leaderboard.RootRect.gameObject.SetActive(true);
            }

            var achievementsPanel = GetComponent<MainMenuAchievementsPanel>();
            achievementsPanel?.RefreshFromProfile();

            GetComponent<MainMenuSettingsPanel>()?.RefreshFromSettings();
        }

        private void StartOfflineTraining()
        {
            ActiveMatchContext.SetMode(MainMenuGameMode.Training);
            ActiveMatchContext.SetOfflineTrainingSession(true);
            LoadingScreenOverlay.Show("Загрузка тренировки...");
            StartCoroutine(LoadGameSceneRoutine());
        }

        private void StartOfflineChallenge()
        {
            ActiveMatchContext.SetMode(MainMenuGameMode.Challenge);
            ActiveMatchContext.SetOfflineChallengeSession(true);
            LoadingScreenOverlay.Show("Загрузка челленджа...");
            StartCoroutine(LoadGameSceneRoutine());
        }

        private void StartOfflineDeathmatch()
        {
            var realtimeClient = RealtimeTransportClient.Active ?? FindFirstObjectByType<RealtimeTransportClient>();
            realtimeClient?.EndMatchSession();
            networkLauncher?.DisconnectClient("Starting offline deathmatch.");
            ActiveMatchContext.SetMode(MainMenuGameMode.Deathmatch);
            ActiveMatchContext.SetOfflineDeathmatchSession(true);
            LoadingScreenOverlay.Show("Загрузка дэзматча...");
            StartCoroutine(LoadGameSceneRoutine());
        }

        private void StartOfflineDuelWithBot()
        {
            var realtimeClient = RealtimeTransportClient.Active ?? FindFirstObjectByType<RealtimeTransportClient>();
            realtimeClient?.EndMatchSession();
            networkLauncher?.DisconnectClient("Starting offline duel bot.");
            ActiveMatchContext.SetMode(MainMenuGameMode.Duel1v1);
            ActiveMatchContext.SetOfflineDuelSession(true);
            LoadingScreenOverlay.Show("Загрузка дуэли...");
            StartCoroutine(LoadGameSceneRoutine());
        }

        private IEnumerator StartOfflineDuelAfterQueueTimeoutRoutine()
        {
            var ticketId = currentTicketId;
            isQueueing = false;
            queuePollingCoroutine = null;
            currentTicketId = string.Empty;
            SetStartButtonState(isQueueing: false, interactable: false);

            if (!string.IsNullOrWhiteSpace(ticketId))
            {
                yield return StartCoroutine(SendDequeueBestEffort(ticketId));
            }

            networkLauncher?.DisconnectClient("Queue timeout, starting offline duel bot.");
            var realtimeClient = RealtimeTransportClient.Active ?? FindFirstObjectByType<RealtimeTransportClient>();
            realtimeClient?.EndMatchSession();
            SetStatus("Соперник не найден. Запуск бота...");
            StartOfflineDuelWithBot();
        }

        public void RefreshLeaderboardForSelectedMode()
        {
            var leaderboard = GetComponent<MainMenuLeaderboardPanel>();
            if (leaderboard != null)
            {
                leaderboard.SetLeaderboardMode(MainMenuGameModeSelector.SelectedMode);
            }

            var editor = GetComponent<MainMenuNicknameEditor>();
            editor?.RefreshFromProfile();
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

            preview.SetAllowPreview(true);
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

        private void RefreshPlayerPreview(bool applyServerProfile)
        {
            var preview = GetComponent<MainMenuPlayerPreview>();
            if (preview == null)
            {
                return;
            }

            preview.SetAllowPreview(true);
            if (applyServerProfile)
            {
                preview.RefreshSkins();
            }
            else
            {
                preview.Refresh();
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

            if (queueApiClient == null)
            {
                queueApiClient = FindObjectOfType<QueueApiClient>();
            }

            if (networkLauncher != null && !Application.isBatchMode)
            {
                var launcherQueueClient = networkLauncher.GetComponent<QueueApiClient>();
                if (launcherQueueClient == null)
                {
                    launcherQueueClient = networkLauncher.gameObject.AddComponent<QueueApiClient>();
                    Debug.Log("[MainMenuController] QueueApiClient auto-created on NetworkLauncher object.");
                }

                queueApiClient = launcherQueueClient;

                var launcherProfileClient = networkLauncher.GetComponent<PlayerProfileApiClient>();
                if (launcherProfileClient == null)
                {
                    launcherProfileClient = networkLauncher.gameObject.AddComponent<PlayerProfileApiClient>();
                }

                profileApiClient = launcherProfileClient;
            }
            else
            {
                if (profileApiClient == null)
                {
                    profileApiClient = FindObjectOfType<PlayerProfileApiClient>();
                }

                if (profileApiClient == null && !Application.isBatchMode)
                {
                    profileApiClient = GetComponent<PlayerProfileApiClient>();
                    if (profileApiClient == null)
                    {
                        profileApiClient = gameObject.AddComponent<PlayerProfileApiClient>();
                    }
                }
            }

            if (networkConfig == null && networkLauncher != null)
            {
                networkConfig = networkLauncher.Config;
            }

            var baseUrl = networkConfig != null
                ? networkConfig.ResolveQueueApiBaseUrl()
                : "http://127.0.0.1:5050";
            var timeout = networkConfig != null ? networkConfig.QueueRequestTimeoutSeconds : 5f;
            queueApiClient?.Configure(baseUrl, timeout);
            profileApiClient?.Configure(baseUrl, timeout);
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
            StartCoroutine(LoadGameSceneRoutine());
        }

        private IEnumerator LoadGameSceneRoutine()
        {
            ActiveMatchContext.PrepareRandomMapForCurrentMode(duelSceneName, deathmatchSceneName);
            var targetScene = ActiveMatchContext.ResolveGameSceneName(
                gameSceneName,
                duelSceneName,
                trainingSceneName,
                challengeSceneName,
                deathmatchSceneName);
            if (string.IsNullOrWhiteSpace(targetScene))
            {
                LoadingScreenOverlay.Hide();
                SetStatus("Сцена матча не задана. Остаемся в MainMenu.");
                yield break;
            }

            if (!Application.CanStreamedLevelBeLoaded(targetScene))
            {
                LoadingScreenOverlay.Hide();
                SetStatus($"Сцена '{targetScene}' не найдена в Build Settings.");
                yield break;
            }

            LoadingScreenOverlay.SetMessage("Загрузка матча...");
            SetStatus($"Загрузка сцены '{targetScene}'...");

            var loadOperation = SceneManager.LoadSceneAsync(targetScene);
            loadOperation.allowSceneActivation = false;
            while (loadOperation.progress < 0.9f)
            {
                LoadingScreenOverlay.SetProgress(loadOperation.progress / 0.9f);
                yield return null;
            }

            LoadingScreenOverlay.SetProgress(1f);
            LoadingScreenOverlay.SetMessage("Подготовка матча...");
            loadOperation.allowSceneActivation = true;
            while (!loadOperation.isDone)
            {
                yield return null;
            }
        }

        private IEnumerator ConnectAndEnterGameRoutine(string address, int port)
        {
            LoadingScreenOverlay.Show(connectingStatusText);
            ActiveMatchContext.SetOfflineDuelSession(false);
            ActiveMatchContext.SetOfflineTrainingSession(false);
            ActiveMatchContext.SetOfflineChallengeSession(false);
            ActiveMatchContext.SetOfflineDeathmatchSession(false);

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
                LoadingScreenOverlay.Hide();
                if (queueApiClient != null && networkLauncher != null && !string.IsNullOrWhiteSpace(networkLauncher.CurrentTicketId))
                {
                    yield return StartCoroutine(SendLeaveMatchBestEffort(networkLauncher.CurrentTicketId));
                }

                SetStatus(connectionFailedStatusText);

                networkLauncher?.ClearMatchContext();
                SetStartButtonState(isQueueing: false, interactable: true);
                yield break;
            }

            var ticketId = networkLauncher != null ? networkLauncher.CurrentTicketId : string.Empty;
            if (!string.IsNullOrWhiteSpace(ticketId))
            {
                var realtimeClient = RealtimeTransportClient.Active ?? FindFirstObjectByType<RealtimeTransportClient>();
                if (realtimeClient == null && networkLauncher != null)
                {
                    realtimeClient = networkLauncher.GetComponent<RealtimeTransportClient>();
                    if (realtimeClient == null)
                    {
                        realtimeClient = networkLauncher.gameObject.AddComponent<RealtimeTransportClient>();
                    }
                }

                if (realtimeClient != null)
                {
                    var wsUrl = networkConfig != null
                        ? networkConfig.ResolveRealtimeWsUrl()
                        : (networkLauncher != null && networkLauncher.Config != null
                            ? networkLauncher.Config.ResolveRealtimeWsUrl()
                            : "ws://127.0.0.1:5051");
                    realtimeClient.Configure(wsUrl);
                    realtimeClient.BeginMatchSession(ticketId);

                    var wsDeadline = Time.unscaledTime + 12f;
                    while (!realtimeClient.IsReady && Time.unscaledTime < wsDeadline)
                    {
                        realtimeClient.EnsureConnected();
                        yield return null;
                    }

                    if (!realtimeClient.IsReady)
                    {
                        LoadingScreenOverlay.Hide();
                        realtimeClient.EndMatchSession();
                        if (queueApiClient != null)
                        {
                            yield return StartCoroutine(SendLeaveMatchBestEffort(ticketId));
                        }

                        SetStatus("Не удалось подключиться к матчу.");
                        networkLauncher?.ClearMatchContext();
                        SetStartButtonState(isQueueing: false, interactable: true);
                        yield break;
                    }
                }
            }

            SetStatus(connectedStatusText);
            SetStartButtonState(isQueueing: false, interactable: true);
            if (autoLoadGameSceneOnSuccess)
            {
                yield return StartCoroutine(LoadGameSceneRoutine());
            }
            else
            {
                LoadingScreenOverlay.Hide();
            }
        }
    }
}
