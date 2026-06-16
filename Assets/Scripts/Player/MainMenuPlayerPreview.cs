using ShooterPrototype.UI;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(40)]
    public sealed class MainMenuPlayerPreview : MonoBehaviour
    {
        public const string PreviewObjectName = "MainMenuPlayerPreview";

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

        public void Refresh()
        {
            DestroyPreviewInstance();
            SpawnPreview();
        }

        private void OnDisable()
        {
            DestroyPreviewInstance();
        }

        private void SpawnPreview()
        {
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

            previewInstance = Instantiate(
                remotePlayerPrefab,
                spawnPoint.position,
                spawnPoint.rotation);
            previewInstance.name = PreviewObjectName;
            previewInstance.transform.SetParent(spawnPoint, true);

            WireAsMenuPreview(previewInstance);
            ApplySelectedCharacterAndSkins(previewInstance);
            if (equipRandomLobbyWeapon)
            {
                ApplyRandomLobbyWeapon(previewInstance);
            }

            ApplyIdlePose(previewInstance);
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

        private static void ApplyRandomLobbyWeapon(GameObject root)
        {
            var thirdPersonBody = root.transform.Find("ThirdPersonBody");
            if (thirdPersonBody == null)
            {
                return;
            }

            var weaponPresentation = root.GetComponent<RemoteWeaponPresentation>();
            if (weaponPresentation == null)
            {
                weaponPresentation = root.AddComponent<RemoteWeaponPresentation>();
            }

            weaponPresentation.Configure(thirdPersonBody);
            weaponPresentation.InvalidateAttachTarget();

            var kind = LobbyWeaponKinds[Random.Range(0, LobbyWeaponKinds.Length)];
            var kindByte = (byte)kind;
            weaponPresentation.SetWeaponLoadout(
                kindByte,
                PlayerWeaponLoadout.EmptySlotKind,
                0,
                holstered: false,
                hasWeapon: true,
                activeWeaponKind: kindByte);
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
            root.GetComponent<RemoteThirdPersonPlayerBootstrap>()?.ApplyRemoteThirdPersonMode();

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
