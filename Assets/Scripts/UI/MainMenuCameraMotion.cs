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

        [Header("Inventory View")]
        [SerializeField] private float inventoryForwardOffset = 0.32f;
        [SerializeField] private float inventoryRightOffset = 0.26f;
        [SerializeField] private float inventoryFov = 28f;
        [SerializeField] private float inventoryTransitionSmoothTime = 0.72f;

        private Camera cameraComponent;
        private Vector3 basePosition;
        private Quaternion baseRotation;
        private Vector3 currentPositionOffset;
        private Vector3 positionOffsetVelocity;
        private Vector3 currentEulerOffset;
        private Vector3 eulerOffsetVelocity;
        private float phaseOffset;
        private bool hasBasePose;
        private float defaultFov = 51f;
        private float targetViewBlend;
        private float currentViewBlend;
        private float viewBlendVelocity;

        public void EnterInventoryView()
        {
            targetViewBlend = 1f;
        }

        public void ExitInventoryView()
        {
            targetViewBlend = 0f;
        }

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

            currentViewBlend = Mathf.SmoothDamp(
                currentViewBlend,
                targetViewBlend,
                ref viewBlendVelocity,
                inventoryTransitionSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

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

            var inventoryOffset = baseRotation * new Vector3(
                inventoryRightOffset * currentViewBlend,
                0f,
                inventoryForwardOffset * currentViewBlend);

            cameraTransform.SetPositionAndRotation(
                basePosition + inventoryOffset + currentPositionOffset,
                baseRotation * Quaternion.Euler(currentEulerOffset));

            if (cameraComponent != null)
            {
                cameraComponent.fieldOfView = Mathf.Lerp(defaultFov, inventoryFov, currentViewBlend);
            }
        }

        private void ResolveCamera()
        {
            if (cameraTransform != null)
            {
                cameraComponent = cameraTransform.GetComponent<Camera>();
                if (cameraComponent == null)
                {
                    cameraComponent = cameraTransform.GetComponentInChildren<Camera>();
                }

                return;
            }

            var taggedCamera = GameObject.FindGameObjectWithTag(cameraTag);
            if (taggedCamera != null)
            {
                cameraTransform = taggedCamera.transform;
                cameraComponent = taggedCamera.GetComponent<Camera>();
                return;
            }

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                cameraTransform = mainCamera.transform;
                cameraComponent = mainCamera;
            }
        }

        private void CaptureBasePose()
        {
            if (cameraTransform == null)
            {
                hasBasePose = false;
                return;
            }

            if (cameraComponent == null)
            {
                cameraComponent = cameraTransform.GetComponent<Camera>();
            }

            basePosition = cameraTransform.position;
            baseRotation = cameraTransform.rotation;
            if (cameraComponent != null)
            {
                defaultFov = cameraComponent.fieldOfView;
            }

            currentPositionOffset = Vector3.zero;
            currentEulerOffset = Vector3.zero;
            positionOffsetVelocity = Vector3.zero;
            eulerOffsetVelocity = Vector3.zero;
            currentViewBlend = targetViewBlend;
            viewBlendVelocity = 0f;
            hasBasePose = true;
        }
    }
}
