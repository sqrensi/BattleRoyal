using ShooterPrototype.Player;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuShopCaseLootHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private CaseDefinition caseDefinition;
        private Canvas hostCanvas;
        private RectTransform slotRect;

        public void Configure(CaseDefinition definition, Canvas canvas)
        {
            caseDefinition = definition;
            hostCanvas = canvas;
            slotRect = transform as RectTransform;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!caseDefinition.IsValid || hostCanvas == null || slotRect == null)
            {
                return;
            }

            MainMenuCaseLootTooltip.Show(hostCanvas, caseDefinition, slotRect);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            MainMenuCaseLootTooltip.Hide();
        }
    }
}
