using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ShooterPrototype.UI
{
    public enum MainMenuServerConnectionState
    {
        Loading = 0,
        Connected = 1,
        Unavailable = 2,
    }

    [DisallowMultipleComponent]
    public sealed class MainMenuServerConnectionGate : MonoBehaviour
    {
        private readonly List<CanvasGroup> menuGroups = new List<CanvasGroup>();

        private TMP_Text statusText;
        private MainMenuSectionController sectionController;
        private bool built;
        private MainMenuServerConnectionState currentState = MainMenuServerConnectionState.Loading;

        public MainMenuServerConnectionState CurrentState => currentState;

        public void Configure(
            MainMenuController menuController,
            TMP_Text bottomStatusText,
            MainMenuSectionController sections = null)
        {
            statusText = bottomStatusText;
            sectionController = sections;
        }

        public void RegisterMenuGroup(CanvasGroup group)
        {
            if (group == null || menuGroups.Contains(group))
            {
                return;
            }

            menuGroups.Add(group);
        }

        public void Build(RectTransform canvasRect, MainMenuUiSoundController uiSound)
        {
            if (built)
            {
                return;
            }

            built = true;
            ApplyState(currentState, force: true);
        }

        public void SetState(MainMenuServerConnectionState state, string message = null)
        {
            currentState = state;
            ApplyState(state, force: false);
        }

        private void ApplyState(MainMenuServerConnectionState state, bool force)
        {
            if (!built && !force)
            {
                return;
            }

            if (sectionController != null && sectionController.IsPanelOpen)
            {
                return;
            }

            for (var i = 0; i < menuGroups.Count; i++)
            {
                SetGroupVisible(menuGroups[i], true);
            }
        }

        private static void SetGroupVisible(CanvasGroup group, bool visible)
        {
            if (group == null)
            {
                return;
            }

            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }
    }
}
