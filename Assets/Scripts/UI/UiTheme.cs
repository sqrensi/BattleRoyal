using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    public enum UiTextRole
    {
        Title,
        Heading,
        Body,
        Label,
        Muted,
        Accent,
        PrimaryButton,
        Danger,
        Success,
    }

    public enum UiPanelStyle
    {
        Standard,
        Heavy,
        Overlay,
        Hud,
        Inventory,
    }

    public enum UiButtonStyle
    {
        Standard,
        Primary,
        NavSelected,
        Ghost,
    }

    /// <summary>
    /// Central military / PUBG-inspired UI palette, fonts, and bordered sprites.
    /// Logic-free — visual tokens and styling helpers only.
    /// </summary>
    public static class UiTheme
    {
        // ── Palette ──────────────────────────────────────────────────────────
        public static readonly Color CanvasDim = new Color(0.02f, 0.018f, 0.015f, 0.72f);
        public static readonly Color PanelFill = new Color(0.07f, 0.065f, 0.055f, 0.9f);
        public static readonly Color PanelFillInventory = new Color(0.07f, 0.065f, 0.055f, 0.26f);
        public static readonly Color PanelFillHeavy = new Color(0.05f, 0.045f, 0.04f, 0.94f);
        public static readonly Color PanelFillOverlay = new Color(0.04f, 0.038f, 0.032f, 0.82f);
        public static readonly Color PanelFillHud = new Color(0.24f, 0.23f, 0.21f, 0.38f);
        public static readonly Color InventorySlotFill = new Color(0.12f, 0.11f, 0.1f, 0.34f);

        public static readonly Color BorderOuter = new Color(0.28f, 0.26f, 0.22f, 0.82f);
        public static readonly Color BorderInner = new Color(0.05f, 0.045f, 0.04f, 0.38f);
        public static readonly Color BorderAccent = new Color(0.62f, 0.52f, 0.28f, 0.55f);

        public static readonly Color ButtonNormal = new Color(0.11f, 0.105f, 0.095f, 0.94f);
        public static readonly Color ButtonHighlight = new Color(0.18f, 0.17f, 0.15f, 0.98f);
        public static readonly Color ButtonPressed = new Color(0.08f, 0.075f, 0.07f, 1f);
        public static readonly Color ButtonSelected = new Color(0.24f, 0.22f, 0.19f, 0.98f);
        public static readonly Color ButtonDisabled = new Color(0.1f, 0.095f, 0.09f, 0.45f);

        public static readonly Color PrimaryTop = new Color(0.93f, 0.78f, 0.28f, 1f);
        public static readonly Color PrimaryBottom = new Color(0.72f, 0.52f, 0.12f, 1f);
        public static readonly Color PrimaryBorder = new Color(0.45f, 0.32f, 0.08f, 1f);
        public static readonly Color PrimaryHighlight = new Color(1f, 0.88f, 0.42f, 1f);
        public static readonly Color PrimaryPressed = new Color(0.62f, 0.44f, 0.1f, 1f);
        public static readonly Color PrimaryText = new Color(0.12f, 0.09f, 0.04f, 0.98f);

        public static readonly Color TextPrimary = new Color(0.94f, 0.91f, 0.86f, 0.98f);
        public static readonly Color TextHeading = new Color(0.97f, 0.94f, 0.88f, 0.99f);
        public static readonly Color TextBody = new Color(0.88f, 0.85f, 0.8f, 0.96f);
        public static readonly Color TextLabel = new Color(0.78f, 0.74f, 0.68f, 0.94f);
        public static readonly Color TextMuted = new Color(0.58f, 0.55f, 0.5f, 0.88f);
        public static readonly Color TextAccent = new Color(0.92f, 0.76f, 0.32f, 0.98f);

        public static readonly Color ScrollTrack = new Color(0.06f, 0.055f, 0.05f, 0.62f);
        public static readonly Color ScrollHandle = new Color(0.28f, 0.25f, 0.21f, 0.92f);

        public static readonly Color ToggleOn = new Color(0.62f, 0.48f, 0.16f, 0.96f);
        public static readonly Color ToggleOff = new Color(0.1f, 0.095f, 0.085f, 0.9f);

        public static readonly Color SectionFill = new Color(0.09f, 0.085f, 0.075f, 0.88f);
        public static readonly Color SlotFill = new Color(0.08f, 0.075f, 0.065f, 0.82f);
        public static readonly Color SlotEmpty = new Color(0.06f, 0.055f, 0.05f, 0.65f);
        public static readonly Color SlotHighlight = new Color(0.18f, 0.16f, 0.13f, 0.92f);

        public static readonly Color Success = new Color(0.55f, 0.72f, 0.38f, 0.96f);
        public static readonly Color Danger = new Color(0.72f, 0.28f, 0.22f, 0.96f);
        public static readonly Color Warning = new Color(0.88f, 0.62f, 0.18f, 0.96f);

        public static readonly Color KillFeedKiller = new Color(0.92f, 0.76f, 0.32f, 0.98f);
        public static readonly Color KillFeedVictim = new Color(0.88f, 0.85f, 0.8f, 0.94f);
        public static readonly Color KillFeedWeapon = new Color(0.62f, 0.58f, 0.52f, 0.88f);

        public static readonly Color HealthHigh = new Color(0.55f, 0.72f, 0.38f, 0.95f);
        public static readonly Color HealthMid = new Color(0.88f, 0.68f, 0.18f, 0.95f);
        public static readonly Color HealthLow = new Color(0.78f, 0.22f, 0.18f, 0.95f);

        public static readonly Color NotificationDot = new Color(0.95f, 0.78f, 0.18f, 0.98f);
        public static readonly Color CurrencyAccent = new Color(0.92f, 0.76f, 0.28f, 0.98f);
        public static readonly Color PriceBadgeFill = new Color(0.06f, 0.055f, 0.048f, 0.88f);
        public static readonly Color PriceBadgeFillMuted = new Color(0.05f, 0.048f, 0.044f, 0.78f);
        public static readonly Color PriceBadgeAffordableFill = new Color(0.11f, 0.10f, 0.08f, 0.9f);
        public static readonly Color PriceBadgeAffordableBorder = new Color(0.48f, 0.38f, 0.14f, 0.55f);

        public static readonly Color EquippedBorder = new Color(0.98f, 0.82f, 0.24f, 1f);
        public static readonly Color EquippedBorderBright = new Color(1f, 0.92f, 0.42f, 1f);
        public static readonly Color EquippedBorderPulse = new Color(1f, 0.78f, 0.18f, 1f);

        public static readonly Color SliderFill = new Color(0.72f, 0.55f, 0.18f, 0.92f);
        public static readonly Color SliderTrack = new Color(0.08f, 0.075f, 0.065f, 0.88f);

        public static readonly Color GameOverWin = new Color(0.92f, 0.76f, 0.28f, 0.98f);
        public static readonly Color GameOverLoss = new Color(0.78f, 0.28f, 0.22f, 0.98f);

        // Legacy aliases used by older panel code during migration
        public static Color PanelColor => PanelFill;
        public static Color TitleColor => TextHeading;
        public static Color LabelColor => TextLabel;
        public static Color ValueColor => TextPrimary;
        public static Color MutedColor => TextMuted;
        public static Color SectionColor => SectionFill;
        public static Color ScrollTrackColor => ScrollTrack;
        public static Color ScrollHandleColor => ScrollHandle;
        public static Color ToggleOnColor => ToggleOn;
        public static Color ToggleOffColor => ToggleOff;

        // ── Cached assets ────────────────────────────────────────────────────
        private static Sprite whiteSprite;
        private static Sprite panelSprite;
        private static Sprite panelHeavySprite;
        private static Sprite overlayPanelSprite;
        private static Sprite hudPanelSprite;
        private static Sprite panelInventorySprite;
        private static Sprite buttonSprite;
        private static Sprite primaryButtonSprite;
        private static Sprite navSelectedSprite;
        private static Sprite equippedFrameSprite;
        private static Sprite priceBadgeSprite;
        private static Sprite priceBadgeAffordableSprite;
        private static TMP_FontAsset titleFont;
        private static TMP_FontAsset bodyFont;
        private static Font legacyFont;
        private static bool fontsResolved;

        public static Sprite WhiteSprite => whiteSprite ??= CreateSolidSprite(Color.white);

        public static Sprite PanelSprite => panelSprite ??= CreateBorderedSprite(
            PanelFill, BorderOuter, BorderInner, borderPixels: 2, shadowPixels: 12);

        public static Sprite PanelHeavySprite => panelHeavySprite ??= CreateBorderedSprite(
            PanelFillHeavy, BorderOuter, BorderInner, borderPixels: 2, shadowPixels: 12);

        public static Sprite OverlayPanelSprite => overlayPanelSprite ??= CreateBorderedSprite(
            PanelFillOverlay, BorderOuter, BorderInner, borderPixels: 2, shadowPixels: 12);

        public static Sprite HudPanelSprite => hudPanelSprite ??= CreateBorderedSprite(
            PanelFillHud, BorderOuter, BorderInner, borderPixels: 2, shadowPixels: 10);

        public static Sprite PanelInventorySprite => panelInventorySprite ??= CreateBorderedSprite(
            PanelFillInventory, BorderOuter, BorderInner, borderPixels: 2, shadowPixels: 12);

        public static Sprite ButtonSprite => buttonSprite ??= CreateBorderedSprite(
            ButtonNormal, BorderOuter, BorderInner, borderPixels: 2, shadowPixels: 10);

        public static Sprite NavSelectedSprite => navSelectedSprite ??= CreateBorderedSprite(
            ButtonSelected, BorderAccent, BorderInner, borderPixels: 2, shadowPixels: 10);

        public static Sprite PrimaryButtonSprite => primaryButtonSprite ??= CreatePrimaryButtonSprite();

        public static Sprite EquippedFrameSprite => equippedFrameSprite ??= CreateFrameOverlaySprite(
            EquippedBorder, borderPixels: 3, glowPixels: 1);

        public static Sprite PriceBadgeSprite => priceBadgeSprite ??= CreateBorderedSprite(
            PriceBadgeFill, BorderOuter, BorderInner, borderPixels: 1, shadowPixels: 4);

        public static Sprite PriceBadgeAffordableSprite => priceBadgeAffordableSprite ??= CreateBorderedSprite(
            PriceBadgeAffordableFill, PriceBadgeAffordableBorder, BorderInner, borderPixels: 1, shadowPixels: 3);

        public static TMP_FontAsset TitleFont => ResolveTitleFont();
        public static TMP_FontAsset BodyFont => ResolveBodyFont();

        // ── Apply helpers ────────────────────────────────────────────────────
        public static void ApplyPanel(Image image, UiPanelStyle style = UiPanelStyle.Standard)
        {
            if (image == null)
            {
                return;
            }

            switch (style)
            {
                case UiPanelStyle.Heavy:
                    image.sprite = PanelHeavySprite;
                    image.color = Color.white;
                    break;
                case UiPanelStyle.Overlay:
                    image.sprite = OverlayPanelSprite;
                    image.color = Color.white;
                    break;
                case UiPanelStyle.Hud:
                    image.sprite = HudPanelSprite;
                    image.color = Color.white;
                    break;
                case UiPanelStyle.Inventory:
                    image.sprite = PanelInventorySprite;
                    image.color = Color.white;
                    break;
                default:
                    image.sprite = PanelSprite;
                    image.color = Color.white;
                    break;
            }

            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
        }

        public static void ApplyFlatFill(Image image, Color color)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = WhiteSprite;
            image.type = Image.Type.Simple;
            image.color = color;
        }

        public static void ApplyPriceBadge(Image image, bool canAfford)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = canAfford ? PriceBadgeAffordableSprite : PriceBadgeSprite;
            image.type = Image.Type.Sliced;
            image.color = canAfford ? new Color(1f, 1f, 1f, 0.98f) : new Color(0.78f, 0.78f, 0.78f, 0.88f);
            image.raycastTarget = false;
        }

        public static void ApplyEquippedFrame(Image image, bool visible)
        {
            if (image == null)
            {
                return;
            }

            image.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            image.sprite = EquippedFrameSprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        public static void StyleButton(Button button, UiButtonStyle style = UiButtonStyle.Standard)
        {
            if (button == null)
            {
                return;
            }

            var graphic = button.targetGraphic as Image;
            if (graphic != null)
            {
                switch (style)
                {
                    case UiButtonStyle.Primary:
                        graphic.sprite = PrimaryButtonSprite;
                        graphic.type = Image.Type.Sliced;
                        graphic.color = Color.white;
                        break;
                    case UiButtonStyle.NavSelected:
                        graphic.sprite = NavSelectedSprite;
                        graphic.type = Image.Type.Sliced;
                        graphic.color = Color.white;
                        break;
                    case UiButtonStyle.Ghost:
                        graphic.sprite = WhiteSprite;
                        graphic.type = Image.Type.Simple;
                        graphic.color = new Color(1f, 1f, 1f, 0.04f);
                        break;
                    default:
                        graphic.sprite = ButtonSprite;
                        graphic.type = Image.Type.Sliced;
                        graphic.color = Color.white;
                        break;
                }
            }

            button.colors = GetButtonColors(style);
        }

        public static ColorBlock GetButtonColors(UiButtonStyle style)
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;

            switch (style)
            {
                case UiButtonStyle.Primary:
                    colors.normalColor = Color.white;
                    colors.highlightedColor = new Color(1.08f, 1.06f, 1f, 1f);
                    colors.pressedColor = new Color(0.82f, 0.78f, 0.72f, 1f);
                    colors.selectedColor = Color.white;
                    colors.disabledColor = ButtonDisabled;
                    break;
                case UiButtonStyle.NavSelected:
                    colors.normalColor = Color.white;
                    colors.highlightedColor = Color.white;
                    colors.pressedColor = Color.white;
                    colors.selectedColor = Color.white;
                    colors.disabledColor = ButtonDisabled;
                    break;
                case UiButtonStyle.Ghost:
                    colors.normalColor = new Color(1f, 1f, 1f, 0.92f);
                    colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
                    colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 0.85f);
                    colors.selectedColor = Color.white;
                    colors.disabledColor = ButtonDisabled;
                    break;
                default:
                    colors.normalColor = Color.white;
                    colors.highlightedColor = new Color(1.12f, 1.1f, 1.06f, 1f);
                    colors.pressedColor = new Color(0.78f, 0.76f, 0.72f, 1f);
                    colors.selectedColor = Color.white;
                    colors.disabledColor = ButtonDisabled;
                    break;
            }

            return colors;
        }

        public static void ApplyTmp(TMP_Text text, UiTextRole role)
        {
            if (text == null)
            {
                return;
            }

            EnsureFontsLoaded();
            text.color = GetTextColor(role);
            text.raycastTarget = false;

            switch (role)
            {
                case UiTextRole.Title:
                    text.font = TitleFont;
                    text.fontStyle = FontStyles.Bold;
                    break;
                case UiTextRole.Heading:
                    text.font = TitleFont;
                    text.fontStyle = FontStyles.Bold;
                    break;
                case UiTextRole.Accent:
                    text.font = TitleFont;
                    text.fontStyle = FontStyles.Bold;
                    break;
                case UiTextRole.PrimaryButton:
                    text.font = TitleFont;
                    text.fontStyle = FontStyles.Bold;
                    break;
                default:
                    text.font = BodyFont;
                    text.fontStyle = FontStyles.Normal;
                    break;
            }
        }

        public static void ApplyMilitaryHeader(TMP_Text text, UiTextRole role = UiTextRole.Heading)
        {
            ApplyTmp(text, role);
            if (text == null)
            {
                return;
            }

            text.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            text.characterSpacing = 2.5f;
        }

        public static void ApplyLegacyText(Text text, UiTextRole role = UiTextRole.Body)
        {
            if (text == null)
            {
                return;
            }

            text.font = GetLegacyFont();
            text.color = GetTextColor(role);
            text.fontStyle = role is UiTextRole.Title or UiTextRole.Heading or UiTextRole.Accent
                or UiTextRole.PrimaryButton
                ? FontStyle.Bold
                : FontStyle.Normal;
        }

        public static Color GetTextColor(UiTextRole role)
        {
            return role switch
            {
                UiTextRole.Title => TextHeading,
                UiTextRole.Heading => TextHeading,
                UiTextRole.Body => TextBody,
                UiTextRole.Label => TextLabel,
                UiTextRole.Muted => TextMuted,
                UiTextRole.Accent => TextAccent,
                UiTextRole.PrimaryButton => PrimaryText,
                UiTextRole.Danger => Danger,
                UiTextRole.Success => Success,
                _ => TextBody,
            };
        }

        public static Font GetLegacyFont()
        {
            if (legacyFont != null)
            {
                return legacyFont;
            }

            legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return legacyFont;
        }

        public static void StyleScrollbar(Image track, Image handle)
        {
            if (track != null)
            {
                ApplyFlatFill(track, ScrollTrack);
            }

            if (handle != null)
            {
                ApplyFlatFill(handle, ScrollHandle);
            }
        }

        // ── Sprite generation ────────────────────────────────────────────────
        private static Sprite CreateSolidSprite(Color color)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateBorderedSprite(
            Color fill,
            Color border,
            Color innerShadow,
            int borderPixels,
            int shadowPixels)
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    pixels[y * size + x] = SampleBorderedPixel(
                        x,
                        y,
                        size,
                        borderPixels,
                        shadowPixels,
                        fill,
                        border,
                        innerShadow);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);

            var slice = borderPixels + shadowPixels;
            var borders = new Vector4(slice, slice, slice, slice);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                borders);
        }

        private static Color SampleBorderedPixel(
            int x,
            int y,
            int size,
            int borderPixels,
            int shadowPixels,
            Color fill,
            Color border,
            Color innerShadow)
        {
            var distEdge = DistanceToEdge(x, y, size);
            var edgeWidth = Mathf.Max(1, borderPixels + shadowPixels);
            if (distEdge >= edgeWidth)
            {
                return fill;
            }

            var edgeT = distEdge / edgeWidth;
            var rimEnd = borderPixels / (float)edgeWidth;
            rimEnd = Mathf.Clamp(rimEnd, 0.06f, 0.22f);

            if (edgeT <= rimEnd)
            {
                var rimT = edgeT / rimEnd;
                return Color.Lerp(border, innerShadow, Smooth01(rimT));
            }

            var shadowT = (edgeT - rimEnd) / (1f - rimEnd);
            return Color.Lerp(innerShadow, fill, Smooth01(shadowT));
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static Sprite CreateFrameOverlaySprite(Color frameColor, int borderPixels, int glowPixels)
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[size * size];
            var outerRing = borderPixels + glowPixels;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distEdge = DistanceToEdge(x, y, size);
                    if (distEdge >= outerRing)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    if (distEdge < borderPixels)
                    {
                        var coreT = borderPixels <= 1 ? 1f : distEdge / (borderPixels - 1f);
                        pixels[y * size + x] = Color.Lerp(
                            EquippedBorderBright,
                            frameColor,
                            coreT);
                        continue;
                    }

                    var glowT = (distEdge - borderPixels) / (float)Mathf.Max(1, glowPixels);
                    var glowAlpha = frameColor.a * (1f - Mathf.SmoothStep(0f, 1f, glowT)) * 0.55f;
                    pixels[y * size + x] = new Color(frameColor.r, frameColor.g, frameColor.b, glowAlpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);

            var borders = new Vector4(outerRing, outerRing, outerRing, outerRing);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                borders);
        }

        private static float DistanceToEdge(int x, int y, int size)
        {
            return Mathf.Min(
                Mathf.Min(x, y),
                Mathf.Min(size - 1 - x, size - 1 - y));
        }

        private static Sprite CreatePrimaryButtonSprite()
        {
            const int width = 16;
            const int height = 48;
            const int border = 2;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                var t = y / (float)(height - 1);
                var fill = Color.Lerp(PrimaryBottom, PrimaryTop, t);
                for (var x = 0; x < width; x++)
                {
                    var onBorder = x < border || y < border || x >= width - border || y >= height - border;
                    var topSheen = y >= height - border - 2 && !onBorder;
                    pixels[y * width + x] = onBorder
                        ? PrimaryBorder
                        : topSheen
                            ? Color.Lerp(fill, PrimaryHighlight, 0.35f)
                            : fill;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);

            var borders = new Vector4(border, border, border, border);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                borders);
        }

        private static void EnsureFontsLoaded()
        {
            if (fontsResolved)
            {
                return;
            }

            ResolveTitleFont();
            ResolveBodyFont();
            fontsResolved = true;
        }

        private static TMP_FontAsset ResolveTitleFont()
        {
            if (titleFont != null)
            {
                return titleFont;
            }

            titleFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/Oswald Bold SDF");
            if (titleFont == null)
            {
                titleFont = TMP_Settings.defaultFontAsset;
            }

            return titleFont;
        }

        private static TMP_FontAsset ResolveBodyFont()
        {
            if (bodyFont != null)
            {
                return bodyFont;
            }

            bodyFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/Roboto-Bold SDF");
            if (bodyFont == null)
            {
                bodyFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
            }

            if (bodyFont == null)
            {
                bodyFont = TMP_Settings.defaultFontAsset;
            }

            return bodyFont;
        }
    }
}
