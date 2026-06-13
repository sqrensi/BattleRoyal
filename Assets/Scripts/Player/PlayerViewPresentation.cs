using UnityEngine;

namespace ShooterPrototype.Player
{
    public sealed class PlayerViewPresentation : MonoBehaviour
    {
        [Header("View Roots")]
        [SerializeField] private GameObject firstPersonRoot;
        [SerializeField] private GameObject thirdPersonRoot;
        [SerializeField] private bool isLocalPlayer = true;
        [SerializeField] private bool useSingleModelForFirstPerson = true;
        [SerializeField] private string armNameContains = "Thread";
        [SerializeField] private string weaponNameContains = "Weapon";

        private bool forceThirdPersonBody;

        public bool IsLocalPlayerView => isLocalPlayer;
        public bool UsesSingleModelForFirstPerson => useSingleModelForFirstPerson;
        public GameObject FirstPersonViewRoot => firstPersonRoot;
        public GameObject ThirdPersonBodyRoot => thirdPersonRoot;

        public void ConfigureRoots(GameObject firstPerson, GameObject thirdPerson)
        {
            firstPersonRoot = firstPerson;
            thirdPersonRoot = thirdPerson;
        }

        public void Configure(bool localPlayer)
        {
            isLocalPlayer = localPlayer;
            ApplyViewMode();
        }

        public void ConfigureSplitView(GameObject firstPerson, GameObject thirdPerson, bool localPlayer)
        {
            firstPersonRoot = firstPerson;
            thirdPersonRoot = thirdPerson;
            useSingleModelForFirstPerson = false;
            isLocalPlayer = localPlayer;
            ApplyViewMode();
        }

        public void RefreshViewMode()
        {
            ApplyViewMode();
        }

        public void SetForceThirdPersonBody(bool force)
        {
            forceThirdPersonBody = force;
            ApplyViewMode();
        }

        private void Awake()
        {
            ApplyViewMode();
        }

        private void Start()
        {
            ApplyViewMode();
        }

        private void ApplyViewMode()
        {
            if (useSingleModelForFirstPerson && thirdPersonRoot != null)
            {
                if (firstPersonRoot != null)
                {
                    firstPersonRoot.SetActive(false);
                }

                thirdPersonRoot.SetActive(true);
                ApplySingleModelVisibility();
                return;
            }

            var splitBody = GetComponent<SyntySplitBodyPresentation>();
            if (splitBody != null && thirdPersonRoot != null)
            {
                if (firstPersonRoot != null)
                {
                    firstPersonRoot.SetActive(isLocalPlayer);
                }

                thirdPersonRoot.SetActive(true);
                splitBody.ApplyViewMode();
                return;
            }

            if (firstPersonRoot != null)
            {
                firstPersonRoot.SetActive(isLocalPlayer);
            }

            if (thirdPersonRoot != null)
            {
                thirdPersonRoot.SetActive(!isLocalPlayer);
            }
        }

        private void ApplySingleModelVisibility()
        {
            if (thirdPersonRoot == null)
            {
                return;
            }

            var syntyBinder = GetComponent<SyntyCharacterVisualBinder>();
            var armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
            var localFirstPersonView = isLocalPlayer;
            var hideArms = forceThirdPersonBody || ShouldHideFirstPersonArms();
            if (hideArms)
            {
                armsPresenter?.SetHolsteredArmsPresentation(true);
            }
            else
            {
                armsPresenter?.ApplyFirstPersonVisibility(localFirstPersonView);
            }

            DisableStaleFirstPersonArmsRenderers(armsPresenter);

            var renderers = thirdPersonRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!isLocalPlayer)
                {
                    renderer.enabled = true;
                    continue;
                }

                if (forceThirdPersonBody)
                {
                    renderer.enabled = true;
                    continue;
                }

                // Keep shadow-only proxies enabled for local first-person view.
                // They are invisible but needed to cast the character shadow.
                if (renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                {
                    renderer.enabled = true;
                    continue;
                }

                // FP arms can be generated under different roots and names (e.g. *_NoShoulders).
                if (IsFirstPersonArmsRendererName(renderer.gameObject.name))
                {
                    renderer.enabled = localFirstPersonView && !hideArms;
                    continue;
                }

                if (armsPresenter != null && armsPresenter.IsFirstPersonGloveRenderer(renderer))
                {
                    renderer.enabled = localFirstPersonView && !hideArms;
                    continue;
                }

                if (syntyBinder != null && syntyBinder.ShouldHideSourceBodyInFirstPerson(renderer))
                {
                    renderer.enabled = false;
                    continue;
                }

                if (armsPresenter != null && armsPresenter.IsFirstPersonArmsRenderer(renderer))
                {
                    renderer.enabled = localFirstPersonView && !hideArms;
                    continue;
                }

                var objectName = renderer.gameObject.name;
                renderer.enabled = IsFirstPersonVisibleRenderer(renderer.transform, objectName);
            }
        }

        private bool IsFirstPersonVisibleRenderer(Transform rendererTransform, string objectName)
        {
            var syntyBinder = GetComponent<SyntyCharacterVisualBinder>();
            if (syntyBinder != null)
            {
                if (syntyBinder.IsRendererUnderSyntyVisual(rendererTransform))
                {
                    if (syntyBinder.ShowSyntyMeshInFirstPerson)
                    {
                        return true;
                    }

                    if (syntyBinder.ShouldHideRendererInFirstPerson(objectName, rendererTransform))
                    {
                        return false;
                    }

                    if (syntyBinder.ShouldShowRendererInFirstPerson(objectName, rendererTransform))
                    {
                        return true;
                    }

                    return false;
                }

                if (syntyBinder.ShouldHideRendererInFirstPerson(objectName, rendererTransform))
                {
                    return false;
                }

                if (syntyBinder.ShouldShowRendererInFirstPerson(objectName, rendererTransform))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(objectName) &&
                objectName.IndexOf(armNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(objectName) &&
                objectName.IndexOf(weaponNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var current = rendererTransform != null ? rendererTransform.parent : null;
            while (current != null)
            {
                var parentName = current.name;
                if ((!string.IsNullOrWhiteSpace(parentName) &&
                     parentName.IndexOf(armNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(parentName) &&
                     parentName.IndexOf(weaponNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private void DisableStaleFirstPersonArmsRenderers(SyntyFirstPersonArmsPresenter armsPresenter)
        {
            if (thirdPersonRoot == null)
            {
                return;
            }

            var renderers = thirdPersonRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null ||
                    !IsFirstPersonArmsRendererName(renderer.gameObject.name))
                {
                    continue;
                }

                if (armsPresenter == null || !armsPresenter.IsFirstPersonArmsRenderer(renderer))
                {
                    renderer.enabled = false;
                }
            }
        }

        private static bool IsFirstPersonArmsRendererName(string objectName)
        {
            return !string.IsNullOrWhiteSpace(objectName) &&
                   objectName.IndexOf("_FirstPersonArms", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ShouldHideFirstPersonArms()
        {
            var holster = GetComponent<PlayerWeaponHolsterController>();
            if (holster != null)
            {
                return holster.ShouldHideFirstPersonArms;
            }

            var weaponMount = GetComponent<PlayerWeaponMount>();
            return weaponMount == null || !weaponMount.HasMountedWeapon;
        }
    }
}
