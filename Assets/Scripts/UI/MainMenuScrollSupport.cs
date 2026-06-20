using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    internal static class MainMenuScrollSupport
    {
        public static void ConfigureVerticalScroll(ScrollRect scroll)
        {
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
        }

        public static void EnableViewportScrollCapture(GameObject viewportObject, Sprite whiteSprite)
        {
            var viewportImage = viewportObject.AddComponent<Image>();
            viewportImage.sprite = whiteSprite;
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;
        }

        public static void EnableContentScrollCapture(GameObject contentObject, Sprite whiteSprite)
        {
            var contentBackground = contentObject.AddComponent<Image>();
            contentBackground.sprite = whiteSprite;
            contentBackground.color = Color.clear;
            contentBackground.raycastTarget = true;
        }

        public static Scrollbar CreateVerticalScrollbar(
            Transform parent,
            float scrollbarWidth,
            Color trackColor,
            Color handleColor,
            Sprite whiteSprite)
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
            trackImage.sprite = whiteSprite;
            trackImage.type = Image.Type.Simple;
            trackImage.color = trackColor;

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
            handleImage.sprite = whiteSprite;
            handleImage.type = Image.Type.Simple;
            handleImage.color = handleColor;

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            return scrollbar;
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
