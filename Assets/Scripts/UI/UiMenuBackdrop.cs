using UnityEngine;

namespace ShooterPrototype.UI
{
    public static class UiMenuBackdrop
    {
        private static GameObject frostRoot;
        private static int openPanelCount;

        public static void PushOpen(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            EnsureFrost(canvas.transform);
            openPanelCount++;
            frostRoot.SetActive(true);
        }

        public static void PopClosed()
        {
            openPanelCount = Mathf.Max(0, openPanelCount - 1);
            if (frostRoot != null)
            {
                frostRoot.SetActive(openPanelCount > 0);
            }
        }

        private static void EnsureFrost(Transform canvasRoot)
        {
            if (frostRoot != null)
            {
                if (frostRoot.transform.parent != canvasRoot)
                {
                    frostRoot.transform.SetParent(canvasRoot, false);
                    frostRoot.transform.SetAsFirstSibling();
                }

                return;
            }

            frostRoot = UiDecor.CreateFrostedBackdrop(canvasRoot, raycast: false).gameObject;
            frostRoot.SetActive(false);
        }
    }
}
