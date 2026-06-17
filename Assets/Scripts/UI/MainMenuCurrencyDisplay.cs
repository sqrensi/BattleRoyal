using ShooterPrototype.Player;
using TMPro;
using UnityEngine;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuCurrencyDisplay : MonoBehaviour
    {
        private static readonly Color ValueColor = new Color(0.98f, 0.9f, 0.58f, 1f);

        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float valueFontSize = 24f;

        private TMP_Text valueText;
        private bool built;

        public void Build(RectTransform canvasRect)
        {
            if (built || canvasRect == null)
            {
                return;
            }

            PlayerSkinOwnershipService.EnsureInitialized();

            var textObject = new GameObject("MainMenuCurrencyDisplay");
            textObject.transform.SetParent(canvasRect, false);

            var rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-edgeMargin, -edgeMargin);
            rect.sizeDelta = new Vector2(240f, 36f);

            valueText = textObject.AddComponent<TextMeshProUGUI>();
            valueText.fontSize = valueFontSize;
            valueText.fontStyle = FontStyles.Bold;
            valueText.alignment = TextAlignmentOptions.TopRight;
            valueText.characterSpacing = 1f;
            valueText.color = ValueColor;
            valueText.raycastTarget = false;

            built = true;
            Refresh();
        }

        private void OnEnable()
        {
            PlayerCurrencyService.BalanceChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            PlayerCurrencyService.BalanceChanged -= Refresh;
        }

        public void Refresh()
        {
            if (valueText == null)
            {
                return;
            }

            valueText.text = FormatBalance(PlayerCurrencyService.Balance);
        }

        private static string FormatBalance(int balance)
        {
            return balance.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
        }
    }
}
