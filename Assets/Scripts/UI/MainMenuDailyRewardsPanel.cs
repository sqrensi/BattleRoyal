using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuDailyRewardsPanel : MonoBehaviour
    {
        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float leftReservedWidth = 228f;
        [SerializeField] private float topReservedHeight = 92f;
        [SerializeField] private float fadeDuration = 0.38f;
        [SerializeField] private float titleFontSize = 36f;

        private CanvasGroup panelGroup;
        private RectTransform panelRect;
        private MainMenuUiSoundController uiSound;
        private TMP_Text pageLabel;
        private TMP_Text statusLabel;
        private Button claimButton;
        private readonly List<SlotVisual> slots = new List<SlotVisual>(7);
        private bool isVisible;
        private Coroutine transitionCoroutine;

        private sealed class SlotVisual
        {
            public Image Background;
            public TMP_Text Title;
            public TMP_Text DayLabel;
        }

        public void Configure(MainMenuUiSoundController sound)
        {
            uiSound = sound;
        }

        public void Build(RectTransform canvasRect)
        {
            if (panelRect != null || canvasRect == null)
            {
                return;
            }

            var panelObject = new GameObject("MainMenuDailyRewardsPanel");
            panelObject.transform.SetParent(canvasRect, false);

            panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = new Vector2(leftReservedWidth, edgeMargin);
            panelRect.offsetMax = new Vector2(-edgeMargin, -topReservedHeight);

            var background = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(background, UiPanelStyle.Heavy);

            panelGroup = panelObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;

            BuildHeader(panelObject.transform);
            BuildGrid(panelObject.transform);
            BuildFooter(panelObject.transform);
        }

        private void OnEnable()
        {
            DailyRewardService.StateChanged += Refresh;
        }

        private void OnDisable()
        {
            DailyRewardService.StateChanged -= Refresh;
        }

        public void Show()
        {
            if (panelGroup == null)
            {
                return;
            }

            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
            }

            isVisible = true;
            Refresh();
            transitionCoroutine = StartCoroutine(FadeRoutine(1f, true));
        }

        public void Hide()
        {
            if (panelGroup == null || !isVisible)
            {
                return;
            }

            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
            }

            isVisible = false;
            transitionCoroutine = StartCoroutine(FadeRoutine(0f, false));
        }

        private void BuildHeader(Transform parent)
        {
            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(parent, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -18f);
            titleRect.sizeDelta = new Vector2(-48f, 48f);
            var titleText = titleObject.AddComponent<TextMeshProUGUI>();
            titleText.text = "НАГРАДЫ";
            titleText.fontSize = titleFontSize;
            titleText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyMilitaryHeader(titleText, UiTextRole.Title);

            var pageObject = new GameObject("PageLabel");
            pageObject.transform.SetParent(parent, false);
            var pageRect = pageObject.AddComponent<RectTransform>();
            pageRect.anchorMin = new Vector2(0f, 1f);
            pageRect.anchorMax = new Vector2(1f, 1f);
            pageRect.pivot = new Vector2(0.5f, 1f);
            pageRect.anchoredPosition = new Vector2(0f, -68f);
            pageRect.sizeDelta = new Vector2(-48f, 28f);
            pageLabel = pageObject.AddComponent<TextMeshProUGUI>();
            pageLabel.fontSize = 18f;
            pageLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(pageLabel, UiTextRole.Muted);
        }

        private void BuildGrid(Transform parent)
        {
            var gridObject = new GameObject("Grid");
            gridObject.transform.SetParent(parent, false);
            var gridRect = gridObject.AddComponent<RectTransform>();
            gridRect.anchorMin = new Vector2(0.5f, 0.5f);
            gridRect.anchorMax = new Vector2(0.5f, 0.5f);
            gridRect.pivot = new Vector2(0.5f, 0.5f);
            gridRect.sizeDelta = new Vector2(1180f, 520f);
            gridRect.anchoredPosition = new Vector2(0f, 10f);

            var layout = gridObject.AddComponent<GridLayoutGroup>();
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 7;
            layout.cellSize = new Vector2(156f, 240f);
            layout.spacing = new Vector2(18f, 18f);
            layout.childAlignment = TextAnchor.MiddleCenter;

            for (var i = 0; i < DailyRewardCatalogService.RewardsPerPage; i++)
            {
                slots.Add(CreateSlot(gridObject.transform, i + 1));
            }
        }

        private SlotVisual CreateSlot(Transform parent, int dayNumber)
        {
            var slotObject = new GameObject($"Slot_{dayNumber}");
            slotObject.transform.SetParent(parent, false);

            var background = slotObject.AddComponent<Image>();
            UiTheme.ApplyPanel(background, UiPanelStyle.Standard);

            var dayObject = new GameObject("Day");
            dayObject.transform.SetParent(slotObject.transform, false);
            var dayRect = dayObject.AddComponent<RectTransform>();
            dayRect.anchorMin = new Vector2(0f, 1f);
            dayRect.anchorMax = new Vector2(1f, 1f);
            dayRect.pivot = new Vector2(0.5f, 1f);
            dayRect.anchoredPosition = new Vector2(0f, -8f);
            dayRect.sizeDelta = new Vector2(-12f, 24f);
            var dayText = dayObject.AddComponent<TextMeshProUGUI>();
            dayText.text = $"День {dayNumber}";
            dayText.fontSize = 20f;
            dayText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(dayText, UiTextRole.Label);

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(slotObject.transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(12f, 16f);
            titleRect.offsetMax = new Vector2(-12f, -44f);
            var titleText = titleObject.AddComponent<TextMeshProUGUI>();
            titleText.fontSize = 22f;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.enableWordWrapping = true;
            UiTheme.ApplyTmp(titleText, UiTextRole.Body);

            return new SlotVisual
            {
                Background = background,
                DayLabel = dayText,
                Title = titleText,
            };
        }

        private void BuildFooter(Transform parent)
        {
            var statusObject = new GameObject("Status");
            statusObject.transform.SetParent(parent, false);
            var statusRect = statusObject.AddComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.5f, 0f);
            statusRect.anchorMax = new Vector2(0.5f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.anchoredPosition = new Vector2(0f, 88f);
            statusRect.sizeDelta = new Vector2(640f, 32f);
            statusLabel = statusObject.AddComponent<TextMeshProUGUI>();
            statusLabel.fontSize = 16f;
            statusLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(statusLabel, UiTextRole.Muted);

            var buttonObject = new GameObject("ClaimButton");
            buttonObject.transform.SetParent(parent, false);
            var buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 28f);
            buttonRect.sizeDelta = new Vector2(260f, 48f);

            var image = buttonObject.AddComponent<Image>();
            claimButton = buttonObject.AddComponent<Button>();
            UiTheme.StyleButton(claimButton, UiButtonStyle.Primary);
            image.sprite = UiTheme.PrimaryButtonSprite;
            image.type = Image.Type.Sliced;
            UiMotion.AttachButtonMotion(claimButton, primary: true);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);
            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = "ЗАБРАТЬ";
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyMilitaryHeader(label, UiTextRole.PrimaryButton);

            claimButton.onClick.AddListener(OnClaimClicked);
            if (uiSound != null)
            {
                claimButton.onClick.AddListener(uiSound.PlayButton);
            }
        }

        private void OnClaimClicked()
        {
            if (!DailyRewardService.TryClaimToday(out _, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    statusLabel.text = error;
                }

                Refresh();
                return;
            }

            uiSound?.PlayStart();
            MatchAchievementReporter.ReportEvent(this, AchievementEventTypes.DailyRewardClaim, 1);
            Refresh();
        }

        private void Refresh()
        {
            if (panelRect == null)
            {
                return;
            }

            DailyRewardCatalogService.EnsureLoaded();
            var page = DailyRewardService.CurrentPage;
            pageLabel.text = $"Страница {page + 1} из {DailyRewardCatalogService.TotalPages}";

            for (var slot = 0; slot < slots.Count; slot++)
            {
                var visual = slots[slot];
                var globalIndex = page * DailyRewardCatalogService.RewardsPerPage + slot;
                if (!DailyRewardCatalogService.TryGetReward(globalIndex, out var reward))
                {
                    visual.Title.text = "—";
                    visual.Background.color = UiTheme.SlotEmpty;
                    continue;
                }

                visual.Title.text = DailyRewardCatalogService.FormatRewardTitle(reward);
                if (DailyRewardService.IsSlotClaimed(slot))
                {
                    visual.Background.color = UiTheme.SlotEmpty;
                    visual.DayLabel.text = "Получено";
                }
                else if (DailyRewardService.IsSlotClaimableToday(slot))
                {
                    visual.Background.color = UiTheme.SlotHighlight;
                    visual.DayLabel.text = $"День {slot + 1}";
                }
                else
                {
                    visual.Background.color = UiTheme.SlotFill;
                    visual.DayLabel.text = $"День {slot + 1}";
                }
            }

            if (DailyRewardService.IsAllRewardsComplete())
            {
                statusLabel.text = "Все 49 наград получены!";
                claimButton.interactable = false;
                return;
            }

            claimButton.interactable = DailyRewardService.CanClaimToday();
            statusLabel.text = DailyRewardService.CanClaimToday()
                ? "Сегодня доступна новая награда."
                : "Возвращайтесь завтра за следующей наградой.";
        }

        private IEnumerator FadeRoutine(float targetAlpha, bool interactable)
        {
            var start = panelGroup.alpha;
            var elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = fadeDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / fadeDuration);
                panelGroup.alpha = Mathf.Lerp(start, targetAlpha, t);
                yield return null;
            }

            panelGroup.alpha = targetAlpha;
            panelGroup.interactable = interactable;
            panelGroup.blocksRaycasts = interactable;
            transitionCoroutine = null;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
