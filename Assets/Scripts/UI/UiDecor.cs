using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    public static class UiDecor
    {
        private static Sprite cornerBracketSprite;
        private static Sprite frostNoiseSprite;
        private static Sprite vignetteSprite;

        public static void AttachPanelChrome(RectTransform panelRoot, float inset = 10f, float armLength = 18f)
        {
            if (panelRoot == null)
            {
                return;
            }

            var chromeRoot = new GameObject("PanelChrome");
            chromeRoot.transform.SetParent(panelRoot, false);
            var chromeRect = chromeRoot.AddComponent<RectTransform>();
            Stretch(chromeRect);
            chromeRect.offsetMin = new Vector2(inset, inset);
            chromeRect.offsetMax = new Vector2(-inset, -inset);

            CreateCorner(chromeRoot.transform, TextAnchor.UpperLeft, armLength);
            CreateCorner(chromeRoot.transform, TextAnchor.UpperRight, armLength);
            CreateCorner(chromeRoot.transform, TextAnchor.LowerLeft, armLength);
            CreateCorner(chromeRoot.transform, TextAnchor.LowerRight, armLength);
        }

        public static RectTransform CreateVignette(Transform canvasRoot, int sortingOffset = 0)
        {
            if (canvasRoot == null)
            {
                return null;
            }

            var existing = canvasRoot.Find("MenuVignette");
            if (existing != null)
            {
                return existing.GetComponent<RectTransform>();
            }

            var vignetteObject = new GameObject("MenuVignette");
            vignetteObject.transform.SetParent(canvasRoot, false);
            vignetteObject.transform.SetAsFirstSibling();

            var rect = vignetteObject.AddComponent<RectTransform>();
            Stretch(rect);

            var image = vignetteObject.AddComponent<Image>();
            image.sprite = GetVignetteSprite();
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;

            var canvas = vignetteObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOffset;
            return rect;
        }

        public static RectTransform CreateFrostedBackdrop(Transform parent, bool raycast = true)
        {
            var backdropObject = new GameObject("FrostedBackdrop");
            backdropObject.transform.SetParent(parent, false);
            backdropObject.transform.SetAsFirstSibling();

            var rect = backdropObject.AddComponent<RectTransform>();
            Stretch(rect);

            var dim = backdropObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(dim, UiTheme.CanvasDim);
            dim.raycastTarget = raycast;

            var frostObject = new GameObject("Frost");
            frostObject.transform.SetParent(backdropObject.transform, false);
            var frostRect = frostObject.AddComponent<RectTransform>();
            Stretch(frostRect);

            var frost = frostObject.AddComponent<Image>();
            frost.sprite = GetFrostNoiseSprite();
            frost.type = Image.Type.Simple;
            frost.color = new Color(0.92f, 0.9f, 0.86f, 0.045f);
            frost.raycastTarget = false;

            return rect;
        }

        public static Image CreateAccentLine(Transform parent, Color color, float height = 2f)
        {
            var lineObject = new GameObject("AccentLine");
            lineObject.transform.SetParent(parent, false);
            var rect = lineObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.08f, 1f);
            rect.anchorMax = new Vector2(0.92f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -6f);
            rect.sizeDelta = new Vector2(0f, height);

            var image = lineObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(image, color);
            image.raycastTarget = false;
            return image;
        }

        public static RectTransform CreateEmptyState(
            Transform parent,
            string title,
            string subtitle,
            UiIconCatalog.IconKind iconKind = UiIconCatalog.IconKind.Inventory)
        {
            var rootObject = new GameObject("EmptyState");
            rootObject.transform.SetParent(parent, false);
            var rootRect = rootObject.AddComponent<RectTransform>();
            Stretch(rootRect);

            var layout = rootObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 10f;
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(rootObject.transform, false);
            var iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.sizeDelta = new Vector2(48f, 48f);
            var iconImage = iconObject.AddComponent<Image>();
            iconImage.sprite = UiIconCatalog.GetIcon(iconKind);
            iconImage.preserveAspect = true;
            iconImage.color = UiTheme.TextMuted;
            iconImage.raycastTarget = false;

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(rootObject.transform, false);
            var titleText = titleObject.AddComponent<TextMeshProUGUI>();
            titleText.text = title.ToUpperInvariant();
            titleText.fontSize = 22f;
            titleText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyMilitaryHeader(titleText, UiTextRole.Heading);

            var subtitleObject = new GameObject("Subtitle");
            subtitleObject.transform.SetParent(rootObject.transform, false);
            var subtitleText = subtitleObject.AddComponent<TextMeshProUGUI>();
            subtitleText.text = subtitle;
            subtitleText.fontSize = 16f;
            subtitleText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(subtitleText, UiTextRole.Muted);

            return rootRect;
        }

        public static Image CreateRarityStripe(Transform parent, Color stripeColor, float height = 4f)
        {
            var stripeObject = new GameObject("RarityStripe");
            stripeObject.transform.SetParent(parent, false);
            var rect = stripeObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);

            var image = stripeObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(image, stripeColor);
            image.raycastTarget = false;
            return image;
        }

        private static void CreateCorner(Transform parent, TextAnchor anchor, float armLength)
        {
            var cornerObject = new GameObject("Corner_" + anchor);
            cornerObject.transform.SetParent(parent, false);
            var rect = cornerObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(armLength, armLength);

            switch (anchor)
            {
                case TextAnchor.UpperLeft:
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.zero;
                    rect.pivot = Vector2.zero;
                    rect.anchoredPosition = Vector2.zero;
                    break;
                case TextAnchor.UpperRight:
                    rect.anchorMin = Vector2.right;
                    rect.anchorMax = Vector2.right;
                    rect.pivot = Vector2.right;
                    rect.anchoredPosition = Vector2.zero;
                    rect.localScale = new Vector3(-1f, 1f, 1f);
                    break;
                case TextAnchor.LowerLeft:
                    rect.anchorMin = Vector2.up;
                    rect.anchorMax = Vector2.up;
                    rect.pivot = Vector2.up;
                    rect.anchoredPosition = Vector2.zero;
                    rect.localScale = new Vector3(1f, -1f, 1f);
                    break;
                default:
                    rect.anchorMin = Vector2.one;
                    rect.anchorMax = Vector2.one;
                    rect.pivot = Vector2.one;
                    rect.anchoredPosition = Vector2.zero;
                    rect.localScale = new Vector3(-1f, -1f, 1f);
                    break;
            }

            var image = cornerObject.AddComponent<Image>();
            image.sprite = GetCornerBracketSprite();
            image.type = Image.Type.Simple;
            image.color = UiTheme.BorderAccent;
            image.raycastTarget = false;
        }

        private static Sprite GetCornerBracketSprite()
        {
            if (cornerBracketSprite != null)
            {
                return cornerBracketSprite;
            }

            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.clear;
            }

            for (var i = 0; i < size; i++)
            {
                pixels[i] = UiTheme.BorderAccent;
                pixels[i * size] = UiTheme.BorderAccent;
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            cornerBracketSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0f, 0f), 100f);
            return cornerBracketSprite;
        }

        private static Sprite GetFrostNoiseSprite()
        {
            if (frostNoiseSprite != null)
            {
                return frostNoiseSprite;
            }

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[size * size];
            var rng = new System.Random(9137);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var n = (float)rng.NextDouble();
                    pixels[y * size + x] = new Color(1f, 1f, 1f, n * 0.08f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            frostNoiseSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return frostNoiseSprite;
        }

        private static Sprite GetVignetteSprite()
        {
            if (vignetteSprite != null)
            {
                return vignetteSprite;
            }

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[size * size];
            var center = (size - 1) * 0.5f;
            var maxDist = center * 1.15f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy) / maxDist;
                    var alpha = Mathf.Clamp01(Mathf.Pow(dist, 1.8f)) * 0.72f;
                    pixels[y * size + x] = new Color(0f, 0f, 0f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            vignetteSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return vignetteSprite;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
