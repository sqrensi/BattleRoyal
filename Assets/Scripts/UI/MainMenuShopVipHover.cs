using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuShopVipHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Canvas hostCanvas;
        private GameObject tooltipRoot;
        private TMP_Text tooltipText;

        public void Configure(Canvas canvas)
        {
            hostCanvas = canvas;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            EnsureTooltip();
            if (tooltipRoot == null)
            {
                return;
            }

            tooltipRoot.SetActive(true);
            PositionTooltip();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (tooltipRoot != null)
            {
                tooltipRoot.SetActive(false);
            }
        }

        private void OnDisable()
        {
            if (tooltipRoot != null)
            {
                tooltipRoot.SetActive(false);
            }
        }

        private void EnsureTooltip()
        {
            if (tooltipRoot != null || hostCanvas == null)
            {
                return;
            }

            tooltipRoot = new GameObject("VipPrefixTooltip");
            tooltipRoot.transform.SetParent(hostCanvas.transform, false);

            var rootRect = tooltipRoot.AddComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(300f, 118f);

            var canvas = tooltipRoot.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = hostCanvas.sortingOrder + 90;

            var background = tooltipRoot.AddComponent<Image>();
            UiTheme.ApplyPanel(background, UiPanelStyle.Overlay);
            UiDecor.AttachPanelChrome(rootRect, 8f, 12f);

            var textObject = new GameObject("Text");
            textObject.transform.SetParent(tooltipRoot.transform, false);
            var textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 10f);
            textRect.offsetMax = new Vector2(-12f, -10f);

            tooltipText = textObject.AddComponent<TextMeshProUGUI>();
            tooltipText.text =
                "Приписка [VIP] перед ником в матчах, таблице и киллчате.\n\n" +
                "Действует 30 дней с момента покупки. После окончания срока приписка пропадает — её нужно купить снова.";
            tooltipText.fontSize = 13f;
            tooltipText.alignment = TextAlignmentOptions.MidlineLeft;
            tooltipText.enableWordWrapping = true;
            UiTheme.ApplyTmp(tooltipText, UiTextRole.Body);
            tooltipText.raycastTarget = false;

            tooltipRoot.SetActive(false);
        }

        private void PositionTooltip()
        {
            if (tooltipRoot == null || hostCanvas == null)
            {
                return;
            }

            var slotRect = transform as RectTransform;
            var canvasRect = hostCanvas.transform as RectTransform;
            if (slotRect == null || canvasRect == null)
            {
                return;
            }

            var corners = new Vector3[4];
            slotRect.GetWorldCorners(corners);
            var topCenter = (corners[1] + corners[2]) * 0.5f;
            var camera = hostCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hostCanvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                RectTransformUtility.WorldToScreenPoint(camera, topCenter),
                camera,
                out var localPoint);

            var tooltipRect = tooltipRoot.GetComponent<RectTransform>();
            tooltipRect.pivot = new Vector2(0.5f, 0f);
            tooltipRect.anchoredPosition = localPoint + new Vector2(0f, 12f);
        }
    }
}
