using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerAttachmentApplier : MonoBehaviour
    {
        private const string FaceSocketName = "face";
        private const string HairSocketName = "hair";
        private static readonly Vector3 FaceSocketLocalPosition = new Vector3(0f, 0.08428376f, 0.07651257f);
        private static readonly Vector3 HairSocketLocalPosition = new Vector3(0f, 0.077130124f, 0.05505173f);
        private const float FaceSocketLocalScale = 0.5361407f;
        private const float HairSocketLocalScale = 0.8820587f;
        private static readonly string[] HeadBoneNames =
        {
            "Head",
            "mixamorig:Head",
            "Head_M",
            "head"
        };

        private string appliedFaceId = string.Empty;
        private string appliedHairId = string.Empty;
        private int resolvedHeadBoneId;

        public void Apply(bool forceReapply = false)
        {
            ApplySlot(PlayerSkinSlot.Face, FaceSocketName, ref appliedFaceId, forceReapply);
            ApplySlot(PlayerSkinSlot.Hair, HairSocketName, ref appliedHairId, forceReapply);
        }

        private void ApplySlot(
            PlayerSkinSlot slot,
            string socketName,
            ref string appliedId,
            bool forceReapply)
        {
            if (!PlayerSkinSelectionService.TryGetAppliedSkin(slot, out var definition) ||
                !definition.IsValid)
            {
                if (!string.IsNullOrEmpty(appliedId))
                {
                    ClearSocket(EnsureAttachmentSocket(socketName));
                    appliedId = string.Empty;
                }

                return;
            }

            if (!forceReapply &&
                string.Equals(appliedId, definition.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var socket = EnsureAttachmentSocket(socketName);
            if (socket == null)
            {
                return;
            }

            ClearSocket(socket);

            var prefab = Resources.Load<GameObject>(definition.PrefabResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"[PlayerAttachmentApplier] Prefab not found at Resources/{definition.PrefabResourcePath}");
                appliedId = string.Empty;
                return;
            }

            var instance = Instantiate(prefab, socket, false);
            instance.name = prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            appliedId = definition.Id;
        }

        private Transform EnsureAttachmentSocket(string socketName)
        {
            var headBone = ResolveHeadBone();
            if (headBone == null)
            {
                return null;
            }

            var headBoneId = headBone.GetInstanceID();
            if (resolvedHeadBoneId != headBoneId)
            {
                resolvedHeadBoneId = headBoneId;
                appliedFaceId = string.Empty;
                appliedHairId = string.Empty;
            }

            var directChild = headBone.Find(socketName);
            if (directChild != null)
            {
                return directChild;
            }

            var existing = FindExistingSocket(socketName);
            if (existing != null)
            {
                existing.SetParent(headBone, false);
                ApplySocketDefaults(existing, socketName);
                return existing;
            }

            return CreateAttachmentSocket(headBone, socketName);
        }

        private Transform FindExistingSocket(string socketName)
        {
            var thirdPersonBody = transform.Find("ThirdPersonBody");
            if (thirdPersonBody == null)
            {
                return null;
            }

            var syntyVisual = thirdPersonBody.Find("SyntyVisual");
            if (syntyVisual != null)
            {
                var inVisual = FindChildByExactName(syntyVisual, socketName);
                if (inVisual != null)
                {
                    return inVisual;
                }
            }

            return thirdPersonBody.Find(socketName);
        }

        private Transform ResolveHeadBone()
        {
            var thirdPersonBody = transform.Find("ThirdPersonBody");
            if (thirdPersonBody == null)
            {
                return null;
            }

            var syntyVisual = thirdPersonBody.Find("SyntyVisual");
            if (syntyVisual == null)
            {
                return null;
            }

            var animator = syntyVisual.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman)
            {
                var humanHead = animator.GetBoneTransform(HumanBodyBones.Head);
                if (humanHead != null)
                {
                    return humanHead;
                }
            }

            return FindBone(syntyVisual, HeadBoneNames);
        }

        private static Transform FindBone(Transform root, string[] names)
        {
            if (root == null || names == null || names.Length == 0)
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
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
                    if (current != null &&
                        string.Equals(current.name, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return current;
                    }
                }
            }

            var bodyRenderer = CharacterModelApplier.FindPrimaryBodyRenderer(root);
            if (bodyRenderer?.bones == null)
            {
                return null;
            }

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
                    if (bone != null &&
                        string.Equals(bone.name, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return bone;
                    }
                }
            }

            return null;
        }

        private static Transform CreateAttachmentSocket(Transform headBone, string socketName)
        {
            var socketObject = new GameObject(socketName);
            var socketTransform = socketObject.transform;
            socketTransform.SetParent(headBone, false);
            ApplySocketDefaults(socketTransform, socketName);
            return socketTransform;
        }

        private static void ApplySocketDefaults(Transform socketTransform, string socketName)
        {
            if (socketTransform == null)
            {
                return;
            }

            if (string.Equals(socketName, FaceSocketName, StringComparison.OrdinalIgnoreCase))
            {
                socketTransform.localPosition = FaceSocketLocalPosition;
                socketTransform.localScale = Vector3.one * FaceSocketLocalScale;
            }
            else if (string.Equals(socketName, HairSocketName, StringComparison.OrdinalIgnoreCase))
            {
                socketTransform.localPosition = HairSocketLocalPosition;
                socketTransform.localScale = Vector3.one * HairSocketLocalScale;
            }

            socketTransform.localRotation = Quaternion.identity;
        }

        private static Transform FindChildByExactName(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var current = all[i];
                if (current != null &&
                    string.Equals(current.name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return current;
                }
            }

            return null;
        }

        private static void ClearSocket(Transform socket)
        {
            if (socket == null)
            {
                return;
            }

            for (var i = socket.childCount - 1; i >= 0; i--)
            {
                var child = socket.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }
    }
}
