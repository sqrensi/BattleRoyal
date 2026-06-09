using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Simple sniper scope vignette shown while ADS with a scoped weapon.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponScopeOverlay : MonoBehaviour
    {
        [SerializeField] private float showBlendThreshold = 0.88f;
        [SerializeField] private float edgeAlpha = 0.82f;
        [SerializeField] private float scopeRadiusNormalized = 0.34f;

        private PlayerWeaponMount weaponMount;
        private Canvas scopeCanvas;
        private GameObject scopeRoot;
        private Image topBar;
        private Image bottomBar;
        private Image leftBar;
        private Image rightBar;
        private Image crosshairVertical;
        private Image crosshairHorizontal;

        private void Awake()
        {
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() != null)
            {
                enabled = false;
                return;
            }

            weaponMount = GetComponent<PlayerWeaponMount>();
            if (weaponMount == null)
            {
                enabled = false;
                return;
            }

            BuildOverlay();
            SetOverlayVisible(false);
        }

        private void LateUpdate()
        {
            if (weaponMount == null || scopeRoot == null)
            {
                return;
            }

            var shouldShow = weaponMount.HasScopedWeapon && weaponMount.AdsBlend >= showBlendThreshold;
            SetOverlayVisible(shouldShow);
        }

        private void SetOverlayVisible(bool visible)
        {
            if (scopeRoot != null && scopeRoot.activeSelf != visible)
            {
                scopeRoot.SetActive(visible);
            }
        }

        private void BuildOverlay()
        {
            var canvasObject = new GameObject("ScopeOverlayCanvas");
            canvasObject.transform.SetParent(transform, false);

            scopeCanvas = canvasObject.AddComponent<Canvas>();
            scopeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scopeCanvas.sortingOrder = 450;

            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>().enabled = false;

            scopeRoot = new GameObject("ScopeOverlay");
            scopeRoot.transform.SetParent(canvasObject.transform, false);

            var rootRect = scopeRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var edgeColor = new Color(0f, 0f, 0f, edgeAlpha);
            topBar = CreateEdge(scopeRoot.transform, "Top", edgeColor);
            bottomBar = CreateEdge(scopeRoot.transform, "Bottom", edgeColor);
            leftBar = CreateEdge(scopeRoot.transform, "Left", edgeColor);
            rightBar = CreateEdge(scopeRoot.transform, "Right", edgeColor);

            crosshairVertical = CreateEdge(scopeRoot.transform, "CrosshairVertical", new Color(1f, 1f, 1f, 0.85f));
            crosshairHorizontal = CreateEdge(scopeRoot.transform, "CrosshairHorizontal", new Color(1f, 1f, 1f, 0.85f));

            LayoutScopeBars();
        }

        private void LayoutScopeBars()
        {
            var radius = Mathf.Clamp(scopeRadiusNormalized, 0.18f, 0.48f);
            var diameter = radius * 2f;
            var horizontalInset = (1f - diameter) * 0.5f;
            var verticalInset = horizontalInset;

            SetStretch(topBar.rectTransform, 0f, verticalInset + diameter, 1f, 1f);
            SetStretch(bottomBar.rectTransform, 0f, 0f, 1f, verticalInset);
            SetStretch(leftBar.rectTransform, 0f, verticalInset, horizontalInset, verticalInset + diameter);
            SetStretch(rightBar.rectTransform, horizontalInset + diameter, verticalInset, 1f, verticalInset + diameter);

            var crosshairThickness = 0.0015f;
            var crosshairLength = radius * 0.55f;
            SetStretch(
                crosshairVertical.rectTransform,
                0.5f - crosshairThickness * 0.5f,
                0.5f - crosshairLength * 0.5f,
                0.5f + crosshairThickness * 0.5f,
                0.5f + crosshairLength * 0.5f);
            SetStretch(
                crosshairHorizontal.rectTransform,
                0.5f - crosshairLength * 0.5f,
                0.5f - crosshairThickness * 0.5f,
                0.5f + crosshairLength * 0.5f,
                0.5f + crosshairThickness * 0.5f);
        }

        private static Image CreateEdge(Transform parent, string name, Color color)
        {
            var edgeObject = new GameObject(name);
            edgeObject.transform.SetParent(parent, false);
            var image = edgeObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void SetStretch(RectTransform rect, float minX, float minY, float maxX, float maxY)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
