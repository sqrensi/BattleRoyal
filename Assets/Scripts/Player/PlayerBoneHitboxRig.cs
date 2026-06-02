using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerBoneHitboxRig : MonoBehaviour
    {
        private const string HitboxChildPrefix = "BoneHitbox_";

        [Header("Build")]
        [SerializeField] private bool autoBuildOnAwake = true;
        [SerializeField] private bool preserveBakedHitboxesAtRuntime = true;
        [SerializeField] private Transform syntyRoot;
        [SerializeField] private bool setAsTrigger = true;
        [SerializeField] private int hitboxLayer = -1;
        [SerializeField] private bool enableArmHitboxes;

        [Header("Radii")]
        [SerializeField] private float headRadius = 0.13f;
        [SerializeField] private float headCenterOffset = 0.045f;
        [SerializeField] private float neckRadius = 0.06f;
        [SerializeField] private float neckHeight = 0.16f;
        [SerializeField] private float torsoRadius = 0.11f;
        [SerializeField] private float torsoHeight = 0.3f;
        [SerializeField] private float hipsRadius = 0.12f;
        [SerializeField] private float hipsHeight = 0.23f;
        [SerializeField] private float upperLegRadius = 0.07f;
        [SerializeField] private float upperLegHeight = 0.41f;
        [SerializeField] private float lowerLegRadius = 0.06f;
        [SerializeField] private float lowerLegHeight = 0.39f;
        [SerializeField] private float footRadius = 0.07f;
        [SerializeField] private float footHeight = 0.18f;
        [SerializeField] private float armRadius = 0.055f;
        [SerializeField] private float upperArmHeight = 0.27f;
        [SerializeField] private float lowerArmHeight = 0.25f;

        private void Awake()
        {
            PlayerHitboxCleanup.RemoveLegacyLineHitboxes(gameObject);
            ResolveSyntyRoot();
            if (autoBuildOnAwake && GetComponent<RemoteThirdPersonPlayerBootstrap>() == null)
            {
                BuildOrRefreshHitboxes();
            }
        }

        [ContextMenu("Build/Refresh Bone Hitboxes")]
        public void BuildOrRefreshHitboxes()
        {
            BuildOrRefreshHitboxes(forceRebuild: false);
        }

        public void BuildOrRefreshHitboxes(bool forceRebuild)
        {
            ResolveSyntyRoot();
            if (syntyRoot == null)
            {
                return;
            }

            RemoveLegacyLineHitboxes();
            if (!forceRebuild && preserveBakedHitboxesAtRuntime && HasActiveHitboxes())
            {
                RefreshExistingHitboxLayers();
                return;
            }

            RemoveHitboxes();
            BuildHead();
            BuildNeck();
            BuildTorso();
            BuildLegs();
            if (enableArmHitboxes)
            {
                BuildArms();
            }
            else
            {
                DisableArmHitboxes();
            }
        }

        public bool HasActiveHitboxes()
        {
            if (syntyRoot == null)
            {
                return false;
            }

            var markers = syntyRoot.GetComponentsInChildren<PlayerBoneHitbox>(true);
            for (var i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                if (marker == null)
                {
                    continue;
                }

                var collider = marker.GetComponent<Collider>();
                if (collider != null && collider.enabled)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshExistingHitboxLayers()
        {
            if (syntyRoot == null)
            {
                return;
            }

            var markers = syntyRoot.GetComponentsInChildren<PlayerBoneHitbox>(true);
            for (var i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                if (marker == null)
                {
                    continue;
                }

                var collider = marker.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.isTrigger = setAsTrigger;
                    collider.enabled = true;
                }

                ApplyColliderLayer(marker.gameObject);
            }
        }

        [ContextMenu("Remove Bone Hitboxes")]
        public void RemoveHitboxes()
        {
            if (syntyRoot == null)
            {
                return;
            }

            var markers = syntyRoot.GetComponentsInChildren<PlayerBoneHitbox>(true);
            for (var i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                if (marker == null)
                {
                    continue;
                }

                var hitboxTransform = marker.transform;
                if (Application.isPlaying)
                {
                    Destroy(hitboxTransform.gameObject);
                }
                else
                {
                    DestroyImmediate(hitboxTransform.gameObject);
                }
            }
        }

        public void Configure(Transform visualRoot)
        {
            syntyRoot = visualRoot;
        }

        private void ResolveSyntyRoot()
        {
            var thirdPersonBody = transform.Find("ThirdPersonBody");
            var visual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (visual != null)
            {
                syntyRoot = visual;
            }
        }

        private void RemoveLegacyLineHitboxes()
        {
            PlayerHitboxCleanup.RemoveLegacyLineHitboxes(gameObject);
        }

        private void BuildHead()
        {
            var bone = FindBone("Head");
            EnsureSphereHitbox(bone, "Head", PlayerBoneHitZone.Head, headRadius, new Vector3(0f, headCenterOffset, 0f));
        }

        private void BuildNeck()
        {
            var bone = FindBone("Neck", "Neck_M");
            EnsureCapsuleHitbox(bone, "Neck", PlayerBoneHitZone.Neck, neckRadius, neckHeight);
        }

        private void BuildTorso()
        {
            var spine = FindBone("Spine2", "Spine1", "Spine_02", "Spine_01", "Spine_03", "Spine");
            EnsureCapsuleHitbox(spine, "Torso", PlayerBoneHitZone.Body, torsoRadius, torsoHeight);

            var hips = FindBone("Hips", "pelvis");
            EnsureCapsuleHitbox(hips, "Hips", PlayerBoneHitZone.Body, hipsRadius, hipsHeight);
        }

        private void BuildLegs()
        {
            var leftUpperLeg = FindBone("LeftUpLeg", "Thigh_L", "UpperLeg_L");
            EnsureCapsuleHitbox(leftUpperLeg, "LeftUpperLeg", PlayerBoneHitZone.Leg, upperLegRadius, upperLegHeight);

            var rightUpperLeg = FindBone("RightUpLeg", "Thigh_R", "UpperLeg_R");
            EnsureCapsuleHitbox(rightUpperLeg, "RightUpperLeg", PlayerBoneHitZone.Leg, upperLegRadius, upperLegHeight);

            var leftLowerLeg = FindBone("LeftLeg", "Shin_L", "Knee_L", "LowerLeg_L");
            EnsureCapsuleHitbox(leftLowerLeg, "LeftLowerLeg", PlayerBoneHitZone.Leg, lowerLegRadius, lowerLegHeight);

            var rightLowerLeg = FindBone("RightLeg", "Shin_R", "Knee_R", "LowerLeg_R");
            EnsureCapsuleHitbox(rightLowerLeg, "RightLowerLeg", PlayerBoneHitZone.Leg, lowerLegRadius, lowerLegHeight);

            var leftFoot = FindBone("LeftFoot", "LeftToeBase", "Foot_L", "Ball_L", "Toes_L");
            EnsureCapsuleHitbox(leftFoot, "LeftFoot", PlayerBoneHitZone.Leg, footRadius, footHeight);

            var rightFoot = FindBone("RightFoot", "RightToeBase", "Foot_R", "Ball_R", "Toes_R");
            EnsureCapsuleHitbox(rightFoot, "RightFoot", PlayerBoneHitZone.Leg, footRadius, footHeight);
        }

        private void BuildArms()
        {
            var leftUpperArm = FindBone("LeftArm", "UpperArm_L", "Shoulder_L");
            EnsureCapsuleHitbox(leftUpperArm, "LeftUpperArm", PlayerBoneHitZone.Body, armRadius, upperArmHeight);

            var rightUpperArm = FindBone("RightArm", "UpperArm_R", "Shoulder_R");
            EnsureCapsuleHitbox(rightUpperArm, "RightUpperArm", PlayerBoneHitZone.Body, armRadius, upperArmHeight);

            var leftLowerArm = FindBone("LeftForeArm", "Elbow_L", "LowerArm_L");
            EnsureCapsuleHitbox(leftLowerArm, "LeftLowerArm", PlayerBoneHitZone.Body, armRadius, lowerArmHeight);

            var rightLowerArm = FindBone("RightForeArm", "Elbow_R", "LowerArm_R");
            EnsureCapsuleHitbox(rightLowerArm, "RightLowerArm", PlayerBoneHitZone.Body, armRadius, lowerArmHeight);
        }

        private void DisableArmHitboxes()
        {
            DisableNamedHitbox("LeftUpperArm");
            DisableNamedHitbox("RightUpperArm");
            DisableNamedHitbox("LeftLowerArm");
            DisableNamedHitbox("RightLowerArm");
        }

        private void DisableNamedHitbox(string suffix)
        {
            if (syntyRoot == null)
            {
                return;
            }

            var hitboxName = HitboxChildPrefix + suffix;
            var existing = syntyRoot.Find(hitboxName);
            if (existing == null)
            {
                var all = syntyRoot.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < all.Length; i++)
                {
                    var tr = all[i];
                    if (tr != null && string.Equals(tr.name, hitboxName, System.StringComparison.Ordinal))
                    {
                        existing = tr;
                        break;
                    }
                }
            }

            if (existing == null)
            {
                return;
            }

            var collider = existing.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        private void EnsureSphereHitbox(
            Transform bone,
            string suffix,
            PlayerBoneHitZone zone,
            float radius,
            Vector3 center = default)
        {
            if (bone == null)
            {
                return;
            }

            var hitboxTransform = EnsureHitboxTransform(bone, suffix);
            var sphere = hitboxTransform.GetComponent<SphereCollider>();
            if (sphere == null)
            {
                sphere = hitboxTransform.gameObject.AddComponent<SphereCollider>();
            }

            sphere.enabled = true;
            sphere.isTrigger = setAsTrigger;
            sphere.radius = Mathf.Max(0.01f, radius);
            sphere.center = center;
            ApplyColliderLayer(hitboxTransform.gameObject);
            EnsureMarker(hitboxTransform.gameObject, zone);
        }

        private void EnsureCapsuleHitbox(
            Transform bone,
            string suffix,
            PlayerBoneHitZone zone,
            float radius,
            float height)
        {
            if (bone == null)
            {
                return;
            }

            var hitboxTransform = EnsureHitboxTransform(bone, suffix);
            var capsule = hitboxTransform.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                capsule = hitboxTransform.gameObject.AddComponent<CapsuleCollider>();
            }

            capsule.enabled = true;
            capsule.isTrigger = setAsTrigger;
            capsule.direction = 1;
            capsule.radius = Mathf.Max(0.01f, radius);
            capsule.height = Mathf.Max(capsule.radius * 2f, height);
            capsule.center = Vector3.zero;
            ApplyColliderLayer(hitboxTransform.gameObject);
            EnsureMarker(hitboxTransform.gameObject, zone);
        }

        private Transform EnsureHitboxTransform(Transform bone, string suffix)
        {
            var hitboxName = HitboxChildPrefix + suffix;
            Transform hitboxTransform = null;
            for (var i = 0; i < bone.childCount; i++)
            {
                var child = bone.GetChild(i);
                if (child != null && string.Equals(child.name, hitboxName, System.StringComparison.Ordinal))
                {
                    hitboxTransform = child;
                    break;
                }
            }

            if (hitboxTransform == null)
            {
                var hitboxObject = new GameObject(hitboxName);
                hitboxTransform = hitboxObject.transform;
                hitboxTransform.SetParent(bone, false);
            }

            hitboxTransform.localPosition = Vector3.zero;
            hitboxTransform.localRotation = Quaternion.identity;
            hitboxTransform.localScale = Vector3.one;
            return hitboxTransform;
        }

        private void ApplyColliderLayer(GameObject hitboxObject)
        {
            if (hitboxLayer >= 0 && hitboxLayer <= 31)
            {
                hitboxObject.layer = hitboxLayer;
                return;
            }

            PlayerHitboxLayers.ApplyHitboxLayer(hitboxObject);
        }

        private static void EnsureMarker(GameObject hitboxObject, PlayerBoneHitZone zone)
        {
            var marker = hitboxObject.GetComponent<PlayerBoneHitbox>();
            if (marker == null)
            {
                marker = hitboxObject.AddComponent<PlayerBoneHitbox>();
            }

            marker.Configure(zone);
        }

        private Transform FindBone(params string[] names)
        {
            if (syntyRoot == null || names == null)
            {
                return null;
            }

            var all = syntyRoot.GetComponentsInChildren<Transform>(true);
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
                    if (current == null ||
                        IsExcludedBoneBranch(current) ||
                        !IsBoneNameMatch(current.name, targetName))
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

            if (best != null)
            {
                return best;
            }

            return FindBoneFromBodyMesh(names);
        }

        private Transform FindBoneFromBodyMesh(string[] names)
        {
            var bodyRenderer = FindPrimaryBodyRenderer(syntyRoot);
            if (bodyRenderer == null || bodyRenderer.bones == null || names == null)
            {
                return null;
            }

            Transform best = null;
            var bestScore = int.MinValue;
            for (var n = 0; n < names.Length; n++)
            {
                var targetName = names[n];
                if (string.IsNullOrWhiteSpace(targetName))
                {
                    continue;
                }

                var bones = bodyRenderer.bones;
                for (var i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i];
                    if (bone == null ||
                        IsExcludedBoneBranch(bone) ||
                        !IsBoneNameMatch(bone.name, targetName))
                    {
                        continue;
                    }

                    var score = ScoreBone(bone) + 1000;
                    if (score <= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    best = bone;
                }
            }

            return best;
        }

        private static int ScoreBone(Transform bone)
        {
            if (bone == null)
            {
                return int.MinValue;
            }

            var score = 0;
            if (bone.gameObject.activeInHierarchy)
            {
                score += 10;
            }

            return score;
        }

        private static SkinnedMeshRenderer FindPrimaryBodyRenderer(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            var skinnedMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer best = null;
            var bestScore = int.MinValue;
            for (var i = 0; i < skinnedMeshes.Length; i++)
            {
                var candidate = skinnedMeshes[i];
                if (!IsCharacterBodyRenderer(candidate))
                {
                    continue;
                }

                var score = 0;
                if (candidate.sharedMesh != null)
                {
                    score += candidate.sharedMesh.vertexCount;
                }

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

        private static bool IsCharacterBodyRenderer(SkinnedMeshRenderer source)
        {
            if (source == null || source.sharedMesh == null)
            {
                return false;
            }

            var objectName = source.gameObject.name;
            if (objectName.IndexOf("_FirstPersonArms", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            if (objectName.StartsWith("SM_Char_Attach", StringComparison.Ordinal))
            {
                return false;
            }

            if (objectName.StartsWith("Character_", StringComparison.Ordinal))
            {
                return true;
            }

            return objectName.StartsWith("Ch", StringComparison.Ordinal);
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

        private static bool IsExcludedBoneBranch(Transform bone)
        {
            var current = bone;
            while (current != null)
            {
                var name = current.name;
                if (string.Equals(name, "RemoteWeaponTarget", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "WeaponModel", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "GeneratedFirstPersonArms", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }
    }
}
