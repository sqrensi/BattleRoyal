using System;
using System.Collections;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Third-person weapon for remote players. Uses WeaponModel already parented to
    /// RemoteWeaponTarget when present; otherwise spawns from weaponPrefab once.
    /// Never modifies weapon local transform after mount (except optional grip align + holster).
    /// </summary>
    [DefaultExecutionOrder(450)]
    public sealed class RemoteWeaponPresentation : MonoBehaviour
    {
        [Header("Weapon")]
        [SerializeField] private GameObject weaponPrefab;
        [SerializeField] private Transform attachTarget;
        [SerializeField] private string defaultWeaponPrefabPath = "Assets/Prefabs/AK-47/rifle_001.prefab";
        [SerializeField] private string sniperWeaponPrefabPath = "Assets/Prefabs/Sniper/sniper_rifle_001.prefab";
        [SerializeField] private string pistolWeaponPrefabPath = "Assets/Prefabs/Pistol/pistol_001.prefab";
        [SerializeField] private string mp7WeaponPrefabPath = "Assets/Prefabs/mp7/mp7.prefab";
        [SerializeField] private string attachTargetName = "RemoteWeaponTarget";
        [SerializeField] private string rightHandBoneName = "Hand_R";
        [Header("Grip Alignment")]
        [SerializeField] private bool alignGripToAttachTarget;
        [SerializeField] private string gripTargetName = "RemoteRightHandTarget";
        [SerializeField] private string gripFallbackName = "RightHandTarget";
        [SerializeField] private string leftHandTargetName = "RemoteLeftHandTarget";
        [SerializeField] private string leftHandTargetFallbackName = "LeftHandTarget";

        [Header("Attach Target Defaults")]
        [SerializeField] private Vector3 defaultAttachLocalPosition = new Vector3(-0.0522f, 0.0951f, 0.0199f);
        [SerializeField] private Vector3 defaultAttachLocalEuler = new Vector3(-42.105f, 202.324f, -107.611f);

        [Header("Holster (Remote Back Only)")]
        [SerializeField] private Transform backHolsterTarget;
        [SerializeField] private string backHolsterTargetName = "BackWeaponTarget";
        [SerializeField] private Transform backHolsterTargetSecondary;
        [SerializeField] private string backHolsterTargetSecondaryName = "BackWeaponTarget2";

        [Header("Posture Lean Compensation")]
        [SerializeField] private bool enablePostureLeanCompensation = true;
        [SerializeField] private float postureLeanCompensation = 0.65f;
        [SerializeField] private float postureLeanSmoothTime = 0.12f;

        [Header("Look Pitch Tilt")]
        [SerializeField] private bool enableLookPitchTilt = false;
        [SerializeField] private float lookPitchInfluence = 1f;
        [SerializeField] private float lookPitchWeaponMax = 50f;
        [SerializeField] private float lookPitchSmoothTime = 0.1f;
        [SerializeField] private float crouchPitchCompensation = 30f;
        [SerializeField] private float crouchPitchSmoothTime = 0.12f;

        [Header("Remote Weapon Swap")]
        [SerializeField] private float weaponSwapIdleSeconds = 0.12f;
        [SerializeField] private float remoteSniperScaleMultiplier = 0.5882353f;

        private Transform weaponRoot;
        private Transform thirdPersonBody;
        private Vector3 baseAttachLocalPosition;
        private Quaternion baseAttachLocalRotation;
        private bool hasBaseAttachPose;
        private float networkLookPitch;
        private bool networkCrouching;
        private float smoothedPitchOffset;
        private float pitchOffsetVelocity;
        private float smoothedCrouchPitchOffset;
        private float crouchPitchOffsetVelocity;
        private float smoothedPostureLeanCompensation;
        private float postureLeanCompensationVelocity;
        private RemoteLookPitchPosture lookPitchPosture;
        private RemoteAnimatorHolsterPresentation holsterAnimation;
        private bool networkHolstered;
        private bool networkHasWeapon;
        private bool networkMedkitActive;
        private WeaponKind networkWeaponKind = WeaponKind.AssaultRifle;
        private byte networkSlot0Kind = PlayerWeaponLoadout.EmptySlotKind;
        private byte networkSlot1Kind = PlayerWeaponLoadout.EmptySlotKind;
        private int networkActiveWeaponSlot = PlayerWeaponLoadout.NoActiveSlot;
        private byte appliedSlot0Kind = PlayerWeaponLoadout.EmptySlotKind;
        private byte appliedSlot1Kind = PlayerWeaponLoadout.EmptySlotKind;
        private int appliedActiveWeaponSlot = PlayerWeaponLoadout.NoActiveSlot;
        private bool appliedHolstered;
        private bool appliedHasWeapon;
        private byte appliedActiveWeaponKind;
        private Transform backWeaponRootSlot0;
        private Transform backWeaponRootSlot1;
        private Coroutine weaponSwapRoutine;
        private bool hasHandPoseSnapshot;
        private Vector3 handPoseLocalPosition;
        private Quaternion handPoseLocalRotation;
        private Vector3 handPoseLocalScale;
        private bool hasNetworkWeaponSkins;
        private PlayerSkinNetworkState networkWeaponSkinState;
        private bool suppressNetworkDisarm;

        public Transform WeaponRoot => weaponRoot;
        public Transform AttachTarget => attachTarget;
        public bool HasWeapon => networkHasWeapon;
        public bool IsHolstered => networkHolstered;

        public void SetWeaponKind(WeaponKind kind)
        {
            if (networkWeaponKind == kind && weaponPrefab == ResolvePrefabForKind(kind))
            {
                return;
            }

            networkWeaponKind = kind;
            weaponPrefab = ResolvePrefabForKind(kind);
            if (weaponRoot != null)
            {
                Destroy(weaponRoot.gameObject);
                weaponRoot = null;
                hasHandPoseSnapshot = false;
            }

            if (networkHasWeapon)
            {
                EnsureAttached();
            }
        }

        public void SetWeaponLoadout(
            byte slot0Kind,
            byte slot1Kind,
            int activeWeaponSlot,
            bool holstered,
            bool hasWeapon,
            byte activeWeaponKind)
        {
            var resolvedWeaponKind = ResolveNetworkWeaponKind(
                slot0Kind,
                slot1Kind,
                activeWeaponSlot,
                holstered,
                activeWeaponKind);
            var shouldAnimateHandSwap = networkHasWeapon &&
                                        !networkHolstered &&
                                        hasWeapon &&
                                        !holstered &&
                                        (resolvedWeaponKind != networkWeaponKind ||
                                         activeWeaponSlot != networkActiveWeaponSlot);
            if (slot0Kind == appliedSlot0Kind &&
                slot1Kind == appliedSlot1Kind &&
                activeWeaponSlot == appliedActiveWeaponSlot &&
                holstered == appliedHolstered &&
                hasWeapon == appliedHasWeapon &&
                activeWeaponKind == appliedActiveWeaponKind &&
                IsHandWeaponPresentationCurrent(resolvedWeaponKind, hasWeapon, holstered))
            {
                return;
            }

            appliedSlot0Kind = slot0Kind;
            appliedSlot1Kind = slot1Kind;
            appliedActiveWeaponSlot = activeWeaponSlot;
            appliedHolstered = holstered;
            appliedHasWeapon = hasWeapon;
            appliedActiveWeaponKind = activeWeaponKind;

            networkSlot0Kind = slot0Kind;
            networkSlot1Kind = slot1Kind;
            networkActiveWeaponSlot = activeWeaponSlot;
            networkHolstered = holstered;
            networkHasWeapon = hasWeapon;
            networkWeaponKind = resolvedWeaponKind;

            if (networkMedkitActive)
            {
                ApplyMedkitWeaponHiddenState();
                return;
            }

            ResolveHolsterAnimation()?.SetWeaponEquipped(hasWeapon);
            if (!hasWeapon)
            {
                ApplyUnarmedPresentation();
                ClearBackWeaponRoots();
                return;
            }

            if (shouldAnimateHandSwap)
            {
                BeginRemoteWeaponSwap();
                return;
            }

            ApplyDualWeaponPresentation();
        }

        private void BeginRemoteWeaponSwap()
        {
            if (weaponSwapRoutine != null)
            {
                StopCoroutine(weaponSwapRoutine);
            }

            weaponSwapRoutine = StartCoroutine(RemoteWeaponSwapRoutine());
        }

        private IEnumerator RemoteWeaponSwapRoutine()
        {
            SetWeaponRenderersEnabled(false);
            GetComponent<RemoteLeftHandIkBinder>()?.SetHandIkEnabled(false);
            ResolveHolsterAnimation()?.SetHolstered(true);

            var delay = Mathf.Max(0f, weaponSwapIdleSeconds);
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            else
            {
                yield return null;
            }

            ApplyDualWeaponPresentation();
            weaponSwapRoutine = null;
        }

        private bool IsHandWeaponPresentationCurrent(WeaponKind expectedKind, bool hasWeapon, bool holstered)
        {
            if (!hasWeapon || holstered)
            {
                return true;
            }

            if (weaponRoot == null)
            {
                return false;
            }

            return HandWeaponRootMatchesKind(expectedKind);
        }

        private static WeaponKind ResolveNetworkWeaponKind(
            byte slot0Kind,
            byte slot1Kind,
            int activeWeaponSlot,
            bool holstered,
            byte activeWeaponKind)
        {
            if (!holstered)
            {
                if (activeWeaponSlot == 0 && slot0Kind != PlayerWeaponLoadout.EmptySlotKind)
                {
                    return WeaponKindUtility.ClampKind((int)slot0Kind);
                }

                if (activeWeaponSlot == 1 && slot1Kind != PlayerWeaponLoadout.EmptySlotKind)
                {
                    return WeaponKindUtility.ClampKind((int)slot1Kind);
                }
            }

            if (slot0Kind != PlayerWeaponLoadout.EmptySlotKind)
            {
                return WeaponKindUtility.ClampKind((int)slot0Kind);
            }

            if (slot1Kind != PlayerWeaponLoadout.EmptySlotKind)
            {
                return WeaponKindUtility.ClampKind((int)slot1Kind);
            }

            return WeaponKindUtility.ClampKind((int)activeWeaponKind);
        }

        public WeaponProfile GetActiveWeaponProfile()
        {
            return weaponRoot != null
                ? weaponRoot.GetComponentInChildren<WeaponProfile>(true)
                : null;
        }

        public bool TryGetHandWeaponKind(out WeaponKind kind)
        {
            kind = default;
            if (!networkHasWeapon || networkHolstered || weaponRoot == null)
            {
                return false;
            }

            return TryGetWeaponRootKind(weaponRoot, out kind);
        }

        public bool TryEnsureMenuAttachTarget()
        {
            thirdPersonBody = thirdPersonBody != null
                ? thirdPersonBody
                : transform.Find("ThirdPersonBody");
            if (thirdPersonBody == null)
            {
                return false;
            }

            EnsureAttachTarget(thirdPersonBody);
            return attachTarget != null;
        }

        /// <summary>
        /// Synchronous main-menu path: always destroys the previous hand model and spawns the
        /// requested weapon prefab before applying the equipped inventory skin.
        /// </summary>
        public void EquipMenuPreviewWeapon(WeaponKind kind)
        {
            if (weaponSwapRoutine != null)
            {
                StopCoroutine(weaponSwapRoutine);
                weaponSwapRoutine = null;
            }

            thirdPersonBody = thirdPersonBody != null
                ? thirdPersonBody
                : transform.Find("ThirdPersonBody");
            if (thirdPersonBody == null)
            {
                return;
            }

            DestroyAllWeaponModelsUnderPlayer();
            ClearBackWeaponRoots();
            ResetAppliedLoadout();

            networkWeaponKind = kind;
            networkHasWeapon = true;
            networkHolstered = false;
            networkSlot0Kind = (byte)kind;
            networkSlot1Kind = PlayerWeaponLoadout.EmptySlotKind;
            networkActiveWeaponSlot = 0;
            appliedSlot0Kind = (byte)kind;
            appliedSlot1Kind = PlayerWeaponLoadout.EmptySlotKind;
            appliedActiveWeaponSlot = 0;
            appliedHolstered = false;
            appliedHasWeapon = true;
            appliedActiveWeaponKind = (byte)kind;

            weaponPrefab = WeaponCatalog.GetWeaponPrefab(kind);
            if (weaponPrefab == null)
            {
                Debug.LogWarning(
                    $"[RemoteWeaponPresentation] Menu preview weapon prefab is missing for {kind}.");
                weaponRoot = null;
                return;
            }

            weaponRoot = null;
            attachTarget = null;
            hasBaseAttachPose = false;
            hasHandPoseSnapshot = false;

            EnsureAttachTarget(thirdPersonBody);
            if (attachTarget == null)
            {
                Debug.LogWarning("[RemoteWeaponPresentation] Menu preview attach target was not found.");
                return;
            }

            SpawnWeaponOnTarget();
            AttachWeaponToHand();
            SetNetworkWeaponSkins(PlayerSkinSelectionService.CaptureLocalNetworkState());
            ReapplyMenuPreviewSkin(kind);
            ResolveHolsterAnimation()?.SetWeaponEquipped(true);
            ResolveHolsterAnimation()?.SetHolstered(false);
        }

        public void SetMenuPreviewMode(bool enabled)
        {
            suppressNetworkDisarm = enabled;
        }

        public void SetNetworkWeaponSkins(in PlayerSkinNetworkState skinState)
        {
            if (hasNetworkWeaponSkins && networkWeaponSkinState.Equals(skinState))
            {
                return;
            }

            hasNetworkWeaponSkins = true;
            networkWeaponSkinState = skinState;
            RefreshAllWeaponSkins();
        }

        public void RefreshAllWeaponSkins()
        {
            if (weaponRoot != null && networkHasWeapon && !networkHolstered)
            {
                ApplyPresentationSkin(weaponRoot, networkWeaponKind);
            }

            ApplyPresentationSkin(backWeaponRootSlot0, ResolveBackWeaponKind(backWeaponRootSlot0));
            ApplyPresentationSkin(backWeaponRootSlot1, ResolveBackWeaponKind(backWeaponRootSlot1));
        }

        private static WeaponKind ResolveBackWeaponKind(Transform root)
        {
            return root != null && TryGetWeaponRootKind(root, out var kind)
                ? kind
                : WeaponKind.AssaultRifle;
        }

        private void ApplyPresentationSkin(Transform root, WeaponKind kind)
        {
            if (root == null)
            {
                return;
            }

            if (hasNetworkWeaponSkins)
            {
                WeaponSkinApplier.ApplyNetworkSkin(root, kind, in networkWeaponSkinState);
                return;
            }

            WeaponSkinApplier.ApplyEquippedSkin(root, kind);
        }

        public void ReapplyMenuPreviewSkin(WeaponKind kind)
        {
            if (weaponRoot == null)
            {
                return;
            }

            SetNetworkWeaponSkins(PlayerSkinSelectionService.CaptureLocalNetworkState());
            ApplyPresentationSkin(weaponRoot, kind);
        }

        public void SetWeaponEquipped(bool equipped)
        {
            if (!equipped && suppressNetworkDisarm)
            {
                return;
            }

            if (networkMedkitActive)
            {
                networkHasWeapon = equipped;
                ApplyMedkitWeaponHiddenState();
                return;
            }

            if (networkHasWeapon == equipped)
            {
                return;
            }

            networkHasWeapon = equipped;
            ResolveHolsterAnimation()?.SetWeaponEquipped(equipped);

            if (!equipped)
            {
                ApplyUnarmedPresentation();
                return;
            }

            EnsureAttached();
        }

        public void SetHolstered(bool holstered)
        {
            if (networkMedkitActive)
            {
                networkHolstered = !networkHasWeapon || holstered;
                ApplyMedkitWeaponHiddenState();
                return;
            }

            if (!networkHasWeapon)
            {
                ApplyUnarmedPresentation();
                return;
            }

            if (networkHolstered == holstered)
            {
                return;
            }

            networkHolstered = holstered;
            ResolveHolsterAnimation()?.SetHolstered(holstered);
            EnsureAttached();
            if (weaponRoot == null)
            {
                return;
            }

            if (holstered)
            {
                AttachWeaponToBack();
                return;
            }

            AttachWeaponToHand();
        }

        public void SetNetworkLookPitch(float lookPitch)
        {
            networkLookPitch = lookPitch;
        }

        public void SetNetworkCrouchState(bool crouching)
        {
            networkCrouching = crouching;
        }

        public void SetMedkitPresentationActive(bool active)
        {
            if (networkMedkitActive == active)
            {
                return;
            }

            networkMedkitActive = active;
            if (active)
            {
                ApplyMedkitWeaponHiddenState();
                return;
            }

            if (!networkHasWeapon)
            {
                ApplyUnarmedPresentation();
                return;
            }

            EnsureAttached();
            if (weaponRoot == null)
            {
                return;
            }

            if (networkHolstered)
            {
                AttachWeaponToBack();
                return;
            }

            AttachWeaponToHand();
            SetWeaponRenderersEnabled(true);
        }

        private void ApplyMedkitWeaponHiddenState()
        {
            EnsureAttached();
            if (weaponRoot == null)
            {
                return;
            }

            if (networkHasWeapon && networkHolstered)
            {
                AttachWeaponToBack();
                SetWeaponRenderersEnabled(true);
            }
            else
            {
                SetWeaponRenderersEnabled(false);
            }

            var handBinder = GetComponent<RemoteLeftHandIkBinder>();
            handBinder?.SetHandIkEnabled(false);
        }

        private void LateUpdate()
        {
            if (networkMedkitActive || networkHolstered)
            {
                return;
            }

            if (lookPitchPosture == null)
            {
                lookPitchPosture = GetComponent<RemoteLookPitchPosture>();
            }

            ApplyAttachTargetLookPitchTilt();
        }

        private void OnEnable()
        {
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() == null)
            {
                return;
            }

            if (networkHasWeapon)
            {
                EnsureAttached();
            }
            else
            {
                ApplyUnarmedPresentation();
            }
        }

        public void Configure(Transform body, GameObject prefab = null)
        {
            thirdPersonBody = body;
            if (prefab != null)
            {
                weaponPrefab = prefab;
            }

            if (weaponPrefab == null)
            {
                weaponPrefab = LoadDefaultWeaponPrefab();
            }

            EnsureAttachTarget(body);
        }

        public void InvalidateAttachTarget()
        {
            attachTarget = null;
            hasBaseAttachPose = false;
            if (!IsWeaponRootAlive())
            {
                weaponRoot = null;
                hasHandPoseSnapshot = false;
            }
        }

        public void ResetForMenuPreview(Transform body)
        {
            if (weaponSwapRoutine != null)
            {
                StopCoroutine(weaponSwapRoutine);
                weaponSwapRoutine = null;
            }

            DestroyAllWeaponModelsUnderPlayer();
            ClearBackWeaponRoots();
            ResetAppliedLoadout();
            networkHasWeapon = false;
            networkHolstered = true;
            thirdPersonBody = body;
            attachTarget = null;
            hasBaseAttachPose = false;
            Configure(body);
        }

        private void DestroyAllWeaponModelsUnderPlayer()
        {
            ClearWeaponInstance();

            var searchRoot = thirdPersonBody != null ? thirdPersonBody : transform;
            DestroyNamedWeaponModels(searchRoot);
            DestroyNamedWeaponModels(transform);
        }

        private static void DestroyNamedWeaponModels(Transform searchRoot)
        {
            if (searchRoot == null)
            {
                return;
            }

            var transforms = searchRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var current = transforms[i];
                if (current == null)
                {
                    continue;
                }

                if (string.Equals(current.name, "WeaponModel", StringComparison.Ordinal) ||
                    string.Equals(current.name, "BackWeaponModel0", StringComparison.Ordinal) ||
                    string.Equals(current.name, "BackWeaponModel1", StringComparison.Ordinal))
                {
                    Destroy(current.gameObject);
                }
            }
        }

        public void ResetAppliedLoadout()
        {
            appliedSlot0Kind = PlayerWeaponLoadout.EmptySlotKind;
            appliedSlot1Kind = PlayerWeaponLoadout.EmptySlotKind;
            appliedActiveWeaponSlot = PlayerWeaponLoadout.NoActiveSlot;
            appliedHolstered = false;
            appliedHasWeapon = false;
            appliedActiveWeaponKind = 0;
        }

        public void ClearWeaponInstance()
        {
            if (IsWeaponRootAlive())
            {
                Destroy(weaponRoot.gameObject);
            }

            weaponRoot = null;
            hasHandPoseSnapshot = false;
        }

        public void EnsureAttached()
        {
            if (thirdPersonBody == null)
            {
                thirdPersonBody = transform.Find("ThirdPersonBody");
            }

            EnsureAttachTarget(thirdPersonBody);
            if (attachTarget == null)
            {
                Debug.LogWarning("[RemoteWeaponPresentation] RemoteWeaponTarget not found on remote player.");
                return;
            }

            if (!networkHasWeapon)
            {
                if (weaponRoot == null)
                {
                    weaponRoot = FindAnyWeaponModelUnderAttachTarget();
                    if (weaponRoot == null)
                    {
                        weaponRoot = FindAnyExistingWeaponModel(transform);
                    }
                }

                if (weaponRoot != null)
                {
                    SetWeaponRenderersEnabled(false);
                }

                return;
            }

            EnsureBaseAttachPose();
            EnsureHierarchyActive(attachTarget);

            if (!IsWeaponRootAlive())
            {
                weaponRoot = null;
                hasHandPoseSnapshot = false;
            }

            if (weaponPrefab == null)
            {
                weaponPrefab = LoadDefaultWeaponPrefab();
            }

            if (weaponRoot == null)
            {
                weaponRoot = FindWeaponModelUnderAttachTarget(networkWeaponKind);
                if (weaponRoot == null && weaponPrefab != null)
                {
                    SpawnWeaponOnTarget();
                }
                else if (weaponRoot == null)
                {
                    weaponRoot = FindExistingWeaponModel(transform, networkWeaponKind);
                }
            }
            else if (!HandWeaponRootMatchesKind(networkWeaponKind))
            {
                Destroy(weaponRoot.gameObject);
                weaponRoot = null;
                hasHandPoseSnapshot = false;
                if (weaponPrefab != null)
                {
                    SpawnWeaponOnTarget();
                }
            }

            if (weaponRoot != null)
            {
                if (!hasHandPoseSnapshot)
                {
                    SnapshotHandPoseFromWeapon();
                }

                PreservePrefabWeaponHierarchy();
                if (networkHolstered)
                {
                    AttachWeaponToBack();
                }
                else if (weaponRoot.parent != attachTarget)
                {
                    AttachWeaponToHand();
                }
                else
                {
                    WireRemoteLeftHandIk();
                }

                return;
            }

            if (weaponPrefab == null)
            {
                Debug.LogWarning("[RemoteWeaponPresentation] weaponPrefab is not assigned on remote player.");
            }
        }

        private void PreservePrefabWeaponHierarchy()
        {
            if (weaponRoot == null)
            {
                return;
            }

            if (weaponRoot.parent != null &&
                string.Equals(weaponRoot.parent.name, attachTargetName, StringComparison.Ordinal))
            {
                attachTarget = weaponRoot.parent;
            }

            EnsureHierarchyActive(weaponRoot);
            SetWeaponRenderersEnabled(true);
        }

        private void SpawnWeaponOnTarget()
        {
            var instance = Instantiate(weaponPrefab, attachTarget);
            instance.name = "WeaponModel";
            weaponRoot = instance.transform;

            if (alignGripToAttachTarget)
            {
                AlignWeaponGripToTarget();
            }

            SnapshotHandPoseFromWeapon();
            ApplyHandWeaponScale();
            EnsureHierarchyActive(weaponRoot);
            SetWeaponRenderersEnabled(true);
            WireRemoteLeftHandIk();
        }

        private void AttachWeaponToBack()
        {
            ResolveBackHolsterTarget();
            if (weaponRoot == null)
            {
                return;
            }

            if (backHolsterTarget != null && weaponRoot.parent == backHolsterTarget)
            {
                SetWeaponRenderersEnabled(true);
                var existingBinder = GetComponent<RemoteLeftHandIkBinder>();
                existingBinder?.SetHandIkEnabled(false);
                return;
            }

            if (attachTarget != null && weaponRoot.parent == attachTarget)
            {
                SnapshotHandPoseFromWeapon();
                ResetAttachTargetBasePose();
            }

            if (backHolsterTarget == null)
            {
                SetWeaponRenderersEnabled(false);
                return;
            }

            weaponRoot.SetParent(backHolsterTarget, false);
            weaponRoot.localPosition = Vector3.zero;
            weaponRoot.localRotation = Quaternion.identity;
            SetWeaponRenderersEnabled(true);

            var handBinder = GetComponent<RemoteLeftHandIkBinder>();
            if (handBinder != null)
            {
                handBinder.SetHandIkEnabled(false);
            }
        }

        private void AttachWeaponToHand()
        {
            EnsureAttachTarget(thirdPersonBody);
            if (weaponRoot == null || attachTarget == null)
            {
                return;
            }

            ResetAttachTargetBasePose();

            if (weaponRoot.parent == attachTarget)
            {
                FinishHandAttachment();
                return;
            }

            var returningFromHolster = backHolsterTarget != null && weaponRoot.parent == backHolsterTarget;
            weaponRoot.SetParent(attachTarget, false);

            if (alignGripToAttachTarget)
            {
                AlignWeaponGripToTarget();
            }
            else if (returningFromHolster && hasHandPoseSnapshot)
            {
                RestoreHandPoseSnapshot();
            }

            FinishHandAttachment();
        }

        private void FinishHandAttachment()
        {
            ApplyHandWeaponScale();
            SetWeaponRenderersEnabled(true);
            WireRemoteLeftHandIk();
            ApplyAttachTargetLookPitchTilt();
            ApplyPresentationSkin(weaponRoot, networkWeaponKind);
        }

        private void ApplyHandWeaponScale()
        {
            if (weaponRoot == null)
            {
                return;
            }

            if (!hasHandPoseSnapshot)
            {
                SnapshotHandPoseFromWeapon();
            }

            var baseScale = hasHandPoseSnapshot ? handPoseLocalScale : weaponRoot.localScale;
            weaponRoot.localScale = baseScale * ResolveRemoteWeaponScale(networkWeaponKind);
        }

        private float ResolveRemoteWeaponScale(WeaponKind kind)
        {
            return kind == WeaponKind.SniperRifle
                ? Mathf.Max(0.01f, remoteSniperScaleMultiplier)
                : 1f;
        }

        private void ResetAttachTargetBasePose()
        {
            if (attachTarget == null)
            {
                return;
            }

            EnsureBaseAttachPose();
            if (!hasBaseAttachPose)
            {
                return;
            }

            attachTarget.localPosition = baseAttachLocalPosition;
            attachTarget.localRotation = baseAttachLocalRotation;
            SnapAttachPitchSmoothers();
        }

        private void SnapshotHandPoseFromWeapon()
        {
            if (weaponRoot == null)
            {
                return;
            }

            handPoseLocalPosition = weaponRoot.localPosition;
            handPoseLocalRotation = weaponRoot.localRotation;
            handPoseLocalScale = weaponRoot.localScale;
            hasHandPoseSnapshot = true;
        }

        private void RestoreHandPoseSnapshot()
        {
            if (weaponRoot == null || !hasHandPoseSnapshot)
            {
                return;
            }

            weaponRoot.localPosition = handPoseLocalPosition;
            weaponRoot.localRotation = handPoseLocalRotation;
            weaponRoot.localScale = handPoseLocalScale;
        }

        private Transform FindAnyWeaponModelUnderAttachTarget()
        {
            if (attachTarget == null)
            {
                return null;
            }

            for (var i = 0; i < attachTarget.childCount; i++)
            {
                var child = attachTarget.GetChild(i);
                if (child != null &&
                    string.Equals(child.name, "WeaponModel", StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private Transform FindWeaponModelUnderAttachTarget(WeaponKind expectedKind)
        {
            if (attachTarget == null)
            {
                return null;
            }

            for (var i = 0; i < attachTarget.childCount; i++)
            {
                var child = attachTarget.GetChild(i);
                if (child == null ||
                    !string.Equals(child.name, "WeaponModel", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryGetWeaponRootKind(child, out var rootKind) && rootKind == expectedKind)
                {
                    return child;
                }

                Destroy(child.gameObject);
            }

            return null;
        }

        private static bool TryGetWeaponRootKind(Transform root, out WeaponKind kind)
        {
            kind = WeaponKind.AssaultRifle;
            if (root == null)
            {
                return false;
            }

            var profile = root.GetComponentInChildren<WeaponProfile>(true);
            if (profile == null)
            {
                return false;
            }

            kind = profile.Kind;
            return true;
        }

        private bool HandWeaponRootMatchesKind(WeaponKind kind)
        {
            return TryGetWeaponRootKind(weaponRoot, out var rootKind) && rootKind == kind;
        }

        private RemoteAnimatorHolsterPresentation ResolveHolsterAnimation()
        {
            if (holsterAnimation == null)
            {
                holsterAnimation = GetComponent<RemoteAnimatorHolsterPresentation>();
            }

            return holsterAnimation;
        }

        private void ResolveBackHolsterTarget()
        {
            if (backHolsterTarget != null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(backHolsterTargetName))
            {
                return;
            }

            var searchRoot = thirdPersonBody != null ? thirdPersonBody : transform;
            var transforms = searchRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var current = transforms[i];
                if (current != null &&
                    string.Equals(current.name, backHolsterTargetName, StringComparison.OrdinalIgnoreCase))
                {
                    backHolsterTarget = current;
                    return;
                }
            }
        }

        private void EnsureAttachTarget(Transform body)
        {
            if (IsValidSceneTransform(attachTarget) &&
                (body == null || attachTarget.IsChildOf(body)))
            {
                return;
            }

            attachTarget = ResolveAttachTarget(body);
        }

        private static bool IsValidSceneTransform(Transform value)
        {
            return value != null && value.gameObject.scene.IsValid();
        }

        private GameObject LoadDefaultWeaponPrefab()
        {
            return ResolvePrefabForKind(WeaponKind.AssaultRifle);
        }

        private GameObject ResolvePrefabForKind(WeaponKind kind)
        {
            var catalogPrefab = WeaponCatalog.GetWeaponPrefab(kind);
            if (catalogPrefab != null)
            {
                return catalogPrefab;
            }

            var path = kind switch
            {
                WeaponKind.SniperRifle => sniperWeaponPrefabPath,
                WeaponKind.Pistol => pistolWeaponPrefabPath,
                WeaponKind.Mp7 => mp7WeaponPrefabPath,
                _ => defaultWeaponPrefabPath
            };
#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(path))
            {
                return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
#endif
            if (weaponPrefab != null)
            {
                var profile = weaponPrefab.GetComponent<WeaponProfile>();
                if (profile == null || profile.Kind == kind)
                {
                    return weaponPrefab;
                }
            }

            return null;
        }

        private bool IsWeaponRootAlive()
        {
            return weaponRoot != null;
        }

        private static Transform FindAnyExistingWeaponModel(Transform searchRoot)
        {
            if (searchRoot == null)
            {
                return null;
            }

            Transform underTarget = null;
            Transform fallback = null;
            var all = searchRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var candidate = all[i];
                if (candidate == null ||
                    !string.Equals(candidate.name, "WeaponModel", StringComparison.Ordinal))
                {
                    continue;
                }

                fallback = candidate;
                if (candidate.parent != null &&
                    string.Equals(candidate.parent.name, "RemoteWeaponTarget", StringComparison.Ordinal))
                {
                    underTarget = candidate;
                }
            }

            return underTarget != null ? underTarget : fallback;
        }

        private static Transform FindExistingWeaponModel(Transform searchRoot, WeaponKind expectedKind)
        {
            if (searchRoot == null)
            {
                return null;
            }

            Transform underTarget = null;
            Transform fallback = null;
            var all = searchRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var candidate = all[i];
                if (candidate == null ||
                    !string.Equals(candidate.name, "WeaponModel", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryGetWeaponRootKind(candidate, out var rootKind))
                {
                    Destroy(candidate.gameObject);
                    continue;
                }

                if (rootKind != expectedKind)
                {
                    Destroy(candidate.gameObject);
                    continue;
                }

                fallback = candidate;
                if (candidate.parent != null &&
                    string.Equals(candidate.parent.name, "RemoteWeaponTarget", StringComparison.Ordinal))
                {
                    underTarget = candidate;
                }
            }

            return underTarget != null ? underTarget : fallback;
        }

        public static Transform EnsureAttachTargetOnHand(
            Transform handBone,
            string targetName = "RemoteWeaponTarget",
            Vector3? localPosition = null,
            Vector3? localEuler = null)
        {
            if (handBone == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(targetName))
            {
                targetName = "RemoteWeaponTarget";
            }

            var existing = handBone.Find(targetName);
            if (existing != null)
            {
                return existing;
            }

            var targetObject = new GameObject(targetName);
            var target = targetObject.transform;
            target.SetParent(handBone, false);
            target.localPosition = localPosition ?? new Vector3(-0.0522f, 0.0951f, 0.0199f);
            target.localRotation = Quaternion.Euler(localEuler ?? new Vector3(-42.105f, 202.324f, -107.611f));
            target.localScale = Vector3.one;
            return target;
        }

        private void EnsureBaseAttachPose()
        {
            if (hasBaseAttachPose || attachTarget == null)
            {
                return;
            }

            baseAttachLocalPosition = attachTarget.localPosition;
            baseAttachLocalRotation = attachTarget.localRotation;
            hasBaseAttachPose = true;
        }

        private void SnapAttachPitchSmoothers()
        {
            smoothedPitchOffset = 0f;
            pitchOffsetVelocity = 0f;
            smoothedCrouchPitchOffset = networkCrouching ? -crouchPitchCompensation : 0f;
            crouchPitchOffsetVelocity = 0f;

            if (enablePostureLeanCompensation && lookPitchPosture != null)
            {
                smoothedPostureLeanCompensation =
                    -lookPitchPosture.CurrentPostureLeanPitch * postureLeanCompensation;
            }
            else
            {
                smoothedPostureLeanCompensation = 0f;
            }

            postureLeanCompensationVelocity = 0f;
        }

        private bool ShouldApplyLookPitchOnAttach()
        {
            return enableLookPitchTilt || lookPitchPosture == null;
        }

        private void ApplyAttachTargetLookPitchTilt()
        {
            if (attachTarget == null)
            {
                return;
            }

            if (lookPitchPosture == null)
            {
                lookPitchPosture = GetComponent<RemoteLookPitchPosture>();
            }

            EnsureBaseAttachPose();
            if (!hasBaseAttachPose)
            {
                return;
            }

            attachTarget.localPosition = baseAttachLocalPosition;
            attachTarget.localRotation = baseAttachLocalRotation;

            var totalWeaponPitch = 0f;

            if (ShouldApplyLookPitchOnAttach() && enableLookPitchTilt)
            {
                var weaponPitchLimit = Mathf.Clamp(lookPitchWeaponMax, 1f, 89f);
                var clampedPitch = Mathf.Clamp(networkLookPitch, -weaponPitchLimit, weaponPitchLimit);
                var targetPitchOffset = clampedPitch * lookPitchInfluence;
                smoothedPitchOffset = Mathf.SmoothDamp(
                    smoothedPitchOffset,
                    targetPitchOffset,
                    ref pitchOffsetVelocity,
                    Mathf.Max(0.01f, lookPitchSmoothTime));
                totalWeaponPitch += smoothedPitchOffset;
            }
            else
            {
                smoothedPitchOffset = Mathf.SmoothDamp(
                    smoothedPitchOffset,
                    0f,
                    ref pitchOffsetVelocity,
                    Mathf.Max(0.01f, lookPitchSmoothTime));
            }

            var targetCrouchOffset = networkCrouching ? -crouchPitchCompensation : 0f;
            smoothedCrouchPitchOffset = Mathf.SmoothDamp(
                smoothedCrouchPitchOffset,
                targetCrouchOffset,
                ref crouchPitchOffsetVelocity,
                Mathf.Max(0.01f, crouchPitchSmoothTime));

            var targetPostureCompensation = 0f;
            if (enablePostureLeanCompensation && lookPitchPosture != null)
            {
                targetPostureCompensation = -lookPitchPosture.CurrentPostureLeanPitch * postureLeanCompensation;
            }

            smoothedPostureLeanCompensation = Mathf.SmoothDamp(
                smoothedPostureLeanCompensation,
                targetPostureCompensation,
                ref postureLeanCompensationVelocity,
                Mathf.Max(0.01f, postureLeanSmoothTime));

            attachTarget.localRotation = baseAttachLocalRotation *
                Quaternion.Euler(
                    totalWeaponPitch + smoothedCrouchPitchOffset + smoothedPostureLeanCompensation,
                    0f,
                    0f);
        }

        public Transform ResolveAttachTarget(Transform body)
        {
            if (body == null)
            {
                return null;
            }

            var syntyVisual = body.Find("SyntyVisual");
            if (syntyVisual == null)
            {
                return null;
            }

            var handBone = FindBone(
                syntyVisual,
                rightHandBoneName,
                "mixamorig1:RightHand",
                "mixamorig:RightHand",
                "RightHand");
            if (handBone == null)
            {
                return null;
            }

            return EnsureAttachTargetOnHand(
                handBone,
                attachTargetName,
                defaultAttachLocalPosition,
                defaultAttachLocalEuler);
        }

        private void AlignWeaponGripToTarget()
        {
            if (weaponRoot == null || attachTarget == null)
            {
                return;
            }

            if (weaponRoot.parent != attachTarget)
            {
                weaponRoot.SetParent(attachTarget, false);
            }

            var grip = FindChildRecursive(weaponRoot, gripTargetName)
                ?? FindChildRecursive(weaponRoot, gripFallbackName);
            if (grip != null)
            {
                weaponRoot.position += attachTarget.position - grip.position;
            }

            weaponRoot.localRotation = Quaternion.identity;
        }

        private void WireRemoteLeftHandIk()
        {
            if (weaponRoot == null)
            {
                return;
            }

            if (thirdPersonBody == null)
            {
                thirdPersonBody = transform.Find("ThirdPersonBody");
            }

            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            var leftGrip = PlayerWeaponMount.FindWeaponGripAnchor(weaponRoot, leftHandTargetName)
                ?? PlayerWeaponMount.FindWeaponGripAnchor(weaponRoot, leftHandTargetFallbackName);
            if (leftGrip == null)
            {
                Debug.LogWarning("[RemoteWeaponPresentation] Left-hand grip target not found on remote weapon.");
                return;
            }

            var handBinder = GetComponent<RemoteLeftHandIkBinder>();
            if (handBinder == null)
            {
                handBinder = gameObject.AddComponent<RemoteLeftHandIkBinder>();
            }

            handBinder.Configure(syntyVisual, leftGrip);
            handBinder.SetHandIkEnabled(networkHasWeapon && !networkHolstered);
        }

        private void ApplyUnarmedPresentation()
        {
            networkHolstered = true;

            if (weaponRoot == null)
            {
                weaponRoot = FindAnyWeaponModelUnderAttachTarget();
                if (weaponRoot == null)
                {
                    weaponRoot = FindAnyExistingWeaponModel(transform);
                }
            }

            if (weaponRoot != null)
            {
                SetWeaponRenderersEnabled(false);
            }

            ResolveHolsterAnimation()?.SetHolstered(true);

            var handBinder = GetComponent<RemoteLeftHandIkBinder>();
            handBinder?.SetHandIkEnabled(false);
        }

        private void SetWeaponRenderersEnabled(bool enabled)
        {
            if (weaponRoot == null)
            {
                return;
            }

            var renderers = weaponRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = enabled;
                }
            }
        }

        private static void EnsureHierarchyActive(Transform node)
        {
            if (node == null)
            {
                return;
            }

            var current = node;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                {
                    current.gameObject.SetActive(true);
                }

                current = current.parent;
            }
        }

        private static Transform FindBone(Transform root, params string[] names)
        {
            if (root == null || names == null)
            {
                return null;
            }

            Transform targetWithAttachPoint = null;
            Transform fallback = null;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (var n = 0; n < names.Length; n++)
            {
                var targetName = names[n];
                if (string.IsNullOrEmpty(targetName))
                {
                    continue;
                }

                for (var i = 0; i < all.Length; i++)
                {
                    var current = all[i];
                    if (current == null ||
                        !IsBoneNameMatch(current.name, targetName))
                    {
                        continue;
                    }

                    fallback ??= current;
                    if (current.Find("RemoteWeaponTarget") != null)
                    {
                        targetWithAttachPoint = current;
                    }
                }
            }

            return targetWithAttachPoint != null ? targetWithAttachPoint : fallback;
        }

        private static bool IsBoneNameMatch(string actualName, string requestedName)
        {
            if (string.IsNullOrWhiteSpace(actualName) || string.IsNullOrWhiteSpace(requestedName))
            {
                return false;
            }

            if (string.Equals(actualName, requestedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var actualCore = ExtractBoneCoreName(actualName);
            var requestedCore = ExtractBoneCoreName(requestedName);
            if (string.Equals(actualCore, requestedCore, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return actualName.EndsWith(":" + requestedCore, StringComparison.OrdinalIgnoreCase) ||
                   requestedName.EndsWith(":" + actualCore, StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractBoneCoreName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var separator = value.LastIndexOf(':');
            if (separator >= 0 && separator < value.Length - 1)
            {
                return value.Substring(separator + 1);
            }

            return value;
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var current = all[i];
                if (current != null && string.Equals(current.name, childName, StringComparison.Ordinal))
                {
                    return current;
                }
            }

            return null;
        }

        private void ApplyDualWeaponPresentation()
        {
            var slot0Occupied = networkSlot0Kind != PlayerWeaponLoadout.EmptySlotKind;
            var slot1Occupied = networkSlot1Kind != PlayerWeaponLoadout.EmptySlotKind;
            if (!slot0Occupied && !slot1Occupied)
            {
                ApplyUnarmedPresentation();
                return;
            }

            if (networkHolstered)
            {
                EnsureBackWeapon(0, networkSlot0Kind);
                EnsureBackWeapon(1, networkSlot1Kind);
                SetWeaponRenderersEnabled(false);
                ResolveHolsterAnimation()?.SetHolstered(true);
                return;
            }

            var activeSlot = ResolveActiveWeaponSlot(slot0Occupied, slot1Occupied);
            var activeKind = activeSlot == 0 ? networkSlot0Kind : networkSlot1Kind;
            if (activeKind == PlayerWeaponLoadout.EmptySlotKind)
            {
                ApplyDualWeaponPresentationHolsteredFallback();
                return;
            }

            networkWeaponKind = WeaponKindUtility.ClampKind((int)activeKind);
            SetRenderersEnabled(backWeaponRootSlot0, false);
            SetRenderersEnabled(backWeaponRootSlot1, false);
            EquipHandWeapon(networkWeaponKind);
            AttachWeaponToHand();
            SetWeaponRenderersEnabled(true);

            var backSlot = activeSlot == 0 ? 1 : 0;
            var backKind = backSlot == 0 ? networkSlot0Kind : networkSlot1Kind;
            if (backKind != PlayerWeaponLoadout.EmptySlotKind)
            {
                EnsureBackWeapon(backSlot, backKind);
            }
            else
            {
                ClearBackWeapon(backSlot);
            }

            ClearBackWeapon(activeSlot);
            ResolveHolsterAnimation()?.SetHolstered(false);
        }

        private int ResolveActiveWeaponSlot(bool slot0Occupied, bool slot1Occupied)
        {
            if (networkHolstered)
            {
                return PlayerWeaponLoadout.NoActiveSlot;
            }

            if (networkActiveWeaponSlot == 0 && slot0Occupied)
            {
                return 0;
            }

            if (networkActiveWeaponSlot == 1 && slot1Occupied)
            {
                return 1;
            }

            if (slot1Occupied && (byte)networkSlot1Kind == (byte)networkWeaponKind)
            {
                return 1;
            }

            if (slot0Occupied && (byte)networkSlot0Kind == (byte)networkWeaponKind)
            {
                return 0;
            }

            if (!slot0Occupied && slot1Occupied)
            {
                return 1;
            }

            if (slot0Occupied)
            {
                return 0;
            }

            return slot1Occupied ? 1 : PlayerWeaponLoadout.NoActiveSlot;
        }

        private void EquipHandWeapon(WeaponKind kind)
        {
            var prefab = ResolvePrefabForKind(kind);
            if (prefab == null)
            {
                return;
            }

            if (networkWeaponKind != kind ||
                weaponPrefab != prefab ||
                weaponRoot == null ||
                !HandWeaponRootMatchesKind(kind))
            {
                networkWeaponKind = kind;
                weaponPrefab = prefab;
                if (weaponRoot != null)
                {
                    Destroy(weaponRoot.gameObject);
                    weaponRoot = null;
                    hasHandPoseSnapshot = false;
                }
            }

            EnsureAttached();
        }

        private void ApplyDualWeaponPresentationHolsteredFallback()
        {
            EnsureBackWeapon(0, networkSlot0Kind);
            EnsureBackWeapon(1, networkSlot1Kind);
            SetWeaponRenderersEnabled(false);
            ResolveHolsterAnimation()?.SetHolstered(true);
        }

        private void EnsureBackWeapon(int slotIndex, byte kindByte)
        {
            if (kindByte == PlayerWeaponLoadout.EmptySlotKind)
            {
                ClearBackWeapon(slotIndex);
                return;
            }

            var kind = WeaponKindUtility.ClampKind((int)kindByte);
            var parent = ResolveBackHolsterTargetForSlot(slotIndex);
            if (parent == null)
            {
                return;
            }

            var prefab = ResolvePrefabForKind(kind);
            if (prefab == null)
            {
                return;
            }

            var existingRoot = slotIndex == 0 ? backWeaponRootSlot0 : backWeaponRootSlot1;
            if (existingRoot != null)
            {
                var existingProfile = existingRoot.GetComponentInChildren<WeaponProfile>(true);
                if (existingProfile != null && existingProfile.Kind == kind)
                {
                    SetRenderersEnabled(existingRoot, true);
                    return;
                }

                Destroy(existingRoot.gameObject);
                if (slotIndex == 0)
                {
                    backWeaponRootSlot0 = null;
                }
                else
                {
                    backWeaponRootSlot1 = null;
                }
            }

            var instance = Instantiate(prefab, parent);
            instance.name = slotIndex == 0 ? "BackWeaponModel0" : "BackWeaponModel1";
            var root = instance.transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale *= ResolveRemoteWeaponScale(kind);
            SetRenderersEnabled(root, true);
            ApplyPresentationSkin(root, kind);
            if (slotIndex == 0)
            {
                backWeaponRootSlot0 = root;
            }
            else
            {
                backWeaponRootSlot1 = root;
            }
        }

        private void ClearBackWeapon(int slotIndex)
        {
            var root = slotIndex == 0 ? backWeaponRootSlot0 : backWeaponRootSlot1;
            if (root == null)
            {
                return;
            }

            Destroy(root.gameObject);
            if (slotIndex == 0)
            {
                backWeaponRootSlot0 = null;
            }
            else
            {
                backWeaponRootSlot1 = null;
            }
        }

        private void ClearBackWeaponRoots()
        {
            ClearBackWeapon(0);
            ClearBackWeapon(1);
        }

        private Transform ResolveBackHolsterTargetForSlot(int slotIndex)
        {
            if (slotIndex == 0)
            {
                ResolveBackHolsterTarget();
                return backHolsterTarget;
            }

            ResolveBackHolsterTargetSecondary();
            return backHolsterTargetSecondary != null ? backHolsterTargetSecondary : backHolsterTarget;
        }

        private void ResolveBackHolsterTargetSecondary()
        {
            if (backHolsterTargetSecondary != null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(backHolsterTargetSecondaryName))
            {
                return;
            }

            var searchRoot = thirdPersonBody != null ? thirdPersonBody : transform;
            var transforms = searchRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var current = transforms[i];
                if (current != null &&
                    string.Equals(current.name, backHolsterTargetSecondaryName, StringComparison.OrdinalIgnoreCase))
                {
                    backHolsterTargetSecondary = current;
                    return;
                }
            }
        }

        private static void SetRenderersEnabled(Transform root, bool enabled)
        {
            if (root == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = enabled;
                }
            }
        }
    }
}
