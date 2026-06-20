using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(900)]
    public sealed class UiTooltipController : MonoBehaviour
    {
        private static UiTooltipController instance;

        private RectTransform rootRect;
        private CanvasGroup group;
        private TMP_Text titleText;
        private TMP_Text bodyText;
        private Image background;
        private float hideAt;

        public static UiTooltipController Ensure(Canvas canvas)
        {
            if (instance != null)
            {
                return instance;
            }

            if (canvas == null)
            {
                return null;
            }

            var hostObject = new GameObject("UiTooltipController");
            hostObject.transform.SetParent(canvas.transform, false);
            instance = hostObject.AddComponent<UiTooltipController>();
            instance.Build(hostObject.transform);
            return instance;
        }

        public static void Show(Canvas canvas, string title, string body, Vector2 screenPosition, float duration = 0f)
        {
            var tooltip = Ensure(canvas);
            tooltip?.ShowInternal(title, body, screenPosition, duration);
        }

        public static void Hide()
        {
            if (instance == null || instance.group == null)
            {
                return;
            }

            instance.group.alpha = 0f;
            instance.group.blocksRaycasts = false;
            instance.hideAt = 0f;
        }

        public static void AttachSlotTooltip(
            GameObject slotRoot,
            Canvas canvas,
            System.Func<string> titleProvider,
            System.Func<string> bodyProvider)
        {
            if (slotRoot == null)
            {
                return;
            }

            var trigger = slotRoot.GetComponent<UiTooltipTrigger>();
            if (trigger == null)
            {
                trigger = slotRoot.AddComponent<UiTooltipTrigger>();
            }

            trigger.Configure(canvas, titleProvider, bodyProvider);
        }

        private void Build(Transform parent)
        {
            rootRect = parent.GetComponent<RectTransform>();
            if (rootRect == null)
            {
                rootRect = parent.gameObject.AddComponent<RectTransform>();
            }

            Stretch(rootRect);

            var panelObject = new GameObject("TooltipPanel");
            panelObject.transform.SetParent(parent, false);
            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.sizeDelta = new Vector2(260f, 72f);

            background = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(background, UiPanelStyle.Hud);
            UiDecor.AttachPanelChrome(panelRect, 6f, 12f);

            group = panelObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(panelObject.transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(12f, -34f);
            titleRect.offsetMax = new Vector2(-12f, -10f);
            titleText = titleObject.AddComponent<TextMeshProUGUI>();
            titleText.fontSize = 16f;
            UiTheme.ApplyMilitaryHeader(titleText, UiTextRole.Accent);

            var bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(panelObject.transform, false);
            var bodyRect = bodyObject.AddComponent<RectTransform>();
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(12f, 8f);
            bodyRect.offsetMax = new Vector2(-12f, -36f);
            bodyText = bodyObject.AddComponent<TextMeshProUGUI>();
            bodyText.fontSize = 14f;
            UiTheme.ApplyTmp(bodyText, UiTextRole.Muted);

            rootRect = panelRect;
        }

        private void LateUpdate()
        {
            if (group == null || group.alpha <= 0.001f)
            {
                return;
            }

            if (hideAt > 0f && Time.unscaledTime >= hideAt)
            {
                Hide();
            }
        }

        private void ShowInternal(string title, string body, Vector2 screenPosition, float duration)
        {
            if (titleText != null)
            {
                titleText.text = string.IsNullOrWhiteSpace(title) ? string.Empty : title.ToUpperInvariant();
            }

            if (bodyText != null)
            {
                bodyText.text = body ?? string.Empty;
            }

            if (rootRect != null)
            {
                rootRect.position = screenPosition + new Vector2(16f, -16f);
            }

            group.alpha = 1f;
            hideAt = duration > 0f ? Time.unscaledTime + duration : 0f;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    public sealed class UiTooltipTrigger : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        private Canvas canvas;
        private System.Func<string> titleProvider;
        private System.Func<string> bodyProvider;

        public void Configure(Canvas hostCanvas, System.Func<string> title, System.Func<string> body)
        {
            canvas = hostCanvas;
            titleProvider = title;
            bodyProvider = body;
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (canvas == null)
            {
                return;
            }

            UiTooltipController.Show(
                canvas,
                titleProvider != null ? titleProvider() : string.Empty,
                bodyProvider != null ? bodyProvider() : string.Empty,
                eventData.position);
        }

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
        {
            UiTooltipController.Hide();
        }
    }
}
