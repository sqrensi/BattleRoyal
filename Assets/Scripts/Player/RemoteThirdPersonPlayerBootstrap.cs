using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Applies third-person remote presentation when this prefab is spawned for other players.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class RemoteThirdPersonPlayerBootstrap : MonoBehaviour
    {
        [SerializeField] private bool applyOnAwake = true;
        [SerializeField] private RuntimeAnimatorController remoteAnimatorController;

        private void Awake()
        {
            if (applyOnAwake)
            {
                ApplyRemoteThirdPersonMode();
            }
        }

        public void RefreshRemoteWeaponPresentation()
        {
            ApplyRemoteThirdPersonMode();
        }

        public void ApplyRemoteThirdPersonMode()
        {
            DisableLocalOnlyComponents();

            var thirdPersonBody = transform.Find("ThirdPersonBody");
            if (thirdPersonBody != null)
            {
                thirdPersonBody.gameObject.SetActive(true);
            }

            EnableThirdPersonAnimator(thirdPersonBody);
            WireRemoteHolsterAnimation(thirdPersonBody);
            WireRemoteWeapon(thirdPersonBody);
            WireRemoteMedkit(thirdPersonBody);
            WireRemoteLookPitchPosture(thirdPersonBody);
            EnsureBoneHitboxes(thirdPersonBody);
            EnsureRemoteShotEffects();
        }

        private void WireRemoteWeapon(Transform thirdPersonBody)
        {
            if (thirdPersonBody == null)
            {
                return;
            }

            var presentation = GetComponent<RemoteWeaponPresentation>();
            if (presentation == null)
            {
                presentation = gameObject.AddComponent<RemoteWeaponPresentation>();
            }

            presentation.Configure(thirdPersonBody);
            presentation.SetWeaponEquipped(false);
        }

        private void WireRemoteMedkit(Transform thirdPersonBody)
        {
            if (thirdPersonBody == null)
            {
                return;
            }

            if (GetComponent<RemoteMedkitPresentation>() == null)
            {
                gameObject.AddComponent<RemoteMedkitPresentation>();
            }
        }

        private void EnableThirdPersonAnimator(Transform thirdPersonBody)
        {
            if (thirdPersonBody == null)
            {
                return;
            }

            var syntyVisual = thirdPersonBody.Find("SyntyVisual");
            if (syntyVisual == null)
            {
                return;
            }

            var animator = syntyVisual.GetComponent<Animator>();
            if (animator != null)
            {
                if (remoteAnimatorController != null)
                {
                    animator.runtimeAnimatorController = remoteAnimatorController;
                }

                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var holsterPresentation = GetComponent<RemoteAnimatorHolsterPresentation>();
                if (holsterPresentation == null)
                {
                    holsterPresentation = gameObject.AddComponent<RemoteAnimatorHolsterPresentation>();
                }

                holsterPresentation.Configure(animator);

                if (animator.layerCount > 0)
                {
                    for (var layer = 0; layer < animator.layerCount; layer++)
                    {
                        if (layer <= RemoteAnimatorHolsterPresentation.TorsoLayerIndex)
                        {
                            animator.SetLayerWeight(layer, 1f);
                        }
                    }
                }
            }

            var renderers = syntyVisual.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.gameObject.name.EndsWith("_FirstPersonArms", System.StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                    continue;
                }

                renderer.enabled = true;
            }
        }

        public void SetRemoteAnimatorController(RuntimeAnimatorController controller)
        {
            remoteAnimatorController = controller;
        }

        private void EnsureBoneHitboxes(Transform thirdPersonBody)
        {
            PlayerHitboxCleanup.RemoveLegacyLineHitboxes(gameObject);
            PlayerHitboxLayers.ApplyBodyLayerToPlayerRoot(gameObject);

            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            var boneRig = GetComponent<PlayerBoneHitboxRig>();
            if (boneRig == null)
            {
                boneRig = gameObject.AddComponent<PlayerBoneHitboxRig>();
            }

            boneRig.Configure(syntyVisual);
            boneRig.BuildOrRefreshHitboxes(forceRebuild: !boneRig.HasActiveHitboxes());
        }

        private void EnsureRemoteShotEffects()
        {
            if (GetComponent<RemotePlayerShotEffects>() == null)
            {
                gameObject.AddComponent<RemotePlayerShotEffects>();
            }
        }

        private void WireRemoteHolsterAnimation(Transform thirdPersonBody)
        {
            if (thirdPersonBody == null)
            {
                return;
            }

            var syntyVisual = thirdPersonBody.Find("SyntyVisual");
            var animator = syntyVisual != null ? syntyVisual.GetComponent<Animator>() : null;
            if (animator == null)
            {
                return;
            }

            var holsterPresentation = GetComponent<RemoteAnimatorHolsterPresentation>();
            if (holsterPresentation == null)
            {
                holsterPresentation = gameObject.AddComponent<RemoteAnimatorHolsterPresentation>();
            }

            holsterPresentation.Configure(animator);
        }

        private void WireRemoteLookPitchPosture(Transform thirdPersonBody)
        {
            if (thirdPersonBody == null)
            {
                return;
            }

            var locomotionRig = thirdPersonBody.GetComponent<ProceduralLocomotionRig>();
            if (locomotionRig == null)
            {
                return;
            }

            var pitchPosture = GetComponent<RemoteLookPitchPosture>();
            if (pitchPosture == null)
            {
                pitchPosture = gameObject.AddComponent<RemoteLookPitchPosture>();
            }

            pitchPosture.Configure(thirdPersonBody, locomotionRig);
        }

        private void DisableLocalOnlyComponents()
        {
            var armGate = GetComponent<SyntyFirstPersonArmLocomotionGate>();
            if (armGate != null)
            {
                armGate.enabled = false;
            }

            var armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
            if (armsPresenter != null)
            {
                armsPresenter.enabled = false;
            }

            var holster = GetComponent<PlayerWeaponHolsterController>();
            if (holster != null)
            {
                holster.enabled = false;
            }
        }
    }
}
