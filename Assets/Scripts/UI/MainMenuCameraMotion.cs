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
        [SerializeField] private float inventoryForwardOffset = 0.58f;
        [SerializeField] private float inventoryRightOffset = 0.28f;
        [SerializeField] private float inventoryFov = 24f;
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

        [Header("Mouse Parallax")]
        [SerializeField] private float mouseParallaxPosition = 0.12f;
        [SerializeField] private float mouseParallaxRotation = 0.45f;
        [SerializeField] private float mouseParallaxSmoothTime = 0.35f;

        private Vector3 mouseParallaxOffset;
        private Vector3 mouseParallaxVelocity;
        private Vector3 mouseEulerOffset;
        private Vector3 mouseEulerVelocity;

        public void EnterInventoryView()
        {
            EnsureReady();
            targetViewBlend = 1f;
        }

        public void ExitInventoryView()
        {
            EnsureReady();
            targetViewBlend = 0f;
        }

        private void EnsureReady()
        {
            ResolveCamera();
            if (cameraTransform == null)
            {
                hasBasePose = false;
                return;
            }

            if (!hasBasePose)
            {
                CaptureBasePose();
            }
            else if (cameraComponent != null && currentViewBlend <= 0.001f && targetViewBlend <= 0.001f)
            {
                defaultFov = cameraComponent.fieldOfView;
            }
        }

        private void Awake()
        {
            ResolveCamera();
            phaseOffset = Random.Range(0f, 100f);
            if (!hasBasePose)
            {
                CaptureBasePose();
            }
        }

        private void OnEnable()
        {
            ResolveCamera();
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

            var mouseNormalized = new Vector2(
                (Input.mousePosition.x / Mathf.Max(1f, Screen.width) - 0.5f) * 2f,
                (Input.mousePosition.y / Mathf.Max(1f, Screen.height) - 0.5f) * 2f);
            var targetMouseParallax = new Vector3(
                mouseNormalized.x * mouseParallaxPosition,
                mouseNormalized.y * mouseParallaxPosition * 0.55f,
                mouseNormalized.x * mouseParallaxPosition * 0.25f);
            var targetMouseEuler = new Vector3(
                -mouseNormalized.y * mouseParallaxRotation,
                mouseNormalized.x * mouseParallaxRotation,
                -mouseNormalized.x * mouseParallaxRotation * 0.35f);
            mouseParallaxOffset = Vector3.SmoothDamp(
                mouseParallaxOffset,
                targetMouseParallax,
                ref mouseParallaxVelocity,
                mouseParallaxSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            mouseEulerOffset = Vector3.SmoothDamp(
                mouseEulerOffset,
                targetMouseEuler,
                ref mouseEulerVelocity,
                mouseParallaxSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            var inventoryOffset = baseRotation * new Vector3(
                inventoryRightOffset * currentViewBlend,
                0f,
                inventoryForwardOffset * currentViewBlend);

            cameraTransform.SetPositionAndRotation(
                basePosition + inventoryOffset + currentPositionOffset + mouseParallaxOffset,
                baseRotation * Quaternion.Euler(currentEulerOffset + mouseEulerOffset));

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
            if (cameraComponent != null && currentViewBlend <= 0.001f)
            {
                defaultFov = cameraComponent.fieldOfView;
            }

            currentPositionOffset = Vector3.zero;
            currentEulerOffset = Vector3.zero;
            positionOffsetVelocity = Vector3.zero;
            eulerOffsetVelocity = Vector3.zero;
            hasBasePose = true;
        }
    }
}
