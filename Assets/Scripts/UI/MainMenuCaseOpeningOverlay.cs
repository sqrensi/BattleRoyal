using System;
using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuCaseOpeningOverlay : MonoBehaviour
    {
        private const string SpinClipPath = "Sounds/case";
        private const string RevealClipPath = "Sounds/case2";

        private static Sprite whiteSprite;

        private static readonly Color OverlayColor = new Color(0.02f, 0.03f, 0.05f, 0.96f);
        private static readonly Color ViewportColor = new Color(0.08f, 0.1f, 0.12f, 0.96f);
        private static readonly Color CloseButtonColor = new Color(0.24f, 0.1f, 0.1f, 0.98f);
        private static readonly Color MarkerColor = new Color(0.92f, 0.84f, 0.55f, 0.95f);
        private static readonly Color TitleColor = new Color(0.94f, 0.96f, 0.98f, 0.98f);

        [SerializeField] private float itemWidth = 188f;
        [SerializeField] private float itemHeight = 188f;
        [SerializeField] private float itemSpacing = 18f;
        [SerializeField] private float spinDuration = 5.6f;
        [SerializeField] private int minimumSpinItems = 36;
        [SerializeField] private int extraItemsAfterWinner = 4;
        [SerializeField] private float soundVolume = 0.8f;

        private RectTransform stripRect;
        private RectTransform viewportRect;
        private CanvasGroup overlayGroup;
        private TMP_Text resultTitle;
        private TMP_Text resultSubtitle;
        private Image resultIcon;
        private Image resultBackground;
        private Button closeButton;
        private AudioSource tickAudioSource;
        private AudioClip tickClip;
        private AudioClip revealClip;
        private int lastTickedCardIndex = int.MinValue;
        private float previousStripX;
        private Coroutine animationCoroutine;

        public static MainMenuCaseOpeningOverlay Show(
            RectTransform canvasRect,
            CaseDefinition caseDefinition,
            string rolledSkinId,
            Action onCompleted = null)
        {
            if (canvasRect == null || !caseDefinition.IsValid)
            {
                onCompleted?.Invoke();
                return null;
            }

            if (!PlayerSkinSelectionService.TryGetDefinitionById(rolledSkinId, out var winner) ||
                !winner.IsValid)
            {
                Debug.LogWarning(
                    $"[MainMenuCaseOpeningOverlay] Unknown rolled skin id '{rolledSkinId}'.");
                onCompleted?.Invoke();
                return null;
            }

            var overlayObject = new GameObject("MainMenuCaseOpeningOverlay");
            overlayObject.transform.SetParent(canvasRect, false);

            var overlayRect = overlayObject.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            var overlay = overlayObject.AddComponent<MainMenuCaseOpeningOverlay>();

            var overlayCanvas = overlayObject.AddComponent<Canvas>();
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = 200;

            overlayObject.AddComponent<GraphicRaycaster>();

            overlay.Build(caseDefinition, winner, onCompleted);
            return overlay;
        }

        private void Build(
            CaseDefinition caseDefinition,
            PlayerSkinDefinition winner,
            Action onCompleted)
        {
            var background = gameObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.color = OverlayColor;
            background.raycastTarget = true;

            overlayGroup = gameObject.AddComponent<CanvasGroup>();
            overlayGroup.alpha = 0f;
            overlayGroup.interactable = false;
            overlayGroup.blocksRaycasts = true;

            EnsureAudio();
            BuildTitle(caseDefinition.DisplayName);
            BuildViewport();
            BuildResultPanel(winner);
            BuildCloseButton(onCompleted);

            animationCoroutine = StartCoroutine(FadeInAndSpin(caseDefinition, winner));
        }

        private void EnsureAudio()
        {
            tickAudioSource = gameObject.AddComponent<AudioSource>();
            tickAudioSource.playOnAwake = false;
            tickAudioSource.loop = false;
            tickAudioSource.spatialBlend = 0f;
            tickAudioSource.priority = 16;
            tickAudioSource.volume = soundVolume;
            tickAudioSource.pitch = 1f;

            tickClip = Resources.Load<AudioClip>(SpinClipPath);
            revealClip = Resources.Load<AudioClip>(RevealClipPath);

            if (tickClip == null)
            {
                Debug.LogWarning($"[MainMenuCaseOpeningOverlay] Clip not found: Resources/{SpinClipPath}");
            }

            if (revealClip == null)
            {
                Debug.LogWarning($"[MainMenuCaseOpeningOverlay] Clip not found: Resources/{RevealClipPath}");
            }
        }

        private void ResetTickTracking(float stripX)
        {
            lastTickedCardIndex = int.MinValue;
            previousStripX = stripX;
        }

        private void UpdateCardTickSounds(float stripX, float itemStep, int totalItems)
        {
            if (tickAudioSource == null || tickClip == null || ClientSettingsService.IsEffectivelyMuted() || viewportRect == null)
            {
                previousStripX = stripX;
                return;
            }

            var viewportCenter = viewportRect.rect.width * 0.5f;
            var focusedIndex = Mathf.FloorToInt((viewportCenter - stripX - itemWidth * 0.5f) / itemStep);
            focusedIndex = Mathf.Clamp(focusedIndex, 0, Mathf.Max(0, totalItems - 1));

            if (focusedIndex != lastTickedCardIndex)
            {
                if (lastTickedCardIndex == int.MinValue)
                {
                    tickAudioSource.PlayOneShot(tickClip, GetCaseSoundVolume());
                }
                else
                {
                    var direction = focusedIndex > lastTickedCardIndex ? 1 : -1;
                    for (var index = lastTickedCardIndex + direction; ; index += direction)
                    {
                        tickAudioSource.PlayOneShot(tickClip, GetCaseSoundVolume());
                        if (index == focusedIndex)
                        {
                            break;
                        }
                    }
                }

                lastTickedCardIndex = focusedIndex;
            }

            previousStripX = stripX;
        }

        private void PlayRevealSound()
        {
            if (tickAudioSource == null || revealClip == null || ClientSettingsService.IsEffectivelyMuted())
            {
                return;
            }

            tickAudioSource.PlayOneShot(revealClip, GetCaseSoundVolume());
        }

        private float GetCaseSoundVolume()
        {
            ClientSettingsService.EnsureLoaded();
            return soundVolume * ClientSettingsService.SfxVolume;
        }

        private void BuildTitle(string caseName)
        {
            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(transform, false);

            var rect = titleObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -56f);
            rect.sizeDelta = new Vector2(900f, 56f);

            var title = titleObject.AddComponent<TextMeshProUGUI>();
            title.text = caseName;
            title.fontSize = 36f;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            title.color = TitleColor;
            title.raycastTarget = false;
        }

        private void BuildViewport()
        {
            var viewportRoot = new GameObject("ViewportRoot");
            viewportRoot.transform.SetParent(transform, false);

            var rootRect = viewportRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(1240f, itemHeight + 72f);
            rootRect.anchoredPosition = Vector2.zero;

            var maskedViewportObject = new GameObject("ViewportMasked");
            maskedViewportObject.transform.SetParent(viewportRoot.transform, false);

            viewportRect = maskedViewportObject.AddComponent<RectTransform>();
            StretchFull(viewportRect);

            var viewportBackground = maskedViewportObject.AddComponent<Image>();
            viewportBackground.sprite = GetWhiteSprite();
            viewportBackground.color = ViewportColor;
            viewportBackground.raycastTarget = false;

            maskedViewportObject.AddComponent<RectMask2D>();

            var stripObject = new GameObject("Strip");
            stripObject.transform.SetParent(maskedViewportObject.transform, false);
            stripRect = stripObject.AddComponent<RectTransform>();
            stripRect.anchorMin = new Vector2(0f, 0.5f);
            stripRect.anchorMax = new Vector2(0f, 0.5f);
            stripRect.pivot = new Vector2(0f, 0.5f);
            stripRect.anchoredPosition = Vector2.zero;
            stripRect.sizeDelta = new Vector2(100f, itemHeight);

            var chromeObject = new GameObject("ViewportChrome");
            chromeObject.transform.SetParent(viewportRoot.transform, false);
            var viewportChromeRect = chromeObject.AddComponent<RectTransform>();
            StretchFull(viewportChromeRect);

            BuildCenterMarker(chromeObject.transform);
            BuildStripFrame(chromeObject.transform);
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void BuildStripFrame(Transform parent)
        {
            CreateFrameBar(
                parent,
                "TopFrame",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, 0f),
                new Vector2(0f, 8f));

            CreateFrameBar(
                parent,
                "BottomFrame",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 8f));
        }

        private void CreateFrameBar(
            Transform parent,
            string objectName,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var frameObject = new GameObject(objectName);
            frameObject.transform.SetParent(parent, false);

            var rect = frameObject.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            var image = frameObject.AddComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.color = MarkerColor;
            image.raycastTarget = false;
        }

        private void BuildCenterMarker(Transform parent)
        {
            CreateMarkerLine(parent, 0f);

            CreateMarkerCap(
                parent,
                "TopCap",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -4f));

            CreateMarkerCap(
                parent,
                "BottomCap",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 4f));
        }

        private void CreateMarkerCap(
            Transform parent,
            string objectName,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition)
        {
            var capObject = new GameObject(objectName);
            capObject.transform.SetParent(parent, false);

            var rect = capObject.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(18f, 4f);
            rect.anchoredPosition = anchoredPosition;

            var image = capObject.AddComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.color = MarkerColor;
            image.raycastTarget = false;
        }

        private void CreateMarkerLine(Transform parent, float xOffset)
        {
            var markerObject = new GameObject("CenterMarker");
            markerObject.transform.SetParent(parent, false);

            var rect = markerObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(4f, 0f);
            rect.anchoredPosition = new Vector2(xOffset, 0f);

            var image = markerObject.AddComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.color = MarkerColor;
            image.raycastTarget = false;
        }

        private void BuildResultPanel(PlayerSkinDefinition winner)
        {
            var resultObject = new GameObject("ResultPanel");
            resultObject.transform.SetParent(transform, false);

            var rect = resultObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -40f);
            rect.sizeDelta = new Vector2(460f, 236f);

            resultBackground = resultObject.AddComponent<Image>();
            resultBackground.sprite = GetWhiteSprite();
            resultBackground.color = ShopCatalogService.GetRarityCardColor(winner.Id);

            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(resultObject.transform, false);
            var iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(0f, 22f);
            iconRect.sizeDelta = new Vector2(128f, 128f);

            resultIcon = iconObject.AddComponent<Image>();
            resultIcon.preserveAspect = true;
            resultIcon.raycastTarget = false;
            resultIcon.sprite = InventoryIconCatalog.GetSkinIcon(winner.PictureResourcePath);

            var titleObject = new GameObject("ResultTitle");
            titleObject.transform.SetParent(resultObject.transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0f);
            titleRect.anchorMax = new Vector2(0.5f, 0f);
            titleRect.pivot = new Vector2(0.5f, 0f);
            titleRect.anchoredPosition = new Vector2(0f, 18f);
            titleRect.sizeDelta = new Vector2(380f, 34f);

            resultTitle = titleObject.AddComponent<TextMeshProUGUI>();
            resultTitle.text = "Выпало";
            resultTitle.fontSize = 24f;
            resultTitle.fontStyle = FontStyles.Bold;
            resultTitle.alignment = TextAlignmentOptions.Center;
            resultTitle.color = TitleColor;

            var subtitleObject = new GameObject("ResultSubtitle");
            subtitleObject.transform.SetParent(resultObject.transform, false);
            var subtitleRect = subtitleObject.AddComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0.5f, 0f);
            subtitleRect.anchorMax = new Vector2(0.5f, 0f);
            subtitleRect.pivot = new Vector2(0.5f, 0f);
            subtitleRect.anchoredPosition = new Vector2(0f, 52f);
            subtitleRect.sizeDelta = new Vector2(380f, 28f);

            resultSubtitle = subtitleObject.AddComponent<TextMeshProUGUI>();
            resultSubtitle.text = winner.DisplayName;
            resultSubtitle.fontSize = 18f;
            resultSubtitle.alignment = TextAlignmentOptions.Center;
            resultSubtitle.color = TitleColor;

            resultObject.SetActive(false);
        }

        private void BuildCloseButton(Action onCompleted)
        {
            var buttonObject = new GameObject("CloseButton");
            buttonObject.transform.SetParent(transform, false);

            var rect = buttonObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var stripHalfHeight = (itemHeight + 72f) * 0.5f;
            rect.anchoredPosition = new Vector2(0f, -(stripHalfHeight + 36f));
            rect.sizeDelta = new Vector2(180f, 48f);

            var background = buttonObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.color = CloseButtonColor;

            closeButton = buttonObject.AddComponent<Button>();
            closeButton.targetGraphic = background;
            closeButton.interactable = false;
            closeButton.gameObject.SetActive(false);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = "Закрыть";
            label.fontSize = 22f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = TitleColor;

            closeButton.onClick.AddListener(() =>
            {
                onCompleted?.Invoke();
                Destroy(gameObject);
            });
        }

        private IEnumerator FadeInAndSpin(CaseDefinition caseDefinition, PlayerSkinDefinition winner)
        {
            var elapsed = 0f;
            const float fadeDuration = 0.25f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                overlayGroup.alpha = Mathf.Clamp01(elapsed / fadeDuration);
                yield return null;
            }

            overlayGroup.alpha = 1f;

            var lootDefinitions = new List<PlayerSkinDefinition>(16);
            if (!CaseCatalogService.TryResolveLootDefinitions(caseDefinition, lootDefinitions))
            {
                lootDefinitions.Add(winner);
            }

            var winnerIndex = minimumSpinItems;
            var totalItems = winnerIndex + extraItemsAfterWinner + 1;
            var itemStep = itemWidth + itemSpacing;
            var stripItems = new List<RectTransform>(totalItems);

            stripRect.sizeDelta = new Vector2(totalItems * itemStep, itemHeight);

            for (var i = 0; i < totalItems; i++)
            {
                PlayerSkinDefinition definition;
                if (i == winnerIndex)
                {
                    definition = winner;
                }
                else
                {
                    definition = lootDefinitions[UnityEngine.Random.Range(0, lootDefinitions.Count)];
                }

                stripItems.Add(CreateStripItem(stripRect, definition, i));
            }

            var viewportWidth = viewportRect.rect.width;
            var startX = viewportWidth * 0.5f;
            var targetX = startX - winnerIndex * itemStep - itemWidth * 0.5f;
            stripRect.anchoredPosition = new Vector2(startX, 0f);
            ResetTickTracking(startX);

            elapsed = 0f;
            while (elapsed < spinDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / spinDuration);
                var eased = EaseOutQuint(t);
                var x = Mathf.Lerp(startX, targetX, eased);
                stripRect.anchoredPosition = new Vector2(x, 0f);
                UpdateCardTickSounds(x, itemStep, totalItems);
                yield return null;
            }

            stripRect.anchoredPosition = new Vector2(targetX, 0f);
            UpdateCardTickSounds(targetX, itemStep, totalItems);
            PlayRevealSound();

            yield return new WaitForSecondsRealtime(0.35f);

            if (resultBackground != null)
            {
                resultBackground.transform.parent.gameObject.SetActive(true);
            }

            if (closeButton != null)
            {
                closeButton.gameObject.SetActive(true);
                closeButton.interactable = true;
            }

            overlayGroup.interactable = true;
            animationCoroutine = null;
        }

        private RectTransform CreateStripItem(
            Transform parent,
            PlayerSkinDefinition definition,
            int index)
        {
            var slotObject = new GameObject("StripItem_" + definition.Id);
            slotObject.transform.SetParent(parent, false);

            var rect = slotObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(index * (itemWidth + itemSpacing), 0f);
            rect.sizeDelta = new Vector2(itemWidth, itemHeight);

            var background = slotObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.color = ShopCatalogService.GetRarityCardColor(definition.Id);

            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(slotObject.transform, false);
            var iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(14f, 14f);
            iconRect.offsetMax = new Vector2(-14f, -14f);

            var icon = iconObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.sprite = InventoryIconCatalog.GetSkinIcon(definition.PictureResourcePath);

            return rect;
        }

        private static float EaseOutQuint(float t)
        {
            var inv = 1f - t;
            return 1f - inv * inv * inv * inv * inv;
        }

        private void OnDestroy()
        {
            if (animationCoroutine != null)
            {
                StopCoroutine(animationCoroutine);
            }
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
