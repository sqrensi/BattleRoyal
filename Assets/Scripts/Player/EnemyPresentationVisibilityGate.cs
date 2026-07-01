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
        private const int VisibilityCheckIntervalFrames = 3;

        private Renderer[] renderers;
        private SyntyLocomotionDriver locomotionDriver;
        private ProceduralLocomotionRig locomotionRig;
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
            SetPresentationActive(true);
        }

        private void Update()
        {
            if (MainMenuPlayerPreview.IsMenuPreviewSpawn)
            {
                return;
            }

            if ((Time.frameCount + frameOffset) % VisibilityCheckIntervalFrames != 0)
            {
                return;
            }

            if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
            {
                cachedCamera = EnemyPresentationVisibilityUtility.ResolveLocalPlayerCamera();
            }

            var visible = EnemyPresentationVisibilityUtility.IsVisibleToCamera(cachedCamera, renderers);
            if (visible == presentationActive)
            {
                return;
            }

            SetPresentationActive(visible);
        }

        private void CacheComponents()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            locomotionDriver = GetComponent<SyntyLocomotionDriver>();
            if (locomotionDriver == null)
            {
                locomotionDriver = GetComponentInChildren<SyntyLocomotionDriver>(true);
            }

            locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
        }

        private void SetPresentationActive(bool active)
        {
            presentationActive = active;

            if (locomotionDriver != null)
            {
                locomotionDriver.enabled = active;
            }

            if (locomotionRig != null)
            {
                locomotionRig.SetProceduralVisualsEnabled(
                    active && GameplayPerformanceOptions.UseProceduralRemoteLocomotion);
            }
        }
    }
}
