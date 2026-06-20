using System.Collections;
using ShooterPrototype.Bootstrap;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace ShooterPrototype.UI
{
    public sealed class GameHudController : MonoBehaviour
    {
        private const string CanvasObjectName = "RuntimeGameHudCanvas";
        public const string RuntimeCanvasObjectName = CanvasObjectName;
        private const string MutePrefKey = "client_audio_muted";
        private const int CornerStatsLayoutVersion = 2;
        private const int MatchCornerStatsLayoutVersion = 2;
        private const float GameOverPanelDelaySeconds = 5f;
        private const float GameOverAutoExitSeconds = 15f;
        private const int GameOverPanelLayoutVersion = 5;
        private const int GameplayHintLayoutVersion = 2;
        private const int MatchWaitStatusLayoutVersion = 3;
        private const float GameplayHintOffsetX = 72f;
        private const float GameplayHintOffsetY = -48f;

        private NetworkLauncher networkLauncher;
        private QueueApiClient queueApiClient;
        private PerformancePresetController performancePreset;
        private string mainMenuSceneName = "MainMenu";

        private Canvas canvas;
        private GameObject topBarPanel;
        private bool topBarVisible;
        private CombatHudController combatHud;
        private GameKillFeedController killFeed;
        private Text connectionText;
        private Text playersText;
        private Text pingText;
        private Text fpsText;
        private Text cornerStatsText;
        private Text matchCornerStatsText;
        private int matchCornerKillCount;
        private int matchCornerAliveCount;
        private int displayPingMs = -1;
        private string displayPingLabel = "--";
        private Text ammoText;
        private Text healthText;
        private Text medkitText;
        private Text killsText;
        private Text inventoryText;
        private GameObject legacyInventoryPanel;
        private Text matchStatusText;
        private GameObject gameplayHintPanel;
        private Text gameplayHintText;
        private string brGameplayHint = string.Empty;
        private string pickupGameplayHint = string.Empty;
        private Text victoryBannerText;
        private Text victorySubtitleText;
        private GameObject gameOverPanel;
        private CanvasGroup gameOverPanelGroup;
        private Text gameOverTitleText;
        private Text gameOverPlacementText;
        private Text gameOverKillsText;
        private Text gameOverRewardsText;
        private Text gameOverHintText;
        private Image gameOverPanelBackground;
        private Button gameOverExitButton;
        private Coroutine gameOverFlowCoroutine;
        private bool gameOverFlowStarted;
        private bool gameOverPanelVisible;
        private MatchOutcomeSummary pendingMatchOutcome;
        private bool matchRewardGranted;
        private Coroutine matchRewardCoroutine;
        private bool matchStatsReported;
        private Coroutine matchStatsCoroutine;
        private bool gameOverInputLocked;
        private GameObject pauseMenuPanel;
        private GameObject pauseSettingsPanel;
        private Text pauseMuteButtonLabel;
        private Text pausePerfButtonLabel;
        private bool pauseMenuOpen;
        private bool pauseSettingsOpen;
        private Button backButton;
        private Button muteButton;
        private Button perfButton;
        private Text muteButtonLabel;
        private Text perfButtonLabel;
        private Image perfButtonImage;
        private Coroutine pingRefreshCoroutine;
        private float fpsSmoothed;

        public static void SetLegacyInventoryVisible(bool visible)
        {
            var hud = FindFirstObjectByType<GameHudController>();
            if (hud != null && hud.legacyInventoryPanel != null)
            {
                hud.legacyInventoryPanel.SetActive(false);
            }
        }
        private int consecutivePingFailures;
        private bool returnToMenuRequested;
        private bool isMuted;

        public void Initialize(NetworkLauncher launcher, string menuSceneName, PerformancePresetController presetController = null)
        {
            networkLauncher = launcher;
            performancePreset = presetController;
            mainMenuSceneName = string.IsNullOrWhiteSpace(menuSceneName) ? "MainMenu" : menuSceneName;
            queueApiClient = FindObjectOfType<QueueApiClient>();

            if (performancePreset == null)
            {
                performancePreset = FindObjectOfType<PerformancePresetController>();
            }

            if (networkLauncher != null)
            {
                networkLauncher.StatusChanged -= HandleNetworkStatusChanged;
                networkLauncher.StatusChanged += HandleNetworkStatusChanged;
            }

            EnsureHudExists();
            LoadMuteState();
            RefreshPerfButtonVisuals();
            RefreshConnectionText();
            SetTopBarVisible(false);
            SetActiveForScene(false);
        }

        private void Update()
        {
            if (canvas == null || !canvas.gameObject.activeSelf)
            {
                return;
            }

            if (ReadToggleMutePressed())
            {
                HandleMutePressed();
            }

            if (ReadEscapePressed())
            {
                HandleEscapePressed();
            }

            RefreshCornerStats();
            RefreshMatchCornerStatsText();
        }

        private static bool ReadEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private void HandleEscapePressed()
        {
            if (returnToMenuRequested || gameOverPanelVisible || gameOverFlowStarted)
            {
                return;
            }

            if (PlayerInventoryPanelController.IsOpen)
            {
                return;
            }

            if (pauseMenuOpen && pauseSettingsOpen)
            {
                SetPauseSettingsOpen(false);
                return;
            }

            SetPauseMenuOpen(!pauseMenuOpen);
        }

        private void SetPauseMenuOpen(bool open)
        {
            EnsureHudExists();
            EnsurePauseMenuPanel(canvas != null ? canvas.transform : null);
            pauseMenuOpen = open;
            if (!open)
            {
                pauseSettingsOpen = false;
                if (pauseSettingsPanel != null)
                {
                    pauseSettingsPanel.SetActive(false);
                }
            }

            if (pauseMenuPanel != null)
            {
                pauseMenuPanel.SetActive(open);
            }

            ApplyPauseCursor(open);
        }

        private void SetPauseSettingsOpen(bool open)
        {
            pauseSettingsOpen = open;
            if (pauseSettingsPanel != null)
            {
                pauseSettingsPanel.SetActive(open);
            }
        }

        private void ApplyPauseCursor(bool pauseOpen)
        {
            if (pauseOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            var fps = FindFirstObjectByType<FpsCharacterController>();
            if (fps != null && fps.isActiveAndEnabled)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void HandlePauseSettingsPressed()
        {
            RefreshPauseSettingsButtonLabels();
            SetPauseSettingsOpen(true);
        }

        private void HandlePauseSettingsBackPressed()
        {
            SetPauseSettingsOpen(false);
        }

        private void HandlePauseExitPressed()
        {
            SetPauseMenuOpen(false);
            RequestReturnToMenu("pause_exit");
        }

        private static bool ReadToggleMutePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F8);
#endif
        }

        private void SetTopBarVisible(bool visible)
        {
            topBarVisible = visible;
            if (topBarPanel != null)
            {
                topBarPanel.SetActive(visible);
            }
        }

        public void SetActiveForScene(bool isGameScene)
        {
            if (isGameScene)
            {
                EnsureEventSystemExists();
            }

            if (canvas == null)
            {
                EnsureHudExists();
            }

            if (canvas != null)
            {
                canvas.gameObject.SetActive(isGameScene);
            }

            if (isGameScene)
            {
                ResetMatchOverlay();
                RefreshConnectionText();
                RefreshPlayersText();
                RefreshPingText();
                EnsurePingRefreshRunning();
                SetTopBarVisible(topBarVisible);
                combatHud?.SetActiveForScene(true);
                killFeed?.SetActiveForScene(true);
            }
            else
            {
                StopPingRefresh();
                combatHud?.SetActiveForScene(false);
                killFeed?.SetActiveForScene(false);
            }
        }

        private void OnDestroy()
        {
            StopPingRefresh();

            if (networkLauncher != null)
            {
                networkLauncher.StatusChanged -= HandleNetworkStatusChanged;
            }
        }

        private void HandleNetworkStatusChanged(string status)
        {
            if (connectionText != null && canvas != null && canvas.gameObject.activeSelf)
            {
                connectionText.text = $"Status: {status}";
                RefreshPlayersText();
                RefreshPingText();
            }
        }

        public void SetMatchStatusMessage(string message)
        {
            if (matchStatusText == null)
            {
                EnsureHudExists();
            }

            if (matchStatusText != null)
            {
                var visible = !string.IsNullOrWhiteSpace(message);
                matchStatusText.text = visible ? message : string.Empty;
                ApplyBoldHudText(matchStatusText);
                matchStatusText.gameObject.SetActive(visible);
            }
        }

        public void SetBrGameplayHint(string hint)
        {
            brGameplayHint = hint ?? string.Empty;
            RefreshGameplayHint();
        }

        public void SetPickupGameplayHint(string hint)
        {
            pickupGameplayHint = hint ?? string.Empty;
            RefreshGameplayHint();
        }

        public void ClearGameplayHints()
        {
            brGameplayHint = string.Empty;
            pickupGameplayHint = string.Empty;
            RefreshGameplayHint();
        }

        private void RefreshGameplayHint()
        {
            if (gameplayHintText == null)
            {
                EnsureHudExists();
            }

            if (gameplayHintText == null)
            {
                return;
            }

            var text = !string.IsNullOrWhiteSpace(pickupGameplayHint)
                ? pickupGameplayHint
                : brGameplayHint;
            var visible = !string.IsNullOrWhiteSpace(text);
            gameplayHintText.text = visible ? text : string.Empty;
            ApplyBoldHudText(gameplayHintText);
            if (gameplayHintPanel != null)
            {
                gameplayHintPanel.SetActive(visible);
            }
        }

        public void SetKillCount(int killCount)
        {
            if (killsText == null)
            {
                EnsureHudExists();
            }

            if (killsText != null)
            {
                killsText.text = $"Киллы: {Mathf.Max(0, killCount)}";
            }
        }

        public void SetVictoryBanner(bool visible, string subtitle = "")
        {
            if (victoryBannerText == null)
            {
                EnsureHudExists();
            }

            if (victoryBannerText != null)
            {
                victoryBannerText.text = visible ? "ПОБЕДА" : string.Empty;
                victoryBannerText.gameObject.SetActive(visible);
            }

            if (victorySubtitleText != null)
            {
                victorySubtitleText.text = subtitle ?? string.Empty;
                victorySubtitleText.gameObject.SetActive(visible && !string.IsNullOrWhiteSpace(subtitle));
            }
        }

        public void SetMatchCornerStats(int killCount, int aliveCount)
        {
            matchCornerKillCount = Mathf.Max(0, killCount);
            matchCornerAliveCount = Mathf.Max(0, aliveCount);
            EnsureHudExists();
            RefreshMatchCornerStatsText();
        }

        private void RefreshMatchCornerStatsText()
        {
            if (matchCornerStatsText == null)
            {
                return;
            }

            matchCornerStatsText.text =
                $"Киллы: {matchCornerKillCount}\nВыживших: {matchCornerAliveCount}";
            ApplyBoldHudText(matchCornerStatsText);
        }

        public void ResetMatchOverlay()
        {
            StopGameOverFlow();
            SetVictoryBanner(false);
            SetMatchStatusMessage(string.Empty);
            SetKillCount(0);
            SetMatchCornerStats(0, 0);
            SetMatchStatusMessage(string.Empty);
            ClearGameplayHints();
            HideGameOverPanel();
            SetPauseMenuOpen(false);
            pendingMatchOutcome = default;
            matchRewardGranted = false;
            matchStatsReported = false;
            ApplyGameOverInputLock(false);
        }

        public void ScheduleGameOver(bool won, MatchOutcomeSummary summary)
        {
            if (gameOverFlowStarted)
            {
                return;
            }

            EnsureHudExists();
            pendingMatchOutcome = summary;
            gameOverFlowStarted = true;
            SetPauseMenuOpen(false);
            ClearGameplayHints();
            gameOverFlowCoroutine = StartCoroutine(GameOverFlowRoutine(won, summary));
        }

        private IEnumerator GameOverFlowRoutine(bool won, MatchOutcomeSummary summary)
        {
            yield return new WaitForSecondsRealtime(GameOverPanelDelaySeconds);

            SetVictoryBanner(false);
            SetMatchStatusMessage(string.Empty);
            ShowGameOverPanel(won, summary);

            var remaining = GameOverAutoExitSeconds;
            while (remaining > 0f)
            {
                if (returnToMenuRequested)
                {
                    yield break;
                }

                remaining -= Time.unscaledDeltaTime;
                if (gameOverHintText != null)
                {
                    gameOverHintText.text = $"Автовыход через {Mathf.CeilToInt(Mathf.Max(0f, remaining))} сек.";
                }

                yield return null;
            }

            if (!returnToMenuRequested)
            {
                RequestReturnToMenu(won ? "game_over_win" : "game_over_loss");
            }
        }

        private void ShowGameOverPanel(bool won, MatchOutcomeSummary summary)
        {
            EnsureHudExists();
            EnsureGameOverPanel(canvas != null ? canvas.transform : null);
            if (gameOverPanel == null)
            {
                return;
            }

            if (!matchRewardGranted && summary.CoinReward > 0)
            {
                matchRewardGranted = true;
                if (matchRewardCoroutine != null)
                {
                    StopCoroutine(matchRewardCoroutine);
                }

                matchRewardCoroutine = StartCoroutine(GrantMatchRewardRoutine(summary.CoinReward));
            }

            if (!matchStatsReported)
            {
                matchStatsReported = true;
                if (matchStatsCoroutine != null)
                {
                    StopCoroutine(matchStatsCoroutine);
                }

                matchStatsCoroutine = StartCoroutine(RecordMatchStatsRoutine(won, summary));
            }

            SetVictoryBanner(false);
            SetMatchStatusMessage(string.Empty);
            SetPauseMenuOpen(false);
            ApplyGameOverInputLock(true);

            gameOverPanelVisible = true;
            gameOverPanel.SetActive(true);
            gameOverPanel.transform.SetAsLastSibling();
            if (gameOverPanelGroup != null)
            {
                gameOverPanelGroup.alpha = 1f;
                gameOverPanelGroup.interactable = true;
                gameOverPanelGroup.blocksRaycasts = true;
            }

            var accentColor = won
                ? new Color(0.95f, 0.82f, 0.35f, 1f)
                : new Color(0.9f, 0.45f, 0.45f, 1f);

            if (gameOverPanelBackground != null)
            {
                gameOverPanelBackground.color = new Color(0.09f, 0.11f, 0.13f, 0.97f);
            }

            if (gameOverTitleText != null)
            {
                gameOverTitleText.text = won ? "Вы выиграли" : "Вы проиграли";
                gameOverTitleText.color = accentColor;
            }

            ApplyGameOverSummary(
                summary,
                MatchRatingUtility.CalculateDelta(summary.Placement, summary.KillCount));

            if (gameOverHintText != null)
            {
                gameOverHintText.text =
                    $"Автовыход через {Mathf.CeilToInt(GameOverAutoExitSeconds)} сек";
            }

            if (gameOverExitButton != null)
            {
                gameOverExitButton.interactable = true;
            }
        }

        private IEnumerator GrantMatchRewardRoutine(int amount)
        {
            var playerId = PlayerIdentityService.GetOrCreatePlayerId();
            var sourceId = ResolveMatchRewardSourceId();

            if (!PlayerProfileService.TryResolveApiClient(out var apiClient) ||
                string.IsNullOrWhiteSpace(playerId))
            {
                PlayerCurrencyService.AddCurrency(amount);
                matchRewardCoroutine = null;
                yield break;
            }

            var success = false;
            yield return PlayerProfileService.GrantMatchReward(
                this,
                apiClient,
                playerId,
                amount,
                sourceId,
                (ok, _) => success = ok);

            if (!success)
            {
                PlayerCurrencyService.AddCurrency(amount);
            }

            matchRewardCoroutine = null;
        }

        private IEnumerator RecordMatchStatsRoutine(bool won, MatchOutcomeSummary summary)
        {
            var playerId = PlayerIdentityService.GetOrCreatePlayerId();
            var sourceId = ResolveMatchRewardSourceId();
            var deaths = won ? 0 : 1;
            var damageDealt = MatchStatsTracker.DamageDealtThisMatch;
            var fallbackDelta = MatchRatingUtility.CalculateDelta(summary.Placement, summary.KillCount);

            if (!PlayerProfileService.TryResolveApiClient(out var apiClient) ||
                string.IsNullOrWhiteSpace(playerId))
            {
                UpdateGameOverRatingText(summary, fallbackDelta);
                matchStatsCoroutine = null;
                yield break;
            }

            yield return PlayerProfileService.RecordMatchStats(
                this,
                apiClient,
                playerId,
                sourceId,
                summary.KillCount,
                deaths,
                summary.Placement,
                won,
                damageDealt,
                (success, ratingDelta, _) =>
                {
                    UpdateGameOverRatingText(
                        summary,
                        success ? ratingDelta : fallbackDelta);
                });

            matchStatsCoroutine = null;
        }

        private void UpdateGameOverRatingText(MatchOutcomeSummary summary, int ratingDelta)
        {
            if (!gameOverPanelVisible)
            {
                return;
            }

            ApplyGameOverSummary(summary, ratingDelta);
        }

        private void ApplyGameOverSummary(MatchOutcomeSummary summary, int ratingDelta)
        {
            if (gameOverPlacementText != null)
            {
                gameOverPlacementText.text = $"ТОП {summary.Placement}";
            }

            if (gameOverKillsText != null)
            {
                gameOverKillsText.text = $"{summary.KillCount} киллов";
            }

            if (gameOverRewardsText != null)
            {
                gameOverRewardsText.text =
                    $"+{summary.CoinReward:N0} монет  ·  {MatchRatingUtility.FormatDelta(ratingDelta)} рейтинг";
            }
        }

        private string ResolveMatchRewardSourceId()
        {
            if (networkLauncher != null && !string.IsNullOrWhiteSpace(networkLauncher.CurrentTicketId))
            {
                return networkLauncher.CurrentTicketId.Trim();
            }

            return $"local-match-{System.Guid.NewGuid():N}";
        }

        private void ApplyGameOverInputLock(bool locked)
        {
            if (gameOverInputLocked == locked)
            {
                return;
            }

            gameOverInputLocked = locked;
            var marker = FindFirstObjectByType<LocalPlayerMarker>();
            if (marker == null)
            {
                if (locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                return;
            }

            var fps = marker.GetComponent<FpsCharacterController>();
            var weaponMount = marker.GetComponent<PlayerWeaponMount>();
            var battleRoyale = FindFirstObjectByType<MatchBattleRoyaleController>();

            if (locked)
            {
                fps?.SetGameOverMode(true);
                battleRoyale?.SetLocalCombatInputEnabled(false);
                weaponMount?.ForceExitAds();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            fps?.SetGameOverMode(false);
        }

        private void HideGameOverPanel()
        {
            gameOverPanelVisible = false;
            ApplyGameOverInputLock(false);
            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(false);
            }
        }

        private void StopGameOverFlow()
        {
            gameOverFlowStarted = false;
            if (gameOverFlowCoroutine != null)
            {
                StopCoroutine(gameOverFlowCoroutine);
                gameOverFlowCoroutine = null;
            }
        }

        private void HandleGameOverExitPressed()
        {
            if (gameOverExitButton != null)
            {
                gameOverExitButton.interactable = false;
            }

            RequestReturnToMenu("game_over_exit");
        }

        public void RequestReturnToMenu(string reason)
        {
            if (returnToMenuRequested)
            {
                return;
            }

            returnToMenuRequested = true;
            StopGameOverFlow();
            HideGameOverPanel();
            SetPauseMenuOpen(false);
            SetVictoryBanner(false);
            networkLauncher?.DisconnectClient(reason ?? "match ended");
            StartCoroutine(LeaveMatchAndReturnRoutine());
        }

        private void HandleBackPressed()
        {
            if (backButton != null)
            {
                backButton.interactable = false;
            }

            ResetMatchOverlay();
            StartCoroutine(LeaveMatchAndReturnRoutine());
        }

        private void HandleMutePressed()
        {
            isMuted = !isMuted;
            PlayerPrefs.SetInt(MutePrefKey, isMuted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMuteState();
            RefreshPauseSettingsButtonLabels();
        }

        private void HandlePerfPressed()
        {
            if (performancePreset == null)
            {
                performancePreset = FindObjectOfType<PerformancePresetController>();
            }

            if (performancePreset == null)
            {
                return;
            }

            performancePreset.ToggleMaxPerformance();
            RefreshPerfButtonVisuals();
            RefreshPauseSettingsButtonLabels();
        }

        private void RefreshPauseSettingsButtonLabels()
        {
            if (pauseMuteButtonLabel != null)
            {
                pauseMuteButtonLabel.text = isMuted ? "Звук: выкл" : "Звук: вкл";
            }

            if (pausePerfButtonLabel != null)
            {
                var maxPerformance = performancePreset != null && performancePreset.MaxPerformanceEnabled;
                pausePerfButtonLabel.text = maxPerformance ? "Графика: MAX" : "Графика: качество";
            }
        }

        private void LoadMuteState()
        {
            isMuted = PlayerPrefs.GetInt(MutePrefKey, 0) == 1;
            ApplyMuteState();
        }

        private void ApplyMuteState()
        {
            AudioListener.volume = isMuted ? 0f : 1f;
            RefreshMuteButtonText();
        }

        private void EnsureHudExists()
        {
            if (canvas == null)
            {
                var existingCanvas = transform.Find(CanvasObjectName);
                if (existingCanvas != null)
                {
                    canvas = existingCanvas.GetComponent<Canvas>();
                }

                if (canvas == null)
                {
                    var canvasObject = new GameObject(CanvasObjectName);
                    canvasObject.transform.SetParent(transform, false);

                    canvas = canvasObject.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 500;

                    canvasObject.AddComponent<CanvasScaler>();
                    canvasObject.AddComponent<GraphicRaycaster>();
                }

                EnsureEventSystemExists();
                BuildHudLayout(canvas.gameObject);
                EnsureCombatHud();
                EnsureKillFeed();
            }

            if (canvas != null)
            {
                EnsureCornerStatsPanel(canvas.transform);
                EnsureMatchCornerStatsPanel(canvas.transform);
                EnsureMatchOverlayElements(canvas.transform);
                EnsureGameplayHintPanel(canvas.transform);
                EnsurePauseMenuPanel(canvas.transform);
            }

            EnsureEventSystemExists();
        }

        private void EnsureCombatHud()
        {
            if (combatHud == null)
            {
                combatHud = GetComponent<CombatHudController>();
                if (combatHud == null)
                {
                    combatHud = gameObject.AddComponent<CombatHudController>();
                }
            }

            combatHud.EnsureOnCanvas(canvas);
        }

        private void EnsureKillFeed()
        {
            if (killFeed == null)
            {
                killFeed = GetComponent<GameKillFeedController>();
                if (killFeed == null)
                {
                    killFeed = gameObject.AddComponent<GameKillFeedController>();
                }
            }

            killFeed.EnsureOnCanvas(canvas);
        }

        private void BuildHudLayout(GameObject rootCanvasObject)
        {
            if (connectionText != null &&
                playersText != null &&
                pingText != null &&
                fpsText != null &&
                ammoText != null &&
                healthText != null &&
                medkitText != null &&
                inventoryText != null &&
                backButton != null &&
                muteButton != null &&
                muteButtonLabel != null)
            {
                return;
            }

            var panelObject = new GameObject("TopBar");
            topBarPanel = panelObject;
            panelObject.transform.SetParent(rootCanvasObject.transform, false);

            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.sizeDelta = new Vector2(0f, 108f);
            panelRect.anchoredPosition = Vector2.zero;

            var panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.45f);

            connectionText = CreateLabel(panelObject.transform, "ConnectionText", new Vector2(10f, -10f), "Status: Connected");
            playersText = CreateLabel(panelObject.transform, "PlayersText", new Vector2(10f, -34f), "Players in match: --");
            pingText = CreateLabel(panelObject.transform, "PingText", new Vector2(10f, -58f), "Ping: -- ms");
            fpsText = CreateLabel(panelObject.transform, "FpsText", new Vector2(10f, -82f), "FPS: --");
            ammoText = CreateLabel(panelObject.transform, "AmmoText", new Vector2(10f, -106f), "Ammo: --/--");
            healthText = CreateLabel(panelObject.transform, "HealthText", new Vector2(10f, -130f), "HP: --/--");
            medkitText = CreateLabel(panelObject.transform, "MedkitText", new Vector2(10f, -154f), "Medkits: -- [8]");
            killsText = CreateLabel(panelObject.transform, "KillsText", new Vector2(10f, -178f), "Киллы: 0");
            panelRect.sizeDelta = new Vector2(0f, 204f);

            var inventoryPanel = new GameObject("InventoryPanel");
            legacyInventoryPanel = inventoryPanel;
            inventoryPanel.transform.SetParent(rootCanvasObject.transform, false);
            var inventoryPanelRect = inventoryPanel.AddComponent<RectTransform>();
            inventoryPanelRect.anchorMin = new Vector2(0f, 0f);
            inventoryPanelRect.anchorMax = new Vector2(0f, 0f);
            inventoryPanelRect.pivot = new Vector2(0f, 0f);
            inventoryPanelRect.sizeDelta = new Vector2(240f, 180f);
            inventoryPanelRect.anchoredPosition = new Vector2(10f, 10f);
            var inventoryPanelImage = inventoryPanel.AddComponent<Image>();
            inventoryPanelImage.color = new Color(0f, 0f, 0f, 0.45f);
            inventoryText = CreateLabel(inventoryPanel.transform, "InventoryText", new Vector2(8f, -8f), "Inventory:\n  (empty)");
            var inventoryLabelRect = inventoryText.rectTransform;
            inventoryLabelRect.anchorMin = new Vector2(0f, 1f);
            inventoryLabelRect.anchorMax = new Vector2(1f, 1f);
            inventoryLabelRect.pivot = new Vector2(0f, 1f);
            inventoryLabelRect.offsetMin = new Vector2(8f, -170f);
            inventoryLabelRect.offsetMax = new Vector2(-8f, -8f);
            inventoryText.alignment = TextAnchor.UpperLeft;
            inventoryText.fontSize = 14;
            inventoryText.horizontalOverflow = HorizontalWrapMode.Wrap;
            inventoryText.verticalOverflow = VerticalWrapMode.Overflow;
            inventoryPanel.SetActive(false);

            EnsureMatchOverlayElements(rootCanvasObject.transform);

            var buttonObject = new GameObject("BackButton");
            buttonObject.transform.SetParent(panelObject.transform, false);

            var buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.sizeDelta = new Vector2(170f, 36f);
            buttonRect.anchoredPosition = new Vector2(-10f, 0f);

            var buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            backButton = buttonObject.AddComponent<Button>();
            backButton.onClick.AddListener(HandleBackPressed);

            var buttonLabel = CreateLabel(buttonObject.transform, "Label", Vector2.zero, "Back to Menu");
            var buttonLabelRect = buttonLabel.rectTransform;
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = Vector2.zero;
            buttonLabelRect.offsetMax = Vector2.zero;
            buttonLabel.alignment = TextAnchor.MiddleCenter;

            var muteObject = new GameObject("MuteButton");
            muteObject.transform.SetParent(panelObject.transform, false);

            var muteRect = muteObject.AddComponent<RectTransform>();
            muteRect.anchorMin = new Vector2(1f, 0.5f);
            muteRect.anchorMax = new Vector2(1f, 0.5f);
            muteRect.pivot = new Vector2(1f, 0.5f);
            muteRect.sizeDelta = new Vector2(170f, 36f);
            muteRect.anchoredPosition = new Vector2(-190f, 0f);

            var muteImage = muteObject.AddComponent<Image>();
            muteImage.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            muteButton = muteObject.AddComponent<Button>();
            muteButton.onClick.AddListener(HandleMutePressed);
            muteButtonLabel = CreateLabel(muteObject.transform, "Label", Vector2.zero, "");
            var muteLabelRect = muteButtonLabel.rectTransform;
            muteLabelRect.anchorMin = Vector2.zero;
            muteLabelRect.anchorMax = Vector2.one;
            muteLabelRect.offsetMin = Vector2.zero;
            muteLabelRect.offsetMax = Vector2.zero;
            muteButtonLabel.alignment = TextAnchor.MiddleCenter;
            RefreshMuteButtonText();

            BuildCornerStatsPanel(rootCanvasObject.transform);
        }

        private void EnsureMatchOverlayElements(Transform root)
        {
            EnsureMatchWaitStatusText(root);

            if (victoryBannerText == null)
            {
                var victoryBannerObject = new GameObject("VictoryBannerText");
                victoryBannerObject.transform.SetParent(root, false);
                var victoryBannerRect = victoryBannerObject.AddComponent<RectTransform>();
                victoryBannerRect.anchorMin = new Vector2(0.5f, 0.5f);
                victoryBannerRect.anchorMax = new Vector2(0.5f, 0.5f);
                victoryBannerRect.pivot = new Vector2(0.5f, 0.5f);
                victoryBannerRect.sizeDelta = new Vector2(720f, 96f);
                victoryBannerRect.anchoredPosition = new Vector2(0f, 40f);
                victoryBannerText = victoryBannerObject.AddComponent<Text>();
                victoryBannerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                victoryBannerText.fontSize = 64;
                victoryBannerText.fontStyle = FontStyle.Bold;
                victoryBannerText.alignment = TextAnchor.MiddleCenter;
                victoryBannerText.color = new Color(1f, 0.84f, 0.2f, 1f);
                victoryBannerText.text = string.Empty;
                victoryBannerObject.SetActive(false);
            }

            if (victorySubtitleText == null)
            {
                var victorySubtitleObject = new GameObject("VictorySubtitleText");
                victorySubtitleObject.transform.SetParent(root, false);
                var victorySubtitleRect = victorySubtitleObject.AddComponent<RectTransform>();
                victorySubtitleRect.anchorMin = new Vector2(0.5f, 0.5f);
                victorySubtitleRect.anchorMax = new Vector2(0.5f, 0.5f);
                victorySubtitleRect.pivot = new Vector2(0.5f, 0.5f);
                victorySubtitleRect.sizeDelta = new Vector2(640f, 36f);
                victorySubtitleRect.anchoredPosition = new Vector2(0f, -24f);
                victorySubtitleText = victorySubtitleObject.AddComponent<Text>();
                victorySubtitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                victorySubtitleText.fontSize = 20;
                victorySubtitleText.alignment = TextAnchor.MiddleCenter;
                victorySubtitleText.color = Color.white;
                victorySubtitleText.text = string.Empty;
                victorySubtitleObject.SetActive(false);
            }

            EnsureGameOverPanel(root);
        }

        private void EnsureMatchWaitStatusText(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var existing = root.Find("MatchStatusText");
            if (existing != null)
            {
                var versionMarker = existing.GetComponent<MatchWaitStatusLayoutMarker>();
                if (versionMarker != null &&
                    versionMarker.Version >= MatchWaitStatusLayoutVersion &&
                    matchStatusText != null)
                {
                    ApplyBoldHudText(matchStatusText);
                    return;
                }

                matchStatusText = null;
                Destroy(existing.gameObject);
            }

            var matchStatusObject = new GameObject("MatchStatusText");
            matchStatusObject.transform.SetParent(root, false);
            matchStatusObject.AddComponent<MatchWaitStatusLayoutMarker>().Version = MatchWaitStatusLayoutVersion;

            var matchStatusRect = matchStatusObject.AddComponent<RectTransform>();
            matchStatusRect.anchorMin = new Vector2(0.5f, 0.5f);
            matchStatusRect.anchorMax = new Vector2(0.5f, 0.5f);
            matchStatusRect.pivot = new Vector2(0.5f, 0.5f);
            matchStatusRect.sizeDelta = new Vector2(900f, 72f);
            matchStatusRect.anchoredPosition = Vector2.zero;

            matchStatusText = matchStatusObject.AddComponent<Text>();
            ApplyBoldHudText(matchStatusText);
            matchStatusText.fontSize = 40;
            matchStatusText.alignment = TextAnchor.MiddleCenter;
            matchStatusText.color = new Color(0.96f, 0.97f, 0.99f, 0.98f);
            matchStatusText.text = string.Empty;
            matchStatusObject.SetActive(false);
        }

        private void EnsureGameplayHintPanel(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var existingPanel = root.Find("GameplayHintPanel");
            if (existingPanel != null)
            {
                var versionMarker = existingPanel.GetComponent<GameplayHintLayoutMarker>();
                if (versionMarker != null &&
                    versionMarker.Version >= GameplayHintLayoutVersion &&
                    gameplayHintText != null)
                {
                    ApplyBoldHudText(gameplayHintText);
                    return;
                }

                gameplayHintPanel = null;
                gameplayHintText = null;
                Destroy(existingPanel.gameObject);
            }

            BuildGameplayHintPanel(root);
        }

        private void BuildGameplayHintPanel(Transform root)
        {
            if (gameplayHintText != null)
            {
                return;
            }

            var panelObject = new GameObject("GameplayHintPanel");
            panelObject.transform.SetParent(root, false);
            panelObject.AddComponent<GameplayHintLayoutMarker>().Version = GameplayHintLayoutVersion;
            gameplayHintPanel = panelObject;

            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0f, 0.5f);
            panelRect.anchoredPosition = new Vector2(GameplayHintOffsetX, GameplayHintOffsetY);
            panelRect.sizeDelta = new Vector2(420f, 40f);

            var panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.38f);
            panelImage.raycastTarget = false;

            var labelObject = new GameObject("HintText");
            labelObject.transform.SetParent(panelObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(14f, 6f);
            labelRect.offsetMax = new Vector2(-14f, -6f);

            gameplayHintText = labelObject.AddComponent<Text>();
            ApplyBoldHudText(gameplayHintText);
            gameplayHintText.fontSize = 22;
            gameplayHintText.alignment = TextAnchor.MiddleLeft;
            gameplayHintText.color = new Color(0.94f, 0.96f, 0.98f, 0.96f);
            gameplayHintText.horizontalOverflow = HorizontalWrapMode.Overflow;
            gameplayHintText.verticalOverflow = VerticalWrapMode.Overflow;

            panelObject.SetActive(false);
        }

        private void EnsureGameOverPanel(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var existingPanel = root.Find("GameOverPanel");
            if (existingPanel != null)
            {
                var versionMarker = existingPanel.GetComponent<GameOverPanelLayoutMarker>();
                if (versionMarker != null &&
                    versionMarker.Version >= GameOverPanelLayoutVersion &&
                    gameOverPanel != null)
                {
                    return;
                }

                gameOverPanel = null;
                gameOverPanelGroup = null;
                gameOverTitleText = null;
                gameOverPlacementText = null;
                gameOverKillsText = null;
                gameOverRewardsText = null;
                gameOverHintText = null;
                gameOverExitButton = null;
                gameOverPanelBackground = null;
                Destroy(existingPanel.gameObject);
            }

            BuildGameOverPanel(root);
        }

        private void BuildGameOverPanel(Transform root)
        {
            if (gameOverPanel != null)
            {
                return;
            }

            var overlayObject = new GameObject("GameOverPanel");
            overlayObject.transform.SetParent(root, false);
            overlayObject.AddComponent<GameOverPanelLayoutMarker>().Version = GameOverPanelLayoutVersion;

            var overlayRect = overlayObject.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            var overlayImage = overlayObject.AddComponent<Image>();
            overlayImage.color = new Color(0.02f, 0.03f, 0.05f, 0.75f);
            overlayImage.raycastTarget = true;

            gameOverPanelGroup = overlayObject.AddComponent<CanvasGroup>();
            gameOverPanel = overlayObject;

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(overlayObject.transform, false);
            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(440f, 340f);

            gameOverPanelBackground = panelObject.AddComponent<Image>();
            gameOverPanelBackground.color = new Color(0.09f, 0.11f, 0.13f, 0.97f);

            var bodyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var bodyColor = new Color(0.86f, 0.9f, 0.93f, 1f);
            var rewardColor = new Color(0.92f, 0.84f, 0.55f, 1f);

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(panelObject.transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -28f);
            titleRect.sizeDelta = new Vector2(380f, 48f);
            gameOverTitleText = titleObject.AddComponent<Text>();
            gameOverTitleText.font = bodyFont;
            gameOverTitleText.fontSize = 36;
            gameOverTitleText.fontStyle = FontStyle.Bold;
            gameOverTitleText.alignment = TextAnchor.MiddleCenter;
            gameOverTitleText.text = "Вы проиграли";

            gameOverPlacementText = CreateGameOverBodyLine(
                panelObject.transform,
                "Placement",
                new Vector2(0f, -92f),
                30,
                FontStyle.Bold,
                bodyColor);

            gameOverKillsText = CreateGameOverBodyLine(
                panelObject.transform,
                "Kills",
                new Vector2(0f, -132f),
                26,
                FontStyle.Normal,
                bodyColor);

            gameOverRewardsText = CreateGameOverBodyLine(
                panelObject.transform,
                "Rewards",
                new Vector2(0f, -178f),
                22,
                FontStyle.Bold,
                rewardColor);

            var buttonObject = new GameObject("ExitButton");
            buttonObject.transform.SetParent(panelObject.transform, false);
            var buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.sizeDelta = new Vector2(260f, 48f);
            buttonRect.anchoredPosition = new Vector2(0f, 52f);

            var buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.46f, 0.4f, 1f);

            gameOverExitButton = buttonObject.AddComponent<Button>();
            gameOverExitButton.onClick.AddListener(HandleGameOverExitPressed);

            var buttonLabel = CreateLabel(buttonObject.transform, "Label", Vector2.zero, "Выйти в меню");
            var buttonLabelRect = buttonLabel.rectTransform;
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = Vector2.zero;
            buttonLabelRect.offsetMax = Vector2.zero;
            buttonLabel.fontSize = 20;
            buttonLabel.fontStyle = FontStyle.Bold;
            buttonLabel.alignment = TextAnchor.MiddleCenter;

            var hintObject = new GameObject("Hint");
            hintObject.transform.SetParent(panelObject.transform, false);
            var hintRect = hintObject.AddComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.5f, 0f);
            hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 20f);
            hintRect.sizeDelta = new Vector2(380f, 22f);
            gameOverHintText = hintObject.AddComponent<Text>();
            gameOverHintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            gameOverHintText.fontSize = 14;
            gameOverHintText.alignment = TextAnchor.MiddleCenter;
            gameOverHintText.color = new Color(0.62f, 0.68f, 0.72f, 0.9f);

            overlayObject.SetActive(false);
            overlayObject.transform.SetAsLastSibling();
        }

        private static Text CreateGameOverBodyLine(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            int fontSize,
            FontStyle fontStyle,
            Color color)
        {
            var lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(parent, false);

            var rect = lineObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(380f, fontSize + 12f);

            var text = lineObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private void EnsureCornerStatsPanel(Transform root)
        {
            var existingPanel = root.Find("CornerStatsPanel");
            if (existingPanel != null)
            {
                var versionMarker = existingPanel.GetComponent<CornerStatsLayoutMarker>();
                if (versionMarker != null && versionMarker.Version >= CornerStatsLayoutVersion && cornerStatsText != null)
                {
                    return;
                }

                cornerStatsText = null;
                Destroy(existingPanel.gameObject);
            }

            BuildCornerStatsPanel(root);
        }

        private void BuildCornerStatsPanel(Transform root)
        {
            if (cornerStatsText != null)
            {
                return;
            }

            var panelObject = new GameObject("CornerStatsPanel");
            panelObject.transform.SetParent(root, false);
            panelObject.AddComponent<CornerStatsLayoutMarker>().Version = CornerStatsLayoutVersion;

            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.sizeDelta = new Vector2(118f, 44f);
            panelRect.anchoredPosition = new Vector2(-8f, -8f);

            var panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.42f);
            panelImage.raycastTarget = false;

            var labelObject = new GameObject("CornerStatsText");
            labelObject.transform.SetParent(panelObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 6f);
            labelRect.offsetMax = new Vector2(-10f, -6f);

            cornerStatsText = labelObject.AddComponent<Text>();
            cornerStatsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            cornerStatsText.fontSize = 14;
            cornerStatsText.alignment = TextAnchor.UpperLeft;
            cornerStatsText.color = Color.white;
            cornerStatsText.horizontalOverflow = HorizontalWrapMode.Overflow;
            cornerStatsText.verticalOverflow = VerticalWrapMode.Overflow;
            cornerStatsText.lineSpacing = 1f;
            cornerStatsText.text = "FPS: --\nPing: -- ms";
        }

        private void EnsureMatchCornerStatsPanel(Transform root)
        {
            var existingPanel = root.Find("MatchCornerStatsPanel");
            if (existingPanel != null)
            {
                var versionMarker = existingPanel.GetComponent<CornerStatsLayoutMarker>();
                if (versionMarker != null &&
                    versionMarker.Version >= MatchCornerStatsLayoutVersion &&
                    matchCornerStatsText != null)
                {
                    ApplyBoldHudText(matchCornerStatsText);
                    return;
                }

                matchCornerStatsText = null;
                Destroy(existingPanel.gameObject);
            }

            BuildMatchCornerStatsPanel(root);
        }

        private void BuildMatchCornerStatsPanel(Transform root)
        {
            if (matchCornerStatsText != null)
            {
                return;
            }

            var panelObject = new GameObject("MatchCornerStatsPanel");
            panelObject.transform.SetParent(root, false);
            panelObject.AddComponent<CornerStatsLayoutMarker>().Version = MatchCornerStatsLayoutVersion;

            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.sizeDelta = new Vector2(168f, 52f);
            panelRect.anchoredPosition = new Vector2(8f, -8f);

            var panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.42f);
            panelImage.raycastTarget = false;

            var labelObject = new GameObject("MatchCornerStatsText");
            labelObject.transform.SetParent(panelObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 6f);
            labelRect.offsetMax = new Vector2(-10f, -6f);

            matchCornerStatsText = labelObject.AddComponent<Text>();
            ApplyBoldHudText(matchCornerStatsText);
            matchCornerStatsText.fontSize = 16;
            matchCornerStatsText.alignment = TextAnchor.UpperLeft;
            matchCornerStatsText.color = Color.white;
            matchCornerStatsText.lineSpacing = 1f;
            matchCornerStatsText.text = "Киллы: 0\nВыживших: 0";
        }

        private void EnsurePauseMenuPanel(Transform root)
        {
            if (pauseMenuPanel != null || root == null)
            {
                return;
            }

            var overlayObject = new GameObject("PauseMenuPanel");
            overlayObject.transform.SetParent(root, false);
            var overlayRect = overlayObject.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            var overlayImage = overlayObject.AddComponent<Image>();
            overlayImage.color = new Color(0.02f, 0.04f, 0.06f, 0.72f);
            overlayImage.raycastTarget = true;

            pauseMenuPanel = overlayObject;

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(overlayObject.transform, false);
            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(420f, 320f);

            var panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.08f, 0.1f, 0.12f, 0.96f);

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(panelObject.transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -24f);
            titleRect.sizeDelta = new Vector2(360f, 48f);
            var titleText = titleObject.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 34;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.text = "Пауза";

            CreatePauseMenuButton(
                panelObject.transform,
                "SettingsButton",
                new Vector2(0f, 36f),
                "Настройки",
                HandlePauseSettingsPressed);

            CreatePauseMenuButton(
                panelObject.transform,
                "ExitButton",
                new Vector2(0f, -36f),
                "Выйти в меню",
                HandlePauseExitPressed);

            pauseSettingsPanel = new GameObject("SettingsPanel");
            pauseSettingsPanel.transform.SetParent(panelObject.transform, false);
            var settingsRect = pauseSettingsPanel.AddComponent<RectTransform>();
            settingsRect.anchorMin = Vector2.zero;
            settingsRect.anchorMax = Vector2.one;
            settingsRect.offsetMin = Vector2.zero;
            settingsRect.offsetMax = Vector2.zero;

            var settingsTitleObject = new GameObject("SettingsTitle");
            settingsTitleObject.transform.SetParent(pauseSettingsPanel.transform, false);
            var settingsTitleRect = settingsTitleObject.AddComponent<RectTransform>();
            settingsTitleRect.anchorMin = new Vector2(0.5f, 1f);
            settingsTitleRect.anchorMax = new Vector2(0.5f, 1f);
            settingsTitleRect.pivot = new Vector2(0.5f, 1f);
            settingsTitleRect.anchoredPosition = new Vector2(0f, -24f);
            settingsTitleRect.sizeDelta = new Vector2(360f, 48f);
            var settingsTitleText = settingsTitleObject.AddComponent<Text>();
            settingsTitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            settingsTitleText.fontSize = 30;
            settingsTitleText.fontStyle = FontStyle.Bold;
            settingsTitleText.alignment = TextAnchor.MiddleCenter;
            settingsTitleText.text = "Настройки";

            pauseMuteButtonLabel = CreatePauseMenuButton(
                pauseSettingsPanel.transform,
                "MuteButton",
                new Vector2(0f, 48f),
                isMuted ? "Звук: выкл" : "Звук: вкл",
                HandleMutePressed);

            pausePerfButtonLabel = CreatePauseMenuButton(
                pauseSettingsPanel.transform,
                "PerfButton",
                new Vector2(0f, -16f),
                performancePreset != null && performancePreset.MaxPerformanceEnabled ? "Графика: MAX" : "Графика: качество",
                HandlePerfPressed);

            CreatePauseMenuButton(
                pauseSettingsPanel.transform,
                "BackButton",
                new Vector2(0f, -96f),
                "Назад",
                HandlePauseSettingsBackPressed);

            pauseSettingsPanel.SetActive(false);
            overlayObject.SetActive(false);
            overlayObject.transform.SetAsLastSibling();
        }

        private Text CreatePauseMenuButton(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            string label,
            UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = new GameObject(objectName);
            buttonObject.transform.SetParent(parent, false);
            var buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(280f, 52f);
            buttonRect.anchoredPosition = anchoredPosition;

            var buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.color = new Color(0.18f, 0.22f, 0.26f, 0.96f);

            var button = buttonObject.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            var buttonLabel = CreateLabel(buttonObject.transform, "Label", Vector2.zero, label);
            var buttonLabelRect = buttonLabel.rectTransform;
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = Vector2.zero;
            buttonLabelRect.offsetMax = Vector2.zero;
            buttonLabel.fontSize = 20;
            buttonLabel.fontStyle = FontStyle.Bold;
            buttonLabel.alignment = TextAnchor.MiddleCenter;
            return buttonLabel;
        }

        private void EnsurePerformancePresetButton()
        {
            if (perfButton != null)
            {
                perfButton.gameObject.SetActive(false);
                return;
            }
        }

        private static Font GetBoldHudFont()
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static void ApplyBoldHudText(Text text)
        {
            if (text == null)
            {
                return;
            }

            text.font = GetBoldHudFont();
            text.fontStyle = FontStyle.Bold;
        }

        private Text CreateLabel(Transform parent, string objectName, Vector2 anchoredPosition, string textValue)
        {
            var labelObject = new GameObject(objectName);
            labelObject.transform.SetParent(parent, false);

            var rect = labelObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(520f, 22f);
            rect.anchoredPosition = anchoredPosition;

            var label = labelObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 16;
            label.alignment = TextAnchor.UpperLeft;
            label.color = Color.white;
            label.text = textValue;
            return label;
        }

        private void RefreshConnectionText()
        {
            if (connectionText == null)
            {
                return;
            }

            var state = networkLauncher != null && networkLauncher.IsClientConnected
                ? "Connected"
                : "Not connected";
            connectionText.text = $"Status: {state}";
        }

        private void RefreshPingText()
        {
            ResolveDisplayPing(out displayPingMs, out displayPingLabel);
            if (pingText == null)
            {
                return;
            }

            if (!Application.isFocused)
            {
                pingText.text = "Ping: paused (unfocused)";
                return;
            }

            pingText.text = displayPingMs > 0
                ? $"Ping: {displayPingMs} ms{displayPingLabel}"
                : "Ping: -- ms";
        }

        private void ResolveDisplayPing(out int pingMs, out string suffix)
        {
            pingMs = -1;
            suffix = string.Empty;

            var realtimeClient = FindFirstObjectByType<RealtimeTransportClient>();
            if (realtimeClient != null && realtimeClient.IsReady)
            {
                var wsPing = realtimeClient.LastRoundTripMs > 0
                    ? realtimeClient.LastRoundTripMs
                    : realtimeClient.SmoothedRoundTripMs;
                if (wsPing > 0)
                {
                    pingMs = wsPing;
                    return;
                }
            }

            if (networkLauncher != null && networkLauncher.IsClientConnected && networkLauncher.LastMeasuredPingMs > 0)
            {
                pingMs = networkLauncher.LastMeasuredPingMs;
                suffix = " (tcp)";
            }
        }

        private void RefreshCornerStats()
        {
            if (cornerStatsText == null)
            {
                return;
            }

            var dt = Time.unscaledDeltaTime;
            if (dt > 0.00001f)
            {
                var currentFps = 1f / dt;
                fpsSmoothed = fpsSmoothed <= 0f
                    ? currentFps
                    : Mathf.Lerp(fpsSmoothed, currentFps, 0.15f);
            }

            if (!Application.isFocused)
            {
                cornerStatsText.text = $"FPS: {Mathf.RoundToInt(fpsSmoothed)}\nPing: paused";
                return;
            }

            var pingLine = displayPingMs > 0
                ? $"Ping: {displayPingMs} ms"
                : "Ping: -- ms";
            cornerStatsText.text = $"FPS: {Mathf.RoundToInt(fpsSmoothed)}\n{pingLine}";
        }

        private void EnsurePingRefreshRunning()
        {
            if (pingRefreshCoroutine == null)
            {
                pingRefreshCoroutine = StartCoroutine(PingRefreshRoutine());
            }
        }

        private void StopPingRefresh()
        {
            if (pingRefreshCoroutine == null)
            {
                return;
            }

            StopCoroutine(pingRefreshCoroutine);
            pingRefreshCoroutine = null;
        }

        private IEnumerator PingRefreshRoutine()
        {
            while (true)
            {
                if (pingText != null && !Application.isFocused)
                {
                    pingText.text = "Ping: paused (unfocused)";
                    displayPingMs = -1;
                    displayPingLabel = string.Empty;
                    consecutivePingFailures = 0;
                    RefreshFpsText();
                    yield return new WaitForSecondsRealtime(1f);
                    continue;
                }

                ResolveDisplayPing(out displayPingMs, out displayPingLabel);
                if (pingText != null)
                {
                    pingText.text = displayPingMs > 0
                        ? $"Ping: {displayPingMs} ms{displayPingLabel}"
                        : "Ping: -- ms";
                }

                var realtimeClient = FindFirstObjectByType<RealtimeTransportClient>();
                if (networkLauncher != null && networkLauncher.IsClientConnected)
                {
                    var snapshotFresh = realtimeClient != null &&
                                          realtimeClient.IsReady &&
                                          realtimeClient.LastSnapshotReceivedUnscaledTime > 0f &&
                                          (Time.unscaledTime - realtimeClient.LastSnapshotReceivedUnscaledTime) < 2f;
                    if (displayPingMs > 0 || snapshotFresh)
                    {
                        consecutivePingFailures = 0;
                    }
                    else
                    {
                        consecutivePingFailures++;
                        if (!returnToMenuRequested &&
                            Application.isFocused &&
                            consecutivePingFailures >= 8)
                        {
                            returnToMenuRequested = true;
                            networkLauncher.DisconnectClient("Lost connection to dedicated server.");
                            StartCoroutine(LeaveMatchAndReturnRoutine());
                            yield break;
                        }
                    }

                    RefreshPlayersText();
                }
                else
                {
                    consecutivePingFailures = 0;
                    RefreshPingText();
                }

                RefreshFpsText();

                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private IEnumerator LeaveMatchAndReturnRoutine()
        {
            var realtimeClient = FindObjectOfType<RealtimeTransportClient>();
            StopPingRefresh();

            if (queueApiClient != null && networkLauncher != null && !string.IsNullOrWhiteSpace(networkLauncher.CurrentTicketId))
            {
                var leaveCompleted = false;
                yield return StartCoroutine(queueApiClient.LeaveMatch(networkLauncher.CurrentTicketId, (_, __, ___) =>
                {
                    leaveCompleted = true;
                }));

                // If request hangs/fails, do not block returning to menu.
                if (!leaveCompleted)
                {
                    yield return null;
                }
            }

            realtimeClient?.Disconnect();
            networkLauncher?.DisconnectClient("Client returned to MainMenu.");
            ResetMatchOverlay();
            SceneManager.LoadScene(mainMenuSceneName);

            if (backButton != null)
            {
                backButton.interactable = true;
            }

            returnToMenuRequested = false;
            consecutivePingFailures = 0;
        }

        private void RefreshPlayersText()
        {
            if (playersText == null)
            {
                return;
            }

            var count = 0;
            var presenceSync = FindFirstObjectByType<ShooterPrototype.Player.MatchPresenceSync>();
            if (presenceSync != null && presenceSync.LiveMatchPlayerCount > 0)
            {
                count = presenceSync.LiveMatchPlayerCount;
            }
            else if (networkLauncher != null)
            {
                count = networkLauncher.CurrentMatchPlayerCount;
            }

            playersText.text = count > 0
                ? $"Players in match: {count}"
                : "Players in match: --";
        }

        private void RefreshFpsText()
        {
            if (fpsText == null)
            {
                return;
            }

            var dt = Time.unscaledDeltaTime;
            if (dt <= 0.00001f)
            {
                return;
            }

            var currentFps = 1f / dt;
            fpsSmoothed = fpsSmoothed <= 0f
                ? currentFps
                : Mathf.Lerp(fpsSmoothed, currentFps, 0.15f);

            fpsText.text = $"FPS: {Mathf.RoundToInt(fpsSmoothed)}";
            if (!topBarVisible)
            {
                return;
            }

            RefreshAmmoText();
            RefreshHealthText();
            RefreshMedkitText();
        }

        private void RefreshAmmoText()
        {
            if (ammoText == null)
            {
                return;
            }

            var local = FindObjectOfType<ShooterPrototype.Player.LocalPlayerMarker>();
            if (local == null)
            {
                ammoText.text = "Ammo: --/--";
                return;
            }

            var weapon = local.GetComponent<ShooterPrototype.Player.PlayerWeaponController>();
            var loadout = local.GetComponent<ShooterPrototype.Player.PlayerWeaponLoadout>();
            var loadoutController = local.GetComponent<ShooterPrototype.Player.PlayerWeaponLoadoutController>();
            var hasWeapon = loadout != null && loadout.HasAnyWeapon;
            var currentAmmo = weapon != null && weapon.enabled ? weapon.CurrentAmmo : 0;
            var spareAmmo = 0;
            if (weapon != null && weapon.enabled)
            {
                spareAmmo = weapon.ReserveAmmo;
            }
            else if (loadout != null && hasWeapon)
            {
                var kind = loadoutController != null
                    ? loadoutController.ResolveEquippedWeaponKind()
                    : loadout.GetActiveWeaponKind();
                spareAmmo = loadout.GetSpareAmmo(kind);
            }
            else if (weapon != null)
            {
                spareAmmo = weapon.ReserveAmmo;
            }
            if (!hasWeapon && spareAmmo <= 0)
            {
                ammoText.text = "Ammo: --/--";
                return;
            }

            var suffix = weapon != null && weapon.IsReloading ? " (reloading)" : string.Empty;
            ammoText.text = $"Ammo: {currentAmmo}/{spareAmmo}{suffix}";
        }

        private void RefreshHealthText()
        {
            if (healthText == null)
            {
                return;
            }

            var local = FindObjectOfType<ShooterPrototype.Player.LocalPlayerMarker>();
            var health = local != null ? local.GetComponent<ShooterPrototype.Player.PlayerHealth>() : null;
            if (health == null)
            {
                healthText.text = "HP: --/--";
                return;
            }

            var suffix = health.IsDead ? " (dead)" : string.Empty;
            healthText.text = $"HP: {Mathf.CeilToInt(health.CurrentHealth)}/{Mathf.CeilToInt(health.MaxHealth)}{suffix}";
        }

        private void RefreshMedkitText()
        {
            if (medkitText == null)
            {
                return;
            }

            var local = FindObjectOfType<ShooterPrototype.Player.LocalPlayerMarker>();
            var medkit = local != null ? local.GetComponent<ShooterPrototype.Player.PlayerMedkitController>() : null;
            if (medkit == null)
            {
                medkitText.text = "Medkits: -- [8]";
                return;
            }

            if (medkit.IsUsingMedkit)
            {
                medkitText.text = $"Healing: {Mathf.CeilToInt(medkit.RemainingUseSeconds)}s";
                return;
            }

            medkitText.text = $"Medkits: {medkit.MedkitCount} [8]";
        }

        private void RefreshInventoryText()
        {
            if (inventoryText == null)
            {
                return;
            }

            var local = FindObjectOfType<ShooterPrototype.Player.LocalPlayerMarker>();
            if (local == null)
            {
                inventoryText.text = "Inventory:\n  --";
                return;
            }

            var inventory = local.GetComponent<ShooterPrototype.Player.PlayerInventory>();
            if (inventory == null)
            {
                inventoryText.text = "Inventory:\n  --";
                return;
            }

            var weaponMount = local.GetComponent<ShooterPrototype.Player.PlayerWeaponMount>();
            var holster = local.GetComponent<ShooterPrototype.Player.PlayerWeaponHolsterController>();
            inventoryText.text = inventory.BuildHudSummary(weaponMount, holster);
        }

        private void RefreshMuteButtonText()
        {
            if (muteButtonLabel == null)
            {
                return;
            }

            muteButtonLabel.text = isMuted ? "Sound: OFF" : "Sound: ON";
        }

        private void RefreshPerfButtonVisuals()
        {
            if (perfButtonLabel == null)
            {
                return;
            }

            var maxPerformance = performancePreset != null && performancePreset.MaxPerformanceEnabled;
            perfButtonLabel.text = maxPerformance ? "Perf: MAX" : "Perf: QUALITY";

            if (perfButtonImage != null)
            {
                perfButtonImage.color = maxPerformance
                    ? new Color(0.12f, 0.34f, 0.16f, 0.95f)
                    : new Color(0.15f, 0.15f, 0.15f, 0.95f);
            }
        }

        private static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null && EventSystem.current.gameObject.activeInHierarchy)
            {
                return;
            }

            var eventSystemObject = new GameObject("RuntimeEventSystem");
            eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }

        private sealed class CornerStatsLayoutMarker : MonoBehaviour
        {
            public int Version;
        }

        private sealed class GameOverPanelLayoutMarker : MonoBehaviour
        {
            public int Version;
        }

        private sealed class GameplayHintLayoutMarker : MonoBehaviour
        {
            public int Version;
        }

        private sealed class MatchWaitStatusLayoutMarker : MonoBehaviour
        {
            public int Version;
        }
    }
}
