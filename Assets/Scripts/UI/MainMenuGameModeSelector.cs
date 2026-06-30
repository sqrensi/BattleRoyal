using ShooterPrototype.Matchmaking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuGameModeSelector : MonoBehaviour
    {
        private const int LayoutVersion = 7;

        private static readonly MainMenuGameMode[] Modes =
        {
            MainMenuGameMode.Duel1v1,
            MainMenuGameMode.Deathmatch,
            MainMenuGameMode.Training,
            MainMenuGameMode.Challenge,
        };

        private static readonly MainMenuGameMode[] OfflineModes =
        {
            MainMenuGameMode.Duel1v1,
            MainMenuGameMode.Deathmatch,
            MainMenuGameMode.Training,
            MainMenuGameMode.Challenge,
        };

        private static MainMenuGameMode selectedMode = MainMenuGameMode.Duel1v1;

        [SerializeField] private float selectorHeight = 54f;
        [SerializeField] private float arrowButtonSize = 28f;
        [SerializeField] private float modeFontSize = 15f;
        [SerializeField] private float panelContentInset = 14f;

        private TMP_Text modeLabel;
        private Button previousButton;
        private Button nextButton;
        private int builtLayoutVersion;
        private bool onlineModesRestricted;

        public static MainMenuGameMode SelectedMode => selectedMode;

        public CanvasGroup CanvasGroup { get; private set; }

        public void Build(
            RectTransform canvasRect,
            float leftMargin,
            float bottomOffset,
            float width,
            MainMenuUiSoundController uiSound)
        {
            if (canvasRect == null)
            {
                return;
            }

            var existing = canvasRect.Find("MainMenuGameModeSelector");
            if (existing != null)
            {
                if (builtLayoutVersion >= LayoutVersion && modeLabel != null)
                {
                    ApplyRect(existing.GetComponent<RectTransform>(), leftMargin, bottomOffset, width);
                    RefreshLabel();
                    return;
                }

                Destroy(existing.gameObject);
            }

            modeLabel = null;
            previousButton = null;
            nextButton = null;

            var rootObject = new GameObject("MainMenuGameModeSelector");
            rootObject.transform.SetParent(canvasRect, false);

            var rootRect = rootObject.AddComponent<RectTransform>();
            ApplyRect(rootRect, leftMargin, bottomOffset, width);

            var panelImage = rootObject.AddComponent<Image>();
            UiTheme.ApplyPanel(panelImage, UiPanelStyle.Standard);
            panelImage.raycastTarget = false;
            rootObject.AddComponent<RectMask2D>();

            var layout = rootObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var inset = Mathf.RoundToInt(panelContentInset);
            layout.padding = new RectOffset(inset, inset, 6, 6);

            previousButton = CreateArrowButton(
                rootObject.transform,
                UiIconCatalog.IconKind.ChevronLeft,
                uiSound,
                () => Cycle(-1));
            CreateModeField(rootObject.transform);
            nextButton = CreateArrowButton(
                rootObject.transform,
                UiIconCatalog.IconKind.ChevronRight,
                uiSound,
                () => Cycle(1));

            CanvasGroup = rootObject.AddComponent<CanvasGroup>();
            builtLayoutVersion = LayoutVersion;
            RefreshLabel();
        }

        private void ApplyRect(RectTransform rootRect, float leftMargin, float bottomOffset, float width)
        {
            if (rootRect == null)
            {
                return;
            }

            rootRect.anchorMin = new Vector2(0f, 0f);
            rootRect.anchorMax = new Vector2(0f, 0f);
            rootRect.pivot = new Vector2(0f, 0f);
            rootRect.anchoredPosition = new Vector2(leftMargin, bottomOffset);
            rootRect.sizeDelta = new Vector2(width, selectorHeight);
        }

        public void SetOnlineModesRestricted(bool restricted)
        {
            onlineModesRestricted = restricted;
            if (restricted && !MainMenuGameModeUtility.IsOfflinePlayMode(selectedMode))
            {
                selectedMode = MainMenuGameMode.Duel1v1;
            }

            RefreshLabel();
            ApplyInteractionState();
        }

        public void SetInteractable(bool interactable)
        {
            ApplyInteractionState(interactable);
        }

        private void ApplyInteractionState(bool? interactableOverride = null)
        {
            var interactable = interactableOverride ?? true;

            if (CanvasGroup != null)
            {
                CanvasGroup.interactable = interactable;
                CanvasGroup.blocksRaycasts = interactable;
            }

            if (previousButton != null)
            {
                previousButton.interactable = interactable;
            }

            if (nextButton != null)
            {
                nextButton.interactable = interactable;
            }
        }

        private void CreateModeField(Transform parent)
        {
            var fieldObject = new GameObject("ModeField");
            fieldObject.transform.SetParent(parent, false);

            var layout = fieldObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.minWidth = 0f;
            layout.preferredHeight = selectorHeight - 12f;
            layout.minHeight = selectorHeight - 12f;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(fieldObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            modeLabel = labelObject.AddComponent<TextMeshProUGUI>();
            modeLabel.fontSize = modeFontSize;
            modeLabel.lineSpacing = -6f;
            modeLabel.alignment = TextAlignmentOptions.Center;
            modeLabel.enableWordWrapping = true;
            modeLabel.overflowMode = TextOverflowModes.Ellipsis;
            modeLabel.maxVisibleLines = 2;
            modeLabel.characterSpacing = 0.4f;
            UiTheme.ApplyMilitaryHeader(modeLabel, UiTextRole.Accent);
        }

        private Button CreateArrowButton(
            Transform parent,
            UiIconCatalog.IconKind iconKind,
            MainMenuUiSoundController uiSound,
            UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = new GameObject(iconKind == UiIconCatalog.IconKind.ChevronLeft
                ? "PrevModeButton"
                : "NextModeButton");
            buttonObject.transform.SetParent(parent, false);

            var layout = buttonObject.AddComponent<LayoutElement>();
            layout.preferredWidth = arrowButtonSize;
            layout.preferredHeight = arrowButtonSize;
            layout.minWidth = arrowButtonSize;
            layout.flexibleWidth = 0f;

            var image = buttonObject.AddComponent<Image>();
            image.sprite = UiIconCatalog.GetIcon(iconKind);
            image.preserveAspect = true;
            image.color = UiTheme.TextAccent;

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;

            button.onClick.AddListener(onClick);
            if (uiSound != null)
            {
                button.onClick.AddListener(uiSound.PlayButton);
            }

            return button;
        }

        private void Cycle(int delta)
        {
            if (onlineModesRestricted)
            {
                selectedMode = CycleMode(selectedMode, OfflineModes, delta);
                RefreshLabel();
                return;
            }

            selectedMode = CycleMode(selectedMode, Modes, delta);
            RefreshLabel();
        }

        private static MainMenuGameMode CycleMode(MainMenuGameMode current, MainMenuGameMode[] modes, int delta)
        {
            var currentIndex = 0;
            for (var i = 0; i < modes.Length; i++)
            {
                if (modes[i] == current)
                {
                    currentIndex = i;
                    break;
                }
            }

            var nextIndex = (currentIndex + delta + modes.Length) % modes.Length;
            return modes[nextIndex];
        }

        private void RefreshLabel()
        {
            if (modeLabel != null)
            {
                modeLabel.text = MainMenuGameModeUtility.GetDisplayName(selectedMode).ToUpperInvariant();
            }

            var menu = FindFirstObjectByType<MainMenuController>();
            menu?.RefreshLeaderboardForSelectedMode();
        }
    }
}
