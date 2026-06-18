using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuStatsPanel : MonoBehaviour
    {
        private static Sprite whiteSprite;

        private static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.08f, 0.94f);
        private static readonly Color RowBackgroundColor = new Color(0.12f, 0.14f, 0.17f, 0.88f);
        private static readonly Color TitleColor = new Color(0.94f, 0.96f, 0.98f, 0.98f);
        private static readonly Color LabelColor = new Color(0.78f, 0.84f, 0.9f, 0.94f);
        private static readonly Color ValueColor = new Color(0.95f, 0.97f, 0.99f, 0.98f);
        private static readonly Color AccentValueColor = new Color(0.95f, 0.82f, 0.35f, 1f);
        private static readonly Color MutedColor = new Color(0.72f, 0.78f, 0.84f, 0.92f);
        private static readonly Color ScrollTrackColor = new Color(0.1f, 0.12f, 0.14f, 0.55f);
        private static readonly Color ScrollHandleColor = new Color(0.24f, 0.28f, 0.32f, 0.92f);

        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float leftReservedWidth = 228f;
        [SerializeField] private float topReservedHeight = 92f;
        [SerializeField] private float innerPadding = 28f;
        [SerializeField] private float fadeDuration = 0.38f;
        [SerializeField] private float headerHeight = 88f;
        [SerializeField] private float titleFontSize = 30f;
        [SerializeField] private float cardHeight = 96f;
        [SerializeField] private float cardSpacing = 16f;
        [SerializeField] private float scrollbarWidth = 10f;
        [SerializeField] private float scrollbarGap = 10f;

        private CanvasGroup panelGroup;
        private RectTransform panelRect;
        private MainMenuUiSoundController uiSound;
        private bool isVisible;
        private Coroutine transitionCoroutine;

        private TMP_Text kdValueLabel;
        private TMP_Text killsValueLabel;
        private TMP_Text deathsValueLabel;
        private TMP_Text avgPlacementValueLabel;
        private TMP_Text winsValueLabel;
        private TMP_Text avgDamageValueLabel;
        private TMP_Text matchesValueLabel;

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

            var panelObject = new GameObject("MainMenuStatsPanel");
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
            BuildContent(panelObject.transform);
            RefreshStats();
        }

        private void OnEnable()
        {
            PlayerProfileService.ProfileSynced += RefreshStats;
        }

        private void OnDisable()
        {
            PlayerProfileService.ProfileSynced -= RefreshStats;
        }

        public void Show()
        {
            if (isVisible)
            {
                RefreshStats();
                return;
            }

            isVisible = true;
            RefreshStats();
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
            title.text = "Статистика";
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
            subtitle.text = "Сводка по вашему профилю";
            subtitle.fontSize = 17f;
            subtitle.alignment = TextAlignmentOptions.Center;
            subtitle.color = MutedColor;
            subtitle.raycastTarget = false;
        }

        private void BuildContent(Transform parent)
        {
            var scrollObject = new GameObject("StatsScroll");
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
            var contentRect = contentObject.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var grid = contentObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(320f, cardHeight);
            grid.spacing = new Vector2(cardSpacing, cardSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(0, 0, 4, 12);

            var fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;

            kdValueLabel = CreateStatCard(contentRect.transform, "K/D", accent: true);
            killsValueLabel = CreateStatCard(contentRect.transform, "Киллы");
            deathsValueLabel = CreateStatCard(contentRect.transform, "Смерти");
            avgPlacementValueLabel = CreateStatCard(contentRect.transform, "Средний топ");
            winsValueLabel = CreateStatCard(contentRect.transform, "Побед", accent: true);
            avgDamageValueLabel = CreateStatCard(contentRect.transform, "Средний урон");
            matchesValueLabel = CreateStatCard(contentRect.transform, "Матчей сыграно");
        }

        private TMP_Text CreateStatCard(Transform parent, string labelText, bool accent = false)
        {
            var cardObject = new GameObject("Stat_" + labelText);
            cardObject.transform.SetParent(parent, false);

            var cardLayout = cardObject.AddComponent<LayoutElement>();
            cardLayout.preferredHeight = cardHeight;

            var background = cardObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.color = RowBackgroundColor;
            background.raycastTarget = false;

            var labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(cardObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.offsetMin = new Vector2(16f, -34f);
            labelRect.offsetMax = new Vector2(-16f, -10f);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = labelText;
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = LabelColor;
            label.raycastTarget = false;

            var valueObject = new GameObject("Value", typeof(RectTransform));
            valueObject.transform.SetParent(cardObject.transform, false);
            var valueRect = valueObject.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0f, 0f);
            valueRect.anchorMax = new Vector2(1f, 0f);
            valueRect.pivot = new Vector2(0.5f, 0f);
            valueRect.offsetMin = new Vector2(16f, 14f);
            valueRect.offsetMax = new Vector2(-16f, 52f);

            var value = valueObject.AddComponent<TextMeshProUGUI>();
            value.text = "—";
            value.fontSize = 30f;
            value.fontStyle = FontStyles.Bold;
            value.alignment = TextAlignmentOptions.BottomLeft;
            value.color = accent ? AccentValueColor : ValueColor;
            value.raycastTarget = false;

            return value;
        }

        private void RefreshStats()
        {
            if (kdValueLabel == null)
            {
                return;
            }

            if (!PlayerProfileService.IsServerSynced)
            {
                SetUnavailableStats();
                return;
            }

            var stats = PlayerProfileService.GetMatchStats();
            kdValueLabel.text = stats.totalDeaths > 0
                ? stats.kdRatio.ToString("0.00")
                : stats.totalKills.ToString("0.00");
            killsValueLabel.text = stats.totalKills.ToString("N0");
            deathsValueLabel.text = stats.totalDeaths.ToString("N0");
            avgPlacementValueLabel.text = stats.matchCount > 0
                ? "#" + stats.avgPlacement.ToString("0.0")
                : "—";
            winsValueLabel.text = stats.totalWins.ToString("N0");
            avgDamageValueLabel.text = stats.matchCount > 0
                ? stats.avgDamage.ToString("0")
                : "0";
            matchesValueLabel.text = stats.matchCount.ToString("N0");
        }

        private void SetUnavailableStats()
        {
            kdValueLabel.text = "—";
            killsValueLabel.text = "—";
            deathsValueLabel.text = "—";
            avgPlacementValueLabel.text = "—";
            winsValueLabel.text = "—";
            avgDamageValueLabel.text = "—";
            matchesValueLabel.text = "—";
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

        private System.Collections.IEnumerator TransitionRoutine(bool show)
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
