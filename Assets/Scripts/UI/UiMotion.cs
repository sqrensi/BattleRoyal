using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class UiHoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private float hoverScale = 1.04f;
        [SerializeField] private float pressedScale = 0.97f;
        [SerializeField] private float speed = 14f;

        private Vector3 baseScale = Vector3.one;
        private Vector3 targetScale = Vector3.one;
        private bool interactable = true;

        public void Configure(float hover = 1.04f, float pressed = 0.97f, float lerpSpeed = 14f)
        {
            hoverScale = hover;
            pressedScale = pressed;
            speed = lerpSpeed;
            baseScale = transform.localScale;
            targetScale = baseScale;
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            if (!interactable)
            {
                targetScale = baseScale;
            }
        }

        private void Awake()
        {
            baseScale = transform.localScale;
            targetScale = baseScale;
        }

        private void Update()
        {
            transform.localScale = Vector3.Lerp(
                transform.localScale,
                targetScale,
                Time.unscaledDeltaTime * speed);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!interactable)
            {
                return;
            }

            targetScale = baseScale * hoverScale;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            targetScale = baseScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!interactable)
            {
                return;
            }

            targetScale = baseScale * pressedScale;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            targetScale = baseScale * hoverScale;
        }
    }

    public static class UiMotion
    {
        public static UiHoverScale AttachHoverScale(GameObject target, float hoverScale = 1.04f, float pressedScale = 0.97f)
        {
            if (target == null)
            {
                return null;
            }

            var motion = target.GetComponent<UiHoverScale>();
            if (motion == null)
            {
                motion = target.AddComponent<UiHoverScale>();
            }

            motion.Configure(hoverScale, pressedScale);
            return motion;
        }

        public static void AttachButtonMotion(Button button, bool primary = false)
        {
            if (button == null)
            {
                return;
            }

            AttachHoverScale(
                button.gameObject,
                primary ? 1.05f : 1.035f,
                primary ? 0.96f : 0.98f);
        }

        public static Coroutine FadeSlide(
            MonoBehaviour host,
            CanvasGroup group,
            RectTransform rect,
            bool show,
            float duration,
            float slidePixels,
            Vector2 slideDirection,
            System.Action onComplete = null)
        {
            if (host == null || group == null)
            {
                return null;
            }

            return host.StartCoroutine(FadeSlideRoutine(
                group,
                rect,
                show,
                duration,
                slidePixels,
                slideDirection,
                onComplete));
        }

        private static IEnumerator FadeSlideRoutine(
            CanvasGroup group,
            RectTransform rect,
            bool show,
            float duration,
            float slidePixels,
            Vector2 slideDirection,
            System.Action onComplete)
        {
            var dir = slideDirection.sqrMagnitude > 0.001f ? slideDirection.normalized : Vector2.right;
            var hiddenOffset = dir * slidePixels;
            var shownPos = rect != null ? rect.anchoredPosition : Vector2.zero;
            var hiddenPos = shownPos + hiddenOffset;

            if (show)
            {
                group.gameObject.SetActive(true);
                if (rect != null)
                {
                    rect.anchoredPosition = hiddenPos;
                }

                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            var fromAlpha = show ? 0f : group.alpha;
            var toAlpha = show ? 1f : 0f;
            var fromPos = rect != null ? rect.anchoredPosition : Vector2.zero;
            var toPos = show ? shownPos : hiddenPos;
            var elapsed = 0f;
            var safeDuration = Mathf.Max(0.01f, duration);

            while (elapsed < safeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.SmoothStep(0f, 1f, elapsed / safeDuration);
                group.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
                if (rect != null)
                {
                    rect.anchoredPosition = Vector2.Lerp(fromPos, toPos, t);
                }

                yield return null;
            }

            group.alpha = toAlpha;
            if (rect != null)
            {
                rect.anchoredPosition = toPos;
            }

            group.interactable = show;
            group.blocksRaycasts = show;
            if (!show)
            {
                group.gameObject.SetActive(false);
            }

            onComplete?.Invoke();
        }

        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
