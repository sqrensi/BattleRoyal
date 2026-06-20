using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.UI
{
    /// <summary>
    /// Drives the main-menu camera sway and inventory zoom. Must live on Main Camera.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    [RequireComponent(typeof(Camera))]
    public sealed class MainMenuCameraMotion : MonoBehaviour
    {
        private static MainMenuCameraMotion instance;

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

        [Header("Mouse Parallax")]
        [SerializeField] private float mouseParallaxPosition = 0.12f;
        [SerializeField] private float mouseParallaxRotation = 0.45f;
        [SerializeField] private float mouseParallaxSmoothTime = 0.35f;

        private Camera cameraComponent;
        private Vector3 basePosition;
        private Quaternion baseRotation;
        private Vector3 currentPositionOffset;
        private Vector3 positionOffsetVelocity;
        private Vector3 currentEulerOffset;
        private Vector3 eulerOffsetVelocity;
        private float phaseOffset;
        private bool hasRestPose;
        private float defaultFov = 48f;
        private float targetViewBlend;
        private float currentViewBlend;
        private float viewBlendVelocity;
        private Vector3 mouseParallaxOffset;
        private Vector3 mouseParallaxVelocity;
        private Vector3 mouseEulerOffset;
        private Vector3 mouseEulerVelocity;

        public static bool IsInventoryViewActive =>
            instance != null &&
            (instance.currentViewBlend > 0.001f || instance.targetViewBlend > 0.001f);

        public static MainMenuCameraMotion Resolve()
        {
            if (instance != null)
            {
                return instance;
            }

            if (!IsMainMenuScene())
            {
                return null;
            }

            var camera = ResolveMainCamera();
            if (camera == null)
            {
                return null;
            }

            instance = camera.GetComponent<MainMenuCameraMotion>();
            if (instance != null)
            {
                return instance;
            }

            RemoveMisplacedCopies();
            instance = camera.gameObject.AddComponent<MainMenuCameraMotion>();
            return instance;
        }

        public void EnterInventoryView()
        {
            if (!IsMainMenuScene())
            {
                return;
            }

            EnsureBound();
            targetViewBlend = 1f;
        }

        public void ExitInventoryView()
        {
            targetViewBlend = 0f;
        }

        private void Awake()
        {
            EnsureBound();
            phaseOffset = Random.Range(0f, 100f);
            CaptureRestPose();
            currentViewBlend = 0f;
            targetViewBlend = 0f;
            viewBlendVelocity = 0f;
        }

        private void OnEnable()
        {
            instance = this;
            EnsureBound();
        }

        private void OnDisable()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void LateUpdate()
        {
            if (!IsMainMenuScene() || cameraComponent == null)
            {
                return;
            }

            if (!hasRestPose)
            {
                CaptureRestPose();
            }

            if (!hasRestPose)
            {
                return;
            }

            currentViewBlend = Mathf.SmoothDamp(
                currentViewBlend,
                targetViewBlend,
                ref viewBlendVelocity,
                inventoryTransitionSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            var inventoryOpen = currentViewBlend > 0.001f || targetViewBlend > 0.001f;
            var swayScale = inventoryOpen ? 0.35f : 1f;
            var parallaxScale = inventoryOpen ? 0.2f : 1f;

            var time = Time.unscaledTime + phaseOffset;
            var targetPositionOffset = new Vector3(
                Mathf.Sin(time * horizontalSpeed) * horizontalAmplitude * swayScale +
                Mathf.Sin(time * horizontalSpeed * 0.43f + 1.8f) * (horizontalAmplitude * 0.35f * swayScale),
                Mathf.Sin(time * verticalSpeed + 1.4f) * verticalAmplitude * swayScale,
                Mathf.Cos(time * depthSpeed + 0.6f) * depthAmplitude * swayScale);
            var targetEulerOffset = new Vector3(
                Mathf.Sin(time * pitchSpeed + 0.9f) * pitchAmplitude * swayScale,
                Mathf.Sin(time * yawSpeed + 2.2f) * yawAmplitude * swayScale,
                Mathf.Sin(time * rollSpeed + 1.1f) * rollAmplitude * swayScale);

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
                mouseNormalized.x * mouseParallaxPosition * parallaxScale,
                mouseNormalized.y * mouseParallaxPosition * 0.55f * parallaxScale,
                mouseNormalized.x * mouseParallaxPosition * 0.25f * parallaxScale);
            var targetMouseEuler = new Vector3(
                -mouseNormalized.y * mouseParallaxRotation * parallaxScale,
                mouseNormalized.x * mouseParallaxRotation * parallaxScale,
                -mouseNormalized.x * mouseParallaxRotation * 0.35f * parallaxScale);
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

            transform.SetPositionAndRotation(
                basePosition + inventoryOffset + currentPositionOffset + mouseParallaxOffset,
                baseRotation * Quaternion.Euler(currentEulerOffset + mouseEulerOffset));

            cameraComponent.fieldOfView = Mathf.Lerp(defaultFov, inventoryFov, currentViewBlend);
        }

        private void EnsureBound()
        {
            if (cameraComponent == null)
            {
                cameraComponent = GetComponent<Camera>();
            }

            instance = this;
        }

        private void CaptureRestPose()
        {
            if (cameraComponent == null)
            {
                hasRestPose = false;
                return;
            }

            basePosition = transform.position;
            baseRotation = transform.rotation;
            defaultFov = cameraComponent.fieldOfView;
            hasRestPose = true;
        }

        private static void RemoveMisplacedCopies()
        {
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            if (controller == null)
            {
                return;
            }

            var misplaced = controller.GetComponent<MainMenuCameraMotion>();
            if (misplaced != null)
            {
                Object.Destroy(misplaced);
            }
        }

        private static Camera ResolveMainCamera()
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                return mainCamera;
            }

            var tagged = GameObject.FindGameObjectWithTag("MainCamera");
            return tagged != null ? tagged.GetComponent<Camera>() : null;
        }

        private static bool IsMainMenuScene()
        {
            var scene = SceneManager.GetActiveScene();
            return scene.IsValid() &&
                   string.Equals(scene.name, "MainMenu", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
