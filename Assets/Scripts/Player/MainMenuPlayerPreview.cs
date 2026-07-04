using System.Collections;
using ShooterPrototype.UI;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(40)]
    public sealed class MainMenuPlayerPreview : MonoBehaviour
    {
        public const string PreviewObjectName = "MainMenuPlayerPreview";

        internal static bool IsMenuPreviewSpawn { get; private set; }

        public static bool IsMenuPreviewInstance(GameObject root)
        {
            return root != null &&
                   string.Equals(root.name, PreviewObjectName, System.StringComparison.Ordinal);
        }

        [SerializeField] private string spawnPointName = "SpawnPlayer";
        [SerializeField] private GameObject remotePlayerPrefab;
        [SerializeField] private string charactersResourcesFolder = "Characters";
        [SerializeField] private bool playIdleAnimation = true;
        [SerializeField] private bool equipRandomLobbyWeapon = false;

        private static readonly WeaponKind[] LobbyWeaponKinds =
        {
            WeaponKind.AssaultRifle,
            WeaponKind.SniperRifle,
            WeaponKind.Pistol,
            WeaponKind.Mp7
        };

        private GameObject previewInstance;
        private WeaponKind? pinnedLobbyWeaponKind;
        private Coroutine equipWeaponCoroutine;
        private Coroutine spawnPreviewCoroutine;
        private Coroutine revealPreviewCoroutine;
        private Transform cachedSpawnPoint;

        private bool allowPreview;
        private bool previewCharacterRevealed;

        public bool IsPreviewActive => allowPreview;

        public void PreviewInventoryItem(PlayerSkinDefinition item)
        {
            if (!item.IsValid)
            {
                return;
            }

            if (PlayerSkinSelectionService.TryResolveWeaponKind(item, out var weaponKind))
            {
                PreviewWeaponSkin(weaponKind);
                return;
            }

            if (!allowPreview)
            {
                SetAllowPreview(true);
                return;
            }

            if (previewInstance == null)
            {
                RequestSpawnPreview();
                return;
            }

            ApplyPreviewAppearance(previewInstance, keepVisible: previewCharacterRevealed);
            ClearMenuWeaponPresentation(previewInstance);
        }

        public void SetAllowPreview(bool allow)
        {
            if (allowPreview == allow)
            {
                if (allow && previewInstance == null)
                {
                    RequestSpawnPreview();
                }

                return;
            }

            allowPreview = allow;
            if (!allowPreview)
            {
                CancelSpawnPreviewCoroutine();
                previewCharacterRevealed = false;
                DestroyPreviewInstance();
                return;
            }

            RequestSpawnPreview();
        }

        public void Refresh()
        {
            CancelSpawnPreviewCoroutine();
            previewCharacterRevealed = false;
            DestroyPreviewInstance();
            RequestSpawnPreview();
        }

        public void RefreshSkins()
        {
            if (!allowPreview)
            {
                return;
            }

            if (previewInstance == null)
            {
                RequestSpawnPreview();
                return;
            }

            ApplyPreviewAppearance(previewInstance, keepVisible: previewCharacterRevealed);
            ClearMenuWeaponPresentation(previewInstance);
        }

        public void ShowWeaponWithSkin(WeaponKind kind)
        {
            PreviewWeaponSkin(kind);
        }

        private void PreviewWeaponSkin(WeaponKind weaponKind)
        {
            pinnedLobbyWeaponKind = weaponKind;

            if (!allowPreview)
            {
                SetAllowPreview(true);
                return;
            }

            if (previewInstance == null)
            {
                RequestSpawnPreview();
                return;
            }

            ApplyPreviewAppearance(previewInstance, keepVisible: previewCharacterRevealed);
            RequestEquipPinnedWeapon(forceReequip: true);
        }

        private void RequestSpawnPreview()
        {
            if (!allowPreview)
            {
                return;
            }

            if (spawnPreviewCoroutine != null)
            {
                StopCoroutine(spawnPreviewCoroutine);
            }

            spawnPreviewCoroutine = StartCoroutine(SpawnPreviewWhenReady());
        }

        private void CancelSpawnPreviewCoroutine()
        {
            if (spawnPreviewCoroutine != null)
            {
                StopCoroutine(spawnPreviewCoroutine);
                spawnPreviewCoroutine = null;
            }
        }

        private void CancelRevealPreviewCoroutine()
        {
            if (revealPreviewCoroutine != null)
            {
                StopCoroutine(revealPreviewCoroutine);
                revealPreviewCoroutine = null;
            }
        }

        private IEnumerator SpawnPreviewWhenReady()
        {
            for (var attempt = 0; attempt < 90; attempt++)
            {
                if (!allowPreview)
                {
                    spawnPreviewCoroutine = null;
                    yield break;
                }

                ResolveRemotePrefab();
                if (remotePlayerPrefab != null && ResolveSpawnPoint() != null)
                {
                    break;
                }

                yield return null;
            }

            spawnPreviewCoroutine = null;

            if (!allowPreview || remotePlayerPrefab == null)
            {
                Debug.LogWarning("[MainMenuPlayerPreview] Remote player prefab was not ready for preview spawn.");
                yield break;
            }

            while (allowPreview && IsWaitingForInitialProfileSync())
            {
                yield return null;
            }

            if (previewInstance != null)
            {
                DestroyPreviewInstance();
            }

            CleanupSpawnPointOrphans();
            SpawnPreviewInternal();
        }

        private void OnEnable()
        {
            PlayerProfileService.ProfileSynced += HandleProfileSynced;
            PlayerSkinOwnershipService.EquipmentChanged += HandleEquipmentChanged;
            if (isActiveAndEnabled)
            {
                CleanupSpawnPointOrphans();
            }
        }

        private void OnDisable()
        {
            PlayerProfileService.ProfileSynced -= HandleProfileSynced;
            PlayerSkinOwnershipService.EquipmentChanged -= HandleEquipmentChanged;
            CancelSpawnPreviewCoroutine();
            CancelRevealPreviewCoroutine();
            if (equipWeaponCoroutine != null)
            {
                StopCoroutine(equipWeaponCoroutine);
                equipWeaponCoroutine = null;
            }

            if (previewInstance != null)
            {
                Destroy(previewInstance);
                previewInstance = null;
                previewCharacterRevealed = false;
            }

            cachedSpawnPoint = null;
        }

        private void HandleProfileSynced()
        {
            if (!allowPreview)
            {
                return;
            }

            if (previewInstance == null)
            {
                RequestSpawnPreview();
                return;
            }

            ApplyPreviewAppearance(previewInstance, keepVisible: previewCharacterRevealed);
            ClearMenuWeaponPresentation(previewInstance);
        }

        private void HandleEquipmentChanged()
        {
            if (!allowPreview)
            {
                return;
            }

            if (previewInstance == null)
            {
                RequestSpawnPreview();
                return;
            }

            ApplyPreviewAppearance(previewInstance, keepVisible: previewCharacterRevealed);
            ClearMenuWeaponPresentation(previewInstance);
        }

        private static bool IsWaitingForInitialProfileSync()
        {
            if (PlayerProfileService.IsServerSynced)
            {
                return false;
            }

            var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
            return menu != null && menu.IsProfileSyncInProgress;
        }

        private void SpawnPreviewInternal()
        {
            if (!allowPreview)
            {
                return;
            }

            ResolveRemotePrefab();
            if (remotePlayerPrefab == null)
            {
                Debug.LogWarning("[MainMenuPlayerPreview] Remote player prefab is not assigned.");
                return;
            }

            var spawnPoint = ResolveSpawnPoint();
            if (spawnPoint == null)
            {
                Debug.LogWarning($"[MainMenuPlayerPreview] Spawn point '{spawnPointName}' was not found.");
                return;
            }

            IsMenuPreviewSpawn = true;
            try
            {
                previewInstance = Instantiate(
                    remotePlayerPrefab,
                    spawnPoint.position,
                    spawnPoint.rotation);
                previewInstance.name = PreviewObjectName;
                previewInstance.transform.SetParent(spawnPoint, true);

                WireAsMenuPreview(previewInstance);
                ApplyPreviewAppearance(previewInstance);
                ApplyIdlePose(previewInstance);
                if (pinnedLobbyWeaponKind.HasValue)
                {
                    RequestEquipPinnedWeapon(forceReequip: true);
                }
                else
                {
                    ClearMenuWeaponPresentation(previewInstance);
                }
            }
            finally
            {
                IsMenuPreviewSpawn = false;
            }
        }

        private void RequestEquipPinnedWeapon(bool forceReequip)
        {
            if (!equipRandomLobbyWeapon || previewInstance == null)
            {
                return;
            }

            if (equipWeaponCoroutine != null)
            {
                StopCoroutine(equipWeaponCoroutine);
            }

            equipWeaponCoroutine = StartCoroutine(EquipPinnedWeaponWhenReady(forceReequip));
        }

        private IEnumerator EquipPinnedWeaponWhenReady(bool forceReequip)
        {
            yield return null;
            yield return new WaitForEndOfFrame();

            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (previewInstance == null || !equipRandomLobbyWeapon)
                {
                    equipWeaponCoroutine = null;
                    yield break;
                }

                ResolveWeaponPresentation(previewInstance)?.SetMenuPreviewMode(true);
                EquipPinnedWeaponPreview(forceReequip);

                var weaponPresentation = previewInstance.GetComponent<RemoteWeaponPresentation>();
                if (weaponPresentation != null &&
                    pinnedLobbyWeaponKind.HasValue &&
                    weaponPresentation.TryGetHandWeaponKind(out var visibleKind) &&
                    visibleKind == pinnedLobbyWeaponKind.Value)
                {
                    break;
                }

                forceReequip = true;
                yield return null;
                if (attempt % 3 == 2)
                {
                    yield return new WaitForEndOfFrame();
                }
            }

            equipWeaponCoroutine = null;
        }

        private void RefreshPinnedWeaponPreview()
        {
            if (!equipRandomLobbyWeapon || previewInstance == null)
            {
                return;
            }

            if (!pinnedLobbyWeaponKind.HasValue)
            {
                pinnedLobbyWeaponKind = LobbyWeaponKinds[Random.Range(0, LobbyWeaponKinds.Length)];
            }

            RequestEquipPinnedWeapon(forceReequip: true);
        }

        private void EquipPinnedWeaponPreview(bool forceReequip)
        {
            if (!equipRandomLobbyWeapon || previewInstance == null || !pinnedLobbyWeaponKind.HasValue)
            {
                return;
            }

            var weaponPresentation = ResolveWeaponPresentation(previewInstance);
            if (weaponPresentation == null)
            {
                return;
            }

            weaponPresentation.InvalidateAttachTarget();
            previewInstance.GetComponent<RemoteThirdPersonPlayerBootstrap>()?.ApplyRemoteThirdPersonMode(activateThirdPersonBody: true);

            if (!weaponPresentation.TryEnsureMenuAttachTarget())
            {
                return;
            }

            var kind = pinnedLobbyWeaponKind.Value;
            if (!forceReequip &&
                weaponPresentation.TryGetHandWeaponKind(out var visibleKind) &&
                visibleKind == kind)
            {
                weaponPresentation.ReapplyMenuPreviewSkin(kind);
                return;
            }

            weaponPresentation.EquipMenuPreviewWeapon(kind);
        }

        private static RemoteWeaponPresentation ResolveWeaponPresentation(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var weaponPresentation = root.GetComponent<RemoteWeaponPresentation>();
            if (weaponPresentation != null)
            {
                weaponPresentation.SetMenuPreviewMode(true);
                return weaponPresentation;
            }

            var thirdPersonBody = root.transform.Find("ThirdPersonBody");
            if (thirdPersonBody == null)
            {
                return null;
            }

            weaponPresentation = root.AddComponent<RemoteWeaponPresentation>();
            weaponPresentation.SetMenuPreviewMode(true);
            weaponPresentation.Configure(thirdPersonBody);
            return weaponPresentation;
        }

        private void ResolveRemotePrefab()
        {
            if (remotePlayerPrefab != null)
            {
                return;
            }

            var spawnManager = FindFirstObjectByType<PlayerSpawnManager>();
            if (spawnManager != null)
            {
                remotePlayerPrefab = spawnManager.GetRemotePlayerPrefabForPreview();
            }

#if UNITY_EDITOR
            if (remotePlayerPrefab == null)
            {
                remotePlayerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Player/PlayerCleanRemote.prefab");
            }
#endif
        }

        private Transform ResolveSpawnPoint()
        {
            if (cachedSpawnPoint != null)
            {
                return cachedSpawnPoint;
            }

            if (!string.IsNullOrWhiteSpace(spawnPointName))
            {
                var found = FindTransformByNameIncludingInactive(spawnPointName);
                if (found != null)
                {
                    cachedSpawnPoint = found;
                    return cachedSpawnPoint;
                }
            }

            return transform;
        }

        private Transform FindTransformByNameIncludingInactive(string targetName)
        {
            var scene = gameObject.scene;
            if (!scene.IsValid())
            {
                return null;
            }

            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var found = FindTransformRecursive(roots[i].transform, targetName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindTransformRecursive(Transform current, string targetName)
        {
            if (current == null)
            {
                return null;
            }

            if (string.Equals(current.name, targetName, System.StringComparison.Ordinal))
            {
                return current;
            }

            for (var i = 0; i < current.childCount; i++)
            {
                var found = FindTransformRecursive(current.GetChild(i), targetName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void ApplyPreviewAppearance(GameObject root, bool keepVisible = false)
        {
            if (root == null)
            {
                return;
            }

            CancelRevealPreviewCoroutine();
            SetPreviewRenderersHidden(root, hidden: true);
            SetPreviewBodyHidden(root, hidden: false);

            ApplyMenuPreviewBootstrap(root, activateBody: false);
            ApplyCharacterModelAndSkins(root);
            ApplyMenuPreviewWeaponSetup(root);
            SetPreviewRenderersHidden(root, hidden: true);

            if (!IsPreviewAssemblyComplete(root))
            {
                revealPreviewCoroutine = StartCoroutine(RevealPreviewWhenAssemblyReady(root));
                return;
            }

            RevealPreviewCharacter(root);
            previewCharacterRevealed = true;
        }

        private IEnumerator RevealPreviewWhenAssemblyReady(GameObject root)
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (!allowPreview || root == null)
                {
                    revealPreviewCoroutine = null;
                    yield break;
                }

                if (IsPreviewAssemblyComplete(root))
                {
                    RevealPreviewCharacter(root);
                    previewCharacterRevealed = true;
                    revealPreviewCoroutine = null;
                    yield break;
                }

                yield return null;
            }

            revealPreviewCoroutine = null;
        }

        private void ApplyCharacterModelAndSkins(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            if (PlayerProfileService.IsServerSynced)
            {
                var selectedModel = CharacterSelectionService.ResolveSelectedModel(charactersResourcesFolder);
                if (selectedModel.ModelAsset != null)
                {
                    CharacterModelApplier.TryApplyToPlayer(root, selectedModel.ModelAsset);
                }
            }
            else if (!CharacterModelApplier.HasRenderableCharacterBody(root))
            {
                var selectedModel = CharacterSelectionService.ResolveSelectedModel(charactersResourcesFolder);
                if (selectedModel.ModelAsset != null)
                {
                    CharacterModelApplier.TryApplyToPlayer(root, selectedModel.ModelAsset);
                }
            }

            if (PlayerProfileService.IsServerSynced)
            {
                PlayerSkinSelectionService.ApplyToPlayer(root, forceReapply: true);
            }
            else
            {
                PlayerSkinSelectionService.ApplyDefaultSkinsToPlayer(root, forceReapply: true);
            }
        }

        private static void ApplyMenuPreviewBootstrap(GameObject root, bool activateBody)
        {
            if (root == null || !MainMenuPlayerPreview.IsMenuPreviewInstance(root))
            {
                root?.GetComponent<RemoteThirdPersonPlayerBootstrap>()?.ApplyRemoteThirdPersonMode(activateBody);
                return;
            }

            root.GetComponent<RemoteThirdPersonPlayerBootstrap>()?.ApplyRemoteThirdPersonMode(activateThirdPersonBody: false);
        }

        private static void ApplyMenuPreviewWeaponSetup(GameObject root)
        {
            ResolveWeaponPresentation(root)?.SetMenuPreviewMode(true);
            ClearMenuWeaponPresentation(root);
        }

        private static bool IsPreviewAssemblyComplete(GameObject root)
        {
            return CharacterModelApplier.HasRenderableCharacterBody(root);
        }

        private static void RevealPreviewCharacter(GameObject root)
        {
            SetPreviewBodyHidden(root, hidden: false);
            EnsurePreviewBodyShown(root);
        }

        private static void SetPreviewRenderersHidden(GameObject root, bool hidden)
        {
            if (root == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = !hidden;
            }
        }

        private static void EnsurePreviewBodyShown(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var thirdPersonBody = root.transform.Find("ThirdPersonBody");
            if (thirdPersonBody != null)
            {
                thirdPersonBody.gameObject.SetActive(true);
            }

            ApplyMenuPreviewBodyClipping(root);
            ForceEnableMenuPreviewRenderers(thirdPersonBody);
            ApplyMenuPreviewBodyClipping(root);
        }

        private static void ApplyMenuPreviewBodyClipping(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var thirdPersonBody = root.transform.Find("ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            var clothingApplier = root.GetComponent<RemoteResourceClothingApplier>();
            if (clothingApplier == null)
            {
                return;
            }

            if (syntyVisual.Find(RemoteResourceClothingApplier.RemoteClothingRootName) == null)
            {
                return;
            }

            clothingApplier.InvalidateBodyMeshCache();
            clothingApplier.RefreshHiddenBodyForVisual(syntyVisual, hideHandsOnBody: true);
        }

        private static void SetPreviewBodyHidden(GameObject root, bool hidden)
        {
            if (root == null)
            {
                return;
            }

            var thirdPersonBody = root.transform.Find("ThirdPersonBody");
            if (thirdPersonBody != null)
            {
                thirdPersonBody.gameObject.SetActive(!hidden);
            }
        }

        private static void ClearMenuWeaponPresentation(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var thirdPersonBody = root.transform.Find("ThirdPersonBody");
            var weaponPresentation = ResolveWeaponPresentation(root);
            if (weaponPresentation == null)
            {
                return;
            }

            weaponPresentation.SetMenuPreviewMode(true);
            if (thirdPersonBody != null)
            {
                weaponPresentation.ResetForMenuPreview(thirdPersonBody);
            }

            weaponPresentation.SetMenuPreviewMode(true);
        }

        private void ApplyIdlePose(GameObject root)
        {
            if (!playIdleAnimation)
            {
                return;
            }

            var locomotionRig = root.GetComponentInChildren<ProceduralLocomotionRig>(true);
            if (locomotionRig != null)
            {
                locomotionRig.SetNetworkMoveInput(0f, 0f);
                locomotionRig.SetNetworkAnimationState(0f, true, 0, 0f, false, false);
                locomotionRig.SetNetworkLookPitch(0f);
            }
        }

        private static void WireAsMenuPreview(GameObject root)
        {
            var presentation = root.GetComponent<PlayerViewPresentation>();
            presentation?.Configure(false);

            var splitBody = root.GetComponent<SyntySplitBodyPresentation>();
            if (splitBody != null)
            {
                splitBody.enabled = false;
            }

            var visibilityGate = root.GetComponent<EnemyPresentationVisibilityGate>();
            if (visibilityGate != null)
            {
                visibilityGate.enabled = false;
            }

            DisableLocalOnlyPreviewComponents(root);
        }

        private static void DisableLocalOnlyPreviewComponents(GameObject root)
        {
            var fpsController = root.GetComponent<FpsCharacterController>();
            if (fpsController != null)
            {
                fpsController.enabled = false;
            }

            var characterController = root.GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            var locomotionRig = root.GetComponentInChildren<ProceduralLocomotionRig>(true);
            locomotionRig?.SetNetworkMode(true);

            var syntyDriver = root.GetComponentInChildren<SyntyLocomotionDriver>(true);
            syntyDriver?.SetNetworkMode(true);

            var weaponMount = root.GetComponent<PlayerWeaponMount>();
            if (weaponMount != null)
            {
                weaponMount.SetNetworkMode(true);
            }

            var sync = root.GetComponent<MatchPresenceSync>();
            if (sync != null)
            {
                sync.enabled = false;
            }

            var localMarker = root.GetComponent<LocalPlayerMarker>();
            if (localMarker != null)
            {
                localMarker.enabled = false;
            }

            foreach (var inventory in root.GetComponents<PlayerInventoryPanelController>())
            {
                inventory.enabled = false;
            }

            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (camera != null)
                {
                    camera.enabled = false;
                }
            }

            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true))
            {
                if (listener != null)
                {
                    listener.enabled = false;
                }
            }
        }

        private static void ForceEnableMenuPreviewRenderers(Transform thirdPersonBody)
        {
            if (thirdPersonBody == null)
            {
                return;
            }

            var renderers = thirdPersonBody.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.gameObject.name.EndsWith("_FirstPersonArms", System.StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                    continue;
                }

                renderer.enabled = true;
            }
        }

        private void DestroyPreviewInstance()
        {
            CancelRevealPreviewCoroutine();

            if (previewInstance == null)
            {
                if (isActiveAndEnabled)
                {
                    CleanupSpawnPointOrphans();
                }

                return;
            }

            Destroy(previewInstance);
            previewInstance = null;
            previewCharacterRevealed = false;

            if (isActiveAndEnabled)
            {
                CleanupSpawnPointOrphans();
            }
        }

        private void CleanupSpawnPointOrphans()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            var spawnPoint = ResolveSpawnPoint();
            if (spawnPoint == null)
            {
                return;
            }

            for (var i = spawnPoint.childCount - 1; i >= 0; i--)
            {
                var child = spawnPoint.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (previewInstance != null && child.gameObject == previewInstance)
                {
                    continue;
                }

                if (IsMenuPreviewInstance(child.gameObject) ||
                    IsOrphanedAttachmentRoot(child))
                {
                    Destroy(child.gameObject);
                }
            }
        }

        private static bool IsOrphanedAttachmentRoot(Transform child)
        {
            if (child == null)
            {
                return false;
            }

            if (string.Equals(child.name, "face", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(child.name, "hair", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return child.name.IndexOf("Attach", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
