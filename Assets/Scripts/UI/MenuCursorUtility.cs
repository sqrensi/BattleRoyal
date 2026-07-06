using ShooterPrototype.Player;
using UnityEngine;

namespace ShooterPrototype.UI
{
    public static class MenuCursorUtility
    {
        public static void UnlockForMenu()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            FpsCharacterController.SuppressTabCursorToggle = false;
        }
    }
}
