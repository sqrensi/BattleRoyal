using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuAchievementsPanel : MonoBehaviour
    {
        private static Sprite whiteSprite;

        private static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.08f, 0.94f);
        private static readonly Color ItemBackgroundColor = new Color(0.12f, 0.14f, 0.17f, 0.88f);
        private static readonly Color CompletedBackgroundColor = new Color(0.14f, 0.22f, 0.2f, 0.96f);
        private static readonly Color CompletedBorderColor = new Color(0.22f, 0.58f, 0.5f, 0.85f);
        private static readonly Color TitleColor = new Color(0.94f, 0.96f, 0.98f, 0.98f);
        private static readonly Color MutedColor = new Color(0.72f, 0.78f, 0.84f, 0.92f);
        private static readonly Color RewardBadgeColor = new Color(0.18f, 0.15f, 0.08f, 0.96f);
        private static readonly Color RewardTextColor = new Color(0.95f, 0.82f, 0.35f, 1f);
        private static readonly Color ProgressBadgeColor = new Color(0.08f, 0.1f, 0.12f, 0.92f);
        private static readonly Color ScrollTrackColor = new Color(0.1f, 0.12f, 0.14f, 0.55f);
        private static readonly Color ScrollHandleColor = new Color(0.24f, 0.28f, 0.32f, 0.92f);

        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float leftReservedWidth = 228f;
        [SerializeField] private float topReservedHeight = 92f;
        [SerializeField] private float innerPadding = 28f;
        [SerializeField] private float fadeDuration = 0.38f;
        [SerializeField] private float itemSpacing = 16f;
        [SerializeField] private float headerHeight = 88f;
        [SerializeField] private float scrollbarWidth = 10f;
        [SerializeField] private float scrollbarGap = 10f;
        [SerializeField] private float titleFontSize = 30f;
        [SerializeField] private float rowHeight = 168f;

        private readonly List<AchievementRowVisual> rows = new List<AchievementRowVisual>(16);

        private CanvasGroup panelGroup;
        private RectTransform panelRect;
        private RectTransform contentRect;
        private MainMenuUiSoundController uiSound;
        private bool isVisible;
        private bool claimInProgress;
        private Coroutine transitionCoroutine;

        private sealed class AchievementRowVisual
        {
            public PlayerAchievementEntry Entry;
            public Image Background;
            public Button ClaimButton;
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

            var panelObject = new GameObject("MainMenuAchievementsPanel");
            panelObject.transform.SetParent(canvasRect, false);

            panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = new Vector2(leftReservedWidth, edgeMargin);
            panelRect.offsetMax = new Vector2(-edgeMargin, -topReservedHeight);

            var background = panelObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.type = Image.Type.Simple;
            background.color = PanelColor;
            background.raycastTarget = true;

            panelGroup = panelObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;

            BuildHeader(panelObject.transform);
            BuildList(panelObject.transform);
        }

        private void OnEnable()
        {
            PlayerProfileService.ProfileSynced += RebuildList;
        }

        private void OnDisable()
        {
            PlayerProfileService.ProfileSynced -= RebuildList;
        }

        public void Show()
        {
            if (isVisible)
            {
                RebuildList();
                return;
            }

            isVisible = true;
            RebuildList();
            StartTransition(show: true);
        }

        public void Hide()
        {
            if (!isVisible)
            {
                return;
            }

            isVisible = false;
            StartTransition(show: false);
        }

        private void BuildHeader(Transform parent)
        {
            var headerObject = new GameObject("Header");
            headerObject.transform.SetParent(parent, false);

            var rect = headerObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(innerPadding, -(headerHeight + innerPadding * 0.35f));
            rect.offsetMax = new Vector2(-innerPadding, -innerPadding * 0.35f);

            var title = headerObject.AddComponent<TextMeshProUGUI>();
            title.text = "Достижения";
            title.fontSize = titleFontSize;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            title.color = TitleColor;
            title.raycastTarget = false;

            var subtitleObject = new GameObject("Subtitle", typeof(RectTransform));
            subtitleObject.transform.SetParent(headerObject.transform, false);
            var subtitleRect = subtitleObject.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0f, 0f);
            subtitleRect.anchorMax = new Vector2(1f, 0f);
            subtitleRect.pivot = new Vector2(0.5f, 0f);
            subtitleRect.offsetMin = new Vector2(0f, 2f);
            subtitleRect.offsetMax = new Vector2(0f, 22f);
            var subtitle = subtitleObject.AddComponent<TextMeshProUGUI>();
            subtitle.text = "Выполняйте задания и забирайте награды";
            subtitle.fontSize = 17f;
            subtitle.alignment = TextAlignmentOptions.Center;
            subtitle.color = MutedColor;
            subtitle.raycastTarget = false;
        }

        private void BuildList(Transform parent)
        {
            var scrollObject = new GameObject("AchievementsScroll");
            scrollObject.transform.SetParent(parent, false);

            var scrollRectTransform = scrollObject.AddComponent<RectTransform>();
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = new Vector2(innerPadding, innerPadding);
            scrollRectTransform.offsetMax = new Vector2(-innerPadding, -(headerHeight + innerPadding));

            var scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            var viewportObject = new GameObject("Viewport");
            viewportObject.transform.SetParent(scrollObject.transform, false);
            var viewportRect = viewportObject.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = new Vector2(-(scrollbarWidth + scrollbarGap), 0f);
            viewportObject.AddComponent<RectMask2D>();

            var scrollbar = CreateVerticalScrollbar(scrollObject.transform);
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            var contentObject = new GameObject("Content");
            contentObject.transform.SetParent(viewportObject.transform, false);
            contentRect = contentObject.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var layout = contentObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = itemSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(0, 0, 4, 8);

            var fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;

            RebuildList();
        }

        private void RebuildList()
        {
            if (contentRect == null)
            {
                return;
            }

            for (var i = contentRect.childCount - 1; i >= 0; i--)
            {
                var child = contentRect.GetChild(i);
                if (child != null)
                {
                    Destroy(child.gameObject);
                }
            }

            rows.Clear();
            var achievements = PlayerProfileService.GetActiveAchievements();
            if (achievements == null || achievements.Count == 0)
            {
                rows.Add(CreateEmptyRow(contentRect, "Нет активных достижений."));
                return;
            }

            for (var i = 0; i < achievements.Count; i++)
            {
                var entry = achievements[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.achievementId))
                {
                    continue;
                }

                rows.Add(CreateAchievementRow(contentRect, entry));
            }

            if (rows.Count == 0)
            {
                rows.Add(CreateEmptyRow(contentRect, "Нет активных достижений."));
            }
        }

        private AchievementRowVisual CreateEmptyRow(Transform parent, string message)
        {
            var rowObject = new GameObject("EmptyRow");
            rowObject.transform.SetParent(parent, false);
            var layout = rowObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 72f;

            var text = rowObject.AddComponent<TextMeshProUGUI>();
            text.text = message;
            text.fontSize = 20f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = MutedColor;
            text.raycastTarget = false;

            return new AchievementRowVisual();
        }

        private AchievementRowVisual CreateAchievementRow(Transform parent, PlayerAchievementEntry entry)
        {
            var rowObject = new GameObject("Achievement_" + entry.achievementId);
            rowObject.transform.SetParent(parent, false);

            var rowLayout = rowObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = rowHeight;
            rowLayout.minHeight = rowHeight;

            var background = rowObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.type = Image.Type.Simple;
            background.color = entry.completed ? CompletedBackgroundColor : ItemBackgroundColor;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(rowObject.transform, false);
            var contentRect = contentObject.GetComponent<RectTransform>();
            StretchFull(contentRect);
            contentRect.offsetMin = new Vector2(18f, 14f);
            contentRect.offsetMax = new Vector2(-18f, -14f);

            var contentLayout = contentObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 10f;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var topRowObject = new GameObject("TopRow", typeof(RectTransform));
            topRowObject.transform.SetParent(contentObject.transform, false);
            var topRowLayout = topRowObject.AddComponent<LayoutElement>();
            topRowLayout.preferredHeight = 34f;
            var topRow = topRowObject.AddComponent<HorizontalLayoutGroup>();
            topRow.spacing = 12f;
            topRow.childAlignment = TextAnchor.MiddleLeft;
            topRow.childControlWidth = true;
            topRow.childControlHeight = true;
            topRow.childForceExpandWidth = false;
            topRow.childForceExpandHeight = true;

            var titleObject = new GameObject("Title", typeof(RectTransform));
            titleObject.transform.SetParent(topRowObject.transform, false);
            var titleLayout = titleObject.AddComponent<LayoutElement>();
            titleLayout.flexibleWidth = 1f;
            titleLayout.minWidth = 120f;
            var title = titleObject.AddComponent<TextMeshProUGUI>();
            title.text = entry.title ?? entry.achievementId;
            title.fontSize = 24f;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.color = TitleColor;
            title.raycastTarget = false;

            var progressBadgeObject = CreateBadge(
                topRowObject.transform,
                $"{Mathf.Clamp(entry.progress, 0, entry.target)}/{Mathf.Max(1, entry.target)}",
                ProgressBadgeColor,
                TitleColor,
                88f,
                30f);
            progressBadgeObject.name = "ProgressBadge";

            var descriptionObject = new GameObject("Description", typeof(RectTransform));
            descriptionObject.transform.SetParent(contentObject.transform, false);
            var descriptionLayout = descriptionObject.AddComponent<LayoutElement>();
            descriptionLayout.preferredHeight = 44f;
            var description = descriptionObject.AddComponent<TextMeshProUGUI>();
            description.text = entry.description ?? string.Empty;
            description.fontSize = 18f;
            description.alignment = TextAlignmentOptions.TopLeft;
            description.color = MutedColor;
            description.raycastTarget = false;

            var bottomRowObject = new GameObject("BottomRow", typeof(RectTransform));
            bottomRowObject.transform.SetParent(contentObject.transform, false);
            var bottomRowLayout = bottomRowObject.AddComponent<LayoutElement>();
            bottomRowLayout.preferredHeight = 40f;
            var bottomRow = bottomRowObject.AddComponent<HorizontalLayoutGroup>();
            bottomRow.spacing = 12f;
            bottomRow.childAlignment = TextAnchor.MiddleLeft;
            bottomRow.childControlWidth = true;
            bottomRow.childControlHeight = true;
            bottomRow.childForceExpandWidth = false;
            bottomRow.childForceExpandHeight = true;

            var rewardBadgeObject = CreateBadge(
                bottomRowObject.transform,
                FormatReward(entry),
                RewardBadgeColor,
                RewardTextColor,
                0f,
                34f,
                flexibleWidth: true);
            rewardBadgeObject.name = "RewardBadge";

            Button claimButton = null;
            if (entry.completed)
            {
                var claimButtonObject = new GameObject("ClaimButton", typeof(RectTransform));
                claimButtonObject.transform.SetParent(bottomRowObject.transform, false);
                var claimLayout = claimButtonObject.AddComponent<LayoutElement>();
                claimLayout.preferredWidth = 188f;
                claimLayout.minHeight = 36f;

                var claimImage = claimButtonObject.AddComponent<Image>();
                claimImage.sprite = GetWhiteSprite();
                claimImage.color = CompletedBorderColor;

                claimButton = claimButtonObject.AddComponent<Button>();
                claimButton.targetGraphic = claimImage;
                claimButton.onClick.AddListener(() => OnClaimClicked(entry));

                var claimLabelObject = new GameObject("Label", typeof(RectTransform));
                claimLabelObject.transform.SetParent(claimButtonObject.transform, false);
                var claimLabelRect = claimLabelObject.GetComponent<RectTransform>();
                StretchFull(claimLabelRect);
                var claimLabel = claimLabelObject.AddComponent<TextMeshProUGUI>();
                claimLabel.text = "Забрать награду";
                claimLabel.fontSize = 17f;
                claimLabel.fontStyle = FontStyles.Bold;
                claimLabel.alignment = TextAlignmentOptions.Center;
                claimLabel.color = TitleColor;
                claimLabel.raycastTarget = false;
            }

            return new AchievementRowVisual
            {
                Entry = entry,
                Background = background,
                ClaimButton = claimButton
            };
        }

        private static GameObject CreateBadge(
            Transform parent,
            string text,
            Color backgroundColor,
            Color textColor,
            float preferredWidth,
            float preferredHeight,
            bool flexibleWidth = false)
        {
            var badgeObject = new GameObject("Badge", typeof(RectTransform));
            badgeObject.transform.SetParent(parent, false);

            var layout = badgeObject.AddComponent<LayoutElement>();
            layout.preferredHeight = preferredHeight;
            if (flexibleWidth)
            {
                layout.flexibleWidth = 1f;
            }
            else if (preferredWidth > 0f)
            {
                layout.preferredWidth = preferredWidth;
            }

            var background = badgeObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.color = backgroundColor;
            background.raycastTarget = false;

            var labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(badgeObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            StretchFull(labelRect);
            labelRect.offsetMin = new Vector2(12f, 4f);
            labelRect.offsetMax = new Vector2(-12f, -4f);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 16f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = textColor;
            label.raycastTarget = false;

            return badgeObject;
        }

        private static string FormatReward(PlayerAchievementEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            if (string.Equals(entry.rewardType, "currency", System.StringComparison.OrdinalIgnoreCase))
            {
                return $"Награда: {entry.rewardAmount:N0} монет";
            }

            if (string.Equals(entry.rewardType, "case", System.StringComparison.OrdinalIgnoreCase))
            {
                var caseId = string.IsNullOrWhiteSpace(entry.rewardCaseId) ? "кейс" : entry.rewardCaseId;
                return entry.rewardAmount > 1
                    ? $"Награда: {entry.rewardAmount} x {caseId}"
                    : $"Награда: кейс {caseId}";
            }

            return "Награда";
        }

        private void OnClaimClicked(PlayerAchievementEntry entry)
        {
            if (entry == null || claimInProgress || !entry.completed)
            {
                return;
            }

            var menu = FindObjectOfType<MainMenuController>();
            if (menu == null || menu.ProfileApiClient == null)
            {
                return;
            }

            StartCoroutine(ClaimAchievementRoutine(menu, entry.achievementId));
        }

        private IEnumerator ClaimAchievementRoutine(MainMenuController menu, string achievementId)
        {
            claimInProgress = true;
            RefreshClaimButtons(false);

            var success = false;
            yield return PlayerProfileService.ClaimAchievement(
                this,
                menu.ProfileApiClient,
                menu.LocalPlayerId,
                achievementId,
                (ok, _) => success = ok);

            claimInProgress = false;
            uiSound?.PlayButton();
            RebuildList();
            RefreshClaimButtons(true);

            if (!success)
            {
                yield break;
            }
        }

        private void RefreshClaimButtons(bool interactable)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].ClaimButton != null)
                {
                    rows[i].ClaimButton.interactable = interactable && !claimInProgress;
                }
            }
        }

        private Scrollbar CreateVerticalScrollbar(Transform parent)
        {
            var scrollbarObject = new GameObject("Scrollbar Vertical");
            scrollbarObject.transform.SetParent(parent, false);

            var rect = scrollbarObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(scrollbarWidth, 0f);
            rect.anchoredPosition = Vector2.zero;

            var trackImage = scrollbarObject.AddComponent<Image>();
            trackImage.sprite = GetWhiteSprite();
            trackImage.type = Image.Type.Simple;
            trackImage.color = ScrollTrackColor;

            var scrollbar = scrollbarObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var slidingAreaObject = new GameObject("Sliding Area");
            slidingAreaObject.transform.SetParent(scrollbarObject.transform, false);
            var slidingAreaRect = slidingAreaObject.AddComponent<RectTransform>();
            StretchFull(slidingAreaRect);
            slidingAreaRect.offsetMin = new Vector2(2f, 8f);
            slidingAreaRect.offsetMax = new Vector2(-2f, -8f);

            var handleObject = new GameObject("Handle");
            handleObject.transform.SetParent(slidingAreaObject.transform, false);
            var handleRect = handleObject.AddComponent<RectTransform>();
            StretchFull(handleRect);

            var handleImage = handleObject.AddComponent<Image>();
            handleImage.sprite = GetWhiteSprite();
            handleImage.type = Image.Type.Simple;
            handleImage.color = ScrollHandleColor;

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            return scrollbar;
        }

        private void StartTransition(bool show)
        {
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
            }

            transitionCoroutine = StartCoroutine(TransitionRoutine(show));
        }

        private IEnumerator TransitionRoutine(bool show)
        {
            var fromAlpha = show ? 0f : 1f;
            var toAlpha = show ? 1f : 0f;

            if (show)
            {
                panelGroup.blocksRaycasts = true;
            }
            else
            {
                panelGroup.interactable = false;
            }

            var elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = fadeDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / fadeDuration);
                var eased = EaseInOut(t);
                panelGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, eased);
                yield return null;
            }

            panelGroup.alpha = toAlpha;
            panelGroup.interactable = show;
            panelGroup.blocksRaycasts = show;
            transitionCoroutine = null;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static float EaseInOut(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static Sprite GetWhiteSprite()
        {
            if (whiteSprite != null)
            {
                return whiteSprite;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.SetPixel(1, 0, Color.white);
            texture.SetPixel(0, 1, Color.white);
            texture.SetPixel(1, 1, Color.white);
            texture.Apply(false, false);
            whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 100f);
            return whiteSprite;
        }
    }
}
