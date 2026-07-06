using System;
using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Platform;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuSettingsPanel : MonoBehaviour
    {
        private const int SettingsLayoutVersion = 4;

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
        private Canvas hostCanvas;
        private MainMenuUiSoundController uiSound;
        private bool isVisible;
        private bool suppressRefresh;
        private bool useMenuBackdrop = true;
        private bool instantTransitions;
        private int builtLayoutVersion;
        private Coroutine transitionCoroutine;
        private Action backHandler;

        private readonly List<SliderRowBinding> sliderRows = new List<SliderRowBinding>(16);
        private readonly List<ToggleRowBinding> toggleRows = new List<ToggleRowBinding>(4);

        private Button yandexAccountButton;
        private TMP_Text yandexAccountValueLabel;
        private Coroutine yandexAccountLinkCoroutine;

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
            public System.Func<bool> Interactable;
        }

        public void Configure(MainMenuUiSoundController sound)
        {
            uiSound = sound;
        }

        public void ConfigureLayout(
            float edgeMarginOverride = 28f,
            float leftReservedWidthOverride = 228f,
            float topReservedHeightOverride = 92f)
        {
            edgeMargin = edgeMarginOverride;
            leftReservedWidth = leftReservedWidthOverride;
            topReservedHeight = topReservedHeightOverride;
        }

        public void SetBackHandler(Action handler)
        {
            backHandler = handler;
        }

        public void SetUseMenuBackdrop(bool enabled)
        {
            useMenuBackdrop = enabled;
        }

        public void SetInstantTransitions(bool enabled)
        {
            instantTransitions = enabled;
        }

        public void Build(RectTransform canvasRect)
        {
            if (canvasRect == null)
            {
                return;
            }

            if (panelRect != null)
            {
                if (builtLayoutVersion >= SettingsLayoutVersion)
                {
                    return;
                }

                Destroy(panelRect.gameObject);
                panelRect = null;
                contentRect = null;
                panelGroup = null;
                sliderRows.Clear();
                toggleRows.Clear();
            }

            hostCanvas = canvasRect.GetComponent<Canvas>();

            var panelObject = new GameObject("MainMenuSettingsPanel");
            panelObject.transform.SetParent(canvasRect, false);

            panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = new Vector2(leftReservedWidth, edgeMargin);
            panelRect.offsetMax = new Vector2(-edgeMargin, -topReservedHeight);

            var background = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(background, UiPanelStyle.Heavy);
            UiDecor.AttachPanelChrome(panelRect, 12f, 16f);

            panelGroup = panelObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;

            BuildHeader(panelObject.transform);
            BuildScrollContent(panelObject.transform);
            builtLayoutVersion = SettingsLayoutVersion;
            RefreshFromSettings();
        }

        private void OnEnable()
        {
            ClientSettingsService.SettingsChanged += RefreshFromSettings;
        }

        private void OnDisable()
        {
            ClientSettingsService.SettingsChanged -= RefreshFromSettings;
            StopTransition();
            ApplyVisibilityImmediate(visible: false);
        }

        public void Show()
        {
            if (isVisible)
            {
                RefreshFromSettings();
                ApplyVisibilityImmediate(visible: true);
                return;
            }

            isVisible = true;
            if (useMenuBackdrop)
            {
                UiMenuBackdrop.PushOpen(hostCanvas);
            }

            RefreshFromSettings();
            if (instantTransitions)
            {
                ApplyVisibilityImmediate(visible: true);
                return;
            }

            StartTransition(show: true);
        }

        public void Hide()
        {
            if (!isVisible)
            {
                ApplyVisibilityImmediate(visible: false);
                return;
            }

            isVisible = false;
            if (useMenuBackdrop)
            {
                UiMenuBackdrop.PopClosed();
            }

            if (instantTransitions)
            {
                ApplyVisibilityImmediate(visible: false);
                return;
            }

            StartTransition(show: false);
        }

        public void HideImmediate()
        {
            if (isVisible && useMenuBackdrop)
            {
                UiMenuBackdrop.PopClosed();
            }

            isVisible = false;
            StopTransition();
            ApplyVisibilityImmediate(visible: false);
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
            title.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyMilitaryHeader(title, UiTextRole.Heading);

            if (backHandler != null)
            {
                BuildBackButton(headerObject.transform);
            }
        }

        private void BuildBackButton(Transform header)
        {
            var buttonObject = new GameObject("BackButton");
            buttonObject.transform.SetParent(header, false);

            var rect = buttonObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(120f, 40f);

            buttonObject.AddComponent<Image>();

            var button = buttonObject.AddComponent<Button>();
            UiTheme.StyleButton(button, UiButtonStyle.Standard);
            button.onClick.AddListener(() =>
            {
                uiSound?.PlayButton();
                backHandler?.Invoke();
            });

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = "Назад";
            label.fontSize = 20f;
            label.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(label, UiTextRole.Body);
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
            MainMenuScrollSupport.EnableViewportScrollCapture(viewportObject, UiTheme.WhiteSprite);

            var scrollbar = MainMenuScrollSupport.CreateVerticalScrollbar(
                scrollObject.transform,
                scrollbarWidth,
                UiTheme.ScrollTrack,
                UiTheme.ScrollHandle,
                UiTheme.WhiteSprite);
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
            MainMenuScrollSupport.EnableContentScrollCapture(contentObject, UiTheme.WhiteSprite);

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
            if (YandexGamesIntegrationService.IsYandexGamesRuntime())
            {
                CreateSectionTitle(parent, "Аккаунт");
                CreateYandexAccountRow(parent);
            }

            CreateSectionTitle(parent, "Матчмейкинг");
            CreateToggleRow(
                parent,
                "Подбор с ботами",
                () => ClientSettingsService.AllowBotMatchmaking,
                ClientSettingsService.SetAllowBotMatchmaking,
                () => ClientSettingsService.CanToggleBotMatchmaking());

            CreateSectionTitle(parent, "Интерфейс");
            CreateToggleRow(
                parent,
                "Подсказки управления",
                () => ClientSettingsService.ShowMatchControlHints,
                ClientSettingsService.SetShowMatchControlHints);

            CreateSectionTitle(parent, "Графика");
            CreateSliderRow(
                parent,
                "Ограничение FPS",
                0f,
                2f,
                () => ClientSettingsService.TargetFpsSliderIndex,
                value => ClientSettingsService.SetTargetFpsSliderIndex(Mathf.RoundToInt(value)),
                wholeNumbers: true,
                valueLabelFormatter: value =>
                    ClientSettingsService.GetTargetFpsLabelFromSliderIndex(Mathf.RoundToInt(value)));
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
                () => ClientSettingsService.TextureQualitySliderIndex,
                value => ClientSettingsService.SetTextureQualitySliderIndex(Mathf.RoundToInt(value)),
                wholeNumbers: true,
                valueLabelFormatter: value =>
                    ClientSettingsService.GetTextureQualityLabelFromSliderIndex(Mathf.RoundToInt(value)));

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
            text.alignment = TextAlignmentOptions.MidlineLeft;
            UiTheme.ApplyTmp(text, UiTextRole.Heading);
        }

        private void CreateToggleRow(
            Transform parent,
            string label,
            System.Func<bool> getter,
            System.Action<bool> setter,
            System.Func<bool> interactable = null)
        {
            var rowObject = new GameObject("ToggleRow_" + label, typeof(RectTransform));
            rowObject.transform.SetParent(parent, false);

            var rowLayout = rowObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = rowHeight;
            rowLayout.minHeight = rowHeight;

            var background = rowObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.SectionFill);
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
            UiTheme.ApplyTmp(labelText, UiTextRole.Label);

            var buttonObject = new GameObject("ToggleButton", typeof(RectTransform));
            buttonObject.transform.SetParent(rowObject.transform, false);
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-16f, 0f);
            buttonRect.sizeDelta = new Vector2(180f, 44f);

            var buttonImage = buttonObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(buttonImage, UiTheme.ToggleOff);

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            var valueLabelObject = new GameObject("Value", typeof(RectTransform));
            valueLabelObject.transform.SetParent(buttonObject.transform, false);
            var valueLabelRect = valueLabelObject.GetComponent<RectTransform>();
            StretchFull(valueLabelRect);
            var valueLabel = valueLabelObject.AddComponent<TextMeshProUGUI>();
            valueLabel.fontSize = 17f;
            valueLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(valueLabel, UiTextRole.Body);

            var binding = new ToggleRowBinding
            {
                Button = button,
                ButtonImage = buttonImage,
                ValueLabel = valueLabel,
                Getter = getter,
                Setter = setter,
                Interactable = interactable
            };
            toggleRows.Add(binding);

            button.onClick.AddListener(() =>
            {
                if (suppressRefresh || binding.Getter == null || binding.Setter == null)
                {
                    return;
                }

                if (binding.Interactable != null && !binding.Interactable())
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
            UiTheme.ApplyFlatFill(background, UiTheme.SectionFill);
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
            UiTheme.ApplyTmp(labelText, UiTextRole.Label);

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
            valueText.alignment = TextAlignmentOptions.MidlineRight;
            UiTheme.ApplyTmp(valueText, UiTextRole.Body);

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
            UiTheme.ApplyFlatFill(sliderBackground, UiTheme.SliderTrack);

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
            UiTheme.ApplyFlatFill(fillImage, UiTheme.SliderFill);

            var handleAreaObject = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaObject.transform.SetParent(sliderObject.transform, false);
            var handleAreaRect = handleAreaObject.GetComponent<RectTransform>();
            StretchFull(handleAreaRect);

            var handleObject = new GameObject("Handle", typeof(RectTransform));
            handleObject.transform.SetParent(handleAreaObject.transform, false);
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(18f, 0f);
            var handleImage = handleObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(handleImage, UiTheme.TextPrimary);

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

        public void RefreshFromSettings()
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

            RefreshYandexAccountRow();

            suppressRefresh = false;
        }

        private void CreateYandexAccountRow(Transform parent)
        {
            var rowObject = new GameObject("YandexAccountRow", typeof(RectTransform));
            rowObject.transform.SetParent(parent, false);

            var rowLayout = rowObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = rowHeight;
            rowLayout.minHeight = rowHeight;

            var background = rowObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.SectionFill);
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
            labelText.text = "Яндекс ID";
            labelText.fontSize = 18f;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            UiTheme.ApplyTmp(labelText, UiTextRole.Label);

            var buttonObject = new GameObject("ActionButton", typeof(RectTransform));
            buttonObject.transform.SetParent(rowObject.transform, false);
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-16f, 0f);
            buttonRect.sizeDelta = new Vector2(220f, 44f);

            var buttonImage = buttonObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(buttonImage, UiTheme.ToggleOff);

            yandexAccountButton = buttonObject.AddComponent<Button>();
            yandexAccountButton.targetGraphic = buttonImage;
            yandexAccountButton.onClick.AddListener(HandleYandexAccountPressed);
            if (uiSound != null)
            {
                yandexAccountButton.onClick.AddListener(uiSound.PlayButton);
            }

            var valueLabelObject = new GameObject("Value", typeof(RectTransform));
            valueLabelObject.transform.SetParent(buttonObject.transform, false);
            var valueLabelRect = valueLabelObject.GetComponent<RectTransform>();
            StretchFull(valueLabelRect);
            yandexAccountValueLabel = valueLabelObject.AddComponent<TextMeshProUGUI>();
            yandexAccountValueLabel.fontSize = 16f;
            yandexAccountValueLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(yandexAccountValueLabel, UiTextRole.Body);

            RefreshYandexAccountRow();
        }

        private void HandleYandexAccountPressed()
        {
            if (YandexGamesIntegrationService.IsYandexPlayerAuthorized() ||
                yandexAccountLinkCoroutine != null)
            {
                return;
            }

            yandexAccountLinkCoroutine = StartCoroutine(YandexAccountLinkRoutine());
        }

        private IEnumerator YandexAccountLinkRoutine()
        {
            if (yandexAccountButton != null)
            {
                yandexAccountButton.interactable = false;
            }

            RefreshYandexAccountRow();

            var granted = false;
            yield return YandexGamesIntegrationService.RequestAuthorizationIfNeeded(
                this,
                YandexGamesIntegrationService.ProfileSyncAuthReason,
                value => granted = value);

            if (granted)
            {
                var menu = FindFirstObjectByType<MainMenuController>();
                menu?.BeginYandexProfileLink();
            }

            RefreshYandexAccountRow();

            if (yandexAccountButton != null)
            {
                yandexAccountButton.interactable = !YandexGamesIntegrationService.IsYandexPlayerAuthorized();
            }

            yandexAccountLinkCoroutine = null;
        }

        private void RefreshYandexAccountRow()
        {
            if (yandexAccountValueLabel == null || yandexAccountButton == null)
            {
                return;
            }

            var authorized = YandexGamesIntegrationService.IsYandexPlayerAuthorized();
            yandexAccountValueLabel.text = authorized ? "Подключён" : "Войти";
            UiTheme.ApplyFlatFill(
                yandexAccountButton.targetGraphic as Image,
                authorized ? UiTheme.ToggleOn : UiTheme.ToggleOff);
            yandexAccountButton.interactable = !authorized && yandexAccountLinkCoroutine == null;
        }

        private static void UpdateToggleLabel(ToggleRowBinding binding)
        {
            if (binding?.ValueLabel == null || binding.Getter == null)
            {
                return;
            }

            var enabled = binding.Getter();
            if (binding.Interactable != null && !binding.Interactable())
            {
                enabled = true;
            }

            binding.ValueLabel.text = enabled ? "Вкл" : "Выкл";
            if (binding.Button != null)
            {
                binding.Button.interactable = binding.Interactable == null || binding.Interactable();
            }

            if (binding.ButtonImage != null)
            {
                binding.ButtonImage.color = enabled ? UiTheme.ToggleOn : UiTheme.ToggleOff;
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

        private void StopTransition()
        {
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
                transitionCoroutine = null;
            }
        }

        private void ApplyVisibilityImmediate(bool visible)
        {
            if (panelGroup == null)
            {
                return;
            }

            panelGroup.alpha = visible ? 1f : 0f;
            panelGroup.interactable = visible;
            panelGroup.blocksRaycasts = visible;
        }

        private void StartTransition(bool show)
        {
            StopTransition();
            transitionCoroutine = StartCoroutine(TransitionRoutine(show));
        }

        private IEnumerator TransitionRoutine(bool show)
        {
            var fromAlpha = panelGroup != null ? panelGroup.alpha : (show ? 0f : 1f);
            var toAlpha = show ? 1f : 0f;

            if (show)
            {
                panelGroup.blocksRaycasts = true;
            }
            else
            {
                panelGroup.interactable = false;
                panelGroup.blocksRaycasts = false;
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
    }
}
