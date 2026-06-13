using System.Collections;
using ShooterPrototype.Bootstrap;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace ShooterPrototype.UI
{
    public sealed class GameHudController : MonoBehaviour
    {
        private const string CanvasObjectName = "RuntimeGameHudCanvas";
        private const string MutePrefKey = "client_audio_muted";

        private NetworkLauncher networkLauncher;
        private QueueApiClient queueApiClient;
        private PerformancePresetController performancePreset;
        private string mainMenuSceneName = "MainMenu";

        private Canvas canvas;
        private Text connectionText;
        private Text playersText;
        private Text pingText;
        private Text fpsText;
        private Text ammoText;
        private Text healthText;
        private Text medkitText;
        private Text inventoryText;
        private Button backButton;
        private Button muteButton;
        private Button perfButton;
        private Text muteButtonLabel;
        private Text perfButtonLabel;
        private Image perfButtonImage;
        private Coroutine pingRefreshCoroutine;
        private float fpsSmoothed;
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
            SetActiveForScene(false);
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
                RefreshConnectionText();
                RefreshPlayersText();
                RefreshPingText();
                EnsurePingRefreshRunning();
            }
            else
            {
                StopPingRefresh();
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

        private void HandleBackPressed()
        {
            if (backButton != null)
            {
                backButton.interactable = false;
            }

            StartCoroutine(LeaveMatchAndReturnRoutine());
        }

        private void HandleMutePressed()
        {
            isMuted = !isMuted;
            PlayerPrefs.SetInt(MutePrefKey, isMuted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMuteState();
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
            }

            EnsureEventSystemExists();
            EnsurePerformancePresetButton();
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
            panelRect.sizeDelta = new Vector2(0f, 180f);

            var inventoryPanel = new GameObject("InventoryPanel");
            inventoryPanel.transform.SetParent(rootCanvasObject.transform, false);
            var inventoryPanelRect = inventoryPanel.AddComponent<RectTransform>();
            inventoryPanelRect.anchorMin = new Vector2(0f, 0f);
            inventoryPanelRect.anchorMax = new Vector2(0f, 0f);
            inventoryPanelRect.pivot = new Vector2(0f, 0f);
            inventoryPanelRect.sizeDelta = new Vector2(220f, 120f);
            inventoryPanelRect.anchoredPosition = new Vector2(10f, 10f);
            var inventoryPanelImage = inventoryPanel.AddComponent<Image>();
            inventoryPanelImage.color = new Color(0f, 0f, 0f, 0.45f);
            inventoryText = CreateLabel(inventoryPanel.transform, "InventoryText", new Vector2(8f, -8f), "Inventory:\n  (empty)");
            var inventoryLabelRect = inventoryText.rectTransform;
            inventoryLabelRect.anchorMin = new Vector2(0f, 1f);
            inventoryLabelRect.anchorMax = new Vector2(1f, 1f);
            inventoryLabelRect.pivot = new Vector2(0f, 1f);
            inventoryLabelRect.offsetMin = new Vector2(8f, -110f);
            inventoryLabelRect.offsetMax = new Vector2(-8f, -8f);
            inventoryText.alignment = TextAnchor.UpperLeft;
            inventoryText.fontSize = 14;
            inventoryText.horizontalOverflow = HorizontalWrapMode.Wrap;
            inventoryText.verticalOverflow = VerticalWrapMode.Overflow;

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
        }

        private void EnsurePerformancePresetButton()
        {
            if (perfButton != null || canvas == null)
            {
                return;
            }

            var topBar = canvas.transform.Find("TopBar");
            if (topBar == null)
            {
                return;
            }

            var perfObject = new GameObject("PerfButton");
            perfObject.transform.SetParent(topBar, false);

            var perfRect = perfObject.AddComponent<RectTransform>();
            perfRect.anchorMin = new Vector2(1f, 0.5f);
            perfRect.anchorMax = new Vector2(1f, 0.5f);
            perfRect.pivot = new Vector2(1f, 0.5f);
            perfRect.sizeDelta = new Vector2(170f, 36f);
            perfRect.anchoredPosition = new Vector2(-370f, 0f);

            perfButtonImage = perfObject.AddComponent<Image>();
            perfButtonImage.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            perfButton = perfObject.AddComponent<Button>();
            perfButton.onClick.AddListener(HandlePerfPressed);
            perfButtonLabel = CreateLabel(perfObject.transform, "Label", Vector2.zero, "");
            var perfLabelRect = perfButtonLabel.rectTransform;
            perfLabelRect.anchorMin = Vector2.zero;
            perfLabelRect.anchorMax = Vector2.one;
            perfLabelRect.offsetMin = Vector2.zero;
            perfLabelRect.offsetMax = Vector2.zero;
            perfButtonLabel.alignment = TextAnchor.MiddleCenter;
            RefreshPerfButtonVisuals();
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
            if (pingText == null)
            {
                return;
            }

            if (networkLauncher != null && networkLauncher.IsClientConnected && networkLauncher.LastConnectLatencyMs > 0)
            {
                pingText.text = $"Ping: {networkLauncher.LastConnectLatencyMs} ms (connect)";
                return;
            }

            pingText.text = "Ping: -- ms";
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
                    consecutivePingFailures = 0;
                    RefreshFpsText();
                    yield return new WaitForSecondsRealtime(1f);
                    continue;
                }

                var realtimeClient = FindObjectOfType<RealtimeTransportClient>();
                var wsPing = realtimeClient != null && realtimeClient.IsReady
                    ? realtimeClient.SmoothedRoundTripMs
                    : -1;

                if (pingText != null)
                {
                    if (wsPing > 0)
                    {
                        pingText.text = $"Ping: {wsPing} ms";
                    }
                    else if (networkLauncher != null && networkLauncher.LastMeasuredPingMs > 0)
                    {
                        pingText.text = $"Ping: {networkLauncher.LastMeasuredPingMs} ms (tcp)";
                    }
                    else
                    {
                        pingText.text = "Ping: -- ms";
                    }
                }

                if (networkLauncher != null && networkLauncher.IsClientConnected)
                {
                    var snapshotFresh = realtimeClient != null &&
                                          realtimeClient.IsReady &&
                                          realtimeClient.LastSnapshotReceivedUnscaledTime > 0f &&
                                          (Time.unscaledTime - realtimeClient.LastSnapshotReceivedUnscaledTime) < 2f;
                    if (wsPing > 0 || snapshotFresh)
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
            RefreshAmmoText();
            RefreshHealthText();
            RefreshMedkitText();
            RefreshInventoryText();
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
            var hasWeapon = loadout != null && loadout.HasAnyWeapon;
            var currentAmmo = weapon != null && weapon.enabled ? weapon.CurrentAmmo : 0;
            var spareAmmo = loadout != null ? loadout.SpareAmmo : weapon != null ? weapon.ReserveAmmo : 0;
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
    }
}
