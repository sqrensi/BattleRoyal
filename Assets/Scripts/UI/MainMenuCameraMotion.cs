using UnityEngine;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class MainMenuCameraMotion : MonoBehaviour
    {
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private string cameraTag = "MainCamera";

        [Header("Position Sway")]
        [SerializeField] private float horizontalAmplitude = 0.22f;
        [SerializeField] private float verticalAmplitude = 0.065f;
        [SerializeField] private float depthAmplitude = 0.12f;
        [SerializeField] private float horizontalSpeed = 0.14f;
        [SerializeField] private float verticalSpeed = 0.11f;
        [SerializeField] private float depthSpeed = 0.09f;

        [Header("Rotation Sway")]
        [SerializeField] private float pitchAmplitude = 0.75f;
        [SerializeField] private float yawAmplitude = 1.05f;
        [SerializeField] private float rollAmplitude = 0.28f;
        [SerializeField] private float pitchSpeed = 0.09f;
        [SerializeField] private float yawSpeed = 0.07f;
        [SerializeField] private float rollSpeed = 0.06f;

        [Header("Smoothing")]
        [SerializeField] private float positionSmoothTime = 0.62f;
        [SerializeField] private float rotationSmoothTime = 0.68f;

        private Vector3 basePosition;
        private Quaternion baseRotation;
        private Vector3 currentPositionOffset;
        private Vector3 positionOffsetVelocity;
        private Vector3 currentEulerOffset;
        private Vector3 eulerOffsetVelocity;
        private float phaseOffset;
        private bool hasBasePose;

        private void Awake()
        {
            ResolveCamera();
            phaseOffset = Random.Range(0f, 100f);
            CaptureBasePose();
        }

        private void OnEnable()
        {
            ResolveCamera();
            CaptureBasePose();
        }

        private void LateUpdate()
        {
            if (cameraTransform == null)
            {
                return;
            }

            if (!hasBasePose)
            {
                CaptureBasePose();
            }

            var time = Time.unscaledTime + phaseOffset;
            var targetPositionOffset = new Vector3(
                Mathf.Sin(time * horizontalSpeed) * horizontalAmplitude +
                Mathf.Sin(time * horizontalSpeed * 0.43f + 1.8f) * (horizontalAmplitude * 0.35f),
                Mathf.Sin(time * verticalSpeed + 1.4f) * verticalAmplitude,
                Mathf.Cos(time * depthSpeed + 0.6f) * depthAmplitude);
            var targetEulerOffset = new Vector3(
                Mathf.Sin(time * pitchSpeed + 0.9f) * pitchAmplitude,
                Mathf.Sin(time * yawSpeed + 2.2f) * yawAmplitude,
                Mathf.Sin(time * rollSpeed + 1.1f) * rollAmplitude);

            currentPositionOffset = Vector3.SmoothDamp(
                currentPositionOffset,
                targetPositionOffset,
                ref positionOffsetVelocity,
                positionSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            currentEulerOffset = Vector3.SmoothDamp(
                currentEulerOffset,
                targetEulerOffset,
                ref eulerOffsetVelocity,
                rotationSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            cameraTransform.SetPositionAndRotation(
                basePosition + currentPositionOffset,
                baseRotation * Quaternion.Euler(currentEulerOffset));
        }

        private void ResolveCamera()
        {
            if (cameraTransform != null)
            {
                return;
            }

            var taggedCamera = GameObject.FindGameObjectWithTag(cameraTag);
            if (taggedCamera != null)
            {
                cameraTransform = taggedCamera.transform;
                return;
            }

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                cameraTransform = mainCamera.transform;
            }
        }

        private void CaptureBasePose()
        {
            if (cameraTransform == null)
            {
                hasBasePose = false;
                return;
            }

            basePosition = cameraTransform.position;
            baseRotation = cameraTransform.rotation;
            currentPositionOffset = Vector3.zero;
            currentEulerOffset = Vector3.zero;
            positionOffsetVelocity = Vector3.zero;
            eulerOffsetVelocity = Vector3.zero;
            hasBasePose = true;
        }
    }
}
