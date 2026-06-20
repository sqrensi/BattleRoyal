using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuCurrencyDisplay : MonoBehaviour
    {
        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float valueFontSize = 24f;

        private TMP_Text valueText;
        private CanvasGroup canvasGroup;
        private bool built;

        public CanvasGroup CanvasGroup => canvasGroup;

        public void Build(RectTransform canvasRect)
        {
            if (built || canvasRect == null)
            {
                return;
            }

            PlayerSkinOwnershipService.EnsureInitialized();

            var rootObject = new GameObject("MainMenuCurrencyDisplay");
            rootObject.transform.SetParent(canvasRect, false);

            var rect = rootObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-edgeMargin, -edgeMargin);

            var layout = rootObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.spacing = 4f;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = rootObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var iconObject = new GameObject("CoinIcon");
            iconObject.transform.SetParent(rootObject.transform, false);
            var iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.sizeDelta = new Vector2(22f, 22f);
            var iconLayout = iconObject.AddComponent<LayoutElement>();
            iconLayout.preferredWidth = 22f;
            iconLayout.preferredHeight = 22f;
            var iconImage = iconObject.AddComponent<Image>();
            iconImage.sprite = UiIconCatalog.GetIcon(UiIconCatalog.IconKind.Coin);
            iconImage.preserveAspect = true;
            iconImage.color = UiTheme.TextAccent;
            iconImage.raycastTarget = false;

            var textObject = new GameObject("Value");
            textObject.transform.SetParent(rootObject.transform, false);
            valueText = textObject.AddComponent<TextMeshProUGUI>();
            valueText.fontSize = valueFontSize;
            valueText.alignment = TextAlignmentOptions.MidlineRight;
            valueText.characterSpacing = 1f;
            valueText.enableWordWrapping = false;
            UiTheme.ApplyTmp(valueText, UiTextRole.Accent);

            canvasGroup = rootObject.AddComponent<CanvasGroup>();
            built = true;
            Refresh();
        }

        private void OnEnable()
        {
            PlayerCurrencyService.BalanceChanged += Refresh;
            PlayerProfileService.ProfileSynced += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            PlayerCurrencyService.BalanceChanged -= Refresh;
            PlayerProfileService.ProfileSynced -= Refresh;
        }

        public void Refresh()
        {
            if (valueText == null)
            {
                return;
            }

            valueText.text = FormatBalance(PlayerProfileService.GetSpendableBalance());
        }

        private static string FormatBalance(int balance)
        {
            return balance.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
        }
    }
}
