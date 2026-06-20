using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuNavNotificationBadges : MonoBehaviour
    {
        private static Sprite whiteSprite;

        private static readonly Color DotColor = new Color(0.95f, 0.82f, 0.22f, 1f);

        [SerializeField] private float dotSize = 10f;
        [SerializeField] private Vector2 dotOffset = new Vector2(-6f, -6f);

        private GameObject inventoryDot;
        private GameObject achievementsDot;

        public void Configure(Button inventoryButton, Button achievementsButton)
        {
            inventoryDot = CreateDot(inventoryButton != null ? inventoryButton.transform : null, "InventoryNotificationDot");
            achievementsDot = CreateDot(
                achievementsButton != null ? achievementsButton.transform : null,
                "AchievementsNotificationDot");
            Refresh();
        }

        private void OnEnable()
        {
            PlayerProfileService.ProfileSynced += Refresh;
            MainMenuNotificationState.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            PlayerProfileService.ProfileSynced -= Refresh;
            MainMenuNotificationState.Changed -= Refresh;
        }

        private void Refresh()
        {
            if (inventoryDot != null)
            {
                inventoryDot.SetActive(MainMenuNotificationState.HasInventoryNotifications);
            }

            if (achievementsDot != null)
            {
                achievementsDot.SetActive(MainMenuNotificationState.HasAchievementNotifications);
            }
        }

        private GameObject CreateDot(Transform parent, string objectName)
        {
            if (parent == null)
            {
                return null;
            }

            var dotObject = new GameObject(objectName);
            dotObject.transform.SetParent(parent, false);

            var rect = dotObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = dotOffset;
            rect.sizeDelta = new Vector2(dotSize, dotSize);

            var image = dotObject.AddComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.type = Image.Type.Simple;
            image.color = DotColor;
            image.raycastTarget = false;

            dotObject.SetActive(false);
            return dotObject;
        }

        private static Sprite GetWhiteSprite()
        {
            if (whiteSprite != null)
            {
                return whiteSprite;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.SetPixel(1, 0, Color.white);
            texture.SetPixel(0, 1, Color.white);
            texture.SetPixel(1, 1, Color.white);
            texture.Apply(false, false);
            whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 100f);
            return whiteSprite;
        }
    }
}
