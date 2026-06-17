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

        [SerializeField] private string spawnPointName = "SpawnPlayer";
        [SerializeField] private GameObject remotePlayerPrefab;
        [SerializeField] private string charactersResourcesFolder = "Characters";
        [SerializeField] private bool playIdleAnimation = true;
        [SerializeField] private bool equipRandomLobbyWeapon = true;

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

        private bool allowPreview;

        public void SetAllowPreview(bool allow)
        {
            if (allowPreview == allow)
            {
                if (allow)
                {
                    if (previewInstance == null)
                    {
                        SpawnPreview();
                    }
                    else
                    {
                        RefreshSkins();
                    }
                }

                return;
            }

            allowPreview = allow;
            if (!allowPreview)
            {
                DestroyPreviewInstance();
                return;
            }

            if (previewInstance == null)
            {
                SpawnPreview();
            }
            else
            {
                RefreshSkins();
            }
        }

        public void Refresh()
        {
            DestroyPreviewInstance();
            SpawnPreview();
        }

        public void RefreshSkins()
        {
            if (!allowPreview)
            {
                return;
            }

            if (previewInstance == null)
            {
                SpawnPreview();
                return;
            }

            PlayerSkinSelectionService.ApplyToPlayer(previewInstance, forceReapply: true);
            EnsurePreviewBodyVisible(previewInstance);
            RefreshPinnedWeaponPreview();
        }

        public void ShowWeaponWithSkin(WeaponKind kind)
        {
            pinnedLobbyWeaponKind = kind;
            if (!allowPreview)
            {
                return;
            }

            if (previewInstance == null)
            {
                SpawnPreview();
                return;
            }

            RequestEquipPinnedWeapon(forceReequip: true);
        }

        private void OnEnable()
        {
            PlayerProfileService.ProfileSynced += HandleProfileSynced;
            PlayerSkinOwnershipService.EquipmentChanged += HandleEquipmentChanged;
        }

        private void OnDisable()
        {
            PlayerProfileService.ProfileSynced -= HandleProfileSynced;
            PlayerSkinOwnershipService.EquipmentChanged -= HandleEquipmentChanged;
            if (equipWeaponCoroutine != null)
            {
                StopCoroutine(equipWeaponCoroutine);
                equipWeaponCoroutine = null;
            }

            DestroyPreviewInstance();
        }

        private void HandleProfileSynced()
        {
            if (!allowPreview)
            {
                return;
            }

            RefreshSkins();
        }

        private void HandleEquipmentChanged()
        {
            if (!allowPreview || previewInstance == null)
            {
                return;
            }

            RefreshSkins();
        }

        private void SpawnPreview()
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
                ApplySelectedCharacterAndSkins(previewInstance);
                FinalizeMenuPreviewPresentation(previewInstance);
                EnsurePreviewBodyVisible(previewInstance);
                ApplyIdlePose(previewInstance);

                if (equipRandomLobbyWeapon)
                {
                    if (!pinnedLobbyWeaponKind.HasValue)
                    {
                        pinnedLobbyWeaponKind = LobbyWeaponKinds[Random.Range(0, LobbyWeaponKinds.Length)];
                    }

                    RequestEquipPinnedWeapon(forceReequip: true);
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

            for (var attempt = 0; attempt < 30; attempt++)
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
        }

        private Transform ResolveSpawnPoint()
        {
            if (!string.IsNullOrWhiteSpace(spawnPointName))
            {
                var named = GameObject.Find(spawnPointName);
                if (named != null)
                {
                    return named.transform;
                }
            }

            return transform;
        }

        private void ApplySelectedCharacterAndSkins(GameObject root)
        {
            var selectedModel = CharacterSelectionService.ResolveSelectedModel(charactersResourcesFolder);
            if (selectedModel.ModelAsset != null)
            {
                CharacterModelApplier.TryApplyToPlayer(root, selectedModel.ModelAsset);
            }
            else
            {
                var bootstrap = root.GetComponent<RemoteThirdPersonPlayerBootstrap>();
                if (bootstrap != null)
                {
                    bootstrap.ApplyRemoteThirdPersonMode();
                }
            }

            ApplySelectedSkins(root);
        }

        private static void ApplySelectedSkins(GameObject root)
        {
            PlayerSkinSelectionService.ApplyToPlayer(root, forceReapply: true);
        }

        private static void FinalizeMenuPreviewPresentation(GameObject root)
        {
            var bootstrap = root.GetComponent<RemoteThirdPersonPlayerBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.ApplyRemoteThirdPersonMode();
            }

            ResolveWeaponPresentation(root)?.SetMenuPreviewMode(true);
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

            root.GetComponent<SyntySplitBodyPresentation>()?.ApplyViewMode();

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

        private static void EnsurePreviewBodyVisible(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var presentation = root.GetComponent<PlayerViewPresentation>();
            presentation?.Configure(false);

            var thirdPersonBody = root.transform.Find("ThirdPersonBody");
            if (thirdPersonBody != null)
            {
                thirdPersonBody.gameObject.SetActive(true);

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

            root.GetComponent<SyntySplitBodyPresentation>()?.ApplyViewMode();
        }

        private void DestroyPreviewInstance()
        {
            if (previewInstance == null)
            {
                return;
            }

            Destroy(previewInstance);
            previewInstance = null;
        }
    }
}
