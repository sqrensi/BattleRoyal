using ShooterPrototype.UI;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(CharacterController))]
    public sealed class FpsCharacterController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Camera playerCamera;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5.5f;
        [SerializeField] private float jumpHeight = 1.25f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float crouchSpeedMultiplier = 0.55f;
        [SerializeField] private float sprintSpeedMultiplier = 1.55f;
        [SerializeField] private float sprintMinForwardInput = 0.1f;
        [SerializeField] private float sideSpeedMultiplier = 0.85f;
        [SerializeField] private float backwardSpeedMultiplier = 0.75f;
        [SerializeField] private float holsteredSpeedMultiplier = 1.12f;
        [SerializeField] private float crouchControllerHeight = 1f;
        [SerializeField] private float crouchDownSmoothTime = 0.18f;
        [SerializeField] private float crouchUpSmoothTime = 0.22f;
        [SerializeField] private float crouchCameraOffset = 0.38f;

        [Header("Look")]
        [Tooltip("Base mouse sensitivity calibrated for Sensitivity Base FOV.")]
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private float sensitivityBaseFov = 75f;
        [SerializeField] private bool scaleSensitivityByFieldOfView = true;
        [Tooltip("Global multiplier applied to look sensitivity while aiming down sights.")]
        [SerializeField] private float globalAdsSensitivityMultiplier = 1f;
        [SerializeField] private float maxLookAngle = 80f;
        [SerializeField] private float hipMaxLookAngle = 50f;
        [SerializeField] private float adsMaxLookAngle = 40f;
        [SerializeField] private bool enableRecoilRecovery = true;
        [SerializeField] private float manualRecoilRecoveryScale = 1f;

        [Header("Shoot Shake")]
        [SerializeField] private bool enableShootShake = true;
        [SerializeField] private float shootShakePitch = 0.28f;
        [SerializeField] private float shootShakeYaw = 0.18f;
        [SerializeField] private float shootShakeRoll = 0.12f;
        [SerializeField] private float shootShakePosition = 0.006f;
        [SerializeField] private float shootShakeMaxAngle = 1.35f;
        [SerializeField] private float shootShakeMaxPosition = 0.02f;
        [SerializeField] private float shootShakeDecay = 24f;

        [Header("State")]
        [SerializeField] private bool lockCursorOnEnable = true;
        [SerializeField] private bool toggleCursorWithTab = false;
        [SerializeField] private bool pauseControlsWhenCursorUnlocked = true;

        public static bool SuppressTabCursorToggle { get; set; }

        [Header("Ground Check")]
        [SerializeField] private float groundedSphereRadius = 0.2f;
        [SerializeField] private float groundedSphereOffset = 0.03f;
        [SerializeField] private float groundedCheckDistance = 0.08f;
        [SerializeField] private LayerMask groundedMask = ~0;

        private CharacterController characterController;
        private float verticalVelocity;
        private float cameraPitch;
        private float horizontalSpeed;
        private float moveInputMagnitude;
        private float networkMoveInputX;
        private float networkMoveInputZ;
        private bool networkJumpPressed;
        private bool isGrounded;
        private int groundedEvalFrame = -1;
        private bool groundedEvalResult;
        private bool isCrouching;
        private bool isSprinting;
        private float recoilPitchOffset;
        private float recoilRecoverySpeed = 18f;
        private float recoilRecoveryBoostUntil;
        private float recoilRecoveryBoostMultiplier = 1f;
        private bool autoRecoilRecoveryActive = true;
        private Vector3 shootShakeEuler;
        private Vector3 shootShakePos;
        private Vector3 restingCameraLocalPos;
        private Quaternion restingCameraLocalRot = Quaternion.identity;
        private float standingHeight;
        private float standingCenterY;
        private float standingCameraLocalY;
        private float characterBottomOffset;
        private float crouchHeightVelocity;
        private float crouchCameraVelocity;
        private readonly Collider[] standCheckHits = new Collider[16];
        private float nextFootstepAt;
        private int footstepSequence;
        private PlayerAudioController audioController;
        private PlayerWeaponHolsterController weaponHolster;
        private PlayerWeaponMount weaponMount;
        private PlayerMedkitController medkitController;
        private PlayerHealth playerHealth;
        [Header("Audio")]
        [SerializeField] private float footstepIntervalSlow = 0.8f;
        [SerializeField] private float footstepIntervalFast = 0.42f;
        [SerializeField] private float footstepIntervalSprint = 0.28f;

        [Header("Network reconciliation")]
        [SerializeField] private bool enableServerReconciliation = true;
        [SerializeField] private float reconcileSnapDistance = 1.15f;
        [SerializeField] private float reconcileBlendSpeed = 16f;
        [SerializeField] private float reconcileMinError = 0.04f;

        private int lastReconciledServerTick = -1;
        private bool reconciliationSuspended;
        private bool gameOverMode;
        private float reconciliationGraceUntilRealtime;
        private bool movementLocked;
        private Vector2 externalHorizontalVelocity;
        private bool medkitUseMovementMode;
        private System.Action medkitMovementCancelHandler;
        private float defaultAdsMaxLookAngle;
        private float adsLookSensitivityMultiplier = 1f;
        private bool weaponPickUiMode;
        private bool weaponPickAllowsMovement;

        public bool IsWeaponPickUiMode => weaponPickUiMode;

        public bool IsGrounded => isGrounded;
        public bool IsMovementLocked => movementLocked;

        public bool IsMedkitUseMovementMode => medkitUseMovementMode;
        public bool IsCrouching => isCrouching;
        public bool IsSprinting => isSprinting;
        public float SprintSpeedMultiplier => Mathf.Clamp(sprintSpeedMultiplier, 1f, 3f);
        public float MaxHorizontalMoveSpeed => moveSpeed * (isSprinting ? SprintSpeedMultiplier : 1f);
        public float CrouchBlend01
        {
            get
            {
                if (cameraPivot == null || crouchCameraOffset <= 0.001f)
                {
                    return isCrouching ? 1f : 0f;
                }

                var down = standingCameraLocalY - cameraPivot.localPosition.y;
                return Mathf.Clamp01(down / Mathf.Max(0.001f, crouchCameraOffset));
            }
        }
        public float VerticalVelocity => verticalVelocity;
        public float HorizontalSpeed => horizontalSpeed;
        public float MoveInputMagnitude => moveInputMagnitude;
        public float NetworkMoveInputX => networkMoveInputX;
        public float NetworkMoveInputZ => networkMoveInputZ;
        public bool NetworkJumpPressed => networkJumpPressed;
        public float CurrentLookPitch => cameraPitch + recoilPitchOffset;
        public Transform CameraPivot => cameraPivot;
        public float HipMaxLookAngle => Mathf.Clamp(hipMaxLookAngle, 1f, 89f);
        public float AdsMaxLookAngle => Mathf.Clamp(adsMaxLookAngle, 1f, 89f);
        public int LastFootstepSequence => footstepSequence;

        public void SetServerReconciliationSuspended(bool suspended)
        {
            reconciliationSuspended = suspended;
        }

        public void NotifyLocalRespawned(float graceSeconds = 1f)
        {
            reconciliationSuspended = false;
            lastReconciledServerTick = -1;
            reconciliationGraceUntilRealtime = Time.realtimeSinceStartup + Mathf.Max(0.1f, graceSeconds);
        }

        public void SetMovementLocked(bool locked)
        {
            movementLocked = locked;
            if (!locked)
            {
                return;
            }

            horizontalSpeed = 0f;
            moveInputMagnitude = 0f;
            networkMoveInputX = 0f;
            networkMoveInputZ = 0f;
            networkJumpPressed = false;
            isSprinting = false;
            externalHorizontalVelocity = Vector2.zero;
        }

        public void SetWeaponPickUiMode(bool enabled, bool allowMovementWhileOpen = false)
        {
            weaponPickUiMode = enabled;
            weaponPickAllowsMovement = enabled && allowMovementWhileOpen;
            ApplyWeaponPickCursorState();
        }

        public void MaintainWeaponPickCursor()
        {
            if (!weaponPickUiMode)
            {
                return;
            }

            ApplyWeaponPickCursorState();
        }

        private void ApplyWeaponPickCursorState()
        {
            if (!weaponPickUiMode)
            {
                if (ShouldManageGameplayCursor() &&
                    !gameOverMode &&
                    !PlayerInventoryPanelController.IsOpen &&
                    !GameHudController.IsPauseMenuOpen)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }

                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static bool ShouldManageGameplayCursor()
        {
            return GameHudController.IsMatchHudActive;
        }

        public void ApplyExternalLaunchVelocity(Vector3 worldVelocity)
        {
            verticalVelocity = worldVelocity.y;
            externalHorizontalVelocity = new Vector2(worldVelocity.x, worldVelocity.z);
            isGrounded = false;
            groundedEvalFrame = -1;
        }

        public void ClearExternalLaunchVelocity()
        {
            externalHorizontalVelocity = Vector2.zero;
        }

        public void PrepareForLanding()
        {
            verticalVelocity = -2f;
            externalHorizontalVelocity = Vector2.zero;
            isGrounded = true;
            groundedEvalFrame = -1;
        }

        public void SetGameOverMode(bool enabled)
        {
            gameOverMode = enabled;
            if (!enabled)
            {
                return;
            }

            SetMovementLocked(true);
            externalHorizontalVelocity = Vector2.zero;
            networkMoveInputX = 0f;
            networkMoveInputZ = 0f;
            networkJumpPressed = false;
            isSprinting = false;
        }

        public bool IsGameOverMode => gameOverMode;

        public void ApplyLookOrientation(float yawDegrees, float pitchDegrees)
        {
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            recoilPitchOffset = 0f;
            var hipUpLimit = Mathf.Clamp(maxLookAngle, 1f, 89f);
            var hipDownLimit = Mathf.Clamp(hipMaxLookAngle, 1f, 89f);
            cameraPitch = Mathf.Clamp(pitchDegrees, -hipUpLimit, hipDownLimit);
            if (cameraPivot != null)
            {
                cameraPivot.localRotation = Quaternion.Euler(cameraPitch + recoilPitchOffset, 0f, 0f);
            }
        }

        /// <summary>
        /// During medkit use: crouch allowed; WASD/jump cancel medkit — jump and move apply same frame after cancel.
        /// </summary>
        public void SetMedkitUseMovementMode(bool enabled, System.Action onMovementCancel = null)
        {
            medkitUseMovementMode = enabled;
            medkitMovementCancelHandler = enabled ? onMovementCancel : null;
            if (!enabled)
            {
                return;
            }

            isSprinting = false;
            networkJumpPressed = false;
        }

        public void ReconcileToServer(
            Vector3 authoritativePosition,
            float authoritativeYaw,
            int serverTick,
            int roundTripMs = 0)
        {
            var localPos = transform.position;
            if (!Application.isFocused)
            {
                MovementNetworkDiagnostics.LogReconcile(
                    localPos, authoritativePosition, 0f, serverTick, string.Empty, "app_unfocused");
                return;
            }
            if (!enableServerReconciliation)
            {
                MovementNetworkDiagnostics.LogReconcile(
                    localPos, authoritativePosition, 0f, serverTick, string.Empty, "reconcile_disabled");
                return;
            }

            if (reconciliationSuspended)
            {
                MovementNetworkDiagnostics.LogReconcile(
                    localPos, authoritativePosition, 0f, serverTick, string.Empty, "reconcile_suspended");
                return;
            }

            if (Time.realtimeSinceStartup < reconciliationGraceUntilRealtime)
            {
                MovementNetworkDiagnostics.LogReconcile(
                    localPos, authoritativePosition, 0f, serverTick, string.Empty, "reconcile_grace");
                return;
            }

            if (characterController == null)
            {
                MovementNetworkDiagnostics.LogReconcile(
                    localPos, authoritativePosition, 0f, serverTick, string.Empty, "no_character_controller");
                return;
            }

            if (!characterController.enabled)
            {
                MovementNetworkDiagnostics.LogReconcile(
                    localPos, authoritativePosition, 0f, serverTick, string.Empty, "character_controller_disabled");
                return;
            }

            if (serverTick <= lastReconciledServerTick)
            {
                MovementNetworkDiagnostics.LogReconcile(
                    localPos, authoritativePosition, 0f, serverTick, string.Empty, "duplicate_tick");
                return;
            }

            lastReconciledServerTick = serverTick;

            var delta = authoritativePosition - transform.position;
            var horizontalError = new Vector2(delta.x, delta.z).magnitude;
            var moveInputMag = Mathf.Sqrt(
                networkMoveInputX * networkMoveInputX +
                networkMoveInputZ * networkMoveInputZ);
            var latencySlack = Mathf.Max(
                reconcileMinError * 2f,
                Mathf.Max(30, roundTripMs) * 0.0045f);
            if (moveInputMag > 0.08f && horizontalError <= latencySlack)
            {
                MovementNetworkDiagnostics.LogReconcile(
                    localPos,
                    authoritativePosition,
                    horizontalError,
                    serverTick,
                    string.Empty,
                    "latency_slack");
                return;
            }

            if (horizontalError >= reconcileSnapDistance)
            {
                MovementNetworkDiagnostics.LogReconcile(
                    localPos, authoritativePosition, horizontalError, serverTick, "snap", string.Empty);
                if (CanUseCharacterController())
                {
                    characterController.enabled = false;
                    transform.position = new Vector3(
                        authoritativePosition.x,
                        authoritativePosition.y,
                        authoritativePosition.z);
                    characterController.enabled = true;
                }
                else
                {
                    transform.position = new Vector3(
                        authoritativePosition.x,
                        authoritativePosition.y,
                        authoritativePosition.z);
                }

                return;
            }

            if (horizontalError > reconcileMinError && CanUseCharacterController())
            {
                MovementNetworkDiagnostics.LogReconcile(
                    localPos, authoritativePosition, horizontalError, serverTick, "blend", string.Empty);
                var move = new Vector3(delta.x, 0f, delta.z);
                characterController.Move(move * Mathf.Clamp01(Time.deltaTime * reconcileBlendSpeed));
            }

            var yawDelta = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, authoritativeYaw));
            if (yawDelta > 2f)
            {
                var nextYaw = Mathf.LerpAngle(transform.eulerAngles.y, authoritativeYaw, Time.deltaTime * reconcileBlendSpeed);
                transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
            }
        }

        public void Configure(Transform pivot, Camera localCamera, bool shouldLockCursor = true)
        {
            cameraPivot = pivot;
            playerCamera = localCamera;
            lockCursorOnEnable = shouldLockCursor;
            CacheRestingCameraLocalTransform();
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            if (cameraPivot == null)
            {
                cameraPivot = transform;
            }

            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
            }
            audioController = GetComponent<PlayerAudioController>();
            weaponHolster = GetComponent<PlayerWeaponHolsterController>();
            weaponMount = GetComponent<PlayerWeaponMount>();
            medkitController = GetComponent<PlayerMedkitController>();
            playerHealth = GetComponent<PlayerHealth>();

            standingHeight = characterController != null ? characterController.height : 1.8f;
            standingCenterY = characterController != null ? characterController.center.y : standingHeight * 0.5f;
            standingCameraLocalY = cameraPivot != null ? cameraPivot.localPosition.y : 1.6f;
            characterBottomOffset = standingCenterY - (standingHeight * 0.5f);
            isGrounded = EvaluateGroundedCached();
            defaultAdsMaxLookAngle = adsMaxLookAngle;
            if (sensitivityBaseFov <= 0.01f && playerCamera != null)
            {
                sensitivityBaseFov = playerCamera.fieldOfView;
            }

            CacheRestingCameraLocalTransform();
            ApplySettingsFromService();
        }

        private void ApplySettingsFromService()
        {
            ClientSettingsService.EnsureLoaded();
            mouseSensitivity = ClientSettingsService.MouseSensitivity;

            var profile = weaponMount != null ? weaponMount.ActiveWeaponProfile : null;
            if (profile != null)
            {
                ConfigureAdsLookSensitivityMultiplier(ClientSettingsService.GetAdsSensitivity(profile.Kind));
            }
        }

        public void ConfigureAdsMaxLookAngle(float value)
        {
            adsMaxLookAngle = Mathf.Clamp(value, 1f, 89f);
        }

        public void ConfigureAdsLookSensitivityMultiplier(float value)
        {
            adsLookSensitivityMultiplier = Mathf.Max(0.01f, value);
        }

        public void RestoreDefaultAdsMaxLookAngle()
        {
            adsMaxLookAngle = defaultAdsMaxLookAngle;
        }

        public void RestoreDefaultAdsLookSensitivityMultiplier()
        {
            adsLookSensitivityMultiplier = 1f;
        }

        private void OnEnable()
        {
            ClientSettingsService.SettingsChanged += ApplySettingsFromService;
            ApplySettingsFromService();

            if (!ShouldManageGameplayCursor())
            {
                return;
            }

            if (weaponPickUiMode)
            {
                ApplyWeaponPickCursorState();
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            ClientSettingsService.SettingsChanged -= ApplySettingsFromService;

            if (!lockCursorOnEnable)
            {
                return;
            }

            if (!GameHudController.IsMatchHudActive)
            {
                MenuCursorUtility.UnlockForMenu();
                return;
            }

            var health = playerHealth;
            if (health != null && health.IsDead)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            MaintainWeaponPickCursor();

            if (!gameOverMode)
            {
                HandleCursorToggle();
            }

            RecordNetworkMoveInput();
            if (!ShouldPauseControls())
            {
                TickLook();
            }

            if (medkitUseMovementMode)
            {
                TickMedkitRestrictedMove();
            }
            else if (!ShouldBlockMovement())
            {
                ApplyLocalMovement();
            }
            else
            {
                horizontalSpeed = 0f;
                moveInputMagnitude = Mathf.Clamp01(
                    new Vector2(networkMoveInputX, networkMoveInputZ).magnitude);
                if (movementLocked)
                {
                    networkMoveInputX = 0f;
                    networkMoveInputZ = 0f;
                    networkJumpPressed = false;
                    isSprinting = false;
                }
            }

            if (MovementNetworkDiagnostics.Enabled)
            {
                LogMovementDiagnostics();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!lockCursorOnEnable || !ShouldManageGameplayCursor())
            {
                return;
            }

            if (hasFocus)
            {
                reconciliationGraceUntilRealtime = Time.realtimeSinceStartup + 0.75f;
                var health = playerHealth;
                if (health != null && health.IsDead)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    return;
                }

                if (gameOverMode)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    return;
                }

                if (weaponPickUiMode)
                {
                    ApplyWeaponPickCursorState();
                    return;
                }

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }
        }

        private void RecordNetworkMoveInput()
        {
            if (movementLocked || medkitUseMovementMode)
            {
                networkMoveInputX = 0f;
                networkMoveInputZ = 0f;
                networkJumpPressed = false;
                return;
            }

            var moveInput = ReadMoveInput();
            networkMoveInputX = moveInput.x;
            networkMoveInputZ = moveInput.y;
        }

        private void LogMovementDiagnostics()
        {
            if (characterController == null)
            {
                return;
            }

            var blocked = ShouldBlockMovement() || medkitUseMovementMode;
            string blockReason = "none";
            if (medkitUseMovementMode)
            {
                blockReason = "medkit_mode";
            }
            else if (movementLocked)
            {
                blockReason = "movement_locked";
            }
            else if (ShouldPauseControls() && !PlayerInventoryPanelController.IsOpen)
            {
                blockReason = "cursor_unlocked";
            }

            MovementNetworkDiagnostics.LogLocalMovement(
                transform.position,
                characterController.velocity,
                networkMoveInputX,
                networkMoveInputZ,
                blocked,
                blockReason,
                enabled,
                characterController.enabled,
                movementLocked,
                Cursor.lockState != CursorLockMode.Locked);
        }

        private void LateUpdate()
        {
            TickCameraShake();
        }

        private void HandleCursorToggle()
        {
            if (SuppressTabCursorToggle ||
                weaponPickUiMode ||
                GameHudController.IsPauseMenuOpen ||
                PlayerInventoryPanelController.IsOpen ||
                !toggleCursorWithTab ||
                !ReadToggleCursorPressed())
            {
                return;
            }

            var locked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;
        }

        private bool ShouldPauseControls()
        {
            if (GameHudController.IsPauseMenuOpen)
            {
                return true;
            }

            if (gameOverMode)
            {
                return true;
            }

            if (PlayerInventoryPanelController.IsOpen)
            {
                return true;
            }

            if (weaponPickUiMode)
            {
                return true;
            }

            if (!pauseControlsWhenCursorUnlocked)
            {
                return false;
            }

            return Cursor.lockState != CursorLockMode.Locked;
        }

        private bool ShouldBlockMovement()
        {
            if (gameOverMode)
            {
                return true;
            }

            if (movementLocked)
            {
                return true;
            }

            if (PlayerInventoryPanelController.IsOpen)
            {
                return false;
            }

            if (weaponPickUiMode && weaponPickAllowsMovement)
            {
                return false;
            }

            return ShouldPauseControls();
        }

        private void TickLook()
        {
            var lookDelta = ReadLookInput();
            var sensitivity = mouseSensitivity *
                              ResolveFieldOfViewSensitivityScale() *
                              ResolveAdsLookSensitivityMultiplier();
            var mouseX = lookDelta.x * sensitivity;
            var mouseY = lookDelta.y * sensitivity;

            transform.Rotate(Vector3.up * mouseX, Space.Self);

            cameraPitch -= mouseY;
            ApplyManualRecoilRecovery(mouseY);
            if (ReadAimPressed() && !IsAimBlockedDuringMedkit())
            {
                var adsCameraLimit = Mathf.Clamp(adsMaxLookAngle, 1f, 89f);
                cameraPitch = Mathf.Clamp(cameraPitch, -adsCameraLimit, adsCameraLimit);
            }
            else
            {
                var hipUpLimit = Mathf.Clamp(maxLookAngle, 1f, 89f);
                var hipDownLimit = Mathf.Clamp(hipMaxLookAngle, 1f, 89f);
                cameraPitch = Mathf.Clamp(cameraPitch, -hipUpLimit, hipDownLimit);
            }
            if (enableRecoilRecovery && autoRecoilRecoveryActive)
            {
                var recoverySpeed = recoilRecoverySpeed;
                if (Time.time < recoilRecoveryBoostUntil)
                {
                    recoverySpeed *= Mathf.Max(1f, recoilRecoveryBoostMultiplier);
                }
                else
                {
                    recoilRecoveryBoostMultiplier = 1f;
                }

                recoilPitchOffset = Mathf.MoveTowards(recoilPitchOffset, 0f, recoverySpeed * Time.deltaTime);
            }
            cameraPivot.localRotation = Quaternion.Euler(cameraPitch + recoilPitchOffset, 0f, 0f);
        }

        private void ApplyManualRecoilRecovery(float mouseY)
        {
            // Pulling mouse down should manually compensate recoil even when auto recovery is disabled.
            if (mouseY >= -0.0001f)
            {
                return;
            }

            var manualRecoveryAmount = -mouseY * Mathf.Max(0f, manualRecoilRecoveryScale);
            recoilPitchOffset = Mathf.MoveTowards(recoilPitchOffset, 0f, manualRecoveryAmount);
        }

        private float ResolveFieldOfViewSensitivityScale()
        {
            if (!scaleSensitivityByFieldOfView || playerCamera == null)
            {
                return 1f;
            }

            var baseFov = ResolveSensitivityBaseFov();
            if (baseFov <= 0.01f)
            {
                return 1f;
            }

            return playerCamera.fieldOfView / baseFov;
        }

        private float ResolveSensitivityBaseFov()
        {
            if (weaponMount != null && weaponMount.BaseCameraFov > 0.01f)
            {
                return weaponMount.BaseCameraFov;
            }

            if (sensitivityBaseFov > 0.01f)
            {
                return sensitivityBaseFov;
            }

            return playerCamera != null ? playerCamera.fieldOfView : 75f;
        }

        private float ResolveAdsLookSensitivityMultiplier()
        {
            var weaponMultiplier = Mathf.Max(0.01f, adsLookSensitivityMultiplier);
            var globalMultiplier = Mathf.Max(0.01f, globalAdsSensitivityMultiplier);
            var targetAdsMultiplier = weaponMultiplier * globalMultiplier;
            if (Mathf.Approximately(targetAdsMultiplier, 1f))
            {
                return 1f;
            }

            var adsBlend = weaponMount != null
                ? weaponMount.AdsBlend
                : ReadAimPressed() && !IsAimBlockedDuringMedkit()
                    ? 1f
                    : 0f;
            return Mathf.Lerp(1f, targetAdsMultiplier, Mathf.Clamp01(adsBlend));
        }

        public void ApplyRecoil(float pitchUpDegrees, float yawDegrees)
        {
            recoilPitchOffset -= Mathf.Abs(pitchUpDegrees);
            if (Mathf.Abs(yawDegrees) > 0.0001f)
            {
                transform.Rotate(Vector3.up * yawDegrees, Space.Self);
            }
        }

        public void BoostRecoilRecovery(float durationSeconds, float multiplier, float dampFactor = 1f)
        {
            if (!enableRecoilRecovery || !autoRecoilRecoveryActive)
            {
                return;
            }

            recoilRecoveryBoostUntil = Time.time + Mathf.Max(0f, durationSeconds);
            recoilRecoveryBoostMultiplier = Mathf.Max(1f, multiplier);
            if (dampFactor < 0.999f)
            {
                recoilPitchOffset *= Mathf.Clamp01(dampFactor);
            }
        }

        public void SetAutoRecoilRecoveryActive(bool isActive)
        {
            autoRecoilRecoveryActive = isActive;
            if (!autoRecoilRecoveryActive)
            {
                recoilRecoveryBoostUntil = 0f;
                recoilRecoveryBoostMultiplier = 1f;
            }
        }

        public void ApplyShootShake(float intensity = 1f)
        {
            if (!enableShootShake || playerCamera == null)
            {
                return;
            }

            intensity = Mathf.Max(0f, intensity);
            shootShakeEuler.x += Random.Range(-shootShakePitch, shootShakePitch) * intensity;
            shootShakeEuler.y += Random.Range(-shootShakeYaw, shootShakeYaw) * intensity;
            shootShakeEuler.z += Random.Range(-shootShakeRoll, shootShakeRoll) * intensity;

            if (shootShakePosition > 0.0001f)
            {
                shootShakePos += Random.insideUnitSphere * (shootShakePosition * intensity);
            }

            shootShakeEuler = Vector3.ClampMagnitude(shootShakeEuler, shootShakeMaxAngle);
            shootShakePos = Vector3.ClampMagnitude(shootShakePos, shootShakeMaxPosition);
        }

        public bool TryGetViewShakePivotLocal(out Vector3 localPositionOffset, out Vector3 localEulerOffset)
        {
            localPositionOffset = shootShakePos;
            localEulerOffset = shootShakeEuler;
            return enableShootShake &&
                   (shootShakeEuler.sqrMagnitude > 0.000001f || shootShakePos.sqrMagnitude > 0.00000001f);
        }

        public void ApplyViewShakeToWorldTransform(Transform target)
        {
            if (target == null || cameraPivot == null || !TryGetViewShakePivotLocal(out var shakePos, out var shakeEuler))
            {
                return;
            }

            target.position += cameraPivot.rotation * shakePos;
            var pivotSpaceRotation = Quaternion.Euler(shakeEuler);
            target.rotation = cameraPivot.rotation * pivotSpaceRotation * Quaternion.Inverse(cameraPivot.rotation) * target.rotation;
        }

        private void CacheRestingCameraLocalTransform()
        {
            if (playerCamera == null)
            {
                return;
            }

            restingCameraLocalPos = playerCamera.transform.localPosition;
            restingCameraLocalRot = playerCamera.transform.localRotation;
        }

        private void TickCameraShake()
        {
            if (playerCamera == null)
            {
                return;
            }

            if (!enableShootShake)
            {
                playerCamera.transform.localPosition = restingCameraLocalPos;
                playerCamera.transform.localRotation = restingCameraLocalRot;
                shootShakeEuler = Vector3.zero;
                shootShakePos = Vector3.zero;
                return;
            }

            var decayFactor = 1f - Mathf.Exp(-Mathf.Max(0.01f, shootShakeDecay) * Time.deltaTime);
            shootShakeEuler = Vector3.Lerp(shootShakeEuler, Vector3.zero, decayFactor);
            shootShakePos = Vector3.Lerp(shootShakePos, Vector3.zero, decayFactor);

            playerCamera.transform.localRotation = restingCameraLocalRot * Quaternion.Euler(shootShakeEuler);
            playerCamera.transform.localPosition = restingCameraLocalPos + shootShakePos;
        }

        private void TickMedkitRestrictedMove()
        {
            UpdateCrouchState();

            var moveInput = ReadMoveInput();
            if (moveInput.sqrMagnitude > 0.04f)
            {
                medkitMovementCancelHandler?.Invoke();
            }

            if (ReadJumpPressed())
            {
                medkitMovementCancelHandler?.Invoke();
            }

            networkMoveInputX = 0f;
            networkMoveInputZ = 0f;
            networkJumpPressed = false;
            moveInputMagnitude = 0f;
            isSprinting = false;

            isGrounded = EvaluateGroundedCached();
            if (isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -1.5f;
            }

            verticalVelocity += gravity * Time.deltaTime;
            var velocity = Vector3.up * verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);
            isGrounded = EvaluateGroundedCached(forceRefresh: true);

            var ccVelocity = characterController.velocity;
            horizontalSpeed = new Vector2(ccVelocity.x, ccVelocity.z).magnitude;
        }

        private bool CanUseCharacterController()
        {
            return characterController != null &&
                   characterController.enabled &&
                   characterController.gameObject.activeInHierarchy;
        }

        private void ApplyLocalMovement()
        {
            if (!CanUseCharacterController())
            {
                horizontalSpeed = 0f;
                return;
            }

            UpdateCrouchState();

            var inputX = networkMoveInputX;
            var inputZ = networkMoveInputZ;
            networkJumpPressed = false;
            moveInputMagnitude = Mathf.Clamp01(Mathf.Sqrt(inputX * inputX + inputZ * inputZ));
            isSprinting = !isCrouching &&
                          ReadSprintPressed() &&
                          moveInputMagnitude > 0.12f &&
                          CanSprintOnMoveInput(inputZ);

            var moveDirection = BuildScaledMoveDirection(inputX, inputZ);

            isGrounded = EvaluateGroundedCached();
            if (isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -1.5f;
                externalHorizontalVelocity = Vector2.zero;
            }

            if (isGrounded && ReadJumpPressed())
            {
                networkJumpPressed = true;
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                audioController?.PlayJump(true);
            }

            verticalVelocity += gravity * Time.deltaTime;

            var speedMultiplier = 1f;
            if (isCrouching)
            {
                speedMultiplier = Mathf.Clamp(crouchSpeedMultiplier, 0.1f, 1f);
            }
            else if (isSprinting)
            {
                speedMultiplier = SprintSpeedMultiplier;
            }

            if (weaponHolster != null && weaponHolster.IsHolstered)
            {
                speedMultiplier *= Mathf.Clamp(holsteredSpeedMultiplier, 1f, 1.5f);
            }

            var velocity = moveDirection * (moveSpeed * speedMultiplier);
            if (externalHorizontalVelocity.sqrMagnitude > 0.04f)
            {
                velocity.x += externalHorizontalVelocity.x;
                velocity.z += externalHorizontalVelocity.y;
                externalHorizontalVelocity *= Mathf.Clamp01(1f - (Time.deltaTime * 0.75f));
            }

            velocity.y = verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);
            isGrounded = EvaluateGroundedCached(forceRefresh: true);

            var ccVelocity = characterController.velocity;
            horizontalSpeed = new Vector2(ccVelocity.x, ccVelocity.z).magnitude;
            TryEmitFootstep();
        }

        private bool EvaluateGroundedCached(bool forceRefresh = false)
        {
            if (!forceRefresh && groundedEvalFrame == Time.frameCount)
            {
                return groundedEvalResult;
            }

            groundedEvalFrame = Time.frameCount;
            groundedEvalResult = EvaluateGrounded();
            return groundedEvalResult;
        }

        private Vector3 BuildScaledMoveDirection(float inputX, float inputZ)
        {
            var mag = Mathf.Sqrt(inputX * inputX + inputZ * inputZ);
            if (mag > 1f)
            {
                inputX /= mag;
                inputZ /= mag;
            }

            inputX *= Mathf.Clamp(sideSpeedMultiplier, 0.1f, 1f);
            if (inputZ < 0f)
            {
                inputZ *= Mathf.Clamp(backwardSpeedMultiplier, 0.1f, 1f);
            }

            var moveDirection = transform.right * inputX + transform.forward * inputZ;
            if (moveDirection.sqrMagnitude > 1f)
            {
                moveDirection.Normalize();
            }

            return moveDirection;
        }

        public void PlayRemoteFootstep()
        {
            audioController?.PlayFootstep(false);
        }

        public void PlayRemoteJump()
        {
            audioController?.PlayJump(false);
        }

        private void TryEmitFootstep()
        {
            // Crouch movement is intentionally silent.
            if (isCrouching)
            {
                return;
            }

            if (!isGrounded || moveInputMagnitude < 0.12f || Time.time < nextFootstepAt)
            {
                return;
            }

            var cadenceFast = isSprinting
                ? Mathf.Max(0.06f, footstepIntervalSprint)
                : Mathf.Max(0.08f, footstepIntervalFast);
            var cadence = Mathf.Lerp(
                Mathf.Max(0.1f, footstepIntervalSlow),
                cadenceFast,
                Mathf.Clamp01(horizontalSpeed / Mathf.Max(0.01f, MaxHorizontalMoveSpeed)));

            nextFootstepAt = Time.time + cadence;
            footstepSequence++;
            audioController?.PlayFootstep(true, isSprinting);
        }

        private void UpdateCrouchState()
        {
            if (characterController == null || cameraPivot == null)
            {
                return;
            }

            // Hold-to-crouch: input state directly drives crouch, no toggle.
            var crouchPressed = ReadCrouchPressed();
            if (!crouchPressed && isCrouching && !CanStandUp())
            {
                crouchPressed = true;
            }

            isCrouching = crouchPressed;

            var targetHeight = isCrouching
                ? Mathf.Clamp(crouchControllerHeight, 0.8f, standingHeight)
                : standingHeight;
            var smoothTime = isCrouching
                ? Mathf.Max(0.01f, crouchDownSmoothTime)
                : Mathf.Max(0.01f, crouchUpSmoothTime);
            var nextHeight = Mathf.SmoothDamp(
                characterController.height,
                targetHeight,
                ref crouchHeightVelocity,
                smoothTime);
            characterController.height = nextHeight;
            var center = characterController.center;
            center.y = characterBottomOffset + nextHeight * 0.5f;
            characterController.center = center;

            var targetCameraY = isCrouching
                ? standingCameraLocalY - Mathf.Max(0.01f, crouchCameraOffset)
                : standingCameraLocalY;
            var cameraLocalPos = cameraPivot.localPosition;
            cameraLocalPos.y = Mathf.SmoothDamp(
                cameraLocalPos.y,
                targetCameraY,
                ref crouchCameraVelocity,
                smoothTime);
            cameraPivot.localPosition = cameraLocalPos;
        }

        private bool CanStandUp()
        {
            if (characterController == null)
            {
                return true;
            }

            var radius = Mathf.Max(0.05f, characterController.radius * 0.95f);
            var bottomY = transform.position.y + characterBottomOffset + radius;
            var desiredTopY = transform.position.y + characterBottomOffset + standingHeight - radius;
            var bottom = new Vector3(transform.position.x, bottomY, transform.position.z);
            var top = new Vector3(transform.position.x, desiredTopY, transform.position.z);
            var hitCount = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                standCheckHits,
                groundedMask,
                QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hitCount; i++)
            {
                var hit = standCheckHits[i];
                if (hit == null)
                {
                    continue;
                }

                if (hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private bool EvaluateGrounded()
        {
            if (characterController == null)
            {
                return false;
            }

            var worldCenter = transform.TransformPoint(characterController.center);
            var feetY = worldCenter.y - (characterController.height * 0.5f) + characterController.radius;
            var radius = Mathf.Max(0.01f, groundedSphereRadius);
            var castDistance = Mathf.Max(0.01f, groundedCheckDistance);
            var castOrigin = new Vector3(
                worldCenter.x,
                feetY + radius + groundedSphereOffset,
                worldCenter.z);

            var hits = Physics.SphereCastAll(
                castOrigin,
                radius,
                Vector3.down,
                castDistance,
                groundedMask,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hits.Length; i++)
            {
                var hitCollider = hits[i].collider;
                if (hitCollider == null)
                {
                    continue;
                }

                if (hitCollider.transform.IsChildOf(transform))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static Vector2 ReadMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
            {
                return Vector2.zero;
            }

            var x = 0f;
            var y = 0f;
            if (Keyboard.current.aKey.isPressed) x -= 1f;
            if (Keyboard.current.dKey.isPressed) x += 1f;
            if (Keyboard.current.sKey.isPressed) y -= 1f;
            if (Keyboard.current.wKey.isPressed) y += 1f;
            return new Vector2(x, y);
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        private static Vector2 ReadLookInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
            {
                return Vector2.zero;
            }

            return Mouse.current.delta.ReadValue() * 0.02f;
#else
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
        }

        private static bool ReadJumpPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
            return Input.GetButtonDown("Jump");
#endif
        }

        private bool IsAimBlockedDuringMedkit()
        {
            if (PlayerInventoryPanelController.IsOpen || GameHudController.IsPauseMenuOpen)
            {
                return true;
            }

            if (medkitController != null && medkitController.IsUsingMedkit)
            {
                return true;
            }

            return weaponHolster != null && weaponHolster.IsMedkitWeaponLocked;
        }

        private static bool ReadAimPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton(1);
#endif
        }

        private static bool ReadCrouchPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.cKey.isPressed;
#else
            return Input.GetKey(KeyCode.C);
#endif
        }

        private bool CanSprintOnMoveInput(float forwardInput)
        {
            return forwardInput > sprintMinForwardInput;
        }

        private static bool ReadSprintPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftShift);
#endif
        }

        private static bool ReadToggleCursorPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Tab);
#endif
        }
    }
}
