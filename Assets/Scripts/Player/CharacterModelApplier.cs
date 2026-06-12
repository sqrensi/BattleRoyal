using System;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class CharacterModelApplier
    {
        private static readonly float[] FirstPersonArmBoneWeightThresholds = { 0.35f, 0.2f, 0.1f };

        public static bool HasCharacterBody(GameObject playerRoot)
        {
            if (playerRoot == null)
            {
                return false;
            }

            var thirdPersonBody = playerRoot.transform.Find("ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            return FindPrimaryBodyRenderer(syntyVisual) != null;
        }

        public static bool TryApplyToPlayer(GameObject playerRoot, GameObject modelAsset)
        {
            if (playerRoot == null || modelAsset == null)
            {
                return false;
            }

            var thirdPersonBody = playerRoot.transform.Find("ThirdPersonBody");
            if (thirdPersonBody == null)
            {
                return false;
            }

            var syntyVisual = EnsureSyntyVisualRoot(thirdPersonBody, modelAsset);
            if (syntyVisual == null)
            {
                return false;
            }

            var targetRenderer = FindPrimaryBodyRenderer(syntyVisual);
            if (targetRenderer == null)
            {
                return false;
            }

            var modelRenderer = FindPrimaryBodyRenderer(modelAsset.transform);
            if (modelRenderer == null || modelRenderer.sharedMesh == null)
            {
                return false;
            }

            targetRenderer.sharedMesh = modelRenderer.sharedMesh;
            targetRenderer.sharedMaterials = modelRenderer.sharedMaterials;
            RebindSkinnedMeshToInstance(targetRenderer, modelRenderer, syntyVisual);
            targetRenderer.enabled = true;

            var binder = playerRoot.GetComponent<SyntyCharacterVisualBinder>();
            binder?.SetVisualRoot(syntyVisual);

            ConfigureFirstPersonArms(playerRoot, syntyVisual, targetRenderer);

            var handBinder = playerRoot.GetComponent<SyntyWeaponHandBinder>();
            handBinder?.ConfigureFromVisualRoot(syntyVisual);

            RefreshRemotePresentationAfterModelSwap(playerRoot, syntyVisual);

            return true;
        }

        private static void RefreshRemotePresentationAfterModelSwap(GameObject playerRoot, Transform syntyVisual)
        {
            if (playerRoot == null)
            {
                return;
            }

            var bootstrap = playerRoot.GetComponent<RemoteThirdPersonPlayerBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.RefreshRemoteWeaponPresentation();
            }
            else
            {
                var remoteWeapon = playerRoot.GetComponent<RemoteWeaponPresentation>();
                if (remoteWeapon != null)
                {
                    remoteWeapon.InvalidateAttachTarget();
                    remoteWeapon.EnsureAttached();
                }

                var handBinder = playerRoot.GetComponent<RemoteLeftHandIkBinder>();
                if (handBinder != null && syntyVisual != null)
                {
                    handBinder.ConfigureFromVisualRoot(syntyVisual);
                    handBinder.SetHandIkEnabled(
                        remoteWeapon != null && remoteWeapon.HasWeapon && !remoteWeapon.IsHolstered);
                }
            }

            RefreshRemoteHitboxes(playerRoot, syntyVisual);
            RefreshRemoteResourceClothing(playerRoot, syntyVisual);
        }

        private static void RefreshRemoteResourceClothing(GameObject playerRoot, Transform syntyVisual)
        {
            if (playerRoot == null || syntyVisual == null)
            {
                return;
            }

            var clothingApplier = playerRoot.GetComponent<RemoteResourceClothingApplier>();
            if (clothingApplier == null)
            {
                clothingApplier = playerRoot.AddComponent<RemoteResourceClothingApplier>();
            }

            clothingApplier.ApplyToRemoteVisual(syntyVisual, forceReapply: true);
        }

        private static void RefreshLocalResourceClothing(GameObject playerRoot, Transform syntyVisual)
        {
            if (playerRoot == null || syntyVisual == null)
            {
                return;
            }

            if (playerRoot.GetComponent<RemoteThirdPersonPlayerBootstrap>() != null)
            {
                return;
            }

            var armsPresenter = playerRoot.GetComponent<SyntyFirstPersonArmsPresenter>();
            if (armsPresenter == null)
            {
                return;
            }

            var clothingApplier = playerRoot.GetComponent<RemoteResourceClothingApplier>();
            if (clothingApplier == null)
            {
                clothingApplier = playerRoot.AddComponent<RemoteResourceClothingApplier>();
            }

            clothingApplier.ApplyToLocalVisual(syntyVisual, armsPresenter, forceReapply: true);
        }

        public static void RefreshRemoteHitboxes(GameObject playerRoot, Transform syntyVisual, bool forceRebuild = false)
        {
            if (playerRoot == null || syntyVisual == null)
            {
                return;
            }

            PlayerHitboxCleanup.RemoveLegacyLineHitboxes(playerRoot);

            PlayerHitboxLayers.ApplyBodyLayerToPlayerRoot(playerRoot);

            var boneRig = playerRoot.GetComponent<PlayerBoneHitboxRig>();
            if (boneRig == null)
            {
                boneRig = playerRoot.AddComponent<PlayerBoneHitboxRig>();
            }

            boneRig.Configure(syntyVisual);
            DisableCharacterMeshColliders(syntyVisual);
            if (!forceRebuild && boneRig.HasActiveHitboxes())
            {
                boneRig.BuildOrRefreshHitboxes(forceRebuild: false);
                return;
            }

            boneRig.BuildOrRefreshHitboxes(forceRebuild: true);
        }

        private static void DisableCharacterMeshColliders(Transform syntyVisual)
        {
            if (syntyVisual == null)
            {
                return;
            }

            var meshColliders = syntyVisual.GetComponentsInChildren<MeshCollider>(true);
            for (var i = 0; i < meshColliders.Length; i++)
            {
                var meshCollider = meshColliders[i];
                if (meshCollider != null)
                {
                    meshCollider.enabled = false;
                }
            }
        }

        private static void ConfigureFirstPersonArms(
            GameObject playerRoot,
            Transform syntyVisual,
            SkinnedMeshRenderer bodyRenderer)
        {
            if (playerRoot.GetComponent<RemoteThirdPersonPlayerBootstrap>() != null)
            {
                return;
            }

            var armsPresenter = playerRoot.GetComponent<SyntyFirstPersonArmsPresenter>();
            if (armsPresenter == null || bodyRenderer == null)
            {
                return;
            }

            armsPresenter.enabled = true;

            if (armsPresenter.TryRefreshArmsFromBody(syntyVisual, bodyRenderer))
            {
                SyncFirstPersonArmsAfterModelApply(playerRoot, armsPresenter);
                RefreshLocalResourceClothing(playerRoot, syntyVisual);
                return;
            }

            for (var i = 0; i < FirstPersonArmBoneWeightThresholds.Length; i++)
            {
                if (armsPresenter.TryBuildArmsFresh(
                        syntyVisual,
                        bodyRenderer,
                        FirstPersonArmBoneWeightThresholds[i],
                        FirstPersonArmsCoverage.ArmsWithoutShoulders))
                {
                    SyncFirstPersonArmsAfterModelApply(playerRoot, armsPresenter);
                    RefreshLocalResourceClothing(playerRoot, syntyVisual);
                    return;
                }
            }

            Debug.LogWarning(
                $"[CharacterModelApplier] Failed to build first-person arms for '{bodyRenderer.gameObject.name}'.",
                playerRoot);
        }

        private static void SyncFirstPersonArmsAfterModelApply(
            GameObject playerRoot,
            SyntyFirstPersonArmsPresenter armsPresenter)
        {
            if (playerRoot == null || armsPresenter == null)
            {
                return;
            }

            var holster = playerRoot.GetComponent<PlayerWeaponHolsterController>();
            if (holster != null)
            {
                holster.SyncFirstPersonArmsPresentation();
                return;
            }

            var weaponMount = playerRoot.GetComponent<PlayerWeaponMount>();
            var showArms = weaponMount != null && weaponMount.HasMountedWeapon;
            if (showArms)
            {
                var viewPresentation = playerRoot.GetComponent<PlayerViewPresentation>();
                var isLocal = viewPresentation == null || viewPresentation.IsLocalPlayerView;
                armsPresenter.ApplyFirstPersonVisibility(isLocal);
                playerRoot.GetComponent<SyntyWeaponHandBinder>()?.SetHandIkEnabled(true);
            }
            else
            {
                armsPresenter.SetHolsteredArmsPresentation(true);
                playerRoot.GetComponent<SyntyWeaponHandBinder>()?.SetHandIkEnabled(false);
            }

            playerRoot.GetComponent<SyntySplitBodyPresentation>()?.SetHolsteredFirstPersonPresentation(!showArms);
        }

        private static void RebindSkinnedMeshToInstance(
            SkinnedMeshRenderer target,
            SkinnedMeshRenderer sourceTemplate,
            Transform skeletonRoot)
        {
            if (target == null || sourceTemplate == null || skeletonRoot == null)
            {
                return;
            }

            var templateBones = sourceTemplate.bones;
            if (templateBones == null || templateBones.Length == 0)
            {
                return;
            }

            var rebound = new Transform[templateBones.Length];
            for (var i = 0; i < templateBones.Length; i++)
            {
                var templateBone = templateBones[i];
                rebound[i] = templateBone != null
                    ? FindBoneInHierarchy(skeletonRoot, templateBone.name)
                    : null;
            }

            target.bones = rebound;
            var rootTemplate = sourceTemplate.rootBone;
            target.rootBone = rootTemplate != null
                ? FindBoneInHierarchy(skeletonRoot, rootTemplate.name)
                : null;
        }

        private static Transform FindBoneInHierarchy(Transform root, string boneName)
        {
            if (root == null || string.IsNullOrWhiteSpace(boneName))
            {
                return null;
            }

            if (string.Equals(root.name, boneName, StringComparison.Ordinal))
            {
                return root;
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var current = transforms[i];
                if (current != null && string.Equals(current.name, boneName, StringComparison.Ordinal))
                {
                    return current;
                }
            }

            return null;
        }

        private static Transform EnsureSyntyVisualRoot(Transform thirdPersonBody, GameObject modelAsset)
        {
            var existing = thirdPersonBody.Find("SyntyVisual");
            if (existing != null)
            {
                var hasBody = FindPrimaryBodyRenderer(existing) != null;
                if (hasBody)
                {
                    return existing;
                }

                UnityEngine.Object.Destroy(existing.gameObject);
            }

            var modelInstance = UnityEngine.Object.Instantiate(modelAsset, thirdPersonBody);
            modelInstance.name = "SyntyVisual";
            var visualRoot = modelInstance.transform;
            visualRoot.localPosition = Vector3.zero;
            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localScale = Vector3.one;
            return visualRoot;
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
    }
}
