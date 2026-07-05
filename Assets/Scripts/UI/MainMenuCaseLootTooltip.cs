using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    /// <summary>
    /// Floating panel that lists every skin inside a shop case on hover.
    /// </summary>
    public static class MainMenuCaseLootTooltip
    {
        private const float PanelWidth = 336f;
        private const float HeaderHeight = 58f;
        private const float SubtitleHeight = 22f;
        private const float FooterHeight = 28f;
        private const float InnerPadding = 14f;
        private const float MaxViewportHeight = 252f;
        private const float CellSize = 68f;
        private const float CellSpacing = 8f;
        private const int GridColumns = 4;
        private const float FadeDuration = 0.14f;
        private const float AnchorGap = 12f;
        private const float ScreenMargin = 16f;

        private static TooltipView view;
        private static readonly List<PlayerSkinDefinition> LootBuffer = new List<PlayerSkinDefinition>(64);
        private static TooltipFadeRunner persistentHideRunner;
        private static Coroutine pendingHideCoroutine;

        public static void Show(Canvas canvas, CaseDefinition caseDefinition, RectTransform anchor)
        {
            CancelPendingHide();
            PruneStaleView();

            if (canvas == null || !caseDefinition.IsValid || anchor == null)
            {
                return;
            }

            if (!CaseCatalogService.TryResolveLootDefinitions(caseDefinition, LootBuffer) || LootBuffer.Count == 0)
            {
                return;
            }

            EnsureView(canvas);
            view.Populate(caseDefinition, LootBuffer);
            view.PositionNear(anchor, canvas);
            view.FadeIn();
        }

        public static void Hide()
        {
            ScheduleHide(0.08f);
        }

        public static void HideImmediate()
        {
            CancelPendingHide();
            PruneStaleView();
            view?.HideImmediate();
        }

        public static void CancelPendingHide()
        {
            if (pendingHideCoroutine != null && persistentHideRunner != null)
            {
                persistentHideRunner.StopCoroutine(pendingHideCoroutine);
                pendingHideCoroutine = null;
            }
        }

        private static void PruneStaleView()
        {
            if (view != null && !view.IsAlive)
            {
                view = null;
            }

            if (persistentHideRunner == null)
            {
                pendingHideCoroutine = null;
            }
        }

        private static void ScheduleHide(float delaySeconds)
        {
            CancelPendingHide();
            PruneStaleView();
            if (view == null || !view.IsShown)
            {
                return;
            }

            EnsureHideRunner(view.Canvas);
            if (persistentHideRunner == null)
            {
                view.FadeOut();
                return;
            }

            pendingHideCoroutine = persistentHideRunner.StartCoroutine(HideAfterDelay(delaySeconds));
        }

        private static IEnumerator HideAfterDelay(float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
            pendingHideCoroutine = null;
            PruneStaleView();
            view?.FadeOut();
        }

        private static void EnsureView(Canvas canvas)
        {
            PruneStaleView();
            if (view != null && view.IsAlive && view.Canvas == canvas)
            {
                return;
            }

            if (view != null)
            {
                if (view.RootObject != null)
                {
                    Object.Destroy(view.RootObject);
                }

                view = null;
            }

            view = TooltipView.Create(canvas);
            EnsureHideRunner(canvas);
        }

        private static void EnsureHideRunner(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            PruneStaleView();

            if (persistentHideRunner != null)
            {
                var parentCanvas = persistentHideRunner.GetComponentInParent<Canvas>();
                if (parentCanvas == canvas)
                {
                    return;
                }

                Object.Destroy(persistentHideRunner.gameObject);
                persistentHideRunner = null;
            }

            var runnerObject = new GameObject("MainMenuCaseLootTooltipRunner");
            runnerObject.transform.SetParent(canvas.transform, false);
            persistentHideRunner = runnerObject.AddComponent<TooltipFadeRunner>();
        }

        private sealed class TooltipView
        {
            public GameObject RootObject { get; }
            public Canvas Canvas { get; }

            private readonly RectTransform panelRect;
            private readonly CanvasGroup panelGroup;
            private readonly Image caseIcon;
            private readonly TMP_Text titleLabel;
            private readonly TMP_Text subtitleLabel;
            private readonly RectTransform viewportRect;
            private readonly RectTransform contentRect;
            private readonly GridLayoutGroup contentGrid;
            private readonly ScrollRect scrollRect;
            private readonly Image panelBackground;
            private Coroutine fadeCoroutine;

            public bool IsShown =>
                IsAlive && RootObject.activeInHierarchy && panelGroup.alpha > 0.01f;

            public bool IsAlive => RootObject != null && panelGroup != null && panelBackground != null;

            private TooltipView(
                GameObject rootObject,
                Canvas canvas,
                RectTransform panel,
                Image panelBackground,
                CanvasGroup group,
                Image icon,
                TMP_Text title,
                TMP_Text subtitle,
                RectTransform viewport,
                RectTransform content,
                GridLayoutGroup grid,
                ScrollRect scroll)
            {
                RootObject = rootObject;
                Canvas = canvas;
                panelRect = panel;
                panelGroup = group;
                this.panelBackground = panelBackground;
                caseIcon = icon;
                titleLabel = title;
                subtitleLabel = subtitle;
                viewportRect = viewport;
                contentRect = content;
                contentGrid = grid;
                scrollRect = scroll;
            }

            public static TooltipView Create(Canvas canvas)
            {
                var rootObject = new GameObject("MainMenuCaseLootTooltip");
                rootObject.transform.SetParent(canvas.transform, false);

                var rootRect = rootObject.AddComponent<RectTransform>();
                StretchFull(rootRect);

                var rootCanvas = rootObject.AddComponent<Canvas>();
                rootCanvas.overrideSorting = true;
                rootCanvas.sortingOrder = canvas.sortingOrder + 80;

                rootObject.AddComponent<GraphicRaycaster>();

                var panelObject = new GameObject("Panel");
                panelObject.transform.SetParent(rootObject.transform, false);

                var panel = panelObject.AddComponent<RectTransform>();
                panel.anchorMin = new Vector2(0.5f, 0.5f);
                panel.anchorMax = new Vector2(0.5f, 0.5f);
                panel.pivot = new Vector2(0.5f, 0f);
                panel.sizeDelta = new Vector2(PanelWidth, 120f);

                var panelBackground = panelObject.AddComponent<Image>();
                UiTheme.ApplyPanel(panelBackground, UiPanelStyle.Overlay);
                UiDecor.AttachPanelChrome(panel, 10f, 14f);

                var panelGroup = panelObject.AddComponent<CanvasGroup>();
                panelGroup.alpha = 0f;
                panelGroup.blocksRaycasts = false;
                panelGroup.interactable = false;

                panelObject.AddComponent<MainMenuCaseLootTooltipPanel>();

                var accentObject = new GameObject("Accent");
                accentObject.transform.SetParent(panelObject.transform, false);
                var accentRect = accentObject.AddComponent<RectTransform>();
                accentRect.anchorMin = new Vector2(0f, 1f);
                accentRect.anchorMax = new Vector2(1f, 1f);
                accentRect.pivot = new Vector2(0.5f, 1f);
                accentRect.anchoredPosition = Vector2.zero;
                accentRect.sizeDelta = new Vector2(-InnerPadding * 2f, 2f);
                accentRect.offsetMin = new Vector2(InnerPadding, -HeaderHeight + 6f);
                accentRect.offsetMax = new Vector2(-InnerPadding, -HeaderHeight + 8f);
                var accentImage = accentObject.AddComponent<Image>();
                UiTheme.ApplyFlatFill(accentImage, UiTheme.BorderAccent);
                accentImage.raycastTarget = false;

                var headerObject = new GameObject("Header");
                headerObject.transform.SetParent(panelObject.transform, false);
                var headerRect = headerObject.AddComponent<RectTransform>();
                headerRect.anchorMin = new Vector2(0f, 1f);
                headerRect.anchorMax = new Vector2(1f, 1f);
                headerRect.pivot = new Vector2(0.5f, 1f);
                headerRect.anchoredPosition = Vector2.zero;
                headerRect.sizeDelta = new Vector2(0f, HeaderHeight);
                headerRect.offsetMin = new Vector2(InnerPadding, -HeaderHeight);
                headerRect.offsetMax = new Vector2(-InnerPadding, 0f);

                var iconObject = new GameObject("CaseIcon");
                iconObject.transform.SetParent(headerObject.transform, false);
                var iconRect = iconObject.AddComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(0f, 0f);
                iconRect.sizeDelta = new Vector2(40f, 40f);
                var iconImage = iconObject.AddComponent<Image>();
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;

                var titleObject = new GameObject("Title");
                titleObject.transform.SetParent(headerObject.transform, false);
                var titleRect = titleObject.AddComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0f, 0f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.offsetMin = new Vector2(48f, 2f);
                titleRect.offsetMax = new Vector2(0f, -2f);
                var titleLabel = titleObject.AddComponent<TextMeshProUGUI>();
                titleLabel.fontSize = 19f;
                titleLabel.fontStyle = FontStyles.Bold;
                titleLabel.alignment = TextAlignmentOptions.MidlineLeft;
                titleLabel.enableWordWrapping = true;
                UiTheme.ApplyTmp(titleLabel, UiTextRole.Heading);
                titleLabel.raycastTarget = false;

                var subtitleObject = new GameObject("Subtitle");
                subtitleObject.transform.SetParent(panelObject.transform, false);
                var subtitleRect = subtitleObject.AddComponent<RectTransform>();
                subtitleRect.anchorMin = new Vector2(0f, 1f);
                subtitleRect.anchorMax = new Vector2(1f, 1f);
                subtitleRect.pivot = new Vector2(0.5f, 1f);
                subtitleRect.anchoredPosition = new Vector2(0f, -(HeaderHeight + 2f));
                subtitleRect.sizeDelta = new Vector2(-InnerPadding * 2f, SubtitleHeight);
                var subtitleLabel = subtitleObject.AddComponent<TextMeshProUGUI>();
                subtitleLabel.fontSize = 13f;
                subtitleLabel.alignment = TextAlignmentOptions.MidlineLeft;
                UiTheme.ApplyTmp(subtitleLabel, UiTextRole.Muted);
                subtitleLabel.raycastTarget = false;

                var scrollObject = new GameObject("LootScroll");
                scrollObject.transform.SetParent(panelObject.transform, false);
                var scrollRectTransform = scrollObject.AddComponent<RectTransform>();
                scrollRectTransform.anchorMin = new Vector2(0f, 0f);
                scrollRectTransform.anchorMax = new Vector2(1f, 1f);
                scrollRectTransform.offsetMin = new Vector2(
                    InnerPadding,
                    FooterHeight + InnerPadding * 0.5f);
                scrollRectTransform.offsetMax = new Vector2(
                    -InnerPadding,
                    -(HeaderHeight + SubtitleHeight + InnerPadding));

                var scroll = scrollObject.AddComponent<ScrollRect>();
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 24f;
                scroll.inertia = true;
                scroll.decelerationRate = 0.135f;

                var viewportObject = new GameObject("Viewport");
                viewportObject.transform.SetParent(scrollObject.transform, false);
                var viewportRect = viewportObject.AddComponent<RectTransform>();
                StretchFull(viewportRect);
                var viewportImage = viewportObject.AddComponent<Image>();
                viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
                viewportImage.raycastTarget = true;
                viewportObject.AddComponent<RectMask2D>();

                var contentObject = new GameObject("Content");
                contentObject.transform.SetParent(viewportObject.transform, false);
                var contentRect = contentObject.AddComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0.5f, 1f);
                contentRect.anchoredPosition = Vector2.zero;
                contentRect.sizeDelta = new Vector2(0f, 0f);

                var contentGrid = contentObject.AddComponent<GridLayoutGroup>();
                contentGrid.cellSize = new Vector2(CellSize, CellSize);
                contentGrid.spacing = new Vector2(CellSpacing, CellSpacing);
                contentGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                contentGrid.constraintCount = GridColumns;
                contentGrid.childAlignment = TextAnchor.UpperCenter;

                var fitter = contentObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                scroll.viewport = viewportRect;
                scroll.content = contentRect;

                var scrollWheel = scrollObject.AddComponent<CaseLootScrollWheel>();
                scrollWheel.Initialize(scroll);

                var footerObject = new GameObject("Footer");
                footerObject.transform.SetParent(panelObject.transform, false);
                var footerRect = footerObject.AddComponent<RectTransform>();
                footerRect.anchorMin = new Vector2(0f, 0f);
                footerRect.anchorMax = new Vector2(1f, 0f);
                footerRect.pivot = new Vector2(0.5f, 0f);
                footerRect.anchoredPosition = Vector2.zero;
                footerRect.sizeDelta = new Vector2(-InnerPadding * 2f, FooterHeight);
                footerRect.offsetMin = new Vector2(InnerPadding, InnerPadding * 0.35f);
                footerRect.offsetMax = new Vector2(-InnerPadding, InnerPadding * 0.35f + FooterHeight);
                var footerLabel = footerObject.AddComponent<TextMeshProUGUI>();
                footerLabel.text = "Случайный предмет из списка";
                footerLabel.fontSize = 12f;
                footerLabel.fontStyle = FontStyles.Italic;
                footerLabel.alignment = TextAlignmentOptions.Center;
                UiTheme.ApplyTmp(footerLabel, UiTextRole.Label);
                footerLabel.raycastTarget = false;

                rootObject.SetActive(false);

                return new TooltipView(
                    rootObject,
                    canvas,
                    panel,
                    panelBackground,
                    panelGroup,
                    iconImage,
                    titleLabel,
                    subtitleLabel,
                    viewportRect,
                    contentRect,
                    contentGrid,
                    scroll);
            }

            public void Populate(CaseDefinition caseDefinition, IReadOnlyList<PlayerSkinDefinition> loot)
            {
                caseIcon.sprite = InventoryIconCatalog.GetCaseIcon(caseDefinition.PictureResourcePath);
                titleLabel.text = caseDefinition.DisplayName;
                subtitleLabel.text = BuildSubtitle(loot);

                var useCompactGrid = loot.Count > 18;
                var columns = useCompactGrid ? 5 : GridColumns;
                var cellSize = useCompactGrid ? 56f : CellSize;
                contentGrid.constraintCount = columns;
                contentGrid.cellSize = new Vector2(cellSize, cellSize);

                ClearContent();

                for (var i = 0; i < loot.Count; i++)
                {
                    CreateLootCell(contentRect, caseDefinition, loot[i]);
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
                Canvas.ForceUpdateCanvases();

                var rowCount = Mathf.CeilToInt(loot.Count / (float)columns);
                var gridHeight = rowCount * cellSize + Mathf.Max(0, rowCount - 1) * CellSpacing;
                contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, gridHeight);

                var viewportHeight = Mathf.Min(MaxViewportHeight, gridHeight);
                var panelHeight = HeaderHeight + SubtitleHeight + viewportHeight + FooterHeight + InnerPadding * 2.2f;
                var panelWidth = useCompactGrid ? 352f : PanelWidth;
                panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

                var canScroll = gridHeight > viewportHeight + 1f;
                scrollRect.vertical = canScroll;
                scrollRect.enabled = true;
                if (canScroll)
                {
                    scrollRect.verticalNormalizedPosition = 1f;
                }
            }

            public void PositionNear(RectTransform anchor, Canvas canvas)
            {
                var canvasRect = canvas.transform as RectTransform;
                if (canvasRect == null)
                {
                    return;
                }

                var corners = new Vector3[4];
                anchor.GetWorldCorners(corners);
                var topCenter = (corners[1] + corners[2]) * 0.5f;
                var bottomCenter = (corners[0] + corners[3]) * 0.5f;

                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    RectTransformUtility.WorldToScreenPoint(camera, topCenter),
                    camera,
                    out var abovePoint);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    RectTransformUtility.WorldToScreenPoint(camera, bottomCenter),
                    camera,
                    out var belowPoint);

                var panelHeight = panelRect.sizeDelta.y;
                var canvasTop = canvasRect.rect.height * 0.5f - ScreenMargin;
                var canvasBottom = -canvasRect.rect.height * 0.5f + ScreenMargin;
                var fitsAbove = abovePoint.y + AnchorGap + panelHeight <= canvasTop;
                var fitsBelow = belowPoint.y - AnchorGap - panelHeight >= canvasBottom;
                var placeAbove = fitsAbove || !fitsBelow;

                panelRect.pivot = new Vector2(0.5f, placeAbove ? 0f : 1f);
                var anchoredPosition = placeAbove
                    ? abovePoint + new Vector2(0f, AnchorGap)
                    : belowPoint - new Vector2(0f, AnchorGap);

                var halfWidth = panelRect.sizeDelta.x * 0.5f;
                var canvasHalfWidth = canvasRect.rect.width * 0.5f;
                anchoredPosition.x = Mathf.Clamp(
                    anchoredPosition.x,
                    -canvasHalfWidth + halfWidth + ScreenMargin,
                    canvasHalfWidth - halfWidth - ScreenMargin);

                if (placeAbove)
                {
                    anchoredPosition.y = Mathf.Max(anchoredPosition.y, canvasBottom);
                    anchoredPosition.y = Mathf.Min(anchoredPosition.y + panelHeight, canvasTop) - panelHeight;
                }
                else
                {
                    anchoredPosition.y = Mathf.Min(anchoredPosition.y, canvasTop);
                    anchoredPosition.y = Mathf.Max(anchoredPosition.y - panelHeight, canvasBottom) + panelHeight;
                }

                panelRect.anchoredPosition = anchoredPosition;
            }

            public void FadeIn()
            {
                RootObject.SetActive(true);
                var fadeRunner = RootObject.GetComponent<TooltipFadeRunner>() ??
                                 RootObject.AddComponent<TooltipFadeRunner>();
                panelBackground.raycastTarget = true;
                panelGroup.blocksRaycasts = true;
                panelGroup.interactable = true;

                if (fadeCoroutine != null)
                {
                    fadeRunner.StopCoroutine(fadeCoroutine);
                }

                fadeCoroutine = fadeRunner.StartCoroutine(FadeTo(1f));
            }

            public void FadeOut()
            {
                if (!IsAlive || !RootObject.activeSelf)
                {
                    return;
                }

                if (fadeCoroutine != null)
                {
                    var runner = RootObject.GetComponent<TooltipFadeRunner>();
                    if (runner != null)
                    {
                        runner.StopCoroutine(fadeCoroutine);
                    }
                }

                var fadeRunner = RootObject.GetComponent<TooltipFadeRunner>() ??
                                 RootObject.AddComponent<TooltipFadeRunner>();
                fadeCoroutine = fadeRunner.StartCoroutine(FadeTo(0f, hideAfter: true));
            }

            public void HideImmediate()
            {
                if (!IsAlive)
                {
                    return;
                }

                if (fadeCoroutine != null)
                {
                    var runner = RootObject.GetComponent<TooltipFadeRunner>();
                    if (runner != null)
                    {
                        runner.StopCoroutine(fadeCoroutine);
                        fadeCoroutine = null;
                    }
                }

                panelGroup.alpha = 0f;
                panelBackground.raycastTarget = false;
                panelGroup.blocksRaycasts = false;
                panelGroup.interactable = false;
                RootObject.SetActive(false);
            }

            private IEnumerator FadeTo(float targetAlpha, bool hideAfter = false)
            {
                if (!IsAlive)
                {
                    yield break;
                }

                var startAlpha = panelGroup.alpha;
                var elapsed = 0f;
                var duration = Mathf.Max(0.01f, FadeDuration);

                while (elapsed < duration)
                {
                    if (!IsAlive)
                    {
                        yield break;
                    }

                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(elapsed / duration);
                    panelGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                    yield return null;
                }

                panelGroup.alpha = targetAlpha;
                fadeCoroutine = null;

                if (hideAfter && targetAlpha <= 0.001f)
                {
                    RootObject.SetActive(false);
                }
            }

            private void ClearContent()
            {
                for (var i = contentRect.childCount - 1; i >= 0; i--)
                {
                    var child = contentRect.GetChild(i);
                    if (child != null)
                    {
                        Object.Destroy(child.gameObject);
                    }
                }
            }

            private static void CreateLootCell(
                Transform parent,
                CaseDefinition caseDefinition,
                PlayerSkinDefinition definition)
            {
                var cellObject = new GameObject("Loot_" + definition.Id);
                cellObject.transform.SetParent(parent, false);

                var background = cellObject.AddComponent<Image>();
                UiTheme.ApplyFlatFill(background, UiTheme.SlotFill);
                background.raycastTarget = false;

                UiDecor.CreateRarityStripe(
                    cellObject.transform,
                    CaseCatalogService.GetLootRarityStripeColor(caseDefinition, definition.Id),
                    3f);

                var iconObject = new GameObject("Icon");
                iconObject.transform.SetParent(cellObject.transform, false);
                var iconRect = iconObject.AddComponent<RectTransform>();
                StretchFull(iconRect);
                iconRect.offsetMin = new Vector2(6f, 8f);
                iconRect.offsetMax = new Vector2(-6f, -6f);

                var iconImage = iconObject.AddComponent<Image>();
                iconImage.sprite = InventoryIconCatalog.GetSkinIcon(definition);
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }

            private static string BuildSubtitle(IReadOnlyList<PlayerSkinDefinition> loot)
            {
                var count = loot.Count;
                var noun = count switch
                {
                    1 => "предмет",
                    >= 2 and <= 4 => "предмета",
                    _ => "предметов",
                };

                return $"{count} {noun}";
            }

            private static void StretchFull(RectTransform rect)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
        }

        private sealed class CaseLootScrollWheel : MonoBehaviour, IScrollHandler
        {
            private ScrollRect scrollRect;

            public void Initialize(ScrollRect scroll)
            {
                scrollRect = scroll;
            }

            public void OnScroll(PointerEventData eventData)
            {
                if (scrollRect == null || !scrollRect.vertical)
                {
                    return;
                }

                scrollRect.OnScroll(eventData);
                eventData.Use();
            }
        }

        private sealed class TooltipFadeRunner : MonoBehaviour
        {
        }

        private sealed class MainMenuCaseLootTooltipPanel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public void OnPointerEnter(PointerEventData eventData)
            {
                CancelPendingHide();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                Hide();
            }
        }
    }
}
