using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuInventoryItemHoverPreview : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private PlayerSkinDefinition item;
        private MainMenuPlayerPreview preview;

        public void Configure(PlayerSkinDefinition definition, MainMenuPlayerPreview playerPreview)
        {
            item = definition;
            preview = playerPreview;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!item.IsValid || preview == null)
            {
                return;
            }

            preview.PreviewInventoryItem(item);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            preview?.ClearInventoryPreview();
        }
    }
}
