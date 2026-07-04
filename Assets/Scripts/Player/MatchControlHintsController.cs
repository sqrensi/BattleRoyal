using ShooterPrototype.Matchmaking;
using ShooterPrototype.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class MatchControlHintsController : MonoBehaviour
    {
        private const int PanelLayoutVersion = 5;
        private const float RowHeight = 24f;
        private const float MultiLineRowHeight = 38f;
        private const float TitleHeight = 34f;

        private static readonly (string Key, string Action)[] BaseHintRows =
        {
            ("W A S D", "Движение"),
            ("ПКМ", "Прицел"),
            ("ЛКМ", "Стрельба"),
            ("R", "Перезарядка"),
            ("Tab", "Таблица"),
            ("E", "Меню / пауза"),
            ("C", "Присесть"),
            ("X", "Убрать/достать\nоружие"),
            ("B", "Выбрать оружие"),
            ("Q", "Подсказки\nвкл/выкл"),
        };

        private static readonly (string Key, string Action)[] TrainingExtraRows =
        {
            ("F", "Подобрать"),
            ("1 / 2", "Смена оружия"),
        };

        private static readonly (string Key, string Action)[] DeathmatchExtraRows =
        {
        };

        private RectTransform panelRect;
        private CanvasGroup canvasGroup;
        private Transform pendingCanvasRoot;
        private int builtLayoutVersion;
        private MainMenuGameMode builtForMode = (MainMenuGameMode)(-1);

        private void OnEnable()
        {
            ClientSettingsService.EnsureLoaded();
            ClientSettingsService.SettingsChanged += HandleSettingsChanged;
        }

        private void OnDisable()
        {
            ClientSettingsService.SettingsChanged -= HandleSettingsChanged;
        }

        public void EnsureBuilt(Transform canvasRoot)
        {
            pendingCanvasRoot = canvasRoot;
            if (canvasRoot == null)
            {
                return;
            }

            var mode = ResolveHintsMode();
            var existing = canvasRoot.Find("MatchControlHintsPanel");
            if (existing != null &&
                (builtLayoutVersion != PanelLayoutVersion || builtForMode != mode))
            {
                Destroy(existing.gameObject);
                existing = null;
                panelRect = null;
                canvasGroup = null;
            }

            if (existing == null)
            {
                BuildPanel(canvasRoot, mode);
                builtLayoutVersion = PanelLayoutVersion;
                builtForMode = mode;
            }

            RefreshVisibility();
        }

        private void Update()
        {
            if (panelRect == null && pendingCanvasRoot != null)
            {
                EnsureBuilt(pendingCanvasRoot);
            }

            if (canvasGroup == null)
            {
                return;
            }

            if (IsMatchGameplayActive() && ReadToggleHintsPressed())
            {
                ClientSettingsService.SetShowMatchControlHints(!ClientSettingsService.ShowMatchControlHints);
            }

            RefreshVisibility();
        }

        private void HandleSettingsChanged()
        {
            RefreshVisibility();
        }

        private void BuildPanel(Transform canvasRoot, MainMenuGameMode mode)
        {
            var rows = BuildHintRows(mode);
            var contentHeight = 0f;
            for (var i = 0; i < rows.Length; i++)
            {
                contentHeight += GetRowHeight(rows[i].Action);
            }

            var panelHeight = TitleHeight + 16f + contentHeight + 12f;

            var panelObject = new GameObject("MatchControlHintsPanel");
            panelObject.transform.SetParent(canvasRoot, false);

            panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 0.5f);
            panelRect.anchorMax = new Vector2(1f, 0.5f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.anchoredPosition = new Vector2(-16f, 0f);
            panelRect.sizeDelta = new Vector2(268f, Mathf.Clamp(panelHeight, 220f, 460f));

            var panelImage = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(panelImage, UiPanelStyle.Hud);
            panelImage.raycastTarget = false;

            canvasGroup = panelObject.AddComponent<CanvasGroup>();

            CreateHintLabel(
                panelObject.transform,
                "Title",
                new Vector2(10f, -10f),
                new Vector2(248f, TitleHeight),
                "Управление",
                17f,
                TextAlignmentOptions.TopLeft,
                UiTextRole.Label,
                allowWrap: false);

            var currentY = -(TitleHeight + 8f);
            for (var i = 0; i < rows.Length; i++)
            {
                var rowHeight = GetRowHeight(rows[i].Action);
                var allowWrap = rows[i].Action.IndexOf('\n') >= 0;
                CreateHintLabel(
                    panelObject.transform,
                    $"Key_{i}",
                    new Vector2(10f, currentY),
                    new Vector2(92f, rowHeight),
                    rows[i].Key,
                    14f,
                    TextAlignmentOptions.MidlineLeft,
                    UiTextRole.Accent,
                    allowWrap: false);

                CreateHintLabel(
                    panelObject.transform,
                    $"Action_{i}",
                    new Vector2(106f, currentY),
                    new Vector2(152f, rowHeight),
                    rows[i].Action,
                    14f,
                    allowWrap ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft,
                    UiTextRole.Body,
                    allowWrap: allowWrap);

                currentY -= rowHeight;
            }
        }

        private static float GetRowHeight(string action)
        {
            return action != null && action.IndexOf('\n') >= 0 ? MultiLineRowHeight : RowHeight;
        }

        private static void CreateHintLabel(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            Vector2 size,
            string textValue,
            float fontSize,
            TextAlignmentOptions alignment,
            UiTextRole role,
            bool allowWrap)
        {
            var labelObject = new GameObject(objectName);
            labelObject.transform.SetParent(parent, false);

            var rect = labelObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = textValue;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.enableWordWrapping = allowWrap;
            label.overflowMode = allowWrap ? TextOverflowModes.Overflow : TextOverflowModes.Overflow;
            label.raycastTarget = false;
            UiTheme.ApplyMilitaryHeader(label, role);
        }

        private static MainMenuGameMode ResolveHintsMode()
        {
            if (ActiveMatchContext.IsTraining || ActiveMatchContext.IsOfflineTrainingSession)
            {
                return MainMenuGameMode.Training;
            }

            if (ActiveMatchContext.IsChallenge || ActiveMatchContext.IsOfflineChallengeSession)
            {
                return MainMenuGameMode.Challenge;
            }

            if (ActiveMatchContext.IsDeathmatch || ActiveMatchContext.IsOfflineDeathmatchSession)
            {
                return MainMenuGameMode.Deathmatch;
            }

            if (ActiveMatchContext.IsDuel || ActiveMatchContext.IsOfflineDuelSession)
            {
                return MainMenuGameMode.Duel1v1;
            }

            return ActiveMatchContext.SelectedMode;
        }

        private static (string Key, string Action)[] BuildHintRows(MainMenuGameMode mode)
        {
            var count = BaseHintRows.Length;
            if (mode == MainMenuGameMode.Training)
            {
                count += TrainingExtraRows.Length;
            }
            else if (mode == MainMenuGameMode.Deathmatch)
            {
                count += DeathmatchExtraRows.Length;
            }

            var rows = new (string Key, string Action)[count];
            var index = 0;
            for (var i = 0; i < BaseHintRows.Length; i++)
            {
                rows[index++] = BaseHintRows[i];
            }

            if (mode == MainMenuGameMode.Training)
            {
                for (var i = 0; i < TrainingExtraRows.Length; i++)
                {
                    rows[index++] = TrainingExtraRows[i];
                }
            }
            else if (mode == MainMenuGameMode.Deathmatch)
            {
                for (var i = 0; i < DeathmatchExtraRows.Length; i++)
                {
                    rows[index++] = DeathmatchExtraRows[i];
                }
            }

            return rows;
        }

        private void RefreshVisibility()
        {
            if (canvasGroup == null)
            {
                return;
            }

            ClientSettingsService.EnsureLoaded();
            var visible = ClientSettingsService.ShowMatchControlHints && IsMatchGameplayActive();
            canvasGroup.gameObject.SetActive(true);
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            if (visible)
            {
                canvasGroup.transform.SetAsLastSibling();
            }
        }

        private static bool IsMatchGameplayActive()
        {
            if (ActiveMatchContext.IsDeathmatch ||
                ActiveMatchContext.IsDuel ||
                ActiveMatchContext.IsOfflineDeathmatchSession ||
                ActiveMatchContext.IsOfflineDuelSession ||
                ActiveMatchContext.IsOfflineTrainingSession ||
                ActiveMatchContext.IsOfflineChallengeSession ||
                ActiveMatchContext.IsTraining ||
                ActiveMatchContext.IsChallenge)
            {
                return true;
            }

            var scene = SceneManager.GetActiveScene().name.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(scene) || scene == "mainmenu")
            {
                return false;
            }

            return scene.Contains("dm") ||
                   scene.Contains("1x1") ||
                   scene == "game" ||
                   scene == "training" ||
                   scene == "challenge";
        }

        private static bool ReadToggleHintsPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Q);
#endif
        }
    }
}
