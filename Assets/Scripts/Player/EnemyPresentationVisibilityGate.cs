using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Pauses expensive enemy presentation work when the local player camera cannot see them.
    /// Gameplay (AI, network pose, health) keeps running.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class EnemyPresentationVisibilityGate : MonoBehaviour
    {
        private Renderer[] renderers;
        private Animator[] animators;
        private SyntyLocomotionDriver locomotionDriver;
        private ProceduralLocomotionRig locomotionRig;
        private RemoteLeftHandIkBinder handIkBinder;
        private RemoteLookPitchPosture lookPitchPosture;
        private RemoteWeaponPresentation weaponPresentation;
        private bool presentationActive = true;
        private int frameOffset;
        private Camera cachedCamera;

        public bool PresentationActive => presentationActive;

        private void Awake()
        {
            frameOffset = GetInstanceID() & 3;
            CacheComponents();
        }

        private void OnEnable()
        {
            CacheComponents();
            EnemyPresentationVisibilityUtility.RegisterPresentationGate(this);
            SetPresentationActive(true);
        }

        private void OnDisable()
        {
            EnemyPresentationVisibilityUtility.UnregisterPresentationGate(this);
        }

        private void Update()
        {
            if (MainMenuPlayerPreview.IsMenuPreviewSpawn ||
                MainMenuPlayerPreview.IsMenuPreviewInstance(gameObject))
            {
                return;
            }

            var interval = Mathf.Max(1, GameplayPerformanceOptions.EnemyVisibilityCheckIntervalFrames);
            if ((Time.frameCount + frameOffset) % interval != 0)
            {
                return;
            }

            if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
            {
                cachedCamera = EnemyPresentationVisibilityUtility.ResolveLocalPlayerCamera();
            }

            var visible = EnemyPresentationVisibilityUtility.IsVisibleToCamera(
                cachedCamera,
                renderers,
                transform.position);
            if (visible == presentationActive)
            {
                return;
            }

            SetPresentationActive(visible);
        }

        private void CacheComponents()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            animators = GetComponentsInChildren<Animator>(true);
            locomotionDriver = GetComponent<SyntyLocomotionDriver>();
            if (locomotionDriver == null)
            {
                locomotionDriver = GetComponentInChildren<SyntyLocomotionDriver>(true);
            }

            locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            handIkBinder = GetComponent<RemoteLeftHandIkBinder>();
            lookPitchPosture = GetComponent<RemoteLookPitchPosture>();
            weaponPresentation = GetComponent<RemoteWeaponPresentation>();
        }

        private void SetPresentationActive(bool active)
        {
            presentationActive = active;

            SetAnimatorsEnabled(active);

            if (locomotionDriver != null)
            {
                locomotionDriver.enabled = active;
            }

            if (locomotionRig != null)
            {
                locomotionRig.SetProceduralVisualsEnabled(
                    active && GameplayPerformanceOptions.UseProceduralRemoteLocomotion);
            }

            if (handIkBinder != null)
            {
                handIkBinder.enabled = active;
            }

            if (lookPitchPosture != null)
            {
                lookPitchPosture.enabled = active;
            }

            if (weaponPresentation != null)
            {
                weaponPresentation.enabled = active;
            }
        }

        private void SetAnimatorsEnabled(bool enabled)
        {
            if (animators == null)
            {
                return;
            }

            for (var i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator != null)
                {
                    animator.enabled = enabled;
                }
            }
        }
    }
}
