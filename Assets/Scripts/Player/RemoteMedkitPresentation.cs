using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Third-person medkit use driven by network snapshots.
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

        private bool ShouldApply()
        {
            return GetComponent<RemoteThirdPersonPlayerBootstrap>() != null;
        }

        private void Awake()
        {
            if (!ShouldApply())
            {
                enabled = false;
            }
        }
    }
}
