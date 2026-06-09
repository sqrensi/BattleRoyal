using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Remote third-person medkit state (weapon holster / animator). No world prop — hand animation later.
    /// </summary>
    [DefaultExecutionOrder(440)]
    public sealed class RemoteMedkitPresentation : MonoBehaviour
    {
        private RemoteWeaponPresentation weaponPresentation;
        private RemoteAnimatorHolsterPresentation holsterPresentation;
        private bool networkUsingMedkit;

        public bool IsUsingMedkit => networkUsingMedkit;

        public void SetNetworkMedkitState(bool usingMedkit)
        {
            if (networkUsingMedkit == usingMedkit)
            {
                return;
            }

            networkUsingMedkit = usingMedkit;
            weaponPresentation ??= GetComponent<RemoteWeaponPresentation>();
            holsterPresentation ??= GetComponent<RemoteAnimatorHolsterPresentation>();
            weaponPresentation?.SetMedkitPresentationActive(usingMedkit);
            holsterPresentation?.SetMedkitUsing(usingMedkit);
        }

        private void Awake()
        {
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() == null)
            {
                enabled = false;
            }
        }
    }
}
