using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class RemoteResourceClothingApplier : MonoBehaviour
    {
        private sealed class FirstPersonGloveRendererMarker : MonoBehaviour
        {
        }

        private const string ClothingRootName = "RemoteResourceClothing";
        public const string FirstPersonGlovesRootName = "LocalFirstPersonGloves";
        private const float MinimumBoneBindRatio = 0.65f;

        private static readonly string[] CriticalArmBoneCores =
        {
            "LeftShoulder",
            "LeftArm",
            "LeftForeArm",
            "RightShoulder",
            "RightArm",
            "RightForeArm"
        };

        private static readonly string[] CriticalLegBoneCores =
        {
            "LeftUpLeg",
            "RightUpLeg",
            "LeftLeg",
            "RightLeg"
        };

        private static readonly string[] CriticalFootBoneCores =
        {
            "LeftFoot",
            "RightFoot"
        };

        private static readonly string[] CriticalHandBoneCores =
        {
            "LeftHand",
            "RightHand"
        };

        private enum ClothingBindProfile
        {
            Arms,
            Legs,
            Feet,
            Hands
        }

        private static readonly Dictionary<string, GameObject> PrefabCache =
            new Dictionary<string, GameObject>(4, StringComparer.Ordinal);

        private static readonly Dictionary<string, Material> MaterialCache =
            new Dictionary<string, Material>(4, StringComparer.Ordinal);

        private static readonly Dictionary<int, Mesh> BodyWithoutTorsoMeshCache =
            new Dictionary<int, Mesh>(8);

        private static readonly Dictionary<int, Mesh> BodyWithoutLegsMeshCache =
            new Dictionary<int, Mesh>(8);

        private static readonly Dictionary<int, Mesh> BodyWithoutFeetMeshCache =
            new Dictionary<int, Mesh>(8);

        private static readonly Dictionary<long, Mesh> BodyWithoutHandsMeshCache =
            new Dictionary<long, Mesh>(8);

        [SerializeField] private bool applyOnRemote = true;
        [SerializeField] private bool hideBodyTorsoWhenClothed = true;
        [SerializeField] private float minTorsoHideBoneWeight = 0.35f;
        [SerializeField] private string clothingResourcePath = "1";
        [SerializeField] private string clothingMaterialResourcePath = "tshirt";
        [SerializeField] private bool applyPantsOnRemote = true;
        [SerializeField] private bool hideBodyLegsWhenPants = true;
        [SerializeField] private float minLegHideBoneWeight = 0.35f;
        [SerializeField] private string pantsResourcePath = "2";
        [SerializeField] private string pantsMaterialResourcePath = "pants";
        [SerializeField] private bool applyBootsOnRemote = true;
        [SerializeField] private bool hideBodyFeetWhenBoots = true;
        [SerializeField] private float minFootHideBoneWeight = 0.35f;
        [SerializeField] private string bootsResourcePath = "3";
        [SerializeField] private string bootsMaterialResourcePath = "feets";
        [SerializeField] private bool applyGloves = true;
        [SerializeField] private bool hideBodyHandsWhenGloves = true;
        [SerializeField] private float minHandHideBoneWeight = 0.35f;
        [SerializeField] private string glovesResourcePath = "4";
        [SerializeField] private string glovesMaterialResourcePath = "gloves";
        [Tooltip("Skinned clothing should stay at zero offset. Non-zero values break sleeve alignment.")]
        [SerializeField] private Vector3 clothingLocalOffset = Vector3.zero;

        private bool warnedMissingResource;
        private bool warnedIncompatibleRig;
        private bool warnedStaticMesh;
        private bool warnedTorsoHideFailed;
        private bool warnedLegHideFailed;
        private bool warnedFootHideFailed;
        private bool warnedHandHideFailed;
        private bool loggedHandBonesOnce;

        private SkinnedMeshRenderer bodyRendererWithHiddenParts;
        private Mesh originalBodyMeshBeforeHide;
        private int lastHiddenBodyStateHash;
        private Mesh lastAppliedHiddenMesh;
        private readonly List<Renderer> lastAppliedGloveRenderers = new List<Renderer>(4);

        public void InvalidateBodyMeshCache()
        {
            bodyRendererWithHiddenParts = null;
            originalBodyMeshBeforeHide = null;
            lastHiddenBodyStateHash = 0;
            lastAppliedHiddenMesh = null;
        }

        public void ConfigureSkinPaths(
            string shirtMeshPath,
            string shirtMaterialPath,
            string pantsMeshPath,
            string pantsMaterialPath,
            string bootsMeshPath,
            string bootsMaterialPath,
            string glovesMeshPath,
            string glovesMaterialPath)
        {
            clothingResourcePath = shirtMeshPath ?? string.Empty;
            clothingMaterialResourcePath = shirtMaterialPath ?? string.Empty;
            pantsResourcePath = pantsMeshPath ?? string.Empty;
            pantsMaterialResourcePath = pantsMaterialPath ?? string.Empty;
            bootsResourcePath = bootsMeshPath ?? string.Empty;
            bootsMaterialResourcePath = bootsMaterialPath ?? string.Empty;
            glovesResourcePath = glovesMeshPath ?? string.Empty;
            glovesMaterialResourcePath = glovesMaterialPath ?? string.Empty;
        }

        public void ApplyToRemoteVisual(Transform syntyVisual, bool forceReapply = false)
        {
            if (!applyOnRemote || syntyVisual == null)
            {
                return;
            }

            var existing = syntyVisual.Find(ClothingRootName);
            if (!forceReapply && existing != null)
            {
                var bodyRenderer = FindCharacterBodyRenderer(syntyVisual);
                if (!NeedsHiddenBodyRefresh(bodyRenderer, hideHandsOnBody: true))
                {
                    return;
                }

                using (NetworkPerformanceMonitor.RefreshHiddenBodyMarker.Auto())
                {
                    RefreshHiddenBodyParts(bodyRenderer, hideHandsOnBody: true);
                }

                return;
            }

            if (forceReapply)
            {
                warnedMissingResource = false;
                warnedIncompatibleRig = false;
                warnedStaticMesh = false;
                warnedTorsoHideFailed = false;
                warnedLegHideFailed = false;
                warnedFootHideFailed = false;
                warnedHandHideFailed = false;
                loggedHandBonesOnce = false;
            }

            ClearExistingClothing(syntyVisual);
            ApplyClothingToVisual(syntyVisual, hideHandsOnThirdPersonBody: true);
        }

        public void ApplyToLocalVisual(
            Transform syntyVisual,
            SyntyFirstPersonArmsPresenter armsPresenter,
            bool forceReapply = false)
        {
            if (syntyVisual == null || armsPresenter == null)
            {
                return;
            }

            armsPresenter.EnsureBuilt();

            var existing = syntyVisual.Find(ClothingRootName);
            if (!forceReapply && existing != null)
            {
                ApplyFirstPersonGloves(syntyVisual, armsPresenter);
                return;
            }

            if (forceReapply)
            {
                warnedMissingResource = false;
                warnedIncompatibleRig = false;
                warnedStaticMesh = false;
                warnedTorsoHideFailed = false;
                warnedLegHideFailed = false;
                warnedFootHideFailed = false;
                warnedHandHideFailed = false;
                loggedHandBonesOnce = false;
            }

            ClearExistingClothing(syntyVisual);
            ClearFirstPersonGloves(armsPresenter);

            ApplyClothingToVisual(syntyVisual, hideHandsOnThirdPersonBody: false);
            ApplyFirstPersonGloves(syntyVisual, armsPresenter);
        }

        private bool ApplyClothingToVisual(Transform syntyVisual, bool hideHandsOnThirdPersonBody)
        {
            lastAppliedGloveRenderers.Clear();
            var bodyRenderer = FindCharacterBodyRenderer(syntyVisual);
            var bodyParent = bodyRenderer != null ? bodyRenderer.transform.parent : syntyVisual;

            var clothingRootObject = new GameObject(ClothingRootName);
            var clothingRoot = clothingRootObject.transform;
            clothingRoot.SetParent(syntyVisual, false);
            clothingRoot.localPosition = clothingLocalOffset;
            clothingRoot.localRotation = Quaternion.identity;
            clothingRoot.localScale = Vector3.one;

            var appliedAny = ApplyAllClothingLayers(
                syntyVisual,
                clothingRoot,
                bodyRenderer,
                bodyParent);

            if (!appliedAny)
            {
                Destroy(clothingRootObject);
                WarnIncompatibleRigOnce();
                return false;
            }

            RefreshHiddenBodyParts(bodyRenderer, hideHandsOnThirdPersonBody);
            return true;
        }

        private bool ApplyAllClothingLayers(
            Transform syntyVisual,
            Transform clothingRoot,
            SkinnedMeshRenderer bodyRenderer,
            Transform bodyParent,
            Transform layerParentOverride = null)
        {
            var layerParent = layerParentOverride != null ? layerParentOverride : clothingRoot;
            var appliedAny = false;
            appliedAny |= ApplyClothingLayer(
                syntyVisual,
                clothingRoot,
                clothingResourcePath,
                clothingMaterialResourcePath,
                bodyRenderer,
                bodyParent,
                ClothingBindProfile.Arms,
                layerParent);
            if (applyPantsOnRemote)
            {
                appliedAny |= ApplyClothingLayer(
                    syntyVisual,
                    clothingRoot,
                    pantsResourcePath,
                    pantsMaterialResourcePath,
                    bodyRenderer,
                    bodyParent,
                    ClothingBindProfile.Legs,
                    layerParent);
            }

            if (applyBootsOnRemote)
            {
                appliedAny |= ApplyClothingLayer(
                    syntyVisual,
                    clothingRoot,
                    bootsResourcePath,
                    bootsMaterialResourcePath,
                    bodyRenderer,
                    bodyParent,
                    ClothingBindProfile.Feet,
                    layerParent);
            }

            if (applyGloves)
            {
                appliedAny |= ApplyClothingLayer(
                    syntyVisual,
                    clothingRoot,
                    glovesResourcePath,
                    glovesMaterialResourcePath,
                    bodyRenderer,
                    bodyParent,
                    ClothingBindProfile.Hands,
                    layerParent);
            }

            return appliedAny;
        }

        private void ApplyFirstPersonGloves(Transform syntyVisual, SyntyFirstPersonArmsPresenter armsPresenter)
        {
            if (!applyGloves || armsPresenter == null)
            {
                return;
            }

            armsPresenter.ClearFirstPersonGloveRenderers();

            if (lastAppliedGloveRenderers.Count > 0)
            {
                armsPresenter.RegisterFirstPersonGloveRenderers(lastAppliedGloveRenderers);
            }
            else if (syntyVisual != null)
            {
                RegisterBodyGloveRenderersFromVisual(syntyVisual, armsPresenter);
            }

            HideHandsOnFirstPersonArms(armsPresenter);
            RefreshLocalFirstPersonView(armsPresenter);
        }

        private void RegisterBodyGloveRenderersFromVisual(
            Transform syntyVisual,
            SyntyFirstPersonArmsPresenter armsPresenter)
        {
            if (syntyVisual == null || armsPresenter == null)
            {
                return;
            }

            var markers = syntyVisual.GetComponentsInChildren<FirstPersonGloveRendererMarker>(true);
            if (markers.Length == 0)
            {
                return;
            }

            var matched = new List<Renderer>(markers.Length);
            for (var i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                if (marker == null)
                {
                    continue;
                }

                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    matched.Add(renderer);
                }
            }

            if (matched.Count > 0)
            {
                armsPresenter.RegisterFirstPersonGloveRenderers(matched);
            }
        }

        private static void RefreshLocalFirstPersonView(SyntyFirstPersonArmsPresenter armsPresenter)
        {
            if (armsPresenter == null)
            {
                return;
            }

            var viewPresentation = armsPresenter.GetComponent<PlayerViewPresentation>();
            if (viewPresentation != null)
            {
                viewPresentation.RefreshViewMode();
                return;
            }

            var holster = armsPresenter.GetComponent<PlayerWeaponHolsterController>();
            if (holster != null)
            {
                holster.SyncFirstPersonArmsPresentation();
                return;
            }

            armsPresenter.ApplyFirstPersonVisibility(true);
        }

        private void HideHandsOnFirstPersonArms(SyntyFirstPersonArmsPresenter armsPresenter)
        {
            if (!hideBodyHandsWhenGloves || armsPresenter == null)
            {
                return;
            }

            var armsRenderers = armsPresenter.FirstPersonArmsRenderers;
            for (var i = 0; i < armsRenderers.Count; i++)
            {
                var armsRenderer = armsRenderers[i];
                if (armsRenderer == null || armsRenderer.sharedMesh == null)
                {
                    continue;
                }

                var withoutHands = GetOrCreateBodyWithoutHandsMesh(armsRenderer, minHandHideBoneWeight);
                if (withoutHands == null)
                {
                    WarnHandHideFailedOnce(armsRenderer);
                    continue;
                }

                LogHandBonesHiddenOnce(armsRenderer, "local first-person arms");
                armsPresenter.RegisterGeneratedMesh(withoutHands);
                armsRenderer.sharedMesh = withoutHands;
            }
        }

        private bool ApplyClothingLayer(
            Transform syntyVisual,
            Transform clothingRoot,
            string resourcePath,
            string materialResourcePath,
            SkinnedMeshRenderer bodyRenderer,
            Transform bodyParent,
            ClothingBindProfile bindProfile,
            Transform layerParent,
            bool snapToBodyHierarchy = true)
        {
            var prefab = LoadClothingAsset(resourcePath);
            if (prefab == null)
            {
                WarnMissingResourceOnce(resourcePath);
                return false;
            }

            var clothingMaterial = LoadClothingMaterial(materialResourcePath);
            var instance = Instantiate(prefab, syntyVisual);
            instance.name = $"RemoteClothing_{prefab.name}";

            var clothingRenderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var appliedAny = RebindSkinnedRenderers(
                clothingRenderers,
                syntyVisual,
                clothingRoot,
                bodyRenderer,
                bodyParent,
                clothingMaterial,
                bindProfile,
                snapToBodyHierarchy);
            if (!appliedAny)
            {
                appliedAny = EnableStaticRenderers(instance);
                if (appliedAny)
                {
                    WarnStaticClothingOnce();
                }
            }

            if (clothingMaterial != null)
            {
                ApplyClothingMaterialToInstance(instance, clothingMaterial);
            }

            if (bindProfile == ClothingBindProfile.Hands && appliedAny)
            {
                TrackAppliedGloveRenderers(instance, clothingRenderers);
            }

            StripEmbeddedArmature(instance);
            DisableColliders(instance);

            if (instance.transform.childCount == 0 && clothingRenderers.Length > 0)
            {
                Destroy(instance);
            }
            else
            {
                instance.transform.SetParent(layerParent, true);
            }

            return appliedAny;
        }

        private void TrackAppliedGloveRenderers(GameObject instance, SkinnedMeshRenderer[] clothingRenderers)
        {
            if (clothingRenderers != null)
            {
                for (var i = 0; i < clothingRenderers.Length; i++)
                {
                    var renderer = clothingRenderers[i];
                    if (renderer != null && renderer.enabled && !lastAppliedGloveRenderers.Contains(renderer))
                    {
                        lastAppliedGloveRenderers.Add(renderer);
                        MarkFirstPersonGloveRenderer(renderer);
                    }
                }
            }

            if (instance == null)
            {
                return;
            }

            var staticRenderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < staticRenderers.Length; i++)
            {
                var renderer = staticRenderers[i];
                if (renderer != null && renderer.enabled && !lastAppliedGloveRenderers.Contains(renderer))
                {
                    lastAppliedGloveRenderers.Add(renderer);
                    MarkFirstPersonGloveRenderer(renderer);
                }
            }
        }

        private static void MarkFirstPersonGloveRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            if (renderer.GetComponent<FirstPersonGloveRendererMarker>() == null)
            {
                renderer.gameObject.AddComponent<FirstPersonGloveRendererMarker>();
            }

            if (renderer is SkinnedMeshRenderer skinnedGloveRenderer)
            {
                skinnedGloveRenderer.updateWhenOffscreen = true;
            }
        }

        private void ClearFirstPersonGloves(SyntyFirstPersonArmsPresenter armsPresenter)
        {
            if (armsPresenter == null)
            {
                return;
            }

            var glovesRoot = armsPresenter.FindFirstPersonGlovesRoot();
            if (glovesRoot != null)
            {
                Destroy(glovesRoot.gameObject);
            }

            armsPresenter.ClearFirstPersonGloveRenderers();
        }

        private void RefreshHiddenBodyParts(SkinnedMeshRenderer bodyRenderer, bool hideHandsOnBody)
        {
            if (bodyRenderer == null)
            {
                return;
            }

            var stateHash = ComputeHiddenBodyStateHash(hideHandsOnBody);
            if (bodyRendererWithHiddenParts == bodyRenderer &&
                stateHash == lastHiddenBodyStateHash &&
                lastAppliedHiddenMesh != null &&
                bodyRenderer.sharedMesh == lastAppliedHiddenMesh)
            {
                return;
            }

            EnsureOriginalBodyMeshCached(bodyRenderer);

            var workingMesh = originalBodyMeshBeforeHide;
            if (workingMesh == null)
            {
                return;
            }

            bodyRenderer.sharedMesh = workingMesh;

            if (hideBodyTorsoWhenClothed && !string.IsNullOrWhiteSpace(clothingResourcePath))
            {
                var withoutTorso = GetOrCreateBodyWithoutTorsoMesh(bodyRenderer, minTorsoHideBoneWeight);
                if (withoutTorso == null)
                {
                    WarnTorsoHideFailedOnce(bodyRenderer);
                }
                else
                {
                    workingMesh = withoutTorso;
                    bodyRenderer.sharedMesh = workingMesh;
                }
            }

            if (hideBodyLegsWhenPants && applyPantsOnRemote && !string.IsNullOrWhiteSpace(pantsResourcePath))
            {
                var withoutLegs = GetOrCreateBodyWithoutLegsMesh(bodyRenderer, minLegHideBoneWeight);
                if (withoutLegs == null)
                {
                    WarnLegHideFailedOnce(bodyRenderer);
                }
                else
                {
                    workingMesh = withoutLegs;
                    bodyRenderer.sharedMesh = workingMesh;
                }
            }

            if (hideBodyFeetWhenBoots && applyBootsOnRemote && !string.IsNullOrWhiteSpace(bootsResourcePath))
            {
                var withoutFeet = GetOrCreateBodyWithoutFeetMesh(bodyRenderer, minFootHideBoneWeight);
                if (withoutFeet == null)
                {
                    WarnFootHideFailedOnce(bodyRenderer);
                }
                else
                {
                    workingMesh = withoutFeet;
                    bodyRenderer.sharedMesh = workingMesh;
                }
            }

            if (hideHandsOnBody && hideBodyHandsWhenGloves && applyGloves && !string.IsNullOrWhiteSpace(glovesResourcePath))
            {
                var withoutHands = GetOrCreateBodyWithoutHandsMesh(bodyRenderer, minHandHideBoneWeight);
                if (withoutHands == null)
                {
                    WarnHandHideFailedOnce(bodyRenderer);
                }
                else
                {
                    LogHandBonesHiddenOnce(bodyRenderer, "remote body");
                    workingMesh = withoutHands;
                    bodyRenderer.sharedMesh = workingMesh;
                }
            }

            bodyRendererWithHiddenParts = bodyRenderer;
            lastHiddenBodyStateHash = stateHash;
            lastAppliedHiddenMesh = bodyRenderer.sharedMesh;
        }

        private bool NeedsHiddenBodyRefresh(SkinnedMeshRenderer bodyRenderer, bool hideHandsOnBody)
        {
            if (bodyRenderer == null)
            {
                return false;
            }

            var stateHash = ComputeHiddenBodyStateHash(hideHandsOnBody);
            return bodyRendererWithHiddenParts != bodyRenderer ||
                   stateHash != lastHiddenBodyStateHash ||
                   lastAppliedHiddenMesh == null ||
                   bodyRenderer.sharedMesh != lastAppliedHiddenMesh;
        }

        private int ComputeHiddenBodyStateHash(bool hideHandsOnBody)
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + (hideBodyTorsoWhenClothed ? 1 : 0);
                hash = (hash * 31) + (hideBodyLegsWhenPants ? 1 : 0);
                hash = (hash * 31) + (hideBodyFeetWhenBoots ? 1 : 0);
                hash = (hash * 31) + (hideBodyHandsWhenGloves ? 1 : 0);
                hash = (hash * 31) + (applyPantsOnRemote ? 1 : 0);
                hash = (hash * 31) + (applyBootsOnRemote ? 1 : 0);
                hash = (hash * 31) + (applyGloves ? 1 : 0);
                hash = (hash * 31) + (hideHandsOnBody ? 1 : 0);
                hash = (hash * 31) + HashString(clothingResourcePath);
                hash = (hash * 31) + HashString(pantsResourcePath);
                hash = (hash * 31) + HashString(bootsResourcePath);
                hash = (hash * 31) + HashString(glovesResourcePath);
                return hash;
            }
        }

        private static int HashString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            unchecked
            {
                var hash = 23;
                var trimmed = value.Trim();
                for (var i = 0; i < trimmed.Length; i++)
                {
                    hash = (hash * 31) + trimmed[i];
                }

                return hash;
            }
        }

        private void EnsureOriginalBodyMeshCached(SkinnedMeshRenderer bodyRenderer)
        {
            if (bodyRenderer == null || bodyRenderer.sharedMesh == null)
            {
                return;
            }

            var currentMesh = bodyRenderer.sharedMesh;
            if (IsRuntimeTrimmedBodyMesh(currentMesh))
            {
                if (originalBodyMeshBeforeHide != null && !IsRuntimeTrimmedBodyMesh(originalBodyMeshBeforeHide))
                {
                    bodyRenderer.sharedMesh = originalBodyMeshBeforeHide;
                    currentMesh = originalBodyMeshBeforeHide;
                }
                else
                {
                    originalBodyMeshBeforeHide = null;
                    bodyRendererWithHiddenParts = null;
                    return;
                }
            }

            if (bodyRendererWithHiddenParts == bodyRenderer &&
                originalBodyMeshBeforeHide != null &&
                !IsRuntimeTrimmedBodyMesh(originalBodyMeshBeforeHide))
            {
                return;
            }

            if (!IsRuntimeTrimmedBodyMesh(currentMesh) &&
                (originalBodyMeshBeforeHide == null || bodyRendererWithHiddenParts != bodyRenderer))
            {
                originalBodyMeshBeforeHide = currentMesh;
                bodyRendererWithHiddenParts = bodyRenderer;
            }
        }

        private static bool IsRuntimeTrimmedBodyMesh(Mesh mesh)
        {
            return mesh != null &&
                   mesh.name.IndexOf("_Without", StringComparison.Ordinal) >= 0;
        }

        private void HideBodyTorsoUnderClothing(SkinnedMeshRenderer bodyRenderer)
        {
            RefreshHiddenBodyParts(bodyRenderer, hideHandsOnBody: true);
        }

        private static Mesh GetOrCreateBodyWithoutTorsoMesh(
            SkinnedMeshRenderer bodyRenderer,
            float minTorsoBoneWeight)
        {
            var sourceMesh = bodyRenderer.sharedMesh;
            if (sourceMesh == null)
            {
                return null;
            }

            var cacheKey = sourceMesh.GetInstanceID();
            if (BodyWithoutTorsoMeshCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }

            var trimmedMesh = SyntyFirstPersonArmsMeshBuilder.ExtractBodyWithoutTorsoMesh(
                bodyRenderer,
                minTorsoBoneWeight);
            if (trimmedMesh != null)
            {
                BodyWithoutTorsoMeshCache[cacheKey] = trimmedMesh;
            }

            return trimmedMesh;
        }

        private static Mesh GetOrCreateBodyWithoutLegsMesh(
            SkinnedMeshRenderer bodyRenderer,
            float minLegBoneWeight)
        {
            var sourceMesh = bodyRenderer.sharedMesh;
            if (sourceMesh == null)
            {
                return null;
            }

            var cacheKey = sourceMesh.GetInstanceID();
            if (BodyWithoutLegsMeshCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }

            var trimmedMesh = SyntyFirstPersonArmsMeshBuilder.ExtractBodyWithoutLegsMesh(
                bodyRenderer,
                minLegBoneWeight);
            if (trimmedMesh != null)
            {
                BodyWithoutLegsMeshCache[cacheKey] = trimmedMesh;
            }

            return trimmedMesh;
        }

        private static Mesh GetOrCreateBodyWithoutFeetMesh(
            SkinnedMeshRenderer bodyRenderer,
            float minFootBoneWeight)
        {
            var sourceMesh = bodyRenderer.sharedMesh;
            if (sourceMesh == null)
            {
                return null;
            }

            var cacheKey = sourceMesh.GetInstanceID();
            if (BodyWithoutFeetMeshCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }

            var trimmedMesh = SyntyFirstPersonArmsMeshBuilder.ExtractBodyWithoutFeetMesh(
                bodyRenderer,
                minFootBoneWeight);
            if (trimmedMesh != null)
            {
                BodyWithoutFeetMeshCache[cacheKey] = trimmedMesh;
            }

            return trimmedMesh;
        }

        private const int HandMeshTrimCacheVersion = 2;

        private static Mesh GetOrCreateBodyWithoutHandsMesh(
            SkinnedMeshRenderer bodyRenderer,
            float minHandBoneWeight)
        {
            var sourceMesh = bodyRenderer.sharedMesh;
            if (sourceMesh == null)
            {
                return null;
            }

            var cacheKey = BuildHandMeshCacheKey(sourceMesh);
            if (BodyWithoutHandsMeshCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }

            var trimmedMesh = SyntyFirstPersonArmsMeshBuilder.ExtractBodyWithoutHandsMesh(
                bodyRenderer,
                minHandBoneWeight);
            if (trimmedMesh != null)
            {
                BodyWithoutHandsMeshCache[cacheKey] = trimmedMesh;
            }

            return trimmedMesh;
        }

        private static long BuildHandMeshCacheKey(Mesh sourceMesh)
        {
            return ((long)sourceMesh.GetInstanceID() << 8) | HandMeshTrimCacheVersion;
        }

        private void RestoreHiddenBodyTorso()
        {
            if (bodyRendererWithHiddenParts == null || originalBodyMeshBeforeHide == null)
            {
                bodyRendererWithHiddenParts = null;
                originalBodyMeshBeforeHide = null;
                return;
            }

            if (bodyRendererWithHiddenParts.sharedMesh != originalBodyMeshBeforeHide)
            {
                bodyRendererWithHiddenParts.sharedMesh = originalBodyMeshBeforeHide;
            }

            bodyRendererWithHiddenParts = null;
            originalBodyMeshBeforeHide = null;
        }

        private void ClearExistingClothing(Transform syntyVisual)
        {
            RestoreHiddenBodyTorso();

            var existing = syntyVisual != null ? syntyVisual.Find(ClothingRootName) : null;
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }
        }

        private static bool RebindSkinnedRenderers(
            SkinnedMeshRenderer[] clothingRenderers,
            Transform skeletonRoot,
            Transform excludeFromBoneSearch,
            SkinnedMeshRenderer bodyRenderer,
            Transform bodyParent,
            Material clothingMaterial,
            ClothingBindProfile bindProfile,
            bool snapToBodyHierarchy)
        {
            if (clothingRenderers == null ||
                clothingRenderers.Length == 0 ||
                bodyRenderer == null ||
                bodyRenderer.bones == null)
            {
                return false;
            }

            var bodyBoneMap = BuildBodyBoneMap(bodyRenderer);
            Dictionary<string, Transform> expandedBoneMap = null;
            Animator animator = null;
            var reboundAny = false;

            for (var i = 0; i < clothingRenderers.Length; i++)
            {
                var renderer = clothingRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!TryRebindRenderer(renderer, bodyRenderer, bodyBoneMap, bindProfile))
                {
                    expandedBoneMap ??= BuildExpandedBoneMap(skeletonRoot, excludeFromBoneSearch, bodyRenderer, bodyBoneMap);
                    animator ??= skeletonRoot.GetComponent<Animator>();
                    if (animator != null)
                    {
                        animator.Update(0f);
                    }

                    if (!TryRebindRendererSlow(renderer, bodyRenderer, expandedBoneMap, animator, bindProfile))
                    {
                        renderer.enabled = false;
                        continue;
                    }
                }

                if (clothingMaterial != null)
                {
                    renderer.sharedMaterial = clothingMaterial;
                }

                renderer.updateWhenOffscreen = false;
                renderer.enabled = true;
                if (snapToBodyHierarchy)
                {
                    SnapRendererToBodyHierarchy(renderer, bodyRenderer, bodyParent);
                }

                reboundAny = true;
            }

            return reboundAny;
        }

        private static Material LoadClothingMaterial(string clothingMaterialResourcePath)
        {
            if (string.IsNullOrWhiteSpace(clothingMaterialResourcePath))
            {
                return null;
            }

            if (MaterialCache.TryGetValue(clothingMaterialResourcePath, out var cached) && cached != null)
            {
                return cached;
            }

            var material = Resources.Load<Material>(clothingMaterialResourcePath);
            if (material != null)
            {
                MaterialCache[clothingMaterialResourcePath] = material;
            }

            return material;
        }

        private static GameObject LoadClothingAsset(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                return null;
            }

            if (PrefabCache.TryGetValue(resourcePath, out var cached) && cached != null)
            {
                return cached;
            }

            var candidates = Resources.LoadAll<GameObject>(resourcePath);
            GameObject resolved = null;
            if (candidates == null || candidates.Length == 0)
            {
                resolved = Resources.Load<GameObject>(resourcePath);
            }
            else
            {
                GameObject fallback = null;
                for (var i = 0; i < candidates.Length; i++)
                {
                    var candidate = candidates[i];
                    if (candidate == null)
                    {
                        continue;
                    }

                    var renderer = candidate.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (renderer == null)
                    {
                        continue;
                    }

                    fallback ??= candidate;
                    if (HasUsableSourceBoneReferences(renderer.bones))
                    {
                        resolved = candidate;
                        break;
                    }
                }

                resolved ??= fallback;
            }

            if (resolved != null)
            {
                PrefabCache[resourcePath] = resolved;
            }

            return resolved;
        }

        private static bool HasUsableSourceBoneReferences(IReadOnlyList<Transform> sourceBones)
        {
            if (sourceBones == null || sourceBones.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < sourceBones.Count; i++)
            {
                if (sourceBones[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryRebindRenderer(
            SkinnedMeshRenderer renderer,
            SkinnedMeshRenderer bodyRenderer,
            Dictionary<string, Transform> bodyBoneMap,
            ClothingBindProfile bindProfile)
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                return false;
            }

            var sourceBindPoses = mesh.bindposes;
            if (sourceBindPoses == null || sourceBindPoses.Length == 0)
            {
                return false;
            }

            var bodyBones = bodyRenderer.bones;
            if (sourceBindPoses.Length == bodyBones.Length &&
                bodyRenderer.rootBone != null &&
                (bindProfile != ClothingBindProfile.Arms || HasCriticalArmBonesOnBody(bodyBoneMap)))
            {
                ApplyRebind(renderer, mesh, bodyBones, bodyRenderer.rootBone, copyMesh: false);
                return true;
            }

            var sourceBones = renderer.bones;
            if (!HasUsableSourceBoneReferences(sourceBones) ||
                sourceBindPoses.Length != sourceBones.Length)
            {
                return false;
            }

            return TryRebindByBodyBoneMap(renderer, sourceBones, sourceBindPoses, bodyRenderer, bodyBoneMap, bindProfile);
        }

        private static bool TryRebindRendererSlow(
            SkinnedMeshRenderer renderer,
            SkinnedMeshRenderer bodyRenderer,
            Dictionary<string, Transform> characterBoneMap,
            Animator animator,
            ClothingBindProfile bindProfile)
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                return false;
            }

            var sourceBones = renderer.bones;
            var sourceBindPoses = mesh.bindposes;
            if (!HasUsableSourceBoneReferences(sourceBones) ||
                sourceBindPoses == null ||
                sourceBindPoses.Length != sourceBones.Length)
            {
                return false;
            }

            return TryRebindByBodyBoneMapSlow(
                renderer,
                sourceBones,
                sourceBindPoses,
                bodyRenderer,
                characterBoneMap,
                animator,
                bindProfile);
        }

        private static bool TryRebindByBodyBoneMap(
            SkinnedMeshRenderer renderer,
            Transform[] sourceBones,
            Matrix4x4[] sourceBindPoses,
            SkinnedMeshRenderer bodyRenderer,
            Dictionary<string, Transform> bodyBoneMap,
            ClothingBindProfile bindProfile)
        {
            var boneCount = sourceBones.Length;
            var reboundBones = new Transform[boneCount];
            var reboundBindPoses = new Matrix4x4[boneCount];
            var matched = 0;
            var preserveMesh = true;

            for (var i = 0; i < boneCount; i++)
            {
                var sourceBone = sourceBones[i];
                if (sourceBone == null)
                {
                    continue;
                }

                var rebound = ResolveBodyBone(bodyBoneMap, sourceBone.name);
                reboundBones[i] = rebound;
                if (rebound == null)
                {
                    continue;
                }

                if (IsExactBoneNameMatch(sourceBone.name, rebound.name))
                {
                    reboundBindPoses[i] = sourceBindPoses[i];
                }
                else
                {
                    reboundBindPoses[i] = rebound.worldToLocalMatrix * sourceBone.localToWorldMatrix * sourceBindPoses[i];
                    preserveMesh = false;
                }

                matched++;
            }

            var bindRatio = matched / Mathf.Max(1f, boneCount);
            if (bindRatio < MinimumBoneBindRatio ||
                !PassesCriticalBoneCheck(sourceBones, reboundBones, bindProfile))
            {
                return false;
            }

            ApplyRebind(
                renderer,
                renderer.sharedMesh,
                reboundBones,
                bodyRenderer.rootBone,
                copyMesh: !preserveMesh,
                reboundBindPoses: preserveMesh ? null : reboundBindPoses);
            return true;
        }

        private static bool TryRebindByBodyBoneMapSlow(
            SkinnedMeshRenderer renderer,
            Transform[] sourceBones,
            Matrix4x4[] sourceBindPoses,
            SkinnedMeshRenderer bodyRenderer,
            Dictionary<string, Transform> characterBoneMap,
            Animator animator,
            ClothingBindProfile bindProfile)
        {
            var boneCount = sourceBones.Length;
            var reboundBones = new Transform[boneCount];
            var reboundBindPoses = new Matrix4x4[boneCount];
            var matched = 0;
            var preserveMesh = true;

            for (var i = 0; i < boneCount; i++)
            {
                var sourceBone = sourceBones[i];
                if (sourceBone == null)
                {
                    continue;
                }

                var rebound = ResolveTargetBone(animator, bodyRenderer, characterBoneMap, sourceBone.name);
                reboundBones[i] = rebound;
                if (rebound == null)
                {
                    continue;
                }

                reboundBindPoses[i] = ComputeReboundBindPose(sourceBone, rebound, sourceBindPoses[i]);
                if (reboundBindPoses[i] != sourceBindPoses[i])
                {
                    preserveMesh = false;
                }

                matched++;
            }

            var bindRatio = matched / Mathf.Max(1f, boneCount);
            if (bindRatio < MinimumBoneBindRatio ||
                !PassesCriticalBoneCheck(sourceBones, reboundBones, bindProfile))
            {
                return false;
            }

            ApplyRebind(
                renderer,
                renderer.sharedMesh,
                reboundBones,
                bodyRenderer.rootBone ?? ResolveRootBone(sourceBones, reboundBones, characterBoneMap, animator),
                copyMesh: !preserveMesh,
                reboundBindPoses: preserveMesh ? null : reboundBindPoses);
            return true;
        }

        private static bool PassesCriticalBoneCheck(
            Transform[] sourceBones,
            Transform[] reboundBones,
            ClothingBindProfile bindProfile)
        {
            if (bindProfile == ClothingBindProfile.Arms || UsesArmBones(sourceBones))
            {
                return HasCriticalArmBones(sourceBones, reboundBones);
            }

            if (bindProfile == ClothingBindProfile.Feet || UsesFootBones(sourceBones))
            {
                return HasCriticalFootBones(sourceBones, reboundBones);
            }

            if (bindProfile == ClothingBindProfile.Hands || UsesHandBones(sourceBones))
            {
                return HasCriticalHandBones(sourceBones, reboundBones);
            }

            if (bindProfile == ClothingBindProfile.Legs || UsesLegBones(sourceBones))
            {
                return HasCriticalLegBones(sourceBones, reboundBones);
            }

            return true;
        }

        private static bool UsesArmBones(Transform[] sourceBones)
        {
            for (var i = 0; i < sourceBones.Length; i++)
            {
                var sourceBone = sourceBones[i];
                if (sourceBone == null)
                {
                    continue;
                }

                for (var coreIndex = 0; coreIndex < CriticalArmBoneCores.Length; coreIndex++)
                {
                    if (MatchesCriticalArmCore(sourceBone.name, CriticalArmBoneCores[coreIndex]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool UsesLegBones(Transform[] sourceBones)
        {
            for (var i = 0; i < sourceBones.Length; i++)
            {
                var sourceBone = sourceBones[i];
                if (sourceBone == null)
                {
                    continue;
                }

                for (var coreIndex = 0; coreIndex < CriticalLegBoneCores.Length; coreIndex++)
                {
                    if (MatchesCriticalLegCore(sourceBone.name, CriticalLegBoneCores[coreIndex]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool UsesHandBones(Transform[] sourceBones)
        {
            for (var i = 0; i < sourceBones.Length; i++)
            {
                var sourceBone = sourceBones[i];
                if (sourceBone == null)
                {
                    continue;
                }

                for (var coreIndex = 0; coreIndex < CriticalHandBoneCores.Length; coreIndex++)
                {
                    if (MatchesCriticalHandCore(sourceBone.name, CriticalHandBoneCores[coreIndex]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasCriticalHandBones(Transform[] sourceBones, Transform[] reboundBones)
        {
            for (var i = 0; i < CriticalHandBoneCores.Length; i++)
            {
                var coreName = CriticalHandBoneCores[i];
                var found = false;
                for (var boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
                {
                    var sourceBone = sourceBones[boneIndex];
                    if (sourceBone == null)
                    {
                        continue;
                    }

                    if (!MatchesCriticalHandCore(sourceBone.name, coreName))
                    {
                        continue;
                    }

                    if (reboundBones[boneIndex] != null)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesCriticalHandCore(string sourceBoneName, string criticalCoreName)
        {
            var sourceCore = ExtractBoneCoreName(sourceBoneName);
            if (string.Equals(sourceCore, criticalCoreName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var alias in ResolveBoneAliases(criticalCoreName))
            {
                if (string.Equals(sourceCore, alias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var alias in ResolveBoneAliases(sourceCore))
            {
                if (string.Equals(alias, criticalCoreName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (criticalCoreName.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0 &&
                sourceCore.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var criticalSide = criticalCoreName.StartsWith("Left", StringComparison.OrdinalIgnoreCase) ? "L" :
                    criticalCoreName.StartsWith("Right", StringComparison.OrdinalIgnoreCase) ? "R" : string.Empty;
                if (!string.IsNullOrEmpty(criticalSide) &&
                    (sourceCore.EndsWith("_" + criticalSide, StringComparison.OrdinalIgnoreCase) ||
                     sourceCore.EndsWith(criticalSide, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCriticalLegBones(Transform[] sourceBones, Transform[] reboundBones)
        {
            for (var i = 0; i < CriticalLegBoneCores.Length; i++)
            {
                var coreName = CriticalLegBoneCores[i];
                var found = false;
                for (var boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
                {
                    var sourceBone = sourceBones[boneIndex];
                    if (sourceBone == null)
                    {
                        continue;
                    }

                    if (!MatchesCriticalLegCore(sourceBone.name, coreName))
                    {
                        continue;
                    }

                    if (reboundBones[boneIndex] != null)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool UsesFootBones(Transform[] sourceBones)
        {
            for (var i = 0; i < sourceBones.Length; i++)
            {
                var sourceBone = sourceBones[i];
                if (sourceBone == null)
                {
                    continue;
                }

                for (var coreIndex = 0; coreIndex < CriticalFootBoneCores.Length; coreIndex++)
                {
                    if (MatchesCriticalFootCore(sourceBone.name, CriticalFootBoneCores[coreIndex]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasCriticalFootBones(Transform[] sourceBones, Transform[] reboundBones)
        {
            for (var i = 0; i < CriticalFootBoneCores.Length; i++)
            {
                var coreName = CriticalFootBoneCores[i];
                var found = false;
                for (var boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
                {
                    var sourceBone = sourceBones[boneIndex];
                    if (sourceBone == null)
                    {
                        continue;
                    }

                    if (!MatchesCriticalFootCore(sourceBone.name, coreName))
                    {
                        continue;
                    }

                    if (reboundBones[boneIndex] != null)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesCriticalFootCore(string sourceBoneName, string criticalCoreName)
        {
            var sourceCore = ExtractBoneCoreName(sourceBoneName);
            if (string.Equals(sourceCore, criticalCoreName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var alias in ResolveBoneAliases(criticalCoreName))
            {
                if (string.Equals(sourceCore, alias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var alias in ResolveBoneAliases(sourceCore))
            {
                if (string.Equals(alias, criticalCoreName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (criticalCoreName.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0 &&
                sourceCore.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var criticalSide = criticalCoreName.StartsWith("Left", StringComparison.OrdinalIgnoreCase) ? "L" :
                    criticalCoreName.StartsWith("Right", StringComparison.OrdinalIgnoreCase) ? "R" : string.Empty;
                if (!string.IsNullOrEmpty(criticalSide) &&
                    (sourceCore.EndsWith("_" + criticalSide, StringComparison.OrdinalIgnoreCase) ||
                     sourceCore.EndsWith(criticalSide, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesCriticalLegCore(string sourceBoneName, string criticalCoreName)
        {
            var sourceCore = ExtractBoneCoreName(sourceBoneName);
            if (string.Equals(sourceCore, criticalCoreName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var alias in ResolveBoneAliases(criticalCoreName))
            {
                if (string.Equals(sourceCore, alias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var alias in ResolveBoneAliases(sourceCore))
            {
                if (string.Equals(alias, criticalCoreName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static Transform ResolveBodyBone(Dictionary<string, Transform> bodyBoneMap, string sourceBoneName)
        {
            if (bodyBoneMap == null || string.IsNullOrWhiteSpace(sourceBoneName))
            {
                return null;
            }

            if (bodyBoneMap.TryGetValue(sourceBoneName, out var match) && match != null)
            {
                return match;
            }

            var coreName = ExtractBoneCoreName(sourceBoneName);
            if (!string.IsNullOrWhiteSpace(coreName) &&
                bodyBoneMap.TryGetValue(coreName, out match) &&
                match != null)
            {
                return match;
            }

            var aliases = ResolveBoneAliases(coreName);
            for (var i = 0; i < aliases.Length; i++)
            {
                if (bodyBoneMap.TryGetValue(aliases[i], out match) && match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static bool HasCriticalArmBonesOnBody(Dictionary<string, Transform> bodyBoneMap)
        {
            for (var i = 0; i < CriticalArmBoneCores.Length; i++)
            {
                if (ResolveBodyBone(bodyBoneMap, CriticalArmBoneCores[i]) == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static Matrix4x4 ComputeReboundBindPose(
            Transform sourceBone,
            Transform targetBone,
            Matrix4x4 sourceBindPose)
        {
            if (IsExactBoneNameMatch(sourceBone.name, targetBone.name))
            {
                return sourceBindPose;
            }

            return targetBone.worldToLocalMatrix * sourceBone.localToWorldMatrix * sourceBindPose;
        }

        private static bool IsExactBoneNameMatch(string sourceBoneName, string targetBoneName)
        {
            if (string.IsNullOrWhiteSpace(sourceBoneName) || string.IsNullOrWhiteSpace(targetBoneName))
            {
                return false;
            }

            if (string.Equals(sourceBoneName, targetBoneName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(
                ExtractBoneCoreName(sourceBoneName),
                ExtractBoneCoreName(targetBoneName),
                StringComparison.OrdinalIgnoreCase);
        }

        private static void SnapRendererToBodyHierarchy(
            SkinnedMeshRenderer clothingRenderer,
            SkinnedMeshRenderer bodyRenderer,
            Transform bodyParent)
        {
            if (clothingRenderer == null || bodyRenderer == null || bodyParent == null)
            {
                return;
            }

            var clothingTransform = clothingRenderer.transform;
            var bodyTransform = bodyRenderer.transform;
            clothingTransform.SetParent(bodyParent, false);
            clothingTransform.localPosition = bodyTransform.localPosition;
            clothingTransform.localRotation = bodyTransform.localRotation;
            clothingTransform.localScale = bodyTransform.localScale;
        }

        private static void ApplyRebind(
            SkinnedMeshRenderer renderer,
            Mesh sourceMesh,
            Transform[] reboundBones,
            Transform rootBone,
            bool copyMesh,
            Matrix4x4[] reboundBindPoses = null)
        {
            if (copyMesh && reboundBindPoses != null)
            {
                var reboundMesh = Instantiate(sourceMesh);
                reboundMesh.name = $"{sourceMesh.name}_Rebound";
                reboundMesh.bindposes = reboundBindPoses;
                renderer.sharedMesh = reboundMesh;
            }
            else
            {
                renderer.sharedMesh = sourceMesh;
            }

            renderer.bones = reboundBones;
            renderer.rootBone = rootBone ?? reboundBones[0];
        }

        private static Transform ResolveTargetBone(
            Animator animator,
            SkinnedMeshRenderer bodyRenderer,
            Dictionary<string, Transform> characterBoneMap,
            string sourceBoneName)
        {
            var fromMap = ResolveCharacterBone(characterBoneMap, sourceBoneName);
            if (fromMap != null)
            {
                return fromMap;
            }

            var coreName = ExtractBoneCoreName(sourceBoneName);
            if (TryMapCoreNameToHumanBodyBone(coreName, out var humanBone) &&
                animator != null &&
                animator.isHuman)
            {
                var fromAnimator = animator.GetBoneTransform(humanBone);
                if (fromAnimator != null && bodyRenderer?.bones != null)
                {
                    for (var i = 0; i < bodyRenderer.bones.Length; i++)
                    {
                        if (bodyRenderer.bones[i] == fromAnimator)
                        {
                            return fromAnimator;
                        }
                    }
                }

                if (fromAnimator != null)
                {
                    return fromAnimator;
                }
            }

            return null;
        }

        private static bool TryMapCoreNameToHumanBodyBone(string coreName, out HumanBodyBones humanBone)
        {
            switch (coreName)
            {
                case "Hips":
                    humanBone = HumanBodyBones.Hips;
                    return true;
                case "Spine":
                    humanBone = HumanBodyBones.Spine;
                    return true;
                case "Spine1":
                    humanBone = HumanBodyBones.Chest;
                    return true;
                case "Spine2":
                    humanBone = HumanBodyBones.UpperChest;
                    return true;
                case "Neck":
                    humanBone = HumanBodyBones.Neck;
                    return true;
                case "Head":
                    humanBone = HumanBodyBones.Head;
                    return true;
                case "LeftShoulder":
                    humanBone = HumanBodyBones.LeftShoulder;
                    return true;
                case "Clavicle_L":
                    humanBone = HumanBodyBones.LeftShoulder;
                    return true;
                case "RightShoulder":
                    humanBone = HumanBodyBones.RightShoulder;
                    return true;
                case "Clavicle_R":
                    humanBone = HumanBodyBones.RightShoulder;
                    return true;
                case "LeftArm":
                    humanBone = HumanBodyBones.LeftUpperArm;
                    return true;
                case "Shoulder_L":
                case "UpperArm_L":
                    humanBone = HumanBodyBones.LeftUpperArm;
                    return true;
                case "RightArm":
                    humanBone = HumanBodyBones.RightUpperArm;
                    return true;
                case "Shoulder_R":
                case "UpperArm_R":
                    humanBone = HumanBodyBones.RightUpperArm;
                    return true;
                case "LeftForeArm":
                    humanBone = HumanBodyBones.LeftLowerArm;
                    return true;
                case "Elbow_L":
                case "LowerArm_L":
                    humanBone = HumanBodyBones.LeftLowerArm;
                    return true;
                case "RightForeArm":
                    humanBone = HumanBodyBones.RightLowerArm;
                    return true;
                case "Elbow_R":
                case "LowerArm_R":
                    humanBone = HumanBodyBones.RightLowerArm;
                    return true;
                case "LeftHand":
                    humanBone = HumanBodyBones.LeftHand;
                    return true;
                case "RightHand":
                    humanBone = HumanBodyBones.RightHand;
                    return true;
                case "LeftUpLeg":
                    humanBone = HumanBodyBones.LeftUpperLeg;
                    return true;
                case "RightUpLeg":
                    humanBone = HumanBodyBones.RightUpperLeg;
                    return true;
                case "LeftLeg":
                    humanBone = HumanBodyBones.LeftLowerLeg;
                    return true;
                case "RightLeg":
                    humanBone = HumanBodyBones.RightLowerLeg;
                    return true;
                case "LeftFoot":
                    humanBone = HumanBodyBones.LeftFoot;
                    return true;
                case "RightFoot":
                    humanBone = HumanBodyBones.RightFoot;
                    return true;
                case "LeftToeBase":
                    humanBone = HumanBodyBones.LeftToes;
                    return true;
                case "RightToeBase":
                    humanBone = HumanBodyBones.RightToes;
                    return true;
                default:
                    humanBone = HumanBodyBones.LastBone;
                    return false;
            }
        }

        private static bool HasCriticalArmBones(Transform[] sourceBones, Transform[] reboundBones)
        {
            for (var i = 0; i < CriticalArmBoneCores.Length; i++)
            {
                var coreName = CriticalArmBoneCores[i];
                var found = false;
                for (var boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
                {
                    var sourceBone = sourceBones[boneIndex];
                    if (sourceBone == null)
                    {
                        continue;
                    }

                    if (!MatchesCriticalArmCore(sourceBone.name, coreName))
                    {
                        continue;
                    }

                    if (reboundBones[boneIndex] != null)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesCriticalArmCore(string sourceBoneName, string criticalCoreName)
        {
            var sourceCore = ExtractBoneCoreName(sourceBoneName);
            if (string.Equals(sourceCore, criticalCoreName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var alias in ResolveBoneAliases(criticalCoreName))
            {
                if (string.Equals(sourceCore, alias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var alias in ResolveBoneAliases(sourceCore))
            {
                if (string.Equals(alias, criticalCoreName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static Transform ResolveRootBone(
            Transform[] sourceBones,
            Transform[] reboundBones,
            Dictionary<string, Transform> characterBoneMap,
            Animator animator)
        {
            if (animator != null &&
                animator.isHuman &&
                animator.GetBoneTransform(HumanBodyBones.Hips) != null)
            {
                return animator.GetBoneTransform(HumanBodyBones.Hips);
            }

            for (var i = 0; i < sourceBones.Length; i++)
            {
                var sourceBone = sourceBones[i];
                if (sourceBone == null)
                {
                    continue;
                }

                var coreName = ExtractBoneCoreName(sourceBone.name);
                if (!string.Equals(coreName, "Hips", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return reboundBones[i] ?? ResolveCharacterBone(characterBoneMap, sourceBone.name);
            }

            return null;
        }

        private static Dictionary<string, Transform> BuildBodyBoneMap(SkinnedMeshRenderer bodyRenderer)
        {
            var map = new Dictionary<string, Transform>(128, StringComparer.OrdinalIgnoreCase);
            if (bodyRenderer?.bones != null)
            {
                for (var i = 0; i < bodyRenderer.bones.Length; i++)
                {
                    RegisterBoneWithAliases(map, bodyRenderer.bones[i]);
                }
            }

            if (bodyRenderer?.rootBone != null)
            {
                RegisterBoneWithAliases(map, bodyRenderer.rootBone);
            }

            return map;
        }

        private static Dictionary<string, Transform> BuildExpandedBoneMap(
            Transform skeletonRoot,
            Transform excludeFromBoneSearch,
            SkinnedMeshRenderer bodyRenderer,
            Dictionary<string, Transform> bodyBoneMap)
        {
            var map = new Dictionary<string, Transform>(bodyBoneMap.Count + 64, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in bodyBoneMap)
            {
                map[entry.Key] = entry.Value;
            }

            var transforms = skeletonRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var current = transforms[i];
                if (current == null || IsUnderSubtree(current, excludeFromBoneSearch))
                {
                    continue;
                }

                RegisterBoneWithAliases(map, current);
            }

            return map;
        }

        private static void RegisterBoneWithAliases(Dictionary<string, Transform> map, Transform bone)
        {
            RegisterBone(map, bone);
            if (bone == null)
            {
                return;
            }

            var aliases = ResolveBoneAliases(ExtractBoneCoreName(bone.name));
            for (var i = 0; i < aliases.Length; i++)
            {
                map[aliases[i]] = bone;
            }
        }

        private static void RegisterBone(Dictionary<string, Transform> map, Transform bone)
        {
            if (bone == null)
            {
                return;
            }

            map[bone.name] = bone;

            var coreName = ExtractBoneCoreName(bone.name);
            if (!string.IsNullOrWhiteSpace(coreName))
            {
                map[coreName] = bone;
            }
        }

        private static Transform ResolveCharacterBone(Dictionary<string, Transform> characterBoneMap, string boneName)
        {
            if (characterBoneMap == null || string.IsNullOrWhiteSpace(boneName))
            {
                return null;
            }

            if (characterBoneMap.TryGetValue(boneName, out var match) && match != null)
            {
                return match;
            }

            var coreName = ExtractBoneCoreName(boneName);
            if (!string.IsNullOrWhiteSpace(coreName) &&
                characterBoneMap.TryGetValue(coreName, out match) &&
                match != null)
            {
                return match;
            }

            var aliases = ResolveBoneAliases(coreName);
            for (var i = 0; i < aliases.Length; i++)
            {
                if (characterBoneMap.TryGetValue(aliases[i], out match) && match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static string[] ResolveBoneAliases(string coreName)
        {
            if (string.IsNullOrWhiteSpace(coreName))
            {
                return Array.Empty<string>();
            }

            switch (coreName)
            {
                case "Hips":
                    return new[] { "Root", "Pelvis" };
                case "Spine":
                    return new[] { "Spine_01" };
                case "Spine1":
                    return new[] { "Spine_02" };
                case "Spine2":
                    return new[] { "Spine_03", "Chest", "UpperChest" };
                case "LeftShoulder":
                    return new[] { "Clavicle_L" };
                case "RightShoulder":
                    return new[] { "Clavicle_R" };
                case "LeftArm":
                    return new[] { "Shoulder_L", "UpperArm_L" };
                case "RightArm":
                    return new[] { "Shoulder_R", "UpperArm_R" };
                case "LeftForeArm":
                    return new[] { "Elbow_L", "LowerArm_L" };
                case "RightForeArm":
                    return new[] { "Elbow_R", "LowerArm_R" };
                case "LeftHand":
                    return new[] { "Hand_L" };
                case "RightHand":
                    return new[] { "Hand_R" };
                case "LeftUpLeg":
                    return new[] { "Thigh_L", "UpperLeg_L" };
                case "RightUpLeg":
                    return new[] { "Thigh_R", "UpperLeg_R" };
                case "LeftLeg":
                    return new[] { "Knee_L", "LowerLeg_L" };
                case "RightLeg":
                    return new[] { "Knee_R", "LowerLeg_R" };
                case "LeftFoot":
                    return new[] { "Foot_L" };
                case "RightFoot":
                    return new[] { "Foot_R" };
                default:
                    return Array.Empty<string>();
            }
        }

        private static string ExtractBoneCoreName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var separatorIndex = name.LastIndexOf(':');
            return separatorIndex >= 0 && separatorIndex < name.Length - 1
                ? name.Substring(separatorIndex + 1)
                : name;
        }

        private static bool IsUnderSubtree(Transform node, Transform subtreeRoot)
        {
            if (node == null || subtreeRoot == null)
            {
                return false;
            }

            var current = node;
            while (current != null)
            {
                if (current == subtreeRoot)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static SkinnedMeshRenderer FindCharacterBodyRenderer(Transform syntyVisual)
        {
            if (syntyVisual == null)
            {
                return null;
            }

            var skinnedMeshes = syntyVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer best = null;
            var bestScore = int.MinValue;
            for (var i = 0; i < skinnedMeshes.Length; i++)
            {
                var candidate = skinnedMeshes[i];
                if (candidate == null || IsClothingRenderer(candidate))
                {
                    continue;
                }

                if (!CharacterModelApplier.IsCharacterBodyRenderer(candidate))
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

        private static bool IsClothingRenderer(SkinnedMeshRenderer renderer)
        {
            var current = renderer != null ? renderer.transform : null;
            while (current != null)
            {
                if (string.Equals(current.name, ClothingRootName, StringComparison.Ordinal))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static void ApplyClothingMaterialToInstance(GameObject instance, Material clothingMaterial)
        {
            if (instance == null || clothingMaterial == null)
            {
                return;
            }

            var skinnedRenderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var i = 0; i < skinnedRenderers.Length; i++)
            {
                var renderer = skinnedRenderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                AssignMaterialToAllSlots(renderer, clothingMaterial);
            }

            var meshRenderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < meshRenderers.Length; i++)
            {
                var renderer = meshRenderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                AssignMaterialToAllSlots(renderer, clothingMaterial);
            }
        }

        private static void AssignMaterialToAllSlots(Renderer renderer, Material clothingMaterial)
        {
            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                renderer.sharedMaterial = clothingMaterial;
                return;
            }

            for (var slot = 0; slot < materials.Length; slot++)
            {
                materials[slot] = clothingMaterial;
            }

            renderer.sharedMaterials = materials;
        }

        private static bool EnableStaticRenderers(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            var enabledAny = false;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;
                enabledAny = true;
            }

            return enabledAny;
        }

        private static void StripEmbeddedArmature(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            var armature = instance.transform.Find("Armature");
            if (armature != null)
            {
                Destroy(armature.gameObject);
                return;
            }

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            var keep = new HashSet<Transform>(renderers.Length * 4);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var current = renderer.transform;
                while (current != null && current != instance.transform)
                {
                    keep.Add(current);
                    current = current.parent;
                }
            }

            var transforms = instance.GetComponentsInChildren<Transform>(true);
            for (var i = transforms.Length - 1; i >= 0; i--)
            {
                var current = transforms[i];
                if (current == null ||
                    current == instance.transform ||
                    keep.Contains(current))
                {
                    continue;
                }

                Destroy(current.gameObject);
            }
        }

        private static void DisableColliders(GameObject instance)
        {
            var colliders = instance.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = false;
                }
            }
        }

        private void WarnMissingResourceOnce(string resourcePath)
        {
            if (warnedMissingResource)
            {
                return;
            }

            warnedMissingResource = true;
            Debug.LogWarning(
                $"[RemoteResourceClothingApplier] Resources.Load<GameObject>(\"{resourcePath}\") returned null.",
                this);
        }

        private void WarnIncompatibleRigOnce()
        {
            if (warnedIncompatibleRig)
            {
                return;
            }

            warnedIncompatibleRig = true;
            Debug.LogWarning(
                "[RemoteResourceClothingApplier] Clothing was loaded, but no compatible renderer could be applied. " +
                "Export hoodie FBX with Armature selected so skin weights are preserved.",
                this);
        }

        private void WarnStaticClothingOnce()
        {
            if (warnedStaticMesh)
            {
                return;
            }

            warnedStaticMesh = true;
            Debug.LogWarning(
                "[RemoteResourceClothingApplier] Clothing loaded as static mesh and will not follow animation. " +
                "Re-export from Blender with Armature + hoodie selected.",
                this);
        }

        private void WarnTorsoHideFailedOnce(SkinnedMeshRenderer bodyRenderer)
        {
            if (warnedTorsoHideFailed)
            {
                return;
            }

            warnedTorsoHideFailed = true;
            Debug.LogWarning(
                "[RemoteResourceClothingApplier] Could not hide body torso under clothing. " +
                "Enable Read/Write on the Ch36 body mesh import settings.",
                bodyRenderer);
        }

        private void WarnLegHideFailedOnce(SkinnedMeshRenderer bodyRenderer)
        {
            if (warnedLegHideFailed)
            {
                return;
            }

            warnedLegHideFailed = true;
            Debug.LogWarning(
                "[RemoteResourceClothingApplier] Could not hide body legs under pants. " +
                "Enable Read/Write on the Ch36 body mesh import settings.",
                bodyRenderer);
        }

        private void WarnFootHideFailedOnce(SkinnedMeshRenderer bodyRenderer)
        {
            if (warnedFootHideFailed)
            {
                return;
            }

            warnedFootHideFailed = true;
            Debug.LogWarning(
                "[RemoteResourceClothingApplier] Could not hide body feet under boots. " +
                "Enable Read/Write on the Ch36 body mesh import settings.",
                bodyRenderer);
        }

        private void LogHandBonesHiddenOnce(SkinnedMeshRenderer renderer, string context)
        {
            if (loggedHandBonesOnce || renderer == null)
            {
                return;
            }

            loggedHandBonesOnce = true;
            var bones = SyntyFirstPersonArmsMeshBuilder.CollectResolvedHandBoneNamesToHide(renderer.bones);
            var tokens = string.Join(", ", SyntyFirstPersonArmsMeshBuilder.HandHideBoneNameTokens);
            Debug.Log(
                $"[RemoteResourceClothingApplier] Glove palm hide ({context}). " +
                $"Matched rig bones ({bones.Count}): {string.Join(", ", bones)}. " +
                $"Name tokens: {tokens}. " +
                "Preserved forearm bones: ForeArm, LowerArm, Elbow. " +
                $"Vertices stay when hand weight must exceed forearm weight; triangles at the wrist stay unless all 3 corners are palm.",
                renderer);
        }

        private void WarnHandHideFailedOnce(SkinnedMeshRenderer bodyRenderer)
        {
            if (warnedHandHideFailed)
            {
                return;
            }

            warnedHandHideFailed = true;
            Debug.LogWarning(
                "[RemoteResourceClothingApplier] Could not hide body hands under gloves. " +
                "Enable Read/Write on the Ch36 body mesh import settings.",
                bodyRenderer);
        }
    }
}
