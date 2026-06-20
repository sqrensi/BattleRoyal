using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuNavNotificationBadges : MonoBehaviour
    {
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
            UiTheme.ApplyFlatFill(image, UiTheme.NotificationDot);
            image.raycastTarget = false;

            dotObject.SetActive(false);
            return dotObject;
        }
    }
}
