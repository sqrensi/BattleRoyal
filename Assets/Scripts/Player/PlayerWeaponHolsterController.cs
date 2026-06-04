using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Local FP holster/draw on X: lower weapon then hide it. Back attach is remote-only via network.
    /// </summary>
    [DefaultExecutionOrder(550)]
    public sealed class PlayerWeaponHolsterController : MonoBehaviour
    {
        private enum HolsterPhase
        {
            Armed,
            Holstering,
            Holstered,
            Drawing
        }

        [Header("Lower / Raise On Anchor")]
        [SerializeField] private Vector3 loweredLocalPosition = new Vector3(0.14f, -0.4f, -0.05f);
        [SerializeField] private Vector3 loweredLocalEulerAngles = new Vector3(58f, -18f, 14f);
        [SerializeField] private Vector3 holsterSwingOffset = new Vector3(0.05f, 0.04f, -0.16f);
        [SerializeField] private float holsterLowerDuration = 0.38f;
        [SerializeField] private float drawRaiseDuration = 0.32f;
        [SerializeField] private float holsteredScaleFactor = 0.94f;
        [SerializeField] private float armsHideHolsterThreshold = 0.68f;
        [SerializeField] private float armsShowDrawThreshold = 0.22f;

        [Header("Field Of View")]
        [SerializeField] private bool scaleLoweredPoseByFieldOfView = true;
        [SerializeField] private float referenceFieldOfView = 75f;
        [SerializeField] private float extraLowerPerFovRatio = 0.14f;

        [Header("Transition Curves")]
        [SerializeField] private AnimationCurve holsterPositionEase =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve holsterRotationEase =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve drawPositionEase =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve drawRotationEase =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private HolsterPhase phase = HolsterPhase.Armed;
        private float transitionElapsed;
        private Vector3 armedLocalPosition;
        private Quaternion armedLocalRotation;
        private Vector3 armedLocalScale = Vector3.one;
        private Camera localCamera;
        private PlayerWeaponMount weaponMount;
        private PlayerWeaponController weaponController;
        private PlayerHealth playerHealth;
        private PlayerMedkitController medkitController;
        private SyntyWeaponHandBinder handBinder;
        private SyntyFirstPersonArmsPresenter armsPresenter;
        private SyntySplitBodyPresentation splitBodyPresentation;
        private MatchPresenceSync presenceSync;
        private PlayerNetworkIdentity networkIdentity;
        private bool medkitUsePresentationActive;
        private bool wasHolsteredBeforeMedkit;

        public bool IsHolstered => phase == HolsterPhase.Holstered;
        public bool IsTransitioning => phase == HolsterPhase.Holstering || phase == HolsterPhase.Drawing;
        public bool IsMedkitWeaponLocked =>
            medkitUsePresentationActive ||
            (medkitController != null && medkitController.IsUsingMedkit);
        public bool IsWeaponReady =>
            weaponMount != null &&
            weaponMount.HasMountedWeapon &&
            phase == HolsterPhase.Armed &&
            !IsMedkitWeaponLocked;

        public bool ShouldHideFirstPersonArms
        {
            get
            {
                if (weaponMount == null || !weaponMount.HasMountedWeapon)
                {
                    return true;
                }

                switch (phase)
                {
                    case HolsterPhase.Holstered:
                        return true;
                    case HolsterPhase.Holstering:
                        return GetTransitionNormalized() >= armsHideHolsterThreshold;
                    case HolsterPhase.Drawing:
                        return GetTransitionNormalized() <= armsShowDrawThreshold;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// FP right-arm idle stays on while the weapon is still visible on the hand during holster/draw.
        /// </summary>
        public bool ShouldKeepRightArmIdleAnimation
        {
            get
            {
                if (weaponMount == null || !weaponMount.HasMountedWeapon)
                {
                    return false;
                }

                if (phase == HolsterPhase.Armed)
                {
                    return true;
                }

                if (phase == HolsterPhase.Holstered)
                {
                    return false;
                }

                var normalized = GetTransitionNormalized();
                if (phase == HolsterPhase.Holstering)
                {
                    return normalized < armsHideHolsterThreshold;
                }

                if (phase == HolsterPhase.Drawing)
                {
                    return normalized > armsShowDrawThreshold;
                }

                return false;
            }
        }

        private void Awake()
        {
            networkIdentity = GetComponent<PlayerNetworkIdentity>();
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() != null ||
                (networkIdentity != null && !networkIdentity.IsLocalPlayer))
            {
                enabled = false;
                return;
            }

            weaponMount = GetComponent<PlayerWeaponMount>();
            weaponController = GetComponent<PlayerWeaponController>();
            playerHealth = GetComponent<PlayerHealth>();
            handBinder = GetComponent<SyntyWeaponHandBinder>();
            armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
            splitBodyPresentation = GetComponent<SyntySplitBodyPresentation>();
            presenceSync = GetComponent<MatchPresenceSync>();
            medkitController = GetComponent<PlayerMedkitController>();
        }

        public void SetMedkitUsePresentationActive(bool active)
        {
            if (active)
            {
                BeginMedkitUsePresentation();
                return;
            }

            RestoreAfterMedkitUse();
        }

        public void BeginMedkitUsePresentation()
        {
            medkitUsePresentationActive = true;
            wasHolsteredBeforeMedkit = phase == HolsterPhase.Holstered && !IsTransitioning;
            weaponController?.CancelActiveReload();
            weaponMount?.ForceExitAds();

            if (weaponMount == null || !weaponMount.HasMountedWeapon)
            {
                ApplyHolsteredPresentation(true);
                handBinder?.SetHandIkEnabled(false);
                GetComponent<PlayerViewPresentation>()?.RefreshViewMode();
                return;
            }

            if (wasHolsteredBeforeMedkit)
            {
                weaponMount.SetLocalHolstered(true);
                ApplyHolsteredPresentation(true);
                handBinder?.SetHandIkEnabled(false);
            }
            else if (phase == HolsterPhase.Armed && !IsTransitioning)
            {
                BeginHolsterInternal(force: true);
            }
            else if (phase == HolsterPhase.Holstering)
            {
                weaponMount.SetLocalHolstered(true);
            }

            GetComponent<PlayerViewPresentation>()?.RefreshViewMode();
        }

        /// <summary>Ends medkit pose; draws only if the weapon was in hands before medkit use.</summary>
        public void RestoreAfterMedkitUse()
        {
            medkitUsePresentationActive = false;

            if (weaponMount == null || !weaponMount.HasMountedWeapon)
            {
                ApplyHolsteredPresentation(false);
                SyncFirstPersonArmsPresentation();
                GetComponent<PlayerViewPresentation>()?.RefreshViewMode();
                return;
            }

            if (wasHolsteredBeforeMedkit)
            {
                EnsureHolsteredAfterMedkit();
                SyncFirstPersonArmsPresentation();
                GetComponent<PlayerViewPresentation>()?.RefreshViewMode();
                return;
            }

            if (phase == HolsterPhase.Holstering)
            {
                CompleteHolsterTransitionImmediate();
            }

            if (phase == HolsterPhase.Holstered)
            {
                BeginDrawInternal(force: true);
            }
            else if (phase == HolsterPhase.Armed)
            {
                SyncFirstPersonArmsPresentation();
            }

            GetComponent<PlayerViewPresentation>()?.RefreshViewMode();
        }

        private void EnsureHolsteredAfterMedkit()
        {
            if (phase == HolsterPhase.Holstering)
            {
                CompleteHolsterTransitionImmediate();
            }

            weaponMount.SetLocalHolstered(true);
            ApplyHolsteredPresentation(true);
            handBinder?.SetHandIkEnabled(false);
        }

        private void CompleteHolsterTransitionImmediate()
        {
            HideWeaponLocally();
            weaponMount.SetHolsterTransitionActive(false);
            ApplyHolsteredPresentation(true);
            handBinder?.SetHandIkEnabled(false);
            transitionElapsed = 0f;
            phase = HolsterPhase.Holstered;
            NotifyHolsterNetworkState();
        }

        private void Start()
        {
            SyncFirstPersonArmsPresentation();
        }

        public void SyncFirstPersonArmsPresentation()
        {
            var hideArms = ShouldHideFirstPersonArms;
            ApplyHolsteredPresentation(hideArms);
            if (hideArms)
            {
                handBinder?.SetHandIkEnabled(false);
            }
            else if (phase == HolsterPhase.Armed)
            {
                handBinder?.SetHandIkEnabled(true);
            }
        }

        private void Update()
        {
            AdvanceTransition(Time.deltaTime);

            if (!CanAcceptInput())
            {
                return;
            }

            if (ReadHolsterPressed())
            {
                if (phase == HolsterPhase.Armed)
                {
                    BeginHolster();
                }
                else if (phase == HolsterPhase.Holstered)
                {
                    BeginDraw();
                }
            }
        }

        private void LateUpdate()
        {
            if (phase != HolsterPhase.Holstering && phase != HolsterPhase.Drawing)
            {
                return;
            }

            ApplyAnchorTransitionPose(GetTransitionNormalized());
            ApplyTransitionPresentation(GetTransitionNormalized());
        }

        public void ForceArmedState()
        {
            if (phase == HolsterPhase.Armed)
            {
                return;
            }

            transitionElapsed = 0f;
            weaponMount?.SetHolsterTransitionActive(false);
            weaponMount?.SetHandAttachedWeaponActive(false);
            weaponMount?.SetLocalHolstered(false);
            ShowWeaponLocally();
            AttachWeaponToAnchorImmediate(armedLocalPosition, armedLocalRotation, armedLocalScale);
            ApplyHolsteredPresentation(false);
            handBinder?.SetHandIkEnabled(true);
            phase = HolsterPhase.Armed;
            NotifyHolsterNetworkState();
        }

        private bool CanAcceptInput()
        {
            if (playerHealth != null && playerHealth.IsDead)
            {
                return false;
            }

            if (medkitController != null && medkitController.IsUsingMedkit)
            {
                return false;
            }

            var fps = GetComponent<FpsCharacterController>();
            return fps == null || fps.enabled;
        }

        private bool CanToggle()
        {
            return !IsTransitioning;
        }

        private void BeginHolster()
        {
            BeginHolsterInternal(force: false);
        }

        private void BeginHolsterInternal(bool force)
        {
            if (weaponMount == null)
            {
                return;
            }

            if (!force && !CanToggle())
            {
                return;
            }

            weaponMount.EnsureMounted();
            if (weaponMount.MountedWeaponRoot == null || weaponMount.WeaponAnchorTransform == null)
            {
                return;
            }

            weaponController?.CancelActiveReload();
            CaptureArmedLocalPose();
            handBinder?.SetHandIkEnabled(false);
            weaponMount.SetHolsterTransitionActive(true);
            weaponMount.SetLocalHolstered(true);
            EnsureArmedTransitionPresentation();
            transitionElapsed = 0f;
            phase = HolsterPhase.Holstering;
        }

        private void BeginDraw()
        {
            BeginDrawInternal(force: false);
        }

        private void BeginDrawInternal(bool force)
        {
            if (weaponMount == null)
            {
                return;
            }

            if (!force && !CanToggle())
            {
                return;
            }

            weaponMount.EnsureMounted();
            if (weaponMount.MountedWeaponRoot == null || weaponMount.WeaponAnchorTransform == null)
            {
                return;
            }

            ApplyHolsteredPresentation(false);
            weaponMount.SetHandAttachedWeaponActive(false);
            ShowWeaponLocally();
            AttachWeaponToAnchorImmediate(
                ResolveLoweredLocalPosition(),
                Quaternion.Euler(loweredLocalEulerAngles),
                armedLocalScale * holsteredScaleFactor);
            weaponMount.SetHolsterTransitionActive(true);
            weaponMount.SetLocalHolstered(true);
            handBinder?.SetHandIkEnabled(false);
            transitionElapsed = 0f;
            phase = HolsterPhase.Drawing;
        }

        private void AdvanceTransition(float deltaTime)
        {
            if (phase != HolsterPhase.Holstering && phase != HolsterPhase.Drawing)
            {
                return;
            }

            var duration = phase == HolsterPhase.Holstering
                ? Mathf.Max(0.02f, holsterLowerDuration)
                : Mathf.Max(0.02f, drawRaiseDuration);
            transitionElapsed += deltaTime;
            if (transitionElapsed < duration)
            {
                return;
            }

            if (phase == HolsterPhase.Holstering)
            {
                HideWeaponLocally();
                weaponMount.SetHolsterTransitionActive(false);
                ApplyHolsteredPresentation(true);
                phase = HolsterPhase.Holstered;
                NotifyHolsterNetworkState();
                return;
            }

            weaponMount.SetHolsterTransitionActive(false);
            weaponMount.SetLocalHolstered(false);
            AttachWeaponToAnchorImmediate(armedLocalPosition, armedLocalRotation, armedLocalScale);
            ApplyHolsteredPresentation(false);
            handBinder?.SetHandIkEnabled(true);
            phase = HolsterPhase.Armed;
            NotifyHolsterNetworkState();
        }

        private void NotifyHolsterNetworkState()
        {
            presenceSync?.FlushLocalPose();
        }

        private float GetTransitionNormalized()
        {
            var duration = phase == HolsterPhase.Holstering
                ? Mathf.Max(0.02f, holsterLowerDuration)
                : Mathf.Max(0.02f, drawRaiseDuration);
            return Mathf.Clamp01(transitionElapsed / duration);
        }

        private void ApplyAnchorTransitionPose(float normalized)
        {
            var weapon = weaponMount?.MountedWeaponRoot;
            if (weapon == null)
            {
                return;
            }

            var isHolstering = phase == HolsterPhase.Holstering;
            var easedPosition = EvaluateTransitionEase(normalized, isHolstering, true);
            var easedRotation = EvaluateTransitionEase(normalized, isHolstering, false);

            var loweredPosition = ResolveLoweredLocalPosition();
            var loweredRotation = Quaternion.Euler(loweredLocalEulerAngles);
            var startPos = isHolstering ? armedLocalPosition : loweredPosition;
            var endPos = isHolstering ? loweredPosition : armedLocalPosition;
            var startRot = isHolstering ? armedLocalRotation : loweredRotation;
            var endRot = isHolstering ? loweredRotation : armedLocalRotation;

            var swingWeight = Mathf.Sin(Mathf.PI * easedPosition);
            var controlPoint = Vector3.Lerp(startPos, endPos, 0.34f) + holsterSwingOffset * swingWeight;
            weapon.localPosition = EvaluateQuadraticBezier(startPos, controlPoint, endPos, easedPosition);
            weapon.localRotation = Quaternion.Slerp(startRot, endRot, easedRotation);

            var scaleT = isHolstering ? easedPosition : 1f - easedPosition;
            var scaleFactor = Mathf.Lerp(1f, holsteredScaleFactor, scaleT);
            weapon.localScale = armedLocalScale * scaleFactor;
        }

        private void ApplyTransitionPresentation(float normalized)
        {
            if (phase == HolsterPhase.Holstering)
            {
                if (normalized >= armsHideHolsterThreshold)
                {
                    ApplyHolsteredPresentation(true);
                }

                return;
            }

            if (phase == HolsterPhase.Drawing)
            {
                if (normalized <= armsShowDrawThreshold)
                {
                    ApplyHolsteredPresentation(true);
                }
                else
                {
                    ApplyHolsteredPresentation(false);
                }
            }
        }

        private float EvaluateTransitionEase(float normalized, bool holstering, bool position)
        {
            var curve = holstering
                ? (position ? holsterPositionEase : holsterRotationEase)
                : (position ? drawPositionEase : drawRotationEase);
            if (curve != null && curve.length > 0)
            {
                return curve.Evaluate(normalized);
            }

            if (holstering)
            {
                return position ? EvaluateHolsterDropEase(normalized) : EvaluateHolsterRotationEase(normalized);
            }

            return position ? EvaluateDrawRaiseEase(normalized) : EvaluateDrawRotationEase(normalized);
        }

        private static float EvaluateHolsterDropEase(float t)
        {
            if (t <= 0.16f)
            {
                var pullBack = t / 0.16f;
                return pullBack * pullBack * 0.1f;
            }

            var drop = (t - 0.16f) / 0.84f;
            return 0.1f + drop * drop * (3f - 2f * drop) * 0.9f;
        }

        private static float EvaluateHolsterRotationEase(float t)
        {
            var drop = EvaluateHolsterDropEase(t);
            return Mathf.Clamp01(drop * 1.08f);
        }

        private static float EvaluateDrawRaiseEase(float t)
        {
            var lift = 1f - t;
            return 1f - lift * lift * (3f - 2f * lift);
        }

        private static float EvaluateDrawRotationEase(float t)
        {
            return EvaluateDrawRaiseEase(Mathf.Clamp01(t * 1.06f));
        }

        private Vector3 ResolveLoweredLocalPosition()
        {
            var position = loweredLocalPosition;
            if (!scaleLoweredPoseByFieldOfView)
            {
                return position;
            }

            var camera = ResolveLocalCamera();
            if (camera == null || referenceFieldOfView <= 1f)
            {
                return position;
            }

            var currentHalfFov = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            var referenceHalfFov = referenceFieldOfView * 0.5f * Mathf.Deg2Rad;
            var fovRatio = Mathf.Tan(currentHalfFov) / Mathf.Tan(referenceHalfFov);
            position.y *= fovRatio;
            position.y -= (fovRatio - 1f) * extraLowerPerFovRatio;
            position.z -= (fovRatio - 1f) * 0.06f;
            return position;
        }

        private Camera ResolveLocalCamera()
        {
            if (localCamera != null)
            {
                return localCamera;
            }

            localCamera = GetComponentInChildren<Camera>(true);
            return localCamera;
        }

        private static Vector3 EvaluateQuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
        {
            var inverse = 1f - t;
            return inverse * inverse * start + 2f * inverse * t * control + t * t * end;
        }

        private void CaptureArmedLocalPose()
        {
            var weapon = weaponMount.MountedWeaponRoot;
            if (weapon == null)
            {
                armedLocalPosition = Vector3.zero;
                armedLocalRotation = Quaternion.identity;
                armedLocalScale = Vector3.one;
                return;
            }

            armedLocalPosition = weapon.localPosition;
            armedLocalRotation = weapon.localRotation;
            armedLocalScale = weapon.localScale;
        }

        private void HideWeaponLocally()
        {
            weaponMount.SetHandAttachedWeaponActive(false);
            weaponMount.SetThirdPersonWeaponRenderersEnabled(false);
            AttachWeaponToAnchorImmediate(
                ResolveLoweredLocalPosition(),
                Quaternion.Euler(loweredLocalEulerAngles),
                armedLocalScale * holsteredScaleFactor);
        }

        private void ShowWeaponLocally()
        {
            weaponMount.SetThirdPersonWeaponRenderersEnabled(true);
        }

        private void AttachWeaponToAnchorImmediate(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            var weapon = weaponMount?.MountedWeaponRoot;
            var anchor = weaponMount?.WeaponAnchorTransform;
            if (weapon == null || anchor == null)
            {
                return;
            }

            weapon.SetParent(anchor, false);
            weapon.localPosition = localPosition;
            weapon.localRotation = localRotation;
            weapon.localScale = localScale;
        }

        private void ApplyHolsteredPresentation(bool holstered)
        {
            splitBodyPresentation?.SetHolsteredFirstPersonPresentation(holstered);
            armsPresenter?.SetHolsteredArmsPresentation(holstered);

            if (!holstered)
            {
                EnsureArmedTransitionPresentation();
            }
        }

        private void EnsureArmedTransitionPresentation()
        {
            var viewPresentation = GetComponent<PlayerViewPresentation>();
            var isLocal = viewPresentation == null || viewPresentation.IsLocalPlayerView;
            armsPresenter?.ApplyFirstPersonVisibility(isLocal);
            splitBodyPresentation?.ApplyViewMode();
            handBinder?.SetHandIkEnabled(true);
        }

        private static bool ReadHolsterPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.X);
#endif
        }
    }
}
