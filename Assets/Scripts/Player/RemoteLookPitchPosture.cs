using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Applies network look pitch to remote TP spine and arms. Works with or without a mounted weapon.
    /// </summary>
    [DefaultExecutionOrder(400)]
    public sealed class RemoteLookPitchPosture : MonoBehaviour
    {
        [SerializeField] private Transform syntyRoot;
        [SerializeField] private ProceduralLocomotionRig locomotionRig;

        [Header("Pitch")]
        [SerializeField] private float lookPitchMax = 50f;
        [SerializeField] private float pitchSmoothTime = 0.1f;
        [SerializeField] private float crouchPitchCompensation = 22f;
        [SerializeField] private float crouchMovePitchCompensation = 34f;
        [SerializeField] private float crouchMoveSpeedThreshold = 0.08f;
        [SerializeField] private float crouchPitchSmoothTime = 0.12f;

        [Header("Sprint Lean")]
        [SerializeField] private float sprintPitchDegrees = 48f;
        [SerializeField] private float sprintPitchSmoothTime = 0.14f;

        [Header("Spine")]
        [SerializeField] private float spinePitchShare = 0.5f;
        [SerializeField] private float hipsPitchShare = 0.12f;

        [Header("Arms")]
        [SerializeField] private float claviclePitchShare = 0.22f;
        [SerializeField] private float rightShoulderPitchShare = 0.18f;
        [SerializeField] private float leftShoulderPitchShare = 0.12f;

        private Transform hipsBone;
        private Transform[] spineBones;
        private float[] spineWeights;
        private Transform clavicleLeft;
        private Transform clavicleRight;
        private Transform shoulderLeft;
        private Transform shoulderRight;
        private float smoothedPitch;
        private float pitchVelocity;
        private float smoothedCrouchPitch;
        private float crouchPitchVelocity;
        private float smoothedSprintPitch;
        private float sprintPitchVelocity;
        private float directNetworkLookPitch;
        private Quaternion hipsAnimatedBase = Quaternion.identity;
        private readonly Quaternion[] spineAnimatedBases = new Quaternion[3];
        private Quaternion clavicleLeftAnimatedBase = Quaternion.identity;
        private Quaternion clavicleRightAnimatedBase = Quaternion.identity;
        private Quaternion shoulderLeftAnimatedBase = Quaternion.identity;
        private Quaternion shoulderRightAnimatedBase = Quaternion.identity;

        public float CurrentPostureLeanPitch => smoothedCrouchPitch + smoothedSprintPitch;

        public void SetNetworkLookPitch(float lookPitch)
        {
            directNetworkLookPitch = lookPitch;
        }

        public void Configure(Transform thirdPersonBody, ProceduralLocomotionRig rig)
        {
            locomotionRig = rig;
            syntyRoot = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            enabled = syntyRoot != null && locomotionRig != null;
            ResolveBones();
        }

        private void Awake()
        {
            if (locomotionRig == null)
            {
                locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            }

            if (syntyRoot == null)
            {
                var thirdPersonBody = transform.Find("ThirdPersonBody");
                syntyRoot = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            }

            ResolveBones();
        }

        private void LateUpdate()
        {
            if (!ShouldApply() || syntyRoot == null)
            {
                return;
            }

            if (locomotionRig == null)
            {
                locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            }

            ResolveSyntyRoot();
            ResolveBones();

            var clampedPitch = Mathf.Clamp(ResolveNetworkLookPitch(), -lookPitchMax, lookPitchMax);
            smoothedPitch = Mathf.SmoothDamp(
                smoothedPitch,
                clampedPitch,
                ref pitchVelocity,
                Mathf.Max(0.01f, pitchSmoothTime));

            var targetCrouchPitch = ResolveTargetCrouchPitch();
            smoothedCrouchPitch = Mathf.SmoothDamp(
                smoothedCrouchPitch,
                targetCrouchPitch,
                ref crouchPitchVelocity,
                Mathf.Max(0.01f, crouchPitchSmoothTime));

            var targetSprintPitch = ResolveTargetSprintPitch();
            smoothedSprintPitch = Mathf.SmoothDamp(
                smoothedSprintPitch,
                targetSprintPitch,
                ref sprintPitchVelocity,
                Mathf.Max(0.01f, sprintPitchSmoothTime));

            var totalPitch = smoothedPitch + smoothedCrouchPitch + smoothedSprintPitch;
            if (Mathf.Abs(totalPitch) <= 0.01f)
            {
                return;
            }

            CaptureAnimatedBaseRotations();
            ApplySpinePitch(totalPitch);
            ApplyArmPitch(totalPitch);
        }

        private float ResolveTargetCrouchPitch()
        {
            if (locomotionRig == null ||
                !locomotionRig.NetworkCrouching ||
                locomotionRig.CurrentJumpState != 0)
            {
                return 0f;
            }

            if (IsCrouchMoving())
            {
                return Mathf.Abs(crouchMovePitchCompensation);
            }

            return Mathf.Abs(crouchPitchCompensation);
        }

        private float ResolveTargetSprintPitch()
        {
            if (locomotionRig == null ||
                locomotionRig.CurrentJumpState != 0 ||
                locomotionRig.NetworkCrouching ||
                !locomotionRig.NetworkSprinting ||
                !IsLocomoting())
            {
                return 0f;
            }

            return Mathf.Abs(sprintPitchDegrees);
        }

        private bool IsLocomoting()
        {
            if (locomotionRig == null)
            {
                return false;
            }

            if (locomotionRig.GetNetworkAnimSpeed01() > crouchMoveSpeedThreshold)
            {
                return true;
            }

            var moveX = locomotionRig.NetworkMoveInputX;
            var moveZ = locomotionRig.NetworkMoveInputZ;
            return moveX * moveX + moveZ * moveZ > crouchMoveSpeedThreshold * crouchMoveSpeedThreshold;
        }

        private bool IsCrouchMoving()
        {
            return IsLocomoting();
        }

        private bool ShouldApply()
        {
            return GetComponent<RemoteThirdPersonPlayerBootstrap>() != null;
        }

        private float ResolveNetworkLookPitch()
        {
            if (locomotionRig != null)
            {
                return locomotionRig.NetworkLookPitch;
            }

            return directNetworkLookPitch;
        }

        private void ResolveSyntyRoot()
        {
            var thirdPersonBody = transform.Find("ThirdPersonBody");
            var liveRoot = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (liveRoot != null)
            {
                syntyRoot = liveRoot;
            }
        }

        private void CaptureAnimatedBaseRotations()
        {
            if (hipsBone != null)
            {
                hipsAnimatedBase = hipsBone.localRotation;
            }

            if (spineBones != null)
            {
                for (var i = 0; i < spineBones.Length; i++)
                {
                    var bone = spineBones[i];
                    if (bone != null && i < spineAnimatedBases.Length)
                    {
                        spineAnimatedBases[i] = bone.localRotation;
                    }
                }
            }

            if (clavicleLeft != null)
            {
                clavicleLeftAnimatedBase = clavicleLeft.localRotation;
            }

            if (clavicleRight != null)
            {
                clavicleRightAnimatedBase = clavicleRight.localRotation;
            }

            if (shoulderLeft != null)
            {
                shoulderLeftAnimatedBase = shoulderLeft.localRotation;
            }

            if (shoulderRight != null)
            {
                shoulderRightAnimatedBase = shoulderRight.localRotation;
            }
        }

        private void ApplySpinePitch(float totalPitch)
        {
            if (hipsBone != null && hipsPitchShare > 0.0001f)
            {
                var hipsPitch = totalPitch * hipsPitchShare;
                hipsBone.localRotation = hipsAnimatedBase * Quaternion.Euler(hipsPitch, 0f, 0f);
            }

            if (spineBones == null || spineWeights == null)
            {
                return;
            }

            var spinePitch = totalPitch * spinePitchShare;
            for (var i = 0; i < spineBones.Length; i++)
            {
                var bone = spineBones[i];
                if (bone == null)
                {
                    continue;
                }

                var weight = i < spineWeights.Length ? spineWeights[i] : 1f;
                var pitch = spinePitch * weight;
                if (Mathf.Abs(pitch) <= 0.01f)
                {
                    continue;
                }

                var animatedBase = i < spineAnimatedBases.Length ? spineAnimatedBases[i] : bone.localRotation;
                bone.localRotation = animatedBase * Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        private void ApplyArmPitch(float totalPitch)
        {
            ApplyBonePitch(clavicleLeft, clavicleLeftAnimatedBase, totalPitch * claviclePitchShare);
            ApplyBonePitch(clavicleRight, clavicleRightAnimatedBase, totalPitch * claviclePitchShare);
            ApplyBonePitch(shoulderLeft, shoulderLeftAnimatedBase, totalPitch * leftShoulderPitchShare);
            ApplyBonePitch(shoulderRight, shoulderRightAnimatedBase, totalPitch * rightShoulderPitchShare);
        }

        private static void ApplyBonePitch(Transform bone, Quaternion animatedBase, float pitch)
        {
            if (bone == null || Mathf.Abs(pitch) <= 0.01f)
            {
                return;
            }

            bone.localRotation = animatedBase * Quaternion.Euler(pitch, 0f, 0f);
        }

        private void ResolveBones()
        {
            if (syntyRoot == null)
            {
                return;
            }

            hipsBone = FindBestBone(syntyRoot, "Hips", "pelvis");

            spineBones = new[]
            {
                FindBestBone(syntyRoot, "Spine", "Spine_01"),
                FindBestBone(syntyRoot, "Spine1", "Spine_02"),
                FindBestBone(syntyRoot, "Spine2", "Spine_03")
            };
            spineWeights = new[] { 0.22f, 0.52f, 0.26f };

            clavicleLeft = FindBestBone(syntyRoot, "Clavicle_L", "LeftShoulder");
            clavicleRight = FindBestBone(syntyRoot, "Clavicle_R", "RightShoulder");
            shoulderLeft = FindBestBone(syntyRoot, "Shoulder_L", "LeftShoulder");
            shoulderRight = FindBestBone(syntyRoot, "Shoulder_R", "RightShoulder");
        }

        private static Transform FindBestBone(Transform root, params string[] names)
        {
            if (root == null || names == null || names.Length == 0)
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            Transform best = null;
            var bestScore = int.MinValue;
            for (var n = 0; n < names.Length; n++)
            {
                var targetName = names[n];
                if (string.IsNullOrWhiteSpace(targetName))
                {
                    continue;
                }

                for (var i = 0; i < all.Length; i++)
                {
                    var current = all[i];
                    if (current == null || !IsBoneNameMatch(current.name, targetName))
                    {
                        continue;
                    }

                    var score = current.gameObject.activeInHierarchy ? 1000 : 0;
                    if (current.gameObject.activeSelf)
                    {
                        score += 500;
                    }

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

            if (string.Equals(actualName, requestedName, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var actualCore = ExtractBoneCoreName(actualName);
            var requestedCore = ExtractBoneCoreName(requestedName);
            if (string.Equals(actualCore, requestedCore, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return actualName.EndsWith(":" + requestedCore, System.StringComparison.OrdinalIgnoreCase) ||
                   requestedName.EndsWith(":" + actualCore, System.StringComparison.OrdinalIgnoreCase);
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
