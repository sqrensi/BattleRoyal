using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    /// <summary>
    /// Runtime UI prefab templates. Prefabs in Resources/UI are preferred; otherwise builds procedurally.
    /// </summary>
    public static class UiPrefabLibrary
    {
        private const string NavTabResourcePath = "UI/NavTab";
        private const string ItemSlotResourcePath = "UI/ItemSlot";
        private const string PriceBadgeResourcePath = "UI/PriceBadge";

        public sealed class NavTabInstance
        {
            public GameObject Root;
            public Button Button;
            public TMP_Text Label;
            public Image Background;
        }

        public sealed class ItemSlotInstance
        {
            public GameObject Root;
            public Image Background;
            public Image EquippedFrame;
            public Image RarityStripe;
            public Image Icon;
            public Button Button;
        }

        public sealed class PriceBadgeInstance
        {
            public GameObject Root;
            public Image Background;
            public TMP_Text Label;
        }

        public static NavTabInstance CreateNavTab(
            Transform parent,
            string label,
            bool selected = false,
            float width = 200f,
            float height = 58f)
        {
            var prefab = Resources.Load<GameObject>(NavTabResourcePath);
            if (prefab != null)
            {
                return BindNavTab(Object.Instantiate(prefab, parent, false), label, selected, width, height);
            }

            return BuildNavTab(parent, label, selected, width, height);
        }

        public static ItemSlotInstance CreateItemSlot(Transform parent, float width, float height)
        {
            var prefab = Resources.Load<GameObject>(ItemSlotResourcePath);
            if (prefab != null)
            {
                var instance = Object.Instantiate(prefab, parent, false);
                var layout = instance.GetComponent<LayoutElement>() ?? instance.AddComponent<LayoutElement>();
                layout.preferredWidth = width;
                layout.preferredHeight = height;
                return BindItemSlot(instance);
            }

            return BuildItemSlot(parent, width, height);
        }

        public static PriceBadgeInstance CreatePriceBadge(Transform parent, float width, float height)
        {
            var prefab = Resources.Load<GameObject>(PriceBadgeResourcePath);
            if (prefab != null)
            {
                var instance = Object.Instantiate(prefab, parent, false);
                var rect = instance.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.sizeDelta = new Vector2(width, height);
                }

                return BindPriceBadge(instance);
            }

            return BuildPriceBadge(parent, width, height);
        }

        public static NavTabInstance BuildNavTab(Transform parent, string label, bool selected, float width = 200f, float height = 58f)
        {
            var root = new GameObject("NavTab");
            root.transform.SetParent(parent, false);
            var layout = root.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;
            layout.minWidth = width;

            var background = root.AddComponent<Image>();
            var button = root.AddComponent<Button>();
            UiTheme.StyleButton(button, selected ? UiButtonStyle.NavSelected : UiButtonStyle.Standard);
            button.interactable = !selected;
            UiMotion.AttachButtonMotion(button);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(root.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            Stretch(labelRect);
            labelRect.offsetMin = new Vector2(30f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);
            var text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = label.ToUpperInvariant();
            text.fontSize = 18f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            UiTheme.ApplyMilitaryHeader(text, selected ? UiTextRole.Accent : UiTextRole.Heading);
            text.characterSpacing = 1.5f;

            return new NavTabInstance
            {
                Root = root,
                Button = button,
                Label = text,
                Background = background,
            };
        }

        public static ItemSlotInstance BuildItemSlot(Transform parent, float width, float height)
        {
            var root = new GameObject("ItemSlot");
            root.transform.SetParent(parent, false);
            var layout = root.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;

            var background = root.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.SlotFill);
            var button = root.AddComponent<Button>();
            button.targetGraphic = background;

            var stripeObject = new GameObject("RarityStripe");
            stripeObject.transform.SetParent(root.transform, false);
            var stripe = UiDecor.CreateRarityStripe(stripeObject.transform, UiTheme.TextMuted);
            stripe.gameObject.SetActive(false);

            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(root.transform, false);
            var iconRect = iconObject.AddComponent<RectTransform>();
            Stretch(iconRect);
            iconRect.offsetMin = new Vector2(10f, 10f);
            iconRect.offsetMax = new Vector2(-10f, -10f);
            var icon = iconObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var frameObject = new GameObject("EquippedFrame");
            frameObject.transform.SetParent(root.transform, false);
            var frameRect = frameObject.AddComponent<RectTransform>();
            Stretch(frameRect);
            var equippedFrame = frameObject.AddComponent<Image>();
            UiTheme.ApplyEquippedFrame(equippedFrame, false);

            return new ItemSlotInstance
            {
                Root = root,
                Background = background,
                Button = button,
                Icon = icon,
                RarityStripe = stripe,
                EquippedFrame = equippedFrame,
            };
        }

        public static PriceBadgeInstance BuildPriceBadge(Transform parent, float width, float height)
        {
            var root = new GameObject("PriceBadge");
            root.transform.SetParent(parent, false);
            var rect = root.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);

            var background = root.AddComponent<Image>();
            UiTheme.ApplyPriceBadge(background, canAfford: true);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(root.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            Stretch(labelRect);
            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 18f;
            UiTheme.ApplyTmp(label, UiTextRole.Accent);

            return new PriceBadgeInstance
            {
                Root = root,
                Background = background,
                Label = label,
            };
        }

        private static NavTabInstance BindNavTab(GameObject root, string label, bool selected, float width, float height)
        {
            var layout = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;
            layout.minWidth = width;

            var button = root.GetComponent<Button>();
            var text = root.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label.ToUpperInvariant();
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
            }

            if (button != null)
            {
                UiTheme.StyleButton(button, selected ? UiButtonStyle.NavSelected : UiButtonStyle.Standard);
                button.interactable = !selected;
            }

            return new NavTabInstance
            {
                Root = root,
                Button = button,
                Label = text,
                Background = root.GetComponent<Image>(),
            };
        }

        private static ItemSlotInstance BindItemSlot(GameObject root)
        {
            var stripeTransform = root.transform.Find("RarityStripe");
            var frameTransform = root.transform.Find("EquippedFrame");
            return new ItemSlotInstance
            {
                Root = root,
                Background = root.GetComponent<Image>(),
                Button = root.GetComponent<Button>(),
                Icon = root.transform.Find("Icon")?.GetComponent<Image>(),
                RarityStripe = stripeTransform != null ? stripeTransform.GetComponent<Image>() : null,
                EquippedFrame = frameTransform != null ? frameTransform.GetComponent<Image>() : null,
            };
        }

        private static PriceBadgeInstance BindPriceBadge(GameObject root)
        {
            return new PriceBadgeInstance
            {
                Root = root,
                Background = root.GetComponent<Image>(),
                Label = root.GetComponentInChildren<TMP_Text>(true),
            };
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
