using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuSettingsPanel : MonoBehaviour
    {
        private static Sprite whiteSprite;

        private static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.08f, 0.94f);
        private static readonly Color SectionColor = new Color(0.1f, 0.12f, 0.15f, 0.92f);
        private static readonly Color TitleColor = new Color(0.94f, 0.96f, 0.98f, 0.98f);
        private static readonly Color LabelColor = new Color(0.78f, 0.84f, 0.9f, 0.94f);
        private static readonly Color ValueColor = new Color(0.95f, 0.97f, 0.99f, 0.98f);
        private static readonly Color MutedColor = new Color(0.72f, 0.78f, 0.84f, 0.92f);
        private static readonly Color ScrollTrackColor = new Color(0.1f, 0.12f, 0.14f, 0.55f);
        private static readonly Color ScrollHandleColor = new Color(0.24f, 0.28f, 0.32f, 0.92f);
        private static readonly Color ToggleOnColor = new Color(0.18f, 0.48f, 0.42f, 0.96f);
        private static readonly Color ToggleOffColor = new Color(0.12f, 0.14f, 0.17f, 0.88f);

        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float leftReservedWidth = 228f;
        [SerializeField] private float topReservedHeight = 92f;
        [SerializeField] private float innerPadding = 28f;
        [SerializeField] private float fadeDuration = 0.38f;
        [SerializeField] private float headerHeight = 88f;
        [SerializeField] private float titleFontSize = 30f;
        [SerializeField] private float rowHeight = 72f;
        [SerializeField] private float rowSpacing = 12f;
        [SerializeField] private float sectionSpacing = 24f;
        [SerializeField] private float scrollbarWidth = 10f;
        [SerializeField] private float scrollbarGap = 10f;

        private CanvasGroup panelGroup;
        private RectTransform panelRect;
        private RectTransform contentRect;
        private MainMenuUiSoundController uiSound;
        private bool isVisible;
        private bool suppressRefresh;
        private Coroutine transitionCoroutine;

        private readonly List<SliderRowBinding> sliderRows = new List<SliderRowBinding>(16);
        private readonly List<ToggleRowBinding> toggleRows = new List<ToggleRowBinding>(4);

        private sealed class SliderRowBinding
        {
            public Slider Slider;
            public TMP_Text ValueLabel;
            public System.Action<float> Setter;
            public System.Func<float> Getter;
            public string ValueFormat;
            public bool PercentFormat;
            public System.Func<float, string> ValueLabelFormatter;
        }

        private sealed class ToggleRowBinding
        {
            public Button Button;
            public Image ButtonImage;
            public TMP_Text ValueLabel;
            public System.Func<bool> Getter;
            public System.Action<bool> Setter;
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

            var panelObject = new GameObject("MainMenuSettingsPanel");
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
            BuildScrollContent(panelObject.transform);
            RefreshFromSettings();
        }

        private void OnEnable()
        {
            ClientSettingsService.SettingsChanged += RefreshFromSettings;
        }

        private void OnDisable()
        {
            ClientSettingsService.SettingsChanged -= RefreshFromSettings;
        }

        public void Show()
        {
            if (isVisible)
            {
                RefreshFromSettings();
                return;
            }

            isVisible = true;
            RefreshFromSettings();
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
            rect.offsetMin = new Vector2(innerPadding, -(headerHeight + innerPadding * 0.5f));
            rect.offsetMax = new Vector2(-innerPadding, -innerPadding * 0.5f);

            var title = headerObject.AddComponent<TextMeshProUGUI>();
            title.text = "Настройки";
            title.fontSize = titleFontSize;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            title.color = TitleColor;
            title.raycastTarget = false;
        }

        private void BuildScrollContent(Transform parent)
        {
            var scrollObject = new GameObject("SettingsScroll");
            scrollObject.transform.SetParent(parent, false);

            var scrollRectTransform = scrollObject.AddComponent<RectTransform>();
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = new Vector2(innerPadding, innerPadding);
            scrollRectTransform.offsetMax = new Vector2(-innerPadding, -(headerHeight + innerPadding));

            var scroll = scrollObject.AddComponent<ScrollRect>();
            MainMenuScrollSupport.ConfigureVerticalScroll(scroll);

            var viewportObject = new GameObject("Viewport");
            viewportObject.transform.SetParent(scrollObject.transform, false);
            var viewportRect = viewportObject.AddComponent<RectTransform>();
            StretchFull(viewportRect);
            viewportRect.offsetMax = new Vector2(-(scrollbarWidth + scrollbarGap), 0f);
            viewportObject.AddComponent<RectMask2D>();
            MainMenuScrollSupport.EnableViewportScrollCapture(viewportObject, GetWhiteSprite());

            var scrollbar = MainMenuScrollSupport.CreateVerticalScrollbar(
                scrollObject.transform,
                scrollbarWidth,
                ScrollTrackColor,
                ScrollHandleColor,
                GetWhiteSprite());
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
            MainMenuScrollSupport.EnableContentScrollCapture(contentObject, GetWhiteSprite());

            var layout = contentObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = rowSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(0, 0, 4, 12);

            var fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;

            BuildSettingsRows(contentObject.transform);
        }

        private void BuildSettingsRows(Transform parent)
        {
            CreateSectionTitle(parent, "Графика");
            CreateSliderRow(
                parent,
                "Масштаб рендера",
                0.65f,
                1f,
                () => ClientSettingsService.RenderScale,
                ClientSettingsService.SetRenderScale,
                percentFormat: true);
            CreateToggleRow(
                parent,
                "Тени",
                () => ClientSettingsService.ShadowsEnabled,
                ClientSettingsService.SetShadowsEnabled);
            CreateToggleRow(
                parent,
                "Постобработка",
                () => ClientSettingsService.PostProcessingEnabled,
                ClientSettingsService.SetPostProcessingEnabled);
            CreateSliderRow(
                parent,
                "Сглаживание",
                0f,
                3f,
                () => ClientSettingsService.MsaaIndexFromSampleCount(ClientSettingsService.MsaaSampleCount),
                index => ClientSettingsService.SetMsaaSampleCount(
                    ClientSettingsService.MsaaSampleCountFromIndex(Mathf.RoundToInt(index))),
                wholeNumbers: true,
                valueLabelFormatter: index =>
                    ClientSettingsService.GetMsaaLabel(
                        ClientSettingsService.MsaaSampleCountFromIndex(Mathf.RoundToInt(index))));
            CreateSliderRow(
                parent,
                "Качество текстур",
                0f,
                2f,
                () => ClientSettingsService.TextureMipmapLimit,
                value => ClientSettingsService.SetTextureMipmapLimit(Mathf.RoundToInt(value)),
                wholeNumbers: true,
                valueLabelFormatter: value =>
                    ClientSettingsService.GetTextureQualityLabel(Mathf.RoundToInt(value)));

            CreateSectionTitle(parent, "Звук");
            CreateSliderRow(
                parent,
                "Общая громкость",
                0f,
                1f,
                () => ClientSettingsService.MasterVolume,
                ClientSettingsService.SetMasterVolume,
                percentFormat: true);
            CreateSliderRow(
                parent,
                "Музыка",
                0f,
                1f,
                () => ClientSettingsService.MusicVolume,
                ClientSettingsService.SetMusicVolume,
                percentFormat: true);
            CreateSliderRow(
                parent,
                "Дождь",
                0f,
                1f,
                () => ClientSettingsService.RainVolume,
                ClientSettingsService.SetRainVolume,
                percentFormat: true);
            CreateSliderRow(
                parent,
                "Эффекты",
                0f,
                1f,
                () => ClientSettingsService.SfxVolume,
                ClientSettingsService.SetSfxVolume,
                percentFormat: true);

            CreateSectionTitle(parent, "Управление");
            CreateSliderRow(
                parent,
                "Чувствительность мыши",
                0.1f,
                10f,
                () => ClientSettingsService.MouseSensitivity,
                ClientSettingsService.SetMouseSensitivity,
                valueFormat: "0.0");

            CreateSectionTitle(parent, "Сенса в прицеле");
            CreateSliderRow(
                parent,
                "Общая сенса в прицеле",
                0.1f,
                3f,
                () => ClientSettingsService.GlobalAdsSensitivityMultiplier,
                ClientSettingsService.SetGlobalAdsSensitivity,
                valueFormat: "0.00");
            CreateAdsSlider(parent, WeaponKind.AssaultRifle);
            CreateAdsSlider(parent, WeaponKind.SniperRifle);
            CreateAdsSlider(parent, WeaponKind.Pistol);
            CreateAdsSlider(parent, WeaponKind.Mp7);
        }

        private void CreateAdsSlider(Transform parent, WeaponKind kind)
        {
            var label = ClientSettingsService.GetWeaponDisplayName(kind) + " (прицел)";
            CreateSliderRow(
                parent,
                label,
                0.1f,
                5f,
                () => ClientSettingsService.GetWeaponAdsSensitivity(kind),
                value => ClientSettingsService.SetAdsSensitivity(kind, value),
                valueFormat: "0.00");
        }

        private void CreateSectionTitle(Transform parent, string title)
        {
            var sectionObject = new GameObject("Section_" + title, typeof(RectTransform));
            sectionObject.transform.SetParent(parent, false);
            var layout = sectionObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 36f;
            layout.minHeight = 36f;

            var text = sectionObject.AddComponent<TextMeshProUGUI>();
            text.text = title;
            text.fontSize = 22f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.color = TitleColor;
            text.raycastTarget = false;
        }

        private void CreateToggleRow(
            Transform parent,
            string label,
            System.Func<bool> getter,
            System.Action<bool> setter)
        {
            var rowObject = new GameObject("ToggleRow_" + label, typeof(RectTransform));
            rowObject.transform.SetParent(parent, false);

            var rowLayout = rowObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = rowHeight;
            rowLayout.minHeight = rowHeight;

            var background = rowObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.color = SectionColor;
            background.raycastTarget = false;

            var labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(rowObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(0f, 0.5f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.anchoredPosition = new Vector2(16f, 0f);
            labelRect.sizeDelta = new Vector2(260f, 32f);
            var labelText = labelObject.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 18f;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = LabelColor;
            labelText.raycastTarget = false;

            var buttonObject = new GameObject("ToggleButton", typeof(RectTransform));
            buttonObject.transform.SetParent(rowObject.transform, false);
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-16f, 0f);
            buttonRect.sizeDelta = new Vector2(180f, 44f);

            var buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.sprite = GetWhiteSprite();
            buttonImage.color = ToggleOffColor;

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            var valueLabelObject = new GameObject("Value", typeof(RectTransform));
            valueLabelObject.transform.SetParent(buttonObject.transform, false);
            var valueLabelRect = valueLabelObject.GetComponent<RectTransform>();
            StretchFull(valueLabelRect);
            var valueLabel = valueLabelObject.AddComponent<TextMeshProUGUI>();
            valueLabel.fontSize = 17f;
            valueLabel.fontStyle = FontStyles.Bold;
            valueLabel.alignment = TextAlignmentOptions.Center;
            valueLabel.color = ValueColor;
            valueLabel.raycastTarget = false;

            var binding = new ToggleRowBinding
            {
                Button = button,
                ButtonImage = buttonImage,
                ValueLabel = valueLabel,
                Getter = getter,
                Setter = setter
            };
            toggleRows.Add(binding);

            button.onClick.AddListener(() =>
            {
                if (suppressRefresh || binding.Getter == null || binding.Setter == null)
                {
                    return;
                }

                binding.Setter(!binding.Getter());
                UpdateToggleLabel(binding);
                uiSound?.PlayButton();
            });
        }

        private void CreateSliderRow(
            Transform parent,
            string label,
            float min,
            float max,
            System.Func<float> getter,
            System.Action<float> setter,
            string valueFormat = null,
            bool percentFormat = false,
            bool wholeNumbers = false,
            System.Func<float, string> valueLabelFormatter = null)
        {
            var rowObject = new GameObject("Row_" + label, typeof(RectTransform));
            rowObject.transform.SetParent(parent, false);

            var rowLayout = rowObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = rowHeight;
            rowLayout.minHeight = rowHeight;

            var background = rowObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.color = SectionColor;
            background.raycastTarget = false;

            var labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(rowObject.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.offsetMin = new Vector2(16f, -30f);
            labelRect.offsetMax = new Vector2(-72f, -8f);
            var labelText = labelObject.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 17f;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = LabelColor;
            labelText.raycastTarget = false;

            var valueObject = new GameObject("Value", typeof(RectTransform));
            valueObject.transform.SetParent(rowObject.transform, false);
            var valueRect = valueObject.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(1f, 1f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 1f);
            valueRect.anchoredPosition = new Vector2(-16f, -8f);
            valueRect.sizeDelta = new Vector2(56f, 24f);
            var valueText = valueObject.AddComponent<TextMeshProUGUI>();
            valueText.fontSize = 16f;
            valueText.fontStyle = FontStyles.Bold;
            valueText.alignment = TextAlignmentOptions.MidlineRight;
            valueText.color = ValueColor;
            valueText.raycastTarget = false;

            var sliderObject = new GameObject("Slider", typeof(RectTransform));
            sliderObject.transform.SetParent(rowObject.transform, false);
            var sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0f);
            sliderRect.anchorMax = new Vector2(1f, 0f);
            sliderRect.pivot = new Vector2(0.5f, 0f);
            sliderRect.offsetMin = new Vector2(16f, 12f);
            sliderRect.offsetMax = new Vector2(-16f, 32f);

            var sliderBackgroundObject = new GameObject("Background", typeof(RectTransform));
            sliderBackgroundObject.transform.SetParent(sliderObject.transform, false);
            var sliderBackgroundRect = sliderBackgroundObject.GetComponent<RectTransform>();
            StretchFull(sliderBackgroundRect);
            var sliderBackground = sliderBackgroundObject.AddComponent<Image>();
            sliderBackground.sprite = GetWhiteSprite();
            sliderBackground.color = new Color(0.08f, 0.09f, 0.11f, 0.92f);

            var fillAreaObject = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaObject.transform.SetParent(sliderObject.transform, false);
            var fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            StretchFull(fillAreaRect);
            fillAreaRect.offsetMin = new Vector2(8f, 6f);
            fillAreaRect.offsetMax = new Vector2(-8f, -6f);

            var fillObject = new GameObject("Fill", typeof(RectTransform));
            fillObject.transform.SetParent(fillAreaObject.transform, false);
            var fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImage = fillObject.AddComponent<Image>();
            fillImage.sprite = GetWhiteSprite();
            fillImage.color = ToggleOnColor;

            var handleAreaObject = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaObject.transform.SetParent(sliderObject.transform, false);
            var handleAreaRect = handleAreaObject.GetComponent<RectTransform>();
            StretchFull(handleAreaRect);

            var handleObject = new GameObject("Handle", typeof(RectTransform));
            handleObject.transform.SetParent(handleAreaObject.transform, false);
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(18f, 0f);
            var handleImage = handleObject.AddComponent<Image>();
            handleImage.sprite = GetWhiteSprite();
            handleImage.color = ValueColor;

            var slider = sliderObject.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;

            var binding = new SliderRowBinding
            {
                Slider = slider,
                ValueLabel = valueText,
                Getter = getter,
                Setter = setter,
                ValueFormat = valueFormat,
                PercentFormat = percentFormat,
                ValueLabelFormatter = valueLabelFormatter
            };
            sliderRows.Add(binding);

            slider.onValueChanged.AddListener(value =>
            {
                if (suppressRefresh)
                {
                    return;
                }

                binding.Setter?.Invoke(value);
                UpdateSliderLabel(binding, value);
            });
        }

        private void RefreshFromSettings()
        {
            if (contentRect == null)
            {
                return;
            }

            suppressRefresh = true;

            for (var i = 0; i < sliderRows.Count; i++)
            {
                var binding = sliderRows[i];
                if (binding?.Slider == null || binding.Getter == null)
                {
                    continue;
                }

                var value = binding.Getter();
                binding.Slider.SetValueWithoutNotify(value);
                UpdateSliderLabel(binding, value);
            }

            for (var i = 0; i < toggleRows.Count; i++)
            {
                UpdateToggleLabel(toggleRows[i]);
            }

            suppressRefresh = false;
        }

        private static void UpdateToggleLabel(ToggleRowBinding binding)
        {
            if (binding?.ValueLabel == null || binding.Getter == null)
            {
                return;
            }

            var enabled = binding.Getter();
            binding.ValueLabel.text = enabled ? "Вкл" : "Выкл";
            if (binding.ButtonImage != null)
            {
                binding.ButtonImage.color = enabled ? ToggleOnColor : ToggleOffColor;
            }
        }

        private static void UpdateSliderLabel(SliderRowBinding binding, float value)
        {
            if (binding?.ValueLabel == null)
            {
                return;
            }

            if (binding.ValueLabelFormatter != null)
            {
                binding.ValueLabel.text = binding.ValueLabelFormatter(value);
                return;
            }

            if (binding.PercentFormat)
            {
                binding.ValueLabel.text = Mathf.RoundToInt(value * 100f) + "%";
                return;
            }

            binding.ValueLabel.text = string.IsNullOrWhiteSpace(binding.ValueFormat)
                ? value.ToString("0.##")
                : value.ToString(binding.ValueFormat);
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
                panelGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, EaseInOut(t));
                yield return null;
            }

            panelGroup.alpha = toAlpha;
            panelGroup.interactable = show;
            panelGroup.blocksRaycasts = show;
            transitionCoroutine = null;
        }

        private static float EaseInOut(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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
