using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuAuthRequiredButtonState : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Button button;
        private Canvas canvas;
        private string tooltipTitle = "Нужна авторизация";
        private string tooltipBody = "Эта функция доступна после входа через Яндекс ID.";
        private CanvasGroup canvasGroup;

        public void Configure(Button targetButton, Canvas hostCanvas, string title, string body)
        {
            button = targetButton;
            canvas = hostCanvas;
            tooltipTitle = string.IsNullOrWhiteSpace(title) ? tooltipTitle : title;
            tooltipBody = string.IsNullOrWhiteSpace(body) ? tooltipBody : body;
            canvasGroup = targetButton != null
                ? targetButton.GetComponent<CanvasGroup>() ?? targetButton.gameObject.AddComponent<CanvasGroup>()
                : null;
            Refresh();
        }

        public void Refresh()
        {
            var authorized = PlayerIdentityService.HasAuthorizedYandexLink();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = authorized ? 1f : 0.42f;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (canvas == null || PlayerIdentityService.HasAuthorizedYandexLink())
            {
                return;
            }

            UiTooltipController.Show(canvas, tooltipTitle, tooltipBody, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            UiTooltipController.Hide();
        }
    }
}
