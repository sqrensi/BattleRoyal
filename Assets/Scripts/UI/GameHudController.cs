using System;
using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Bootstrap;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
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
        private const int CornerStatsLayoutVersion = 10;
        private const int MatchCornerStatsLayoutVersion = 13;
        private const int MatchModeIntroLayoutVersion = 3;
        private const float GameOverPanelDelaySeconds = 5f;
        private const float GameOverAutoExitSeconds = 15f;
        private const int GameOverPanelLayoutVersion = 7;
        private const int GameplayHintLayoutVersion = 5;
        private const int MatchWaitStatusLayoutVersion = 5;
        private const int DuelTopCenterHudLayoutVersion = 2;
        private const int DuelPlayersPanelLayoutVersion = 6;
        private const int DuelRoundCountdownLayoutVersion = 2;
        private const int DuelWeaponPickLayoutVersion = 1;
        private const float GameplayHintOffsetX = 72f;
        private const float GameplayHintOffsetY = -48f;

        private MatchControlHintsController matchControlHints;
        private Coroutine modeIntroCoroutine;
        private TMP_Text modeIntroText;
        private CanvasGroup modeIntroCanvasGroup;
        private bool modeIntroActive;
        private NetworkLauncher networkLauncher;
        private QueueApiClient queueApiClient;
        private PerformancePresetController performancePreset;
        private string mainMenuSceneName = "MainMenu";

        private Canvas canvas;
        private GameObject topBarPanel;
        private bool topBarVisible;
        private bool matchHudActive;
        private CombatHudController combatHud;
        private GameKillFeedController killFeed;
        private MatchScoreboardPanelController scoreboard;
        private TMP_Text connectionText;
        private TMP_Text playersText;
        private TMP_Text pingText;
        private TMP_Text fpsText;
        private TMP_Text cornerStatsText;
        private TMP_Text matchCornerStatsText;
        private int matchCornerKillCount;
        private int matchCornerAliveCount;
        private int displayPingMs = -1;
        private string displayPingLabel = "--";
        private TMP_Text ammoText;
        private TMP_Text healthText;
        private TMP_Text medkitText;
        private TMP_Text killsText;
        private TMP_Text inventoryText;
        private GameObject legacyInventoryPanel;
        private TMP_Text matchStatusText;
        private TMP_Text duelTopCenterText;
        private TMP_Text duelPlayersPanelText;
        private TMP_Text duelPlayersLocalNickText;
        private TMP_Text duelPlayersLocalRatingText;
        private TMP_Text duelPlayersOpponentNickText;
        private TMP_Text duelPlayersOpponentRatingText;
        private TMP_Text duelRoundCountdownText;
        private RectTransform duelWeaponPickPanel;
        private readonly Dictionary<WeaponKind, Image> duelWeaponPickIcons = new Dictionary<WeaponKind, Image>(4);
        private Action<WeaponKind> duelWeaponPickHandler;
        private bool duelWeaponPickVisible;
        private GameObject gameplayHintPanel;
        private TMP_Text gameplayHintText;
        private string brGameplayHint = string.Empty;
        private string pickupGameplayHint = string.Empty;
        private TMP_Text victoryBannerText;
        private TMP_Text victorySubtitleText;
        private GameObject gameOverPanel;
        private CanvasGroup gameOverPanelGroup;
        private TMP_Text gameOverTitleText;
        private TMP_Text gameOverPlacementText;
        private TMP_Text gameOverKillsText;
        private TMP_Text gameOverRewardsText;
        private TMP_Text gameOverHintText;
        private Image gameOverPanelBackground;
        private Image gameOverAccentLine;
        private Button gameOverExitButton;
        private Coroutine gameOverFlowCoroutine;
        private bool gameOverFlowStarted;
        private bool gameOverPanelVisible;
        private MatchOutcomeSummary pendingMatchOutcome;
        private int pendingTrainingElapsedSeconds;
        private bool matchRewardGranted;
        private Coroutine matchRewardCoroutine;
        private bool matchStatsReported;
        private bool serverMatchStatsReceived;
        private int pendingServerRatingDelta;
        private Coroutine matchStatsCoroutine;
        private bool gameOverInputLocked;
        private GameObject pauseMenuPanel;
        private GameObject pauseMainPanel;
        private MainMenuSettingsPanel pauseSettingsPanel;
        private bool pauseMenuOpen;
        private bool pauseSettingsOpen;
        private Button backButton;
        private Button muteButton;
        private TMP_Text muteButtonLabel;
        private Coroutine pingRefreshCoroutine;
        private float fpsSmoothed;
        private float cornerStatsNextRefreshAt;
        private string lastCornerStatsText = string.Empty;
        private int lastCornerStatsDisplayedFps = -1;
        private int lastCornerStatsDisplayedPing = int.MinValue;
        private bool lastCornerStatsDisplayedPaused;

        public static bool IsPauseMenuOpen { get; private set; }

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
            ClientSettingsService.SettingsChanged += HandleClientSettingsChanged;
            ClientSettingsService.EnsureLoaded();
            ClientSettingsService.ApplyMasterVolume();
            ClientSettingsService.ApplyGraphicsPreset();
            LoadMuteState();
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

            if (ReadPauseMenuPressed())
            {
                HandlePauseMenuPressed();
            }

            RefreshCornerStats();
        }

        private static bool ReadPauseMenuPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }

        private void HandlePauseMenuPressed()
        {
            if (returnToMenuRequested || gameOverPanelVisible || gameOverFlowStarted)
            {
                return;
            }

            var fps = FindFirstObjectByType<FpsCharacterController>();
            var health = fps != null ? fps.GetComponent<PlayerHealth>() : null;
            if (health != null && health.IsDead)
            {
                return;
            }

            if (PlayerInventoryPanelController.IsOpen)
            {
                return;
            }

            if (scoreboard != null && scoreboard.IsOpen)
            {
                scoreboard.SetOpen(false);
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
            IsPauseMenuOpen = open;
            RefreshMatchTabSuppression();
            if (!open)
            {
                pauseSettingsOpen = false;
                pauseSettingsPanel?.HideImmediate();
                if (pauseMainPanel != null)
                {
                    pauseMainPanel.SetActive(true);
                }
            }
            else
            {
                pauseSettingsOpen = false;
                pauseSettingsPanel?.HideImmediate();
                if (pauseMainPanel != null)
                {
                    pauseMainPanel.SetActive(true);
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
            if (pauseMainPanel != null)
            {
                pauseMainPanel.SetActive(!open);
            }

            if (pauseSettingsPanel == null)
            {
                return;
            }

            if (open)
            {
                pauseSettingsPanel.Show();
            }
            else
            {
                pauseSettingsPanel.HideImmediate();
            }
        }

        private void ApplyPauseCursor(bool pauseOpen)
        {
            if (pauseOpen)
            {
                ApplyMenuCursor();
                return;
            }

            if (canvas == null || !canvas.gameObject.activeSelf)
            {
                ApplyMenuCursor();
                return;
            }

            var fps = FindFirstObjectByType<FpsCharacterController>();
            var health = fps != null ? fps.GetComponent<PlayerHealth>() : null;
            if (fps != null &&
                fps.isActiveAndEnabled &&
                !fps.IsWeaponPickUiMode &&
                (health == null || !health.IsDead))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }

            if (health != null && health.IsDead)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }

            ApplyMenuCursor();
        }

        private void HandlePauseSettingsPressed()
        {
            SetPauseSettingsOpen(true);
        }

        private void HandlePauseResumePressed()
        {
            SetPauseMenuOpen(false);
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

        private void SetTopBarVisible(bool visible)
        {
            topBarVisible = visible;
            if (topBarPanel != null)
            {
                topBarPanel.SetActive(visible);
            }
        }

        public bool TryGetHudCanvas(out Canvas hudCanvas)
        {
            EnsureHudExists();
            hudCanvas = canvas;
            return hudCanvas != null;
        }

        public void SetActiveForScene(bool isGameScene)
        {
            matchHudActive = isGameScene;
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
                EnsureScoreboard();
                if (canvas != null)
                {
                    scoreboard?.EnsureOnCanvas(canvas);
                }

                scoreboard?.SetActiveForScene(true);
                RefreshMatchTabSuppression();
                SetMatchCornerStatsPanelVisible(!ActiveMatchContext.IsDuel);
                ApplyMatchCursor();
            }
            else
            {
                SetPauseMenuOpen(false);
                StopPingRefresh();
                combatHud?.SetActiveForScene(false);
                killFeed?.SetActiveForScene(false);
                scoreboard?.SetActiveForScene(false);
                RefreshMatchTabSuppression();
                ApplyMenuCursor();
            }
        }

        private void RefreshMatchTabSuppression()
        {
            FpsCharacterController.SuppressTabCursorToggle = matchHudActive || pauseMenuOpen;
        }

        private static void ApplyMenuCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static void ApplyMatchCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDestroy()
        {
            StopPingRefresh();
            IsPauseMenuOpen = false;
            FpsCharacterController.SuppressTabCursorToggle = false;
            ClientSettingsService.SettingsChanged -= HandleClientSettingsChanged;

            if (networkLauncher != null)
            {
                networkLauncher.StatusChanged -= HandleNetworkStatusChanged;
            }
        }

        private void HandleClientSettingsChanged()
        {
            RefreshMuteButtonText();
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

        public void ShowModeIntroBanner(string message, float durationSeconds = 5f)
        {
            EnsureHudExists();
            EnsureModeIntroBanner(canvas != null ? canvas.transform : null);
            if (modeIntroCoroutine != null)
            {
                StopCoroutine(modeIntroCoroutine);
            }

            modeIntroCoroutine = StartCoroutine(ModeIntroBannerRoutine(message, durationSeconds));
        }

        public bool IsModeIntroActive => modeIntroActive;

        private IEnumerator ModeIntroBannerRoutine(string message, float durationSeconds)
        {
            modeIntroActive = true;
            EnsureModeIntroBanner(canvas != null ? canvas.transform : null);
            if (modeIntroText == null || modeIntroCanvasGroup == null)
            {
                modeIntroActive = false;
                modeIntroCoroutine = null;
                yield break;
            }

            modeIntroText.text = message ?? string.Empty;
            var introObject = modeIntroText.gameObject;
            introObject.transform.SetAsLastSibling();
            introObject.SetActive(true);

            var duration = Mathf.Clamp(durationSeconds, 1f, 12f);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var normalized = elapsed / duration;
                var shimmer = 0.62f + 0.38f * (0.5f + 0.5f * Mathf.Sin(elapsed * 5.5f));
                var fadeOut = normalized > 0.78f ? Mathf.Clamp01((1f - normalized) / 0.22f) : 1f;
                modeIntroCanvasGroup.alpha = shimmer * fadeOut;
                yield return null;
            }

            modeIntroCanvasGroup.alpha = 0f;
            introObject.SetActive(false);
            modeIntroActive = false;
            modeIntroCoroutine = null;
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
            if (ActiveMatchContext.IsDuel)
            {
                SetMatchCornerStatsPanelVisible(false);
                return;
            }

            matchCornerKillCount = Mathf.Max(0, killCount);
            matchCornerAliveCount = Mathf.Max(0, aliveCount);
            EnsureHudExists();
            SetMatchCornerStatsPanelVisible(true);
            RefreshMatchCornerStatsText();
        }

        public void SetDuelRoundScore(int localWins, int opponentWins, int roundNumber, int roundsToWin)
        {
            SetDuelMatchHud(localWins, opponentWins, roundNumber, roundsToWin, 0, string.Empty);
        }

        public void SetDuelMatchHud(
            int localWins,
            int opponentWins,
            int roundNumber,
            int roundsToWin,
            int countdownSeconds,
            string phaseLabel)
        {
            EnsureHudExists();
            EnsureDuelTopCenterHud(canvas != null ? canvas.transform : null);
            if (!ActiveMatchContext.IsDeathmatch)
            {
                SetMatchCornerStatsPanelVisible(false);
            }
            if (duelTopCenterText == null)
            {
                return;
            }

            var scoreLine = $"{Mathf.Max(0, localWins)} : {Mathf.Max(0, opponentWins)}";
            var detailLine = phaseLabel ?? string.Empty;
            if (string.IsNullOrWhiteSpace(detailLine) && roundNumber > 0)
            {
                detailLine = $"Раунд {Mathf.Max(1, roundNumber)} — {Mathf.Max(0, countdownSeconds)} сек.";
            }

            duelTopCenterText.text = string.IsNullOrWhiteSpace(detailLine)
                ? scoreLine
                : $"{scoreLine}\n{detailLine}";
            ApplyBoldHudText(duelTopCenterText);
            ApplyDuelTopCenterPanelLayout(compact: false);
            var panel = duelTopCenterText.transform.parent;
            if (panel != null)
            {
                panel.gameObject.SetActive(true);
            }

            duelTopCenterText.gameObject.SetActive(true);
        }

        public void ClearDuelMatchHud()
        {
            ClearDuelRoundEndCountdown();
            ClearDuelPlayersPanel();
            if (duelTopCenterText == null)
            {
                return;
            }

            duelTopCenterText.text = string.Empty;
            duelTopCenterText.gameObject.SetActive(false);
            var panel = duelTopCenterText.transform.parent;
            if (panel != null)
            {
                panel.gameObject.SetActive(false);
            }
        }

        public void SetDuelPlayersPanel(
            string localNickname,
            int localRating,
            string opponentNickname,
            int opponentRating)
        {
            EnsureHudExists();
            EnsureDuelPlayersPanel(canvas != null ? canvas.transform : null);
            if (duelPlayersLocalNickText == null && duelPlayersPanelText == null)
            {
                return;
            }

            var localName = FormatDuelHudName(localNickname, "Вы");
            var opponentName = FormatDuelHudName(opponentNickname, "Соперник");
            if (duelPlayersLocalNickText != null)
            {
                duelPlayersLocalNickText.text = localName;
            }

            if (duelPlayersLocalRatingText != null)
            {
                duelPlayersLocalRatingText.text = FormatDuelRating(localRating);
            }

            if (duelPlayersOpponentNickText != null)
            {
                duelPlayersOpponentNickText.text = opponentName;
            }

            if (duelPlayersOpponentRatingText != null)
            {
                duelPlayersOpponentRatingText.text = FormatDuelRating(opponentRating);
            }

            if (duelPlayersPanelText != null &&
                (duelPlayersLocalNickText == null || duelPlayersOpponentNickText == null))
            {
                duelPlayersPanelText.text =
                    $"{FormatDuelPlayerLine(localName, localRating)}\n{FormatDuelPlayerLine(opponentName, opponentRating)}";
            }
            ApplyDuelPanelNickText(duelPlayersPanelText);
            ApplyDuelPanelNickText(duelPlayersLocalNickText);
            ApplyDuelPanelNickText(duelPlayersLocalRatingText);
            ApplyDuelPanelNickText(duelPlayersOpponentNickText);
            ApplyDuelPanelNickText(duelPlayersOpponentRatingText);

            var panel = duelPlayersLocalNickText != null
                ? duelPlayersLocalNickText.transform.parent.parent
                : duelPlayersPanelText != null
                    ? duelPlayersPanelText.transform.parent
                    : null;
            if (panel != null)
            {
                panel.gameObject.SetActive(true);
            }

            if (duelPlayersPanelText != null)
            {
                duelPlayersPanelText.gameObject.SetActive(duelPlayersLocalNickText == null);
            }
        }

        public void ClearDuelPlayersPanel()
        {
            if (duelPlayersPanelText == null && duelPlayersLocalNickText == null)
            {
                return;
            }

            if (duelPlayersPanelText != null)
            {
                duelPlayersPanelText.text = string.Empty;
                duelPlayersPanelText.gameObject.SetActive(false);
            }

            if (duelPlayersLocalNickText != null)
            {
                duelPlayersLocalNickText.text = string.Empty;
            }

            if (duelPlayersLocalRatingText != null)
            {
                duelPlayersLocalRatingText.text = string.Empty;
            }

            if (duelPlayersOpponentNickText != null)
            {
                duelPlayersOpponentNickText.text = string.Empty;
            }

            if (duelPlayersOpponentRatingText != null)
            {
                duelPlayersOpponentRatingText.text = string.Empty;
            }

            var panel = duelPlayersPanelText != null
                ? duelPlayersPanelText.transform.parent
                : duelPlayersLocalNickText != null
                    ? duelPlayersLocalNickText.transform.parent.parent
                    : null;
            if (panel != null)
            {
                panel.gameObject.SetActive(false);
            }
        }

        private static string FormatDuelHudName(string nickname, string fallback)
        {
            return string.IsNullOrWhiteSpace(nickname) ? fallback : nickname.Trim();
        }

        private static string FormatDuelRating(int rating)
        {
            return Mathf.Max(0, rating).ToString("N0");
        }

        private static string FormatDuelPlayerLine(string nickname, int rating)
        {
            return $"{nickname}  {FormatDuelRating(rating)}";
        }

        public void SetTrainingTimerSeconds(int secondsRemaining)
        {
            EnsureHudExists();
            EnsureDuelTopCenterHud(canvas != null ? canvas.transform : null);
            if (duelTopCenterText == null)
            {
                return;
            }

            var clamped = Mathf.Max(0, secondsRemaining);
            var minutes = clamped / 60;
            var seconds = clamped % 60;
            duelTopCenterText.text = $"{minutes:00}:{seconds:00}";
            ApplyBoldHudText(duelTopCenterText);
            var panel = duelTopCenterText.transform.parent;
            if (panel != null)
            {
                panel.gameObject.SetActive(true);
            }

            duelTopCenterText.gameObject.SetActive(true);
        }

        public void SetDeathmatchHud(int killCount, int aliveCount, int secondsRemaining)
        {
            SetMatchCornerStats(killCount, aliveCount);
            SetTrainingTimerSeconds(secondsRemaining);
        }

        public void ScheduleTrainingGameOver(int killCount, int elapsedSeconds = 0)
        {
            if (gameOverFlowStarted)
            {
                return;
            }

            EnsureHudExists();
            pendingTrainingElapsedSeconds = Mathf.Max(0, elapsedSeconds);
            pendingMatchOutcome = MatchOutcomeSummary.CreateTraining(killCount);
            gameOverFlowStarted = true;
            SetPauseMenuOpen(false);
            ClearGameplayHints();
            ClearDuelMatchHud();
            gameOverFlowCoroutine = StartCoroutine(TrainingGameOverFlowRoutine(killCount));
        }

        public void SetChallengeTargetStats(int destroyed, int total)
        {
            EnsureHudExists();
            EnsureMatchCornerStatsPanel(canvas != null ? canvas.transform : null);
            SetMatchCornerStatsPanelVisible(true);
            if (matchCornerStatsText != null)
            {
                matchCornerStatsText.text = $"Цели: {Mathf.Max(0, destroyed)}/{Mathf.Max(0, total)}";
                ApplyBoldHudText(matchCornerStatsText);
            }
        }

        public void SetChallengeElapsedSeconds(float elapsedSeconds)
        {
            EnsureHudExists();
            EnsureDuelTopCenterHud(canvas != null ? canvas.transform : null);
            if (duelTopCenterText == null)
            {
                return;
            }

            duelTopCenterText.text = FormatChallengeTime(elapsedSeconds);
            ApplyBoldHudText(duelTopCenterText);
            ApplyDuelTopCenterPanelLayout(compact: true);
            var panel = duelTopCenterText.transform.parent;
            if (panel != null)
            {
                panel.gameObject.SetActive(true);
            }

            duelTopCenterText.gameObject.SetActive(true);
        }

        public void SetChallengePrepCountdown(int secondsRemaining)
        {
            EnsureHudExists();
            EnsureDuelTopCenterHud(canvas != null ? canvas.transform : null);
            if (duelTopCenterText == null)
            {
                return;
            }

            duelTopCenterText.text = FormatChallengeTime(Mathf.Max(0, secondsRemaining));
            ApplyBoldHudText(duelTopCenterText);
            ApplyDuelTopCenterPanelLayout(compact: true);
            var panel = duelTopCenterText.transform.parent;
            if (panel != null)
            {
                panel.gameObject.SetActive(true);
            }

            duelTopCenterText.gameObject.SetActive(true);
        }

        public void ClearChallengePrepCountdown()
        {
            if (duelTopCenterText == null)
            {
                return;
            }

            duelTopCenterText.text = string.Empty;
            duelTopCenterText.gameObject.SetActive(false);
        }

        public void ShowChallengeStartedBanner()
        {
            SetDuelRoundBanner("Челлендж начался");
        }

        public void ScheduleChallengeGameOver(float elapsedSeconds)
        {
            if (gameOverFlowStarted)
            {
                return;
            }

            EnsureHudExists();
            pendingMatchOutcome = MatchOutcomeSummary.CreateChallenge(elapsedSeconds);
            gameOverFlowStarted = true;
            SetPauseMenuOpen(false);
            ClearGameplayHints();
            ClearDuelMatchHud();
            gameOverFlowCoroutine = StartCoroutine(ChallengeGameOverFlowRoutine(elapsedSeconds));
        }

        private static string FormatChallengeTime(float elapsedSeconds)
        {
            var clamped = Mathf.Max(0f, elapsedSeconds);
            var minutes = Mathf.FloorToInt(clamped / 60f);
            var seconds = Mathf.FloorToInt(clamped % 60f);
            var tenths = Mathf.FloorToInt((clamped - Mathf.Floor(clamped)) * 10f);
            return $"{minutes}:{seconds:00}.{tenths}";
        }

        private IEnumerator ChallengeGameOverFlowRoutine(float elapsedSeconds)
        {
            yield return new WaitForSecondsRealtime(MatchChallengeController.GameOverDelaySeconds);

            SetVictoryBanner(false);
            SetMatchStatusMessage(string.Empty);
            ShowChallengeGameOverPanel(elapsedSeconds);

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
                RequestReturnToMenu("challenge_game_over");
            }
        }

        private void ShowChallengeGameOverPanel(float elapsedSeconds)
        {
            EnsureHudExists();
            EnsureGameOverPanel(canvas != null ? canvas.transform : null);
            if (gameOverPanel == null)
            {
                return;
            }

            matchRewardGranted = true;
            matchStatsReported = false;
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

            if (gameOverPanelBackground != null)
            {
                UiTheme.ApplyPanel(gameOverPanelBackground, UiPanelStyle.Heavy);
            }

            if (gameOverAccentLine != null)
            {
                UiTheme.ApplyFlatFill(gameOverAccentLine, UiTheme.GameOverWin);
            }

            if (gameOverTitleText != null)
            {
                gameOverTitleText.text = "ЧЕЛЛЕНДЖ";
                gameOverTitleText.color = UiTheme.GameOverWin;
            }

            if (gameOverPlacementText != null)
            {
                gameOverPlacementText.gameObject.SetActive(true);
                gameOverPlacementText.text = $"Время: {FormatChallengeTime(elapsedSeconds)}";
            }

            if (gameOverKillsText != null)
            {
                gameOverKillsText.gameObject.SetActive(false);
            }

            if (gameOverRewardsText != null)
            {
                gameOverRewardsText.gameObject.SetActive(false);
            }

            if (gameOverHintText != null)
            {
                gameOverHintText.text = "Нажмите выход, чтобы вернуться в меню.";
            }

            if (gameOverExitButton != null)
            {
                gameOverExitButton.interactable = true;
            }

            StartCoroutine(RecordChallengeResultRoutine(elapsedSeconds));
        }

        private IEnumerator RecordChallengeResultRoutine(float elapsedSeconds)
        {
            if (matchStatsReported)
            {
                yield break;
            }

            yield return PlayerProfileService.RecordChallengeCompletion(this, elapsedSeconds, (ok, improved) =>
            {
                matchStatsReported = ok;
                if (gameOverRewardsText != null && improved)
                {
                    gameOverRewardsText.gameObject.SetActive(true);
                    gameOverRewardsText.text = "Новый лучший результат!";
                }
            });
        }

        private IEnumerator TrainingGameOverFlowRoutine(int killCount)
        {
            if (!matchStatsReported &&
                pendingTrainingElapsedSeconds > 0 &&
                PlayerProfileService.CanReportMatchStatsToServer)
            {
                matchStatsReported = true;
                yield return RecordTrainingStatsRoutine(pendingTrainingElapsedSeconds);
            }

            yield return new WaitForSecondsRealtime(1f);

            SetVictoryBanner(false);
            SetMatchStatusMessage(string.Empty);
            ShowTrainingGameOverPanel(killCount);

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
                RequestReturnToMenu("training_game_over");
            }
        }

        private void ShowTrainingGameOverPanel(int killCount)
        {
            EnsureHudExists();
            EnsureGameOverPanel(canvas != null ? canvas.transform : null);
            if (gameOverPanel == null)
            {
                return;
            }

            matchRewardGranted = true;
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

            if (gameOverPanelBackground != null)
            {
                UiTheme.ApplyPanel(gameOverPanelBackground, UiPanelStyle.Heavy);
            }

            if (gameOverAccentLine != null)
            {
                UiTheme.ApplyFlatFill(gameOverAccentLine, UiTheme.GameOverLoss);
            }

            if (gameOverTitleText != null)
            {
                gameOverTitleText.text = "GAME OVER";
                gameOverTitleText.color = UiTheme.GameOverLoss;
            }

            if (gameOverPlacementText != null)
            {
                gameOverPlacementText.text = string.Empty;
                gameOverPlacementText.gameObject.SetActive(false);
            }

            if (gameOverKillsText != null)
            {
                gameOverKillsText.gameObject.SetActive(true);
                gameOverKillsText.text = $"{Mathf.Max(0, killCount)} убийств";
            }

            if (gameOverRewardsText != null)
            {
                gameOverRewardsText.text = string.Empty;
                gameOverRewardsText.gameObject.SetActive(false);
            }

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

        public void SetDuelRoundEndCountdown(int secondsRemaining)
        {
            EnsureHudExists();
            EnsureDuelRoundCountdownText(canvas != null ? canvas.transform : null);
            if (duelRoundCountdownText == null)
            {
                return;
            }

            var seconds = Mathf.Clamp(secondsRemaining, 1, 5);
            duelRoundCountdownText.text = seconds.ToString();
            duelRoundCountdownText.gameObject.SetActive(true);
        }

        public void ClearDuelRoundEndCountdown()
        {
            if (duelRoundCountdownText == null)
            {
                return;
            }

            duelRoundCountdownText.text = string.Empty;
            duelRoundCountdownText.gameObject.SetActive(false);
        }

        public void SetDuelRoundBanner(string message)
        {
            EnsureHudExists();
            combatHud?.ShowDuelRoundBanner(message);
        }

        public void ClearDuelRoundBanner()
        {
            combatHud?.ClearDuelRoundBanner();
        }

        public void ShowDuelWeaponPickPanel(Action<WeaponKind> onPick)
        {
            EnsureHudExists();
            EnsureDuelWeaponPickPanel(canvas != null ? canvas.transform : null);
            duelWeaponPickHandler = onPick;
            duelWeaponPickVisible = true;
            RefreshDuelWeaponPickIcons();
            if (duelWeaponPickPanel != null)
            {
                duelWeaponPickPanel.gameObject.SetActive(true);
            }
        }

        public void HideDuelWeaponPickPanel()
        {
            duelWeaponPickHandler = null;
            duelWeaponPickVisible = false;
            if (duelWeaponPickPanel != null)
            {
                duelWeaponPickPanel.gameObject.SetActive(false);
            }
        }

        public Transform GetHudCanvasTransform()
        {
            EnsureHudExists();
            return canvas != null ? canvas.transform : null;
        }

        private void RefreshMatchCornerStatsText()
        {
            if (matchCornerStatsText == null || ActiveMatchContext.IsDuel)
            {
                return;
            }

            matchCornerStatsText.text = $"Киллы: {matchCornerKillCount}";
            ApplyBoldHudText(matchCornerStatsText);
        }

        private void SetMatchCornerStatsPanelVisible(bool visible)
        {
            if (matchCornerStatsText == null)
            {
                return;
            }

            var panel = matchCornerStatsText.transform.parent;
            if (panel != null)
            {
                panel.gameObject.SetActive(visible);
            }
        }

        public void ResetMatchOverlay()
        {
            StopGameOverFlow();
            SetVictoryBanner(false);
            SetMatchStatusMessage(string.Empty);
            ClearDuelMatchHud();
            SetKillCount(0);
            SetMatchCornerStatsPanelVisible(!ActiveMatchContext.IsDuel);
            if (!ActiveMatchContext.IsDuel)
            {
                SetMatchCornerStats(0, 0);
            }
            SetMatchStatusMessage(string.Empty);
            ClearGameplayHints();
            HideGameOverPanel();
            SetPauseMenuOpen(false);
            pendingMatchOutcome = default;
            matchRewardGranted = false;
            matchStatsReported = false;
            serverMatchStatsReceived = false;
            pendingServerRatingDelta = 0;
            ApplyGameOverInputLock(false);
        }

        public void NotifyServerMatchStatsApplied(int ratingDelta)
        {
            serverMatchStatsReceived = true;
            pendingServerRatingDelta = ratingDelta;
            if (gameOverPanelVisible)
            {
                UpdateGameOverRatingText(pendingMatchOutcome, ratingDelta);
            }
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

            if (!matchStatsReported && ShouldReportMatchStatsViaProfileApi())
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

            var accentColor = won ? UiTheme.GameOverWin : UiTheme.GameOverLoss;

            if (gameOverPanelBackground != null)
            {
                UiTheme.ApplyPanel(gameOverPanelBackground, UiPanelStyle.Heavy);
            }

            if (gameOverAccentLine != null)
            {
                UiTheme.ApplyFlatFill(gameOverAccentLine, accentColor);
            }

            if (gameOverTitleText != null)
            {
                gameOverTitleText.text = won ? "Вы выиграли" : "Вы проиграли";
                gameOverTitleText.color = accentColor;
            }

            var ratingDelta = ResolveGameOverRatingDelta(won, summary);
            ApplyGameOverSummary(summary, ratingDelta);

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

        private static bool ShouldReportMatchStatsViaProfileApi()
        {
            if (ActiveMatchContext.IsDeathmatch)
            {
                return false;
            }

            if (!ActiveMatchContext.IsDuel)
            {
                return true;
            }

            return ActiveMatchContext.IsOfflineDuelSession &&
                   PlayerProfileService.CanReportMatchStatsToServer;
        }

        private int ResolveGameOverRatingDelta(bool won, MatchOutcomeSummary summary)
        {
            if (ActiveMatchContext.IsDeathmatch)
            {
                return 0;
            }

            if (serverMatchStatsReceived)
            {
                return pendingServerRatingDelta;
            }

            return ActiveMatchContext.IsDuel
                ? summary.ResolveDuelRatingDelta(won)
                : MatchRatingUtility.CalculateDelta(summary.Placement, summary.KillCount);
        }

        private IEnumerator RecordMatchStatsRoutine(bool won, MatchOutcomeSummary summary)
        {
            var playerId = PlayerIdentityService.GetOrCreatePlayerId();
            var sourceId = ResolveMatchRewardSourceId();
            var deaths = won ? 0 : 1;
            var damageDealt = MatchStatsTracker.DamageDealtThisMatch;
            var fallbackDelta = ResolveGameOverRatingDelta(won, summary);

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
                Mathf.Max(damageDealt, summary.DamageDealt),
                ActiveMatchContext.IsDuel ? "duel" :
                ActiveMatchContext.IsDeathmatch ? "deathmatch" : "battle_royale",
                summary.KillCount,
                summary.RoundLosses,
                summary.OpponentRating,
                (success, ratingDelta, _) =>
                {
                    UpdateGameOverRatingText(
                        summary,
                        success ? ratingDelta : fallbackDelta);
                });

            matchStatsCoroutine = null;
        }

        private IEnumerator RecordTrainingStatsRoutine(int elapsedSeconds)
        {
            var completed = false;
            var success = false;
            yield return PlayerProfileService.RecordTrainingSession(
                this,
                elapsedSeconds,
                ok =>
                {
                    completed = true;
                    success = ok;
                });

            if (!completed)
            {
                yield break;
            }

            if (!success)
            {
                Debug.LogWarning("[GameHudController] Failed to report training session time.");
            }
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
            if (ActiveMatchContext.IsDuel)
            {
                if (gameOverPlacementText != null)
                {
                    gameOverPlacementText.text = string.Empty;
                    gameOverPlacementText.gameObject.SetActive(false);
                }

                if (gameOverKillsText != null)
                {
                    gameOverKillsText.text = string.Empty;
                    gameOverKillsText.gameObject.SetActive(false);
                }
            }
            else
            {
                if (gameOverPlacementText != null)
                {
                    gameOverPlacementText.gameObject.SetActive(true);
                    gameOverPlacementText.text = $"ТОП {summary.Placement}";
                }

                if (gameOverKillsText != null)
                {
                    gameOverKillsText.gameObject.SetActive(true);
                    gameOverKillsText.text = $"{summary.KillCount} киллов";
                }
            }

            if (gameOverRewardsText != null)
            {
                gameOverRewardsText.gameObject.SetActive(true);
                if (ActiveMatchContext.IsDeathmatch)
                {
                    gameOverRewardsText.text = $"+{summary.CoinReward:N0} монет";
                }
                else
                {
                    gameOverRewardsText.text =
                        $"+{summary.CoinReward:N0} монет  ·  {MatchRatingUtility.FormatDelta(ratingDelta)} рейтинг";
                }
            }
        }

        private string ResolveMatchRewardSourceId()
        {
            if (ActiveMatchContext.IsOfflineDuelSession)
            {
                return $"offline-duel:{System.Guid.NewGuid():N}";
            }

            if (networkLauncher != null)
            {
                if (!string.IsNullOrWhiteSpace(networkLauncher.CurrentMatchId))
                {
                    if (ActiveMatchContext.IsDeathmatch)
                    {
                        return $"deathmatch:{networkLauncher.CurrentMatchId.Trim()}";
                    }

                    return $"duel:{networkLauncher.CurrentMatchId.Trim()}";
                }

                if (!string.IsNullOrWhiteSpace(networkLauncher.CurrentTicketId))
                {
                    return networkLauncher.CurrentTicketId.Trim();
                }
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
            var realtimeClient = RealtimeTransportClient.Active ?? FindFirstObjectByType<RealtimeTransportClient>();
            realtimeClient?.EndMatchSession();
            networkLauncher?.DisconnectClient(reason ?? "match ended");
            StartCoroutine(LeaveMatchAndReturnRoutine());
        }

        public void RequestConnectionRecovery(string reason)
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
            ConnectionRecoveryState.MarkPending(reason ?? "Соединение с сервером потеряно");

            var realtimeClient = RealtimeTransportClient.Active ?? FindFirstObjectByType<RealtimeTransportClient>();
            realtimeClient?.EndMatchSession();
            networkLauncher?.DisconnectClient(reason ?? "Lost connection to dedicated server.");
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
            ClientSettingsService.EnsureLoaded();
            ClientSettingsService.SetMasterVolume(
                ClientSettingsService.IsEffectivelyMuted()
                    ? ClientSettingsService.DefaultMasterVolume
                    : 0f);
            RefreshMuteButtonText();
        }

        private void LoadMuteState()
        {
            ClientSettingsService.EnsureLoaded();
            ClientSettingsService.ApplyMasterVolume();
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
                EnsureDuelTopCenterHud(canvas.transform);
                EnsureDuelPlayersPanel(canvas.transform);
                EnsureMatchOverlayElements(canvas.transform);
                EnsureGameplayHintPanel(canvas.transform);
                EnsureMatchControlHints(canvas.transform);
                EnsurePauseMenuPanel(canvas.transform);
            }

            EnsureEventSystemExists();
        }

        public void WarmUpMatchUi()
        {
            EnsureHudExists();
            if (canvas == null)
            {
                return;
            }

            var root = canvas.transform;
            EnsureCornerStatsPanel(root);
            EnsureMatchCornerStatsPanel(root);
            EnsureDuelPlayersPanel(root);
            EnsureDuelTopCenterHud(root);
            EnsureDuelWeaponPickPanel(root);
            EnsureMatchOverlayElements(root);
            EnsureGameplayHintPanel(root);
            EnsureMatchControlHints(root);
            EnsurePauseMenuPanel(root);
            combatHud?.EnsureOnCanvas(canvas);
            killFeed?.EnsureOnCanvas(canvas);
            EnsureScoreboard();
            scoreboard?.EnsureOnCanvas(canvas);
            Canvas.ForceUpdateCanvases();
        }

        private void EnsureScoreboard()
        {
            if (scoreboard == null)
            {
                scoreboard = GetComponent<MatchScoreboardPanelController>();
                if (scoreboard == null)
                {
                    scoreboard = gameObject.AddComponent<MatchScoreboardPanelController>();
                }
            }
        }

        public void SetScoreboardLocalTicket(string ticketId)
        {
            EnsureScoreboard();
            scoreboard?.SetLocalTicketId(ticketId);
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
            UiTheme.ApplyPanel(panelImage, UiPanelStyle.Hud);
            panelImage.raycastTarget = false;

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
            UiTheme.ApplyPanel(inventoryPanelImage, UiPanelStyle.Hud);
            inventoryPanelImage.raycastTarget = false;
            inventoryText = CreateLabel(inventoryPanel.transform, "InventoryText", new Vector2(8f, -8f), "Inventory:\n  (empty)");
            var inventoryLabelRect = inventoryText.rectTransform;
            inventoryLabelRect.anchorMin = new Vector2(0f, 1f);
            inventoryLabelRect.anchorMax = new Vector2(1f, 1f);
            inventoryLabelRect.pivot = new Vector2(0f, 1f);
            inventoryLabelRect.offsetMin = new Vector2(8f, -170f);
            inventoryLabelRect.offsetMax = new Vector2(-8f, -8f);
            inventoryText.alignment = TextAlignmentOptions.TopLeft;
            inventoryText.fontSize = 14;
            inventoryText.enableWordWrapping = true;
            inventoryText.overflowMode = TextOverflowModes.Overflow;
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
            backButton = buttonObject.AddComponent<Button>();
            backButton.targetGraphic = buttonImage;
            UiTheme.StyleButton(backButton);
            backButton.onClick.AddListener(HandleBackPressed);

            var buttonLabel = CreateLabel(buttonObject.transform, "Label", Vector2.zero, "Back to Menu");
            var buttonLabelRect = buttonLabel.rectTransform;
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = Vector2.zero;
            buttonLabelRect.offsetMax = Vector2.zero;
            buttonLabel.alignment = TextAlignmentOptions.Center;

            var muteObject = new GameObject("MuteButton");
            muteObject.transform.SetParent(panelObject.transform, false);

            var muteRect = muteObject.AddComponent<RectTransform>();
            muteRect.anchorMin = new Vector2(1f, 0.5f);
            muteRect.anchorMax = new Vector2(1f, 0.5f);
            muteRect.pivot = new Vector2(1f, 0.5f);
            muteRect.sizeDelta = new Vector2(170f, 36f);
            muteRect.anchoredPosition = new Vector2(-190f, 0f);

            var muteImage = muteObject.AddComponent<Image>();
            muteButton = muteObject.AddComponent<Button>();
            muteButton.targetGraphic = muteImage;
            UiTheme.StyleButton(muteButton);
            muteButton.onClick.AddListener(HandleMutePressed);
            muteButtonLabel = CreateLabel(muteObject.transform, "Label", Vector2.zero, "");
            var muteLabelRect = muteButtonLabel.rectTransform;
            muteLabelRect.anchorMin = Vector2.zero;
            muteLabelRect.anchorMax = Vector2.one;
            muteLabelRect.offsetMin = Vector2.zero;
            muteLabelRect.offsetMax = Vector2.zero;
            muteButtonLabel.alignment = TextAlignmentOptions.Center;
            RefreshMuteButtonText();

            BuildCornerStatsPanel(rootCanvasObject.transform);
        }

        private void EnsureMatchOverlayElements(Transform root)
        {
            EnsureMatchWaitStatusText(root);
            EnsureDuelTopCenterHud(root);
            EnsureDuelPlayersPanel(root);
            EnsureDuelRoundCountdownText(root);
            EnsureDuelWeaponPickPanel(root);

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
                victoryBannerText = victoryBannerObject.AddComponent<TextMeshProUGUI>();
                UiTheme.ApplyMilitaryHeader(victoryBannerText, UiTextRole.Accent);
                victoryBannerText.fontSize = 64;
                victoryBannerText.alignment = TextAlignmentOptions.Center;
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
                victorySubtitleText = victorySubtitleObject.AddComponent<TextMeshProUGUI>();
                UiTheme.ApplyTmp(victorySubtitleText, UiTextRole.Body);
                victorySubtitleText.fontSize = 20;
                victorySubtitleText.alignment = TextAlignmentOptions.Center;
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

            matchStatusText = matchStatusObject.AddComponent<TextMeshProUGUI>();
            ApplyBoldHudText(matchStatusText);
            matchStatusText.fontSize = 40;
            matchStatusText.alignment = TextAlignmentOptions.Center;
            matchStatusText.text = string.Empty;
            matchStatusObject.SetActive(false);
            EnsureModeIntroBanner(root);
        }

        private void EnsureModeIntroBanner(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var existing = root.Find("MatchModeIntroBanner");
            if (existing != null)
            {
                var versionMarker = existing.GetComponent<MatchWaitStatusLayoutMarker>();
                if (versionMarker != null &&
                    versionMarker.Version >= MatchModeIntroLayoutVersion &&
                    modeIntroText != null &&
                    modeIntroCanvasGroup != null)
                {
                    return;
                }

                modeIntroText = null;
                modeIntroCanvasGroup = null;
                Destroy(existing.gameObject);
            }

            var introObject = new GameObject("MatchModeIntroBanner");
            introObject.transform.SetParent(root, false);
            introObject.AddComponent<MatchWaitStatusLayoutMarker>().Version = MatchModeIntroLayoutVersion;

            var introRect = introObject.AddComponent<RectTransform>();
            introRect.anchorMin = new Vector2(0.5f, 0.5f);
            introRect.anchorMax = new Vector2(0.5f, 0.5f);
            introRect.pivot = new Vector2(0.5f, 0.5f);
            introRect.sizeDelta = new Vector2(920f, 96f);
            introRect.anchoredPosition = new Vector2(0f, 198f);

            modeIntroCanvasGroup = introObject.AddComponent<CanvasGroup>();
            modeIntroCanvasGroup.alpha = 0f;

            modeIntroText = introObject.AddComponent<TextMeshProUGUI>();
            modeIntroText.fontSize = 34f;
            modeIntroText.alignment = TextAlignmentOptions.Center;
            modeIntroText.enableWordWrapping = true;
            modeIntroText.text = string.Empty;
            UiTheme.ApplyMilitaryHeader(modeIntroText, UiTextRole.Accent);
            introObject.SetActive(false);
        }

        private void EnsureDuelTopCenterHud(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var existingPanel = root.Find("DuelTopCenterHudPanel");
            if (existingPanel != null)
            {
                var versionMarker = existingPanel.GetComponent<CornerStatsLayoutMarker>();
                if (versionMarker != null &&
                    versionMarker.Version >= DuelTopCenterHudLayoutVersion &&
                    duelTopCenterText != null)
                {
                    ApplyBoldHudText(duelTopCenterText);
                    return;
                }

                duelTopCenterText = null;
                Destroy(existingPanel.gameObject);
            }

            var panelObject = new GameObject("DuelTopCenterHudPanel");
            panelObject.transform.SetParent(root, false);
            panelObject.AddComponent<CornerStatsLayoutMarker>().Version = DuelTopCenterHudLayoutVersion;

            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.sizeDelta = new Vector2(420f, 72f);
            panelRect.anchoredPosition = new Vector2(0f, -12f);

            var panelImage = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(panelImage, UiPanelStyle.Hud);
            panelImage.raycastTarget = false;

            var labelObject = new GameObject("DuelTopCenterText");
            labelObject.transform.SetParent(panelObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 4f);
            labelRect.offsetMax = new Vector2(-8f, -4f);

            duelTopCenterText = labelObject.AddComponent<TextMeshProUGUI>();
            ApplyBoldHudText(duelTopCenterText);
            duelTopCenterText.fontSize = 22f;
            duelTopCenterText.alignment = TextAlignmentOptions.Center;
            duelTopCenterText.enableWordWrapping = false;
            duelTopCenterText.overflowMode = TextOverflowModes.Overflow;
            duelTopCenterText.lineSpacing = -2f;
            duelTopCenterText.text = string.Empty;
            panelObject.SetActive(false);
        }

        private void ApplyDuelTopCenterPanelLayout(bool compact)
        {
            if (duelTopCenterText == null)
            {
                return;
            }

            var panel = duelTopCenterText.transform.parent as RectTransform;
            if (panel == null)
            {
                return;
            }

            if (compact)
            {
                panel.sizeDelta = new Vector2(168f, 40f);
                duelTopCenterText.fontSize = 26f;
                duelTopCenterText.lineSpacing = 0f;
                return;
            }

            panel.sizeDelta = new Vector2(420f, 72f);
            duelTopCenterText.fontSize = 22f;
            duelTopCenterText.lineSpacing = -2f;
        }

        private void EnsureDuelPlayersPanel(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var existingPanel = root.Find("DuelPlayersPanel");
            if (existingPanel != null)
            {
                var versionMarker = existingPanel.GetComponent<CornerStatsLayoutMarker>();
                if (versionMarker != null &&
                    versionMarker.Version >= DuelPlayersPanelLayoutVersion)
                {
                    if (duelPlayersLocalNickText == null)
                    {
                        RebindDuelPlayersPanelTexts(existingPanel);
                    }

                    if (duelPlayersLocalNickText != null)
                    {
                        ApplyDuelPanelNickText(duelPlayersLocalNickText);
                        ApplyDuelPanelNickText(duelPlayersLocalRatingText);
                        ApplyDuelPanelNickText(duelPlayersOpponentNickText);
                        ApplyDuelPanelNickText(duelPlayersOpponentRatingText);
                        return;
                    }
                }

                duelPlayersPanelText = null;
                duelPlayersLocalNickText = null;
                duelPlayersLocalRatingText = null;
                duelPlayersOpponentNickText = null;
                duelPlayersOpponentRatingText = null;
                Destroy(existingPanel.gameObject);
            }

            var panelObject = new GameObject("DuelPlayersPanel");
            panelObject.transform.SetParent(root, false);
            panelObject.AddComponent<CornerStatsLayoutMarker>().Version = DuelPlayersPanelLayoutVersion;

            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.sizeDelta = new Vector2(210f, 46f);
            panelRect.anchoredPosition = new Vector2(8f, -8f);

            var panelImage = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(panelImage, UiPanelStyle.Hud);

            var layout = panelObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.spacing = 1f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            duelPlayersLocalNickText = CreateDuelPlayersRow(
                panelObject.transform,
                "LocalRow",
                out duelPlayersLocalRatingText);
            duelPlayersOpponentNickText = CreateDuelPlayersRow(
                panelObject.transform,
                "OpponentRow",
                out duelPlayersOpponentRatingText);
            duelPlayersPanelText = null;
            panelObject.SetActive(false);
        }

        private void RebindDuelPlayersPanelTexts(Transform panel)
        {
            if (panel == null)
            {
                return;
            }

            var localRow = panel.Find("LocalRow");
            var opponentRow = panel.Find("OpponentRow");
            duelPlayersLocalNickText = localRow != null
                ? localRow.Find("Nickname")?.GetComponent<TMP_Text>()
                : null;
            duelPlayersLocalRatingText = localRow != null
                ? localRow.Find("Rating")?.GetComponent<TMP_Text>()
                : null;
            duelPlayersOpponentNickText = opponentRow != null
                ? opponentRow.Find("Nickname")?.GetComponent<TMP_Text>()
                : null;
            duelPlayersOpponentRatingText = opponentRow != null
                ? opponentRow.Find("Rating")?.GetComponent<TMP_Text>()
                : null;
        }

        private TMP_Text CreateDuelPlayersRow(Transform parent, string rowName, out TMP_Text ratingText)
        {
            var rowObject = new GameObject(rowName);
            rowObject.transform.SetParent(parent, false);
            var rowLayout = rowObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 17f;
            rowLayout.minHeight = 17f;

            var nickObject = new GameObject("Nickname");
            nickObject.transform.SetParent(rowObject.transform, false);
            var nickRect = nickObject.AddComponent<RectTransform>();
            nickRect.anchorMin = Vector2.zero;
            nickRect.anchorMax = Vector2.one;
            nickRect.offsetMin = Vector2.zero;
            nickRect.offsetMax = new Vector2(-46f, 0f);
            var nickText = nickObject.AddComponent<TextMeshProUGUI>();
            nickText.fontSize = 12f;
            nickText.alignment = TextAlignmentOptions.MidlineLeft;
            nickText.overflowMode = TextOverflowModes.Ellipsis;
            nickText.enableWordWrapping = false;
            ApplyDuelPanelNickText(nickText);

            var ratingObject = new GameObject("Rating");
            ratingObject.transform.SetParent(rowObject.transform, false);
            var ratingRect = ratingObject.AddComponent<RectTransform>();
            ratingRect.anchorMin = new Vector2(1f, 0f);
            ratingRect.anchorMax = new Vector2(1f, 1f);
            ratingRect.pivot = new Vector2(1f, 0.5f);
            ratingRect.sizeDelta = new Vector2(44f, 0f);
            ratingRect.anchoredPosition = Vector2.zero;
            ratingText = ratingObject.AddComponent<TextMeshProUGUI>();
            ratingText.fontSize = 12f;
            ratingText.alignment = TextAlignmentOptions.MidlineRight;
            ratingText.overflowMode = TextOverflowModes.Overflow;
            ApplyDuelPanelNickText(ratingText);

            return nickText;
        }

        private static void ApplyDuelPanelNickText(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            UiTheme.ApplyTmp(text, UiTextRole.Body);
            text.fontStyle = FontStyles.Bold;
            text.color = UiTheme.TextPrimary;
        }

        private void EnsureDuelRoundCountdownText(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var existing = root.Find("DuelRoundCountdownText");
            if (existing != null)
            {
                var versionMarker = existing.GetComponent<CornerStatsLayoutMarker>();
                if (versionMarker != null &&
                    versionMarker.Version >= DuelRoundCountdownLayoutVersion &&
                    duelRoundCountdownText != null)
                {
                    return;
                }

                duelRoundCountdownText = null;
                Destroy(existing.gameObject);
            }

            var countdownObject = new GameObject("DuelRoundCountdownText");
            countdownObject.transform.SetParent(root, false);
            countdownObject.AddComponent<CornerStatsLayoutMarker>().Version = DuelRoundCountdownLayoutVersion;

            var countdownRect = countdownObject.AddComponent<RectTransform>();
            countdownRect.anchorMin = new Vector2(0.5f, 0.5f);
            countdownRect.anchorMax = new Vector2(0.5f, 0.5f);
            countdownRect.pivot = new Vector2(0.5f, 0.5f);
            countdownRect.sizeDelta = new Vector2(240f, 240f);
            countdownRect.anchoredPosition = new Vector2(0f, 72f);

            duelRoundCountdownText = countdownObject.AddComponent<TextMeshProUGUI>();
            duelRoundCountdownText.fontSize = 120f;
            duelRoundCountdownText.fontStyle = FontStyles.Bold;
            duelRoundCountdownText.alignment = TextAlignmentOptions.Center;
            duelRoundCountdownText.color = new Color(1f, 1f, 1f, 0.42f);
            duelRoundCountdownText.enableWordWrapping = false;
            duelRoundCountdownText.overflowMode = TextOverflowModes.Overflow;
            duelRoundCountdownText.raycastTarget = false;
            duelRoundCountdownText.text = string.Empty;
            countdownObject.SetActive(false);
        }

        private void EnsureDuelWeaponPickPanel(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var existingPanel = root.Find("DuelWeaponPickPanel");
            if (existingPanel != null)
            {
                var versionMarker = existingPanel.GetComponent<CornerStatsLayoutMarker>();
                if (duelWeaponPickPanel == null)
                {
                    duelWeaponPickPanel = existingPanel.GetComponent<RectTransform>();
                    RebindDuelWeaponPickIcons(existingPanel);
                }

                if (versionMarker != null &&
                    versionMarker.Version >= DuelWeaponPickLayoutVersion &&
                    duelWeaponPickPanel != null)
                {
                    return;
                }

                duelWeaponPickPanel = null;
                duelWeaponPickIcons.Clear();
                Destroy(existingPanel.gameObject);
            }

            var panelObject = new GameObject("DuelWeaponPickPanel");
            panelObject.transform.SetParent(root, false);
            panelObject.AddComponent<CornerStatsLayoutMarker>().Version = DuelWeaponPickLayoutVersion;

            duelWeaponPickPanel = panelObject.AddComponent<RectTransform>();
            duelWeaponPickPanel.anchorMin = new Vector2(0.5f, 0.5f);
            duelWeaponPickPanel.anchorMax = new Vector2(0.5f, 0.5f);
            duelWeaponPickPanel.pivot = new Vector2(0.5f, 0.5f);
            duelWeaponPickPanel.sizeDelta = new Vector2(760f, 220f);
            duelWeaponPickPanel.anchoredPosition = new Vector2(0f, -24f);

            var panelImage = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(panelImage, UiPanelStyle.Hud);
            panelImage.raycastTarget = true;

            var titleObject = new GameObject("DuelWeaponPickTitle");
            titleObject.transform.SetParent(panelObject.transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(0f, 36f);
            titleRect.anchoredPosition = new Vector2(0f, -8f);
            var titleText = titleObject.AddComponent<TextMeshProUGUI>();
            ApplyBoldHudText(titleText);
            titleText.fontSize = 24f;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.text = "Выберите оружие";

            var buttonsRoot = new GameObject("DuelWeaponPickButtons");
            buttonsRoot.transform.SetParent(panelObject.transform, false);
            var buttonsRect = buttonsRoot.AddComponent<RectTransform>();
            buttonsRect.anchorMin = new Vector2(0.05f, 0.08f);
            buttonsRect.anchorMax = new Vector2(0.95f, 0.72f);
            buttonsRect.offsetMin = Vector2.zero;
            buttonsRect.offsetMax = Vector2.zero;

            var weaponKinds = new[]
            {
                WeaponKind.AssaultRifle,
                WeaponKind.SniperRifle,
                WeaponKind.Pistol,
                WeaponKind.Mp7
            };

            for (var i = 0; i < weaponKinds.Length; i++)
            {
                CreateDuelWeaponPickButton(buttonsRoot.transform, weaponKinds[i], i, weaponKinds.Length);
            }

            panelObject.SetActive(false);
        }

        private void CreateDuelWeaponPickButton(Transform parent, WeaponKind kind, int index, int count)
        {
            var slotWidth = 1f / Mathf.Max(1, count);
            var minX = index * slotWidth;
            var maxX = minX + slotWidth;

            var buttonObject = new GameObject($"DuelWeaponPick_{kind}");
            buttonObject.transform.SetParent(parent, false);

            var buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(minX, 0f);
            buttonRect.anchorMax = new Vector2(maxX, 1f);
            buttonRect.offsetMin = new Vector2(8f, 0f);
            buttonRect.offsetMax = new Vector2(-8f, 0f);

            var background = buttonObject.AddComponent<Image>();
            UiTheme.ApplyPanel(background, UiPanelStyle.Hud);
            background.raycastTarget = true;

            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(buttonObject.transform, false);
            var iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.12f, 0.12f);
            iconRect.anchorMax = new Vector2(0.88f, 0.88f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            var iconImage = iconObject.AddComponent<Image>();
            iconImage.sprite = InventoryIconCatalog.GetWeaponIcon(kind);
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            duelWeaponPickIcons[kind] = iconImage;

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            var capturedKind = kind;
            button.onClick.AddListener(() => duelWeaponPickHandler?.Invoke(capturedKind));
        }

        private void RebindDuelWeaponPickIcons(Transform panelRoot)
        {
            if (panelRoot == null)
            {
                return;
            }

            duelWeaponPickIcons.Clear();
            var buttonsRoot = panelRoot.Find("DuelWeaponPickButtons");
            if (buttonsRoot == null)
            {
                return;
            }

            for (var i = 0; i < buttonsRoot.childCount; i++)
            {
                var child = buttonsRoot.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                var prefix = "DuelWeaponPick_";
                if (!child.name.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                var kindName = child.name.Substring(prefix.Length);
                if (!System.Enum.TryParse(kindName, out WeaponKind kind))
                {
                    continue;
                }

                var icon = child.GetComponentInChildren<Image>();
                if (icon != null)
                {
                    duelWeaponPickIcons[kind] = icon;
                }
            }
        }

        private void RefreshDuelWeaponPickIcons()
        {
            foreach (var pair in duelWeaponPickIcons)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                pair.Value.sprite = InventoryIconCatalog.GetWeaponIcon(pair.Key);
            }
        }

        private void EnsureMatchControlHints(Transform root)
        {
            if (root == null)
            {
                return;
            }

            if (matchControlHints == null)
            {
                matchControlHints = GetComponent<MatchControlHintsController>();
                if (matchControlHints == null)
                {
                    matchControlHints = gameObject.AddComponent<MatchControlHintsController>();
                }
            }

            matchControlHints.EnsureBuilt(root);
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
            panelRect.sizeDelta = new Vector2(420f, 32f);

            var labelObject = new GameObject("HintText");
            labelObject.transform.SetParent(panelObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            gameplayHintText = labelObject.AddComponent<TextMeshProUGUI>();
            ApplyBoldHudText(gameplayHintText);
            gameplayHintText.fontSize = 22;
            gameplayHintText.alignment = TextAlignmentOptions.MidlineLeft;
            gameplayHintText.enableWordWrapping = false;
            gameplayHintText.overflowMode = TextOverflowModes.Overflow;
            gameplayHintText.outlineWidth = 0.18f;
            gameplayHintText.outlineColor = new Color(0.02f, 0.02f, 0.02f, 0.82f);

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
                    versionMarker.Version >= GameOverPanelLayoutVersion)
                {
                    BindGameOverPanelReferences(existingPanel);
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
                gameOverAccentLine = null;
                Destroy(existingPanel.gameObject);
            }

            BuildGameOverPanel(root);
        }

        private void BindGameOverPanelReferences(Transform existingPanel)
        {
            gameOverPanel = existingPanel.gameObject;
            gameOverPanelGroup = existingPanel.GetComponent<CanvasGroup>();

            var panel = existingPanel.Find("Panel");
            if (panel == null)
            {
                return;
            }

            gameOverPanelBackground = panel.GetComponent<Image>();
            gameOverAccentLine = panel.Find("AccentLine")?.GetComponent<Image>();
            gameOverTitleText = panel.Find("Title")?.GetComponent<TMP_Text>();
            gameOverPlacementText = panel.Find("Placement")?.GetComponent<TMP_Text>();
            gameOverKillsText = panel.Find("Kills")?.GetComponent<TMP_Text>();
            gameOverRewardsText = panel.Find("Rewards")?.GetComponent<TMP_Text>();
            gameOverHintText = panel.Find("Hint")?.GetComponent<TMP_Text>();
            gameOverExitButton = panel.Find("ExitButton")?.GetComponent<Button>();
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
            UiTheme.ApplyFlatFill(overlayImage, UiTheme.CanvasDim);
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
            UiTheme.ApplyPanel(gameOverPanelBackground, UiPanelStyle.Heavy);
            gameOverAccentLine = UiDecor.CreateAccentLine(panelObject.transform, UiTheme.GameOverLoss);

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(panelObject.transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -28f);
            titleRect.sizeDelta = new Vector2(380f, 48f);
            gameOverTitleText = titleObject.AddComponent<TextMeshProUGUI>();
            UiTheme.ApplyMilitaryHeader(gameOverTitleText, UiTextRole.Heading);
            gameOverTitleText.fontSize = 36;
            gameOverTitleText.alignment = TextAlignmentOptions.Center;
            gameOverTitleText.text = "Вы проиграли";

            gameOverPlacementText = CreateGameOverBodyLine(
                panelObject.transform,
                "Placement",
                new Vector2(0f, -92f),
                30,
                UiTextRole.Heading);

            gameOverKillsText = CreateGameOverBodyLine(
                panelObject.transform,
                "Kills",
                new Vector2(0f, -132f),
                26,
                UiTextRole.Body);

            gameOverRewardsText = CreateGameOverBodyLine(
                panelObject.transform,
                "Rewards",
                new Vector2(0f, -178f),
                22,
                UiTextRole.Accent);

            var buttonObject = new GameObject("ExitButton");
            buttonObject.transform.SetParent(panelObject.transform, false);
            var buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.sizeDelta = new Vector2(260f, 48f);
            buttonRect.anchoredPosition = new Vector2(0f, 52f);

            var buttonImage = buttonObject.AddComponent<Image>();
            gameOverExitButton = buttonObject.AddComponent<Button>();
            gameOverExitButton.targetGraphic = buttonImage;
            UiTheme.StyleButton(gameOverExitButton, UiButtonStyle.Primary);
            gameOverExitButton.onClick.AddListener(HandleGameOverExitPressed);

            var buttonLabel = CreateLabel(buttonObject.transform, "Label", Vector2.zero, "Выйти в меню");
            var buttonLabelRect = buttonLabel.rectTransform;
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = Vector2.zero;
            buttonLabelRect.offsetMax = Vector2.zero;
            buttonLabel.fontSize = 20;
            UiTheme.ApplyTmp(buttonLabel, UiTextRole.PrimaryButton);
            buttonLabel.alignment = TextAlignmentOptions.Center;

            var hintObject = new GameObject("Hint");
            hintObject.transform.SetParent(panelObject.transform, false);
            var hintRect = hintObject.AddComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.5f, 0f);
            hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 20f);
            hintRect.sizeDelta = new Vector2(380f, 22f);
            gameOverHintText = hintObject.AddComponent<TextMeshProUGUI>();
            UiTheme.ApplyTmp(gameOverHintText, UiTextRole.Muted);
            gameOverHintText.fontSize = 14;
            gameOverHintText.alignment = TextAlignmentOptions.Center;

            overlayObject.SetActive(false);
            overlayObject.transform.SetAsLastSibling();
        }

        private static TMP_Text CreateGameOverBodyLine(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            int fontSize,
            UiTextRole role)
        {
            var lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(parent, false);

            var rect = lineObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(380f, fontSize + 12f);

            var text = lineObject.AddComponent<TextMeshProUGUI>();
            if (role == UiTextRole.Heading)
            {
                UiTheme.ApplyMilitaryHeader(text, role);
            }
            else
            {
                UiTheme.ApplyTmp(text, role);
            }

            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
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
            UiTheme.ApplyPanel(panelImage, UiPanelStyle.Hud);
            panelImage.raycastTarget = false;

            var labelObject = new GameObject("CornerStatsText");
            labelObject.transform.SetParent(panelObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4f, 4f);
            labelRect.offsetMax = new Vector2(-4f, -4f);

            cornerStatsText = labelObject.AddComponent<TextMeshProUGUI>();
            UiTheme.ApplyTmp(cornerStatsText, UiTextRole.Body);
            cornerStatsText.fontSize = 14f;
            cornerStatsText.alignment = TextAlignmentOptions.Center;
            cornerStatsText.enableWordWrapping = false;
            cornerStatsText.overflowMode = TextOverflowModes.Overflow;
            cornerStatsText.lineSpacing = -2f;
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
            panelRect.sizeDelta = new Vector2(152f, 34f);
            panelRect.anchoredPosition = new Vector2(8f, -8f);

            var panelImage = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(panelImage, UiPanelStyle.Hud);
            panelImage.raycastTarget = false;

            var labelObject = new GameObject("MatchCornerStatsText");
            labelObject.transform.SetParent(panelObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12f, 2f);
            labelRect.offsetMax = new Vector2(-10f, -2f);

            matchCornerStatsText = labelObject.AddComponent<TextMeshProUGUI>();
            ApplyBoldHudText(matchCornerStatsText);
            matchCornerStatsText.fontSize = 16f;
            matchCornerStatsText.alignment = TextAlignmentOptions.MidlineLeft;
            matchCornerStatsText.enableWordWrapping = false;
            matchCornerStatsText.overflowMode = TextOverflowModes.Overflow;
            matchCornerStatsText.lineSpacing = 0f;
            matchCornerStatsText.text = "Киллы: 0";
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
            UiTheme.ApplyFlatFill(overlayImage, UiTheme.CanvasDim);
            overlayImage.raycastTarget = true;

            pauseMenuPanel = overlayObject;

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(overlayObject.transform, false);
            pauseMainPanel = panelObject;
            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(420f, 360f);

            var panelImage = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(panelImage, UiPanelStyle.Heavy);

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(panelObject.transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -24f);
            titleRect.sizeDelta = new Vector2(360f, 48f);
            var titleText = titleObject.AddComponent<TextMeshProUGUI>();
            UiTheme.ApplyMilitaryHeader(titleText, UiTextRole.Title);
            titleText.fontSize = 34;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.text = "Пауза";

            CreatePauseMenuButton(
                panelObject.transform,
                "ResumeButton",
                new Vector2(0f, 54f),
                "Продолжить игру",
                HandlePauseResumePressed);

            CreatePauseMenuButton(
                panelObject.transform,
                "SettingsButton",
                new Vector2(0f, -6f),
                "Настройки",
                HandlePauseSettingsPressed);

            CreatePauseMenuButton(
                panelObject.transform,
                "ExitButton",
                new Vector2(0f, -66f),
                "Выйти в меню",
                HandlePauseExitPressed);

            var settingsHostObject = new GameObject("PauseSettingsHost");
            settingsHostObject.transform.SetParent(overlayObject.transform, false);
            var settingsHostRect = settingsHostObject.AddComponent<RectTransform>();
            settingsHostRect.anchorMin = Vector2.zero;
            settingsHostRect.anchorMax = Vector2.one;
            settingsHostRect.offsetMin = Vector2.zero;
            settingsHostRect.offsetMax = Vector2.zero;

            pauseSettingsPanel = settingsHostObject.AddComponent<MainMenuSettingsPanel>();
            pauseSettingsPanel.ConfigureLayout(edgeMarginOverride: 28f, leftReservedWidthOverride: 28f, topReservedHeightOverride: 28f);
            pauseSettingsPanel.SetUseMenuBackdrop(false);
            pauseSettingsPanel.SetInstantTransitions(true);
            pauseSettingsPanel.SetBackHandler(HandlePauseSettingsBackPressed);
            pauseSettingsPanel.Build(settingsHostRect);

            overlayObject.SetActive(false);
            overlayObject.transform.SetAsLastSibling();
        }

        private TMP_Text CreatePauseMenuButton(
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
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            UiTheme.StyleButton(button);
            button.onClick.AddListener(onClick);

            var buttonLabel = CreateLabel(buttonObject.transform, "Label", Vector2.zero, label);
            var buttonLabelRect = buttonLabel.rectTransform;
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = Vector2.zero;
            buttonLabelRect.offsetMax = Vector2.zero;
            buttonLabel.fontSize = 20;
            UiTheme.ApplyTmp(buttonLabel, UiTextRole.Heading);
            buttonLabel.alignment = TextAlignmentOptions.Center;
            return buttonLabel;
        }

        private static void ApplyBoldHudText(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            UiTheme.ApplyMilitaryHeader(text, UiTextRole.Heading);
        }

        private TMP_Text CreateLabel(Transform parent, string objectName, Vector2 anchoredPosition, string textValue)
        {
            var labelObject = new GameObject(objectName);
            labelObject.transform.SetParent(parent, false);

            var rect = labelObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(520f, 22f);
            rect.anchoredPosition = anchoredPosition;

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            UiTheme.ApplyTmp(label, UiTextRole.Body);
            label.fontSize = 16;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.text = textValue;
            label.raycastTarget = false;
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

            if (Time.unscaledTime < cornerStatsNextRefreshAt)
            {
                return;
            }

            cornerStatsNextRefreshAt = Time.unscaledTime + GameplayPerformanceOptions.CornerStatsTextRefreshSeconds;

            var fpsInt = Mathf.RoundToInt(fpsSmoothed);
            var isPaused = !Application.isFocused;
            var pingInt = isPaused ? -1 : displayPingMs;
            if (fpsInt == lastCornerStatsDisplayedFps &&
                pingInt == lastCornerStatsDisplayedPing &&
                isPaused == lastCornerStatsDisplayedPaused)
            {
                return;
            }

            lastCornerStatsDisplayedFps = fpsInt;
            lastCornerStatsDisplayedPing = pingInt;
            lastCornerStatsDisplayedPaused = isPaused;

            string nextText;
            if (isPaused)
            {
                nextText = "FPS: " + fpsInt + "\nPing: paused";
            }
            else if (displayPingMs > 0)
            {
                nextText = "FPS: " + fpsInt + "\nPing: " + displayPingMs + " ms";
            }
            else
            {
                nextText = "FPS: " + fpsInt + "\nPing: -- ms";
            }

            if (string.Equals(lastCornerStatsText, nextText, System.StringComparison.Ordinal))
            {
                return;
            }

            lastCornerStatsText = nextText;
            cornerStatsText.text = nextText;
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

                if (ActiveMatchContext.IsTraining)
                {
                    consecutivePingFailures = 0;
                    if (pingText != null)
                    {
                        pingText.text = "Тренировка";
                    }

                    RefreshFpsText();
                    yield return new WaitForSecondsRealtime(1f);
                    continue;
                }

                if (ActiveMatchContext.IsChallenge)
                {
                    consecutivePingFailures = 0;
                    if (pingText != null)
                    {
                        pingText.text = ActiveMatchContext.IsOfflineChallengeSession
                            ? "Челлендж (офлайн)"
                            : "Челлендж";
                    }

                    RefreshFpsText();
                    yield return new WaitForSecondsRealtime(1f);
                    continue;
                }

                if (ActiveMatchContext.IsOfflineDuelSession)
                {
                    consecutivePingFailures = 0;
                    if (pingText != null)
                    {
                        pingText.text = "Дуэль с ботом";
                    }

                    RefreshFpsText();
                    yield return new WaitForSecondsRealtime(1f);
                    continue;
                }

                if (ActiveMatchContext.IsOfflineSoloSession)
                {
                    consecutivePingFailures = 0;
                    if (pingText != null)
                    {
                        pingText.text = ActiveMatchContext.IsTraining
                            ? "Тренировка"
                            : "Офлайн";
                    }

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
                                          (RealtimeTransportClient.MonotonicNowSeconds -
                                           realtimeClient.LastSnapshotReceivedUnscaledTime) < 2f;
                    var wsConnected = realtimeClient != null &&
                                      (realtimeClient.IsConnected || realtimeClient.IsConnecting);
                    if ((displayPingMs > 0 || snapshotFresh) && wsConnected)
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
                            RequestConnectionRecovery("Соединение с сервером потеряно");
                            yield break;
                        }
                    }

                    RefreshPlayersText();
                }
                else
                {
                    if (!returnToMenuRequested &&
                        Application.isFocused &&
                        SceneManager.GetActiveScene().name != mainMenuSceneName)
                    {
                        consecutivePingFailures++;
                        if (consecutivePingFailures >= 3)
                        {
                            RequestConnectionRecovery("Соединение с сервером потеряно");
                            yield break;
                        }
                    }
                    else
                    {
                        consecutivePingFailures = 0;
                    }

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

            realtimeClient?.EndMatchSession();
            networkLauncher?.DisconnectClient("Client returned to MainMenu.");
            ResetMatchOverlay();
            ApplyMenuCursor();
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

            muteButtonLabel.text = ClientSettingsService.IsEffectivelyMuted() ? "Sound: OFF" : "Sound: ON";
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
