using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Left-hand IK for remote third-person players only. Keeps FP hand binding untouched.
    /// </summary>
    [DefaultExecutionOrder(920)]
    public sealed class RemoteLeftHandIkBinder : MonoBehaviour
    {
        [Header("Bones")]
        [SerializeField] private Transform leftShoulder;
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform leftLowerArm;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform facingRoot;

        [Header("Grip")]
        [SerializeField] private Transform leftGripTarget;
        [SerializeField] private string leftHandTargetName = "RemoteLeftHandTarget";
        [SerializeField] private string leftHandTargetFallbackName = "LeftHandTarget";

        [Header("IK")]
        [SerializeField] private float handIkWeight = 1f;
        [SerializeField] private bool rotateShoulder = true;
        [SerializeField] private int positionSolveIterations = 4;
        [SerializeField] private float maxReachRotationWeight = 0.05f;
        [SerializeField] private bool applyIkBeforeRender = false;

        private RemoteWeaponPresentation weaponPresentation;
        private RemoteThirdPersonPlayerBootstrap remoteBootstrap;
        private bool hasValidatedArmChain;
        private bool hasBindSegmentLengths;
        private int lastIkAppliedFrame = -1;
        private readonly List<Transform> ikChain = new List<Transform>(4);
        private readonly float[] segmentLengths = new float[3];
        private readonly Vector3[] fabrikPositionScratch = new Vector3[4];
        private readonly Quaternion[] chainRotationScratch = new Quaternion[4];
        private bool hasCachedHandIkPose;
        private Quaternion cachedShoulderRotation;
        private Quaternion cachedUpperArmRotation;
        private Quaternion cachedLowerArmRotation;
        private Quaternion cachedHandRotation;

        public void Configure(Transform syntyVisualRoot, Transform leftTarget)
        {
            leftGripTarget = leftTarget;

            if (syntyVisualRoot != null)
            {
                ConfigureFromVisualRoot(syntyVisualRoot);
            }
        }

        public void SetHandIkEnabled(bool enabled)
        {
            this.enabled = enabled;
            if (!enabled)
            {
                hasCachedHandIkPose = false;
            }
        }

        public void ConfigureFromVisualRoot(Transform syntyRoot)
        {
            if (syntyRoot == null)
            {
                return;
            }

            facingRoot = FindBestBone(syntyRoot, "mixamorig1:Hips", "mixamorig:Hips", "Hips", "Spine")
                ?? syntyRoot;
            hasValidatedArmChain = false;
            hasBindSegmentLengths = false;
            ResolveArmChainFromSyntyRoot(syntyRoot);
            RebuildIkChain();
            CacheSegmentLengths(forceRecache: true);
            hasValidatedArmChain = IsValidArmChain(leftUpperArm, leftLowerArm, leftHand);
        }

        private int ikFrameOffset;

        private void Awake()
        {
            weaponPresentation = GetComponent<RemoteWeaponPresentation>();
            remoteBootstrap = GetComponent<RemoteThirdPersonPlayerBootstrap>();
            ikFrameOffset = GetInstanceID() & 3;
        }

        private void OnEnable()
        {
            Application.onBeforeRender -= HandleBeforeRender;
            if (applyIkBeforeRender)
            {
                Application.onBeforeRender += HandleBeforeRender;
            }
        }

        private bool ShouldRunFullIkSolveThisFrame()
        {
            var interval = Mathf.Max(1, GameplayPerformanceOptions.BotPresentationSolveIntervalFrames);
            return (Time.frameCount + ikFrameOffset) % interval == 0;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= HandleBeforeRender;
        }

        private void LateUpdate()
        {
            if (remoteBootstrap == null)
            {
                return;
            }

            ApplyLeftHandIkIfNeeded();
        }

        private void HandleBeforeRender()
        {
            if (!applyIkBeforeRender)
            {
                return;
            }

            ApplyLeftHandIkIfNeeded();
        }

        private void ApplyLeftHandIkIfNeeded()
        {
            if (remoteBootstrap == null)
            {
                return;
            }

            if (!ShouldApplyHandIkThisFrame())
            {
                return;
            }

            if (Time.frameCount == lastIkAppliedFrame)
            {
                return;
            }

            lastIkAppliedFrame = Time.frameCount;

            if (!PrepareHandIkContext())
            {
                hasCachedHandIkPose = false;
                return;
            }

            if (ShouldRunFullIkSolveThisFrame())
            {
                ApplyLeftHandIk();
                CacheHandIkPose();
                return;
            }

            if (hasCachedHandIkPose)
            {
                ApplyCachedHandIkPose();
            }
        }

        private bool PrepareHandIkContext()
        {
            if (handIkWeight <= 0.0001f)
            {
                return false;
            }

            if (weaponPresentation == null)
            {
                weaponPresentation = GetComponent<RemoteWeaponPresentation>();
            }

            if (weaponPresentation != null &&
                (!weaponPresentation.HasWeapon || weaponPresentation.IsHolstered))
            {
                return false;
            }

            EnsureArmChain();
            if (leftUpperArm == null || leftLowerArm == null || leftHand == null || ikChain.Count < 3)
            {
                return false;
            }

            ResolveLeftGripTarget();
            return leftGripTarget != null;
        }

        private void CacheHandIkPose()
        {
            if (rotateShoulder && leftShoulder != null)
            {
                cachedShoulderRotation = leftShoulder.rotation;
            }

            if (leftUpperArm != null)
            {
                cachedUpperArmRotation = leftUpperArm.rotation;
            }

            if (leftLowerArm != null)
            {
                cachedLowerArmRotation = leftLowerArm.rotation;
            }

            if (leftHand != null)
            {
                cachedHandRotation = leftHand.rotation;
            }

            hasCachedHandIkPose = true;
        }

        private void ApplyCachedHandIkPose()
        {
            if (!hasCachedHandIkPose)
            {
                return;
            }

            if (rotateShoulder && leftShoulder != null)
            {
                leftShoulder.rotation = cachedShoulderRotation;
            }

            if (leftUpperArm != null)
            {
                leftUpperArm.rotation = cachedUpperArmRotation;
            }

            if (leftLowerArm != null)
            {
                leftLowerArm.rotation = cachedLowerArmRotation;
            }

            if (leftHand != null)
            {
                leftHand.rotation = cachedHandRotation;
            }
        }

        private bool ShouldApplyHandIkThisFrame()
        {
            return EnemyPresentationVisibilityUtility.IsPresentationActive(gameObject);
        }

        private void ApplyLeftHandIk()
        {
            EnsureFacingRoot();
            RebuildIkChain();
            CacheSegmentLengths(forceRecache: !hasBindSegmentLengths);

            var targetPosition = leftGripTarget.position;
            var weight = Mathf.Clamp01(handIkWeight);
            var oldHandRot = leftHand.rotation;
            CaptureChainRotations();

            SolveHandPositionIk(targetPosition);

            var reachError = Vector3.Distance(leftHand.position, targetPosition);
            var rotationWeight = ComputeHandRotationWeight(reachError, weight);
            var targetRotation = leftGripTarget.rotation;
            leftHand.rotation = rotationWeight <= 0.0001f
                ? oldHandRot
                : Quaternion.Slerp(oldHandRot, targetRotation, rotationWeight);

            if (weight < 0.999f)
            {
                RestoreChainRotations(weight);
                leftHand.rotation = Quaternion.Slerp(oldHandRot, leftHand.rotation, weight);
            }
        }

        private void SolveHandPositionIk(Vector3 targetPosition)
        {
            if (rotateShoulder && leftShoulder != null && leftUpperArm != null)
            {
                RotateShoulderTowardTarget(targetPosition);
            }

            var iterations = Mathf.Clamp(
                Mathf.Min(positionSolveIterations, GameplayPerformanceOptions.EnemyHandIkPositionSolveIterations),
                1,
                12);
            var poleHint = ComputeElbowPoleHint(targetPosition);
            SolveFabrikChain(targetPosition, poleHint, iterations);
        }

        private void RotateShoulderTowardTarget(Vector3 targetPosition)
        {
            var shoulderPos = leftShoulder.position;
            var toTarget = targetPosition - shoulderPos;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var currentUpperDir = leftUpperArm.position - shoulderPos;
            if (currentUpperDir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            leftShoulder.rotation = Quaternion.FromToRotation(currentUpperDir, toTarget) * leftShoulder.rotation;
        }

        private void SolveFabrikChain(Vector3 targetPosition, Vector3 poleHint, int iterations)
        {
            var jointCount = ikChain.Count;
            if (jointCount < 3)
            {
                return;
            }

            var segmentCount = jointCount - 1;
            var rootPos = ikChain[0].position;
            var positions = fabrikPositionScratch;
            for (var i = 0; i < jointCount; i++)
            {
                positions[i] = ikChain[i].position;
            }

            var totalLength = 0f;
            for (var i = 0; i < segmentCount; i++)
            {
                if (segmentLengths[i] <= 0.0001f)
                {
                    segmentLengths[i] = Vector3.Distance(positions[i], positions[i + 1]);
                }

                totalLength += segmentLengths[i];
            }

            var rootToTarget = targetPosition - rootPos;
            var targetDistance = rootToTarget.magnitude;
            var reachableTarget = targetPosition;
            if (targetDistance > totalLength - 0.001f && targetDistance > 0.0001f)
            {
                reachableTarget = rootPos + rootToTarget.normalized * (totalLength - 0.001f);
            }

            for (var iteration = 0; iteration < iterations; iteration++)
            {
                positions[jointCount - 1] = reachableTarget;

                for (var i = jointCount - 2; i >= 0; i--)
                {
                    var direction = positions[i] - positions[i + 1];
                    if (direction.sqrMagnitude < 0.0001f)
                    {
                        direction = i > 0
                            ? positions[i] - positions[i - 1]
                            : Vector3.up;
                    }

                    positions[i] = positions[i + 1] + direction.normalized * segmentLengths[i];
                }

                positions[0] = rootPos;

                for (var i = 0; i < jointCount - 1; i++)
                {
                    var direction = positions[i + 1] - positions[i];
                    if (direction.sqrMagnitude < 0.0001f)
                    {
                        direction = i < jointCount - 2
                            ? positions[i + 2] - positions[i]
                            : reachableTarget - positions[i];
                    }

                    positions[i + 1] = positions[i] + direction.normalized * segmentLengths[i];
                }

                if (jointCount >= 3)
                {
                    ApplyElbowPoleBias(positions, poleHint, rootPos, reachableTarget, jointCount);
                }
            }

            ApplyChainRotations(positions);
        }

        private void ApplyChainRotations(Vector3[] targetPositions)
        {
            for (var i = 0; i < ikChain.Count - 1; i++)
            {
                var bone = ikChain[i];
                var currentDirection = ikChain[i + 1].position - bone.position;
                var desiredDirection = targetPositions[i + 1] - targetPositions[i];
                if (currentDirection.sqrMagnitude < 0.0001f || desiredDirection.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                bone.rotation = Quaternion.FromToRotation(currentDirection, desiredDirection) * bone.rotation;
            }
        }

        private static void ApplyElbowPoleBias(
            Vector3[] positions,
            Vector3 poleHint,
            Vector3 rootPos,
            Vector3 targetPos,
            int jointCount)
        {
            if (positions.Length < 3)
            {
                return;
            }

            var rootToTarget = targetPos - rootPos;
            if (rootToTarget.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var planeNormal = Vector3.Cross(rootToTarget, poleHint - rootPos);
            if (planeNormal.sqrMagnitude < 0.0001f)
            {
                return;
            }

            planeNormal.Normalize();
            var elbowIndex = jointCount >= 4 ? 2 : 1;
            var elbowOffset = positions[elbowIndex] - rootPos;
            var projectedElbow = rootPos + Vector3.ProjectOnPlane(elbowOffset, planeNormal);
            if ((projectedElbow - rootPos).sqrMagnitude > 0.0001f)
            {
                var desiredOffset = poleHint - rootPos;
                desiredOffset = Vector3.ProjectOnPlane(desiredOffset, rootToTarget.normalized);
                if (desiredOffset.sqrMagnitude > 0.0001f)
                {
                    var currentOffset = projectedElbow - rootPos;
                    var blendedOffset = Vector3.Slerp(
                        currentOffset.normalized,
                        desiredOffset.normalized,
                        0.35f);
                    var elbowDistance = elbowOffset.magnitude;
                    positions[elbowIndex] = rootPos + blendedOffset.normalized * elbowDistance;
                }
            }
        }

        private void CaptureChainRotations()
        {
            for (var i = 0; i < ikChain.Count; i++)
            {
                chainRotationScratch[i] = ikChain[i].rotation;
            }
        }

        private void RestoreChainRotations(float weight)
        {
            for (var i = 0; i < ikChain.Count; i++)
            {
                ikChain[i].rotation = Quaternion.Slerp(chainRotationScratch[i], ikChain[i].rotation, weight);
            }
        }

        private float ComputeHandRotationWeight(float reachError, float weight)
        {
            if (weight <= 0.0001f)
            {
                return 0f;
            }

            var reachLimit = Mathf.Max(0.01f, maxReachRotationWeight);
            if (reachError <= reachLimit)
            {
                return weight;
            }

            var fadeRange = 0.12f;
            var reachBlend = 1f - Mathf.Clamp01((reachError - reachLimit) / fadeRange);
            return weight * reachBlend;
        }

        private void EnsureArmChain()
        {
            if (hasValidatedArmChain &&
                leftShoulder != null &&
                AreBonesAlive(leftShoulder, leftUpperArm, leftLowerArm, leftHand) &&
                IsValidArmChain(leftUpperArm, leftLowerArm, leftHand))
            {
                return;
            }

            var thirdPersonBody = transform.Find("ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            ResolveArmChainFromSyntyRoot(syntyVisual);
            hasValidatedArmChain = leftShoulder != null &&
                                   IsValidArmChain(leftUpperArm, leftLowerArm, leftHand);
        }

        private static bool AreBonesAlive(params Transform[] bones)
        {
            for (var i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null || !bone.gameObject.scene.IsValid())
                {
                    return false;
                }
            }

            return true;
        }

        private void RebuildIkChain()
        {
            ikChain.Clear();

            if (rotateShoulder &&
                leftShoulder != null &&
                leftUpperArm != null &&
                leftLowerArm != null &&
                leftHand != null &&
                leftUpperArm.IsChildOf(leftShoulder))
            {
                ikChain.Add(leftShoulder);
            }

            if (leftUpperArm != null)
            {
                ikChain.Add(leftUpperArm);
            }

            if (leftLowerArm != null)
            {
                ikChain.Add(leftLowerArm);
            }

            if (leftHand != null)
            {
                ikChain.Add(leftHand);
            }
        }

        private void CacheSegmentLengths(bool forceRecache = false)
        {
            if (hasBindSegmentLengths && !forceRecache)
            {
                return;
            }

            for (var i = 0; i < segmentLengths.Length; i++)
            {
                segmentLengths[i] = 0f;
            }

            for (var i = 0; i < ikChain.Count - 1 && i < segmentLengths.Length; i++)
            {
                segmentLengths[i] = Vector3.Distance(ikChain[i].position, ikChain[i + 1].position);
            }

            hasBindSegmentLengths = ikChain.Count >= 3;
        }

        private static bool IsValidArmChain(Transform upper, Transform lower, Transform hand)
        {
            if (upper == null || lower == null || hand == null)
            {
                return false;
            }

            if (lower.parent != upper || hand.parent != lower)
            {
                return false;
            }

            if (IsBoneNameMatch(upper.name, "UpperArm_L") ||
                IsBoneNameMatch(upper.name, "LowerArm_L") ||
                IsBoneNameMatch(upper.name, "LeftArm") ||
                IsBoneNameMatch(upper.name, "LeftForeArm"))
            {
                return true;
            }

            return !IsBoneNameMatch(upper.name, "Shoulder_L") &&
                   !IsBoneNameMatch(upper.name, "Clavicle_L") &&
                   !IsBoneNameMatch(upper.name, "LeftShoulder");
        }

        private void ResolveArmChainFromSyntyRoot(Transform syntyRoot)
        {
            leftHand = null;
            leftLowerArm = null;
            leftUpperArm = null;
            leftShoulder = null;

            var hand = FindHandBoneFromBodyMesh(syntyRoot)
                ?? FindBestBone(
                    syntyRoot,
                    "mixamorig1:LeftHand",
                    "mixamorig:LeftHand",
                    "LeftHand",
                    "Hand_L");
            var lower = FindBestBone(
                syntyRoot,
                "mixamorig1:LeftForeArm",
                "mixamorig:LeftForeArm",
                "LeftForeArm",
                "LowerArm_L",
                "Elbow_L");
            var upper = FindBestBone(
                syntyRoot,
                "mixamorig1:LeftArm",
                "mixamorig:LeftArm",
                "LeftArm",
                "UpperArm_L");

            if (hand == null)
            {
                return;
            }

            if (lower == null)
            {
                lower = hand.parent;
            }

            if (upper == null && lower != null)
            {
                upper = lower.parent;
            }

            var explicitUpperArm = FindBestBone(syntyRoot, "UpperArm_L", "mixamorig1:LeftArm", "mixamorig:LeftArm", "LeftArm");
            if (explicitUpperArm != null &&
                lower != null &&
                (lower.parent == explicitUpperArm || lower.IsChildOf(explicitUpperArm)))
            {
                upper = explicitUpperArm;
            }

            if (lower != null && hand.parent != lower)
            {
                lower = hand.parent;
            }

            if (upper != null && lower != null && lower.parent != upper)
            {
                upper = lower.parent;
            }

            if (upper != null &&
                (IsBoneNameMatch(upper.name, "Shoulder_L") ||
                 IsBoneNameMatch(upper.name, "Clavicle_L") ||
                 IsBoneNameMatch(upper.name, "LeftShoulder")))
            {
                var upperArm = FindBestBone(
                    syntyRoot,
                    "UpperArm_L",
                    "mixamorig1:LeftArm",
                    "mixamorig:LeftArm",
                    "LeftArm");
                if (upperArm != null && lower != null && lower.IsChildOf(upperArm))
                {
                    upper = upperArm;
                }
            }

            leftHand = hand;
            leftLowerArm = lower;
            leftUpperArm = upper;
            leftShoulder = FindBestBone(
                syntyRoot,
                "mixamorig1:LeftShoulder",
                "mixamorig:LeftShoulder",
                "LeftShoulder",
                "Shoulder_L")
                ?? ResolveShoulderBone(upper);

            if (leftShoulder == null && upper != null && upper.parent != null &&
                IsBoneNameMatch(upper.parent.name, "LeftShoulder"))
            {
                leftShoulder = upper.parent;
            }
        }

        private static Transform FindHandBoneFromBodyMesh(Transform syntyRoot)
        {
            var bodyRenderer = FindPrimaryBodyRenderer(syntyRoot);
            if (bodyRenderer == null || bodyRenderer.bones == null)
            {
                return null;
            }

            Transform best = null;
            var bestScore = int.MinValue;
            for (var i = 0; i < bodyRenderer.bones.Length; i++)
            {
                var bone = bodyRenderer.bones[i];
                if (bone == null || !IsBoneNameMatch(bone.name, "LeftHand"))
                {
                    continue;
                }

                var score = ScoreBone(bone);
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                best = bone;
            }

            return best;
        }

        private static SkinnedMeshRenderer FindPrimaryBodyRenderer(Transform syntyRoot)
        {
            if (syntyRoot == null)
            {
                return null;
            }

            var skinnedMeshes = syntyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer best = null;
            var bestScore = int.MinValue;
            for (var i = 0; i < skinnedMeshes.Length; i++)
            {
                var candidate = skinnedMeshes[i];
                if (candidate == null ||
                    candidate.sharedMesh == null ||
                    IsFirstPersonArmsName(candidate.gameObject.name))
                {
                    continue;
                }

                var objectName = candidate.gameObject.name;
                if (objectName.StartsWith("SM_Char_Attach", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var score = candidate.sharedMesh.vertexCount;
                if (candidate.gameObject.activeInHierarchy)
                {
                    score += 1000;
                }

                if (candidate.enabled)
                {
                    score += 500;
                }

                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                best = candidate;
            }

            return best;
        }

        private static bool IsFirstPersonArmsName(string objectName)
        {
            return !string.IsNullOrWhiteSpace(objectName) &&
                   objectName.IndexOf("_FirstPersonArms", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ScoreBone(Transform bone)
        {
            var score = 0;
            if (bone.gameObject.activeInHierarchy)
            {
                score += 1000;
            }

            if (bone.gameObject.activeSelf)
            {
                score += 500;
            }

            return score;
        }

        private static Transform ResolveShoulderBone(Transform upperArm)
        {
            if (upperArm == null || upperArm.parent == null)
            {
                return null;
            }

            var parent = upperArm.parent;
            if (IsBoneNameMatch(parent.name, "Shoulder_L") ||
                IsBoneNameMatch(parent.name, "LeftShoulder"))
            {
                return parent;
            }

            if (IsBoneNameMatch(parent.name, "Clavicle_L"))
            {
                var shoulder = parent.Find("Shoulder_L");
                return shoulder != null ? shoulder : parent;
            }

            return null;
        }

        private Vector3 ComputeElbowPoleHint(Vector3 gripPosition)
        {
            var root = ikChain.Count > 0 ? ikChain[0].position : leftUpperArm.position;
            var characterLeft = ResolveCharacterLeft();
            if (characterLeft.sqrMagnitude > 0.0001f)
            {
                return root + characterLeft.normalized * 0.35f;
            }

            return root + Vector3.left * 0.35f;
        }

        private Vector3 ResolveCharacterLeft()
        {
            if (facingRoot != null)
            {
                return -facingRoot.right;
            }

            return -transform.right;
        }

        private void EnsureFacingRoot()
        {
            if (facingRoot != null)
            {
                return;
            }

            var thirdPersonBody = transform.Find("ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            facingRoot = FindBestBone(syntyVisual, "mixamorig1:Hips", "mixamorig:Hips", "Hips", "Spine")
                ?? syntyVisual;
        }

        private void ResolveLeftGripTarget()
        {
            if (weaponPresentation == null)
            {
                weaponPresentation = GetComponent<RemoteWeaponPresentation>();
            }

            if (weaponPresentation != null &&
                (!weaponPresentation.HasWeapon || weaponPresentation.IsHolstered))
            {
                leftGripTarget = null;
                return;
            }

            var weaponRoot = weaponPresentation != null ? weaponPresentation.WeaponRoot : null;
            if (weaponRoot == null)
            {
                return;
            }

            var resolved = PlayerWeaponMount.FindWeaponGripAnchor(weaponRoot, leftHandTargetName)
                ?? PlayerWeaponMount.FindWeaponGripAnchor(weaponRoot, leftHandTargetFallbackName);
            if (resolved != null)
            {
                leftGripTarget = resolved;
            }
        }

        private static Transform FindBestBone(Transform root, params string[] boneNames)
        {
            if (root == null || boneNames == null || boneNames.Length == 0)
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            Transform best = null;
            var bestScore = int.MinValue;
            for (var n = 0; n < boneNames.Length; n++)
            {
                var boneName = boneNames[n];
                if (string.IsNullOrWhiteSpace(boneName))
                {
                    continue;
                }

                for (var i = 0; i < all.Length; i++)
                {
                    var current = all[i];
                    if (current == null || !IsBoneNameMatch(current.name, boneName))
                    {
                        continue;
                    }

                    var score = ScoreBone(current);
                    if (score <= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    best = current;
                }
            }

            return best;
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

            var separatorIndex = value.LastIndexOf(':');
            if (separatorIndex >= 0 && separatorIndex < value.Length - 1)
            {
                return value.Substring(separatorIndex + 1);
            }

            return value;
        }
    }
}
