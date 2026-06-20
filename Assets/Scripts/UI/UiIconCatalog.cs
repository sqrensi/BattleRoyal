using UnityEngine;

namespace ShooterPrototype.UI
{
    public static class UiIconCatalog
    {
        public enum IconKind
        {
            Coin,
            Settings,
            Inventory,
            Shop,
            Back,
            Case,
            Skin,
        }

        private static Sprite coinIcon;
        private static Sprite settingsIcon;
        private static Sprite inventoryIcon;
        private static Sprite shopIcon;
        private static Sprite backIcon;
        private static Sprite caseIcon;
        private static Sprite skinIcon;

        public static Sprite GetIcon(IconKind kind)
        {
            return kind switch
            {
                IconKind.Coin => coinIcon ??= CreateCoinIcon(),
                IconKind.Settings => settingsIcon ??= CreateGearIcon(),
                IconKind.Inventory => inventoryIcon ??= CreateBagIcon(),
                IconKind.Shop => shopIcon ??= CreateCartIcon(),
                IconKind.Back => backIcon ??= CreateArrowIcon(),
                IconKind.Case => caseIcon ??= CreateCaseIcon(),
                _ => skinIcon ??= CreateSkinIcon(),
            };
        }

        public static void AttachIcon(
            RectTransform parent,
            IconKind kind,
            float size = 22f,
            Vector2 anchoredPosition = default,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var iconObject = new GameObject("Icon_" + kind);
            iconObject.transform.SetParent(parent, false);
            var rect = iconObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = anchoredPosition;
            switch (anchor)
            {
                case TextAnchor.MiddleLeft:
                    rect.anchorMin = new Vector2(0f, 0.5f);
                    rect.anchorMax = new Vector2(0f, 0.5f);
                    rect.pivot = new Vector2(0f, 0.5f);
                    break;
                case TextAnchor.MiddleRight:
                    rect.anchorMin = new Vector2(1f, 0.5f);
                    rect.anchorMax = new Vector2(1f, 0.5f);
                    rect.pivot = new Vector2(1f, 0.5f);
                    break;
                default:
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    break;
            }

            var image = iconObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = GetIcon(kind);
            image.preserveAspect = true;
            image.color = UiTheme.TextAccent;
            image.raycastTarget = false;
        }

        private static Sprite CreateCoinIcon()
        {
            return CreateRingIcon(new Color(0.95f, 0.78f, 0.22f, 1f), new Color(0.62f, 0.44f, 0.08f, 1f));
        }

        private static Sprite CreateGearIcon()
        {
            const int size = 24;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = Clear(size);
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    var angle = Mathf.Atan2(dy, dx);
                    var tooth = Mathf.Abs(Mathf.Sin(angle * 6f)) > 0.55f ? 1.12f : 1f;
                    if (dist >= 4f * tooth && dist <= 9.5f * tooth)
                    {
                        pixels[y * size + x] = Color.white;
                    }
                }
            }

            return Finish(texture, pixels, size);
        }

        private static Sprite CreateBagIcon()
        {
            return CreateBoxIcon(true);
        }

        private static Sprite CreateCartIcon()
        {
            return CreateBoxIcon(false);
        }

        private static Sprite CreateArrowIcon()
        {
            const int size = 24;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = Clear(size);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    if (x >= 6 && x <= 17 && y >= 10 && y <= 13)
                    {
                        pixels[y * size + x] = Color.white;
                    }

                    var localX = x - 8;
                    var localY = y - 11;
                    if (localX >= 0 && localY >= 0 && localX + localY <= 8)
                    {
                        pixels[y * size + x] = Color.white;
                    }
                }
            }

            return Finish(texture, pixels, size);
        }

        private static Sprite CreateCaseIcon() => CreateBoxIcon(false);
        private static Sprite CreateSkinIcon() => CreateRingIcon(Color.white, new Color(0.75f, 0.75f, 0.75f, 1f));

        private static Sprite CreateRingIcon(Color inner, Color outer)
        {
            const int size = 24;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = Clear(size);
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    if (dist >= 6f && dist <= 9f)
                    {
                        pixels[y * size + x] = outer;
                    }
                    else if (dist <= 5f)
                    {
                        pixels[y * size + x] = inner;
                    }
                }
            }

            return Finish(texture, pixels, size);
        }

        private static Sprite CreateBoxIcon(bool withHandle)
        {
            const int size = 24;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = Clear(size);
            FillRect(pixels, size, 6, 8, 17, 18, Color.white);
            if (withHandle)
            {
                FillRect(pixels, size, 9, 5, 15, 7, Color.white);
            }

            return Finish(texture, pixels, size);
        }

        private static Color[] Clear(int size)
        {
            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.clear;
            }

            return pixels;
        }

        private static void FillRect(Color[] pixels, int size, int x0, int y0, int x1, int y1, Color color)
        {
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    pixels[y * size + x] = color;
                }
            }
        }

        private static Sprite Finish(Texture2D texture, Color[] pixels, int size)
        {
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
