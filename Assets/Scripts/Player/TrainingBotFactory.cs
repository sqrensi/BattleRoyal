using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShooterPrototype.Player
{
    public static class TrainingBotFactory
    {
        private const string RemotePlayerPrefabPath = "Assets/Prefabs/Player/PlayerCleanRemote.prefab";
        private const string Ch36ModelResourcePath = "Characters/Ch18_nonPBR";

        private static GameObject cachedBotPrefab;

        public static TrainingBotController Create(Vector3 position, Quaternion rotation, int botNumber)
        {
            var prefab = ResolveBotVisualPrefab();
            GameObject root;
            if (prefab != null)
            {
                root = Object.Instantiate(prefab, position, rotation);
                root.name = $"TrainingBot_{botNumber}";
                PrepareCharacterBot(root, botNumber);
            }
            else
            {
                root = CreateFallbackCapsuleBot(position, rotation, botNumber);
            }

            ConfigureNavMeshAgent(root);
            var bot = root.GetComponent<TrainingBotController>();
            if (bot == null)
            {
                bot = root.AddComponent<TrainingBotController>();
            }

            bot.Initialize(botNumber);
            return bot;
        }

        private static void PrepareCharacterBot(GameObject root, int botNumber)
        {
            StripHeavyVisualComponents(root);
            RemoveLocalOnlyObjects(root);

            var identity = root.GetComponent<PlayerNetworkIdentity>();
            if (identity == null)
            {
                identity = root.AddComponent<PlayerNetworkIdentity>();
            }

            identity.Configure($"training_bot_{botNumber}", false);

            var health = root.GetComponent<PlayerHealth>();
            if (health == null)
            {
                health = root.AddComponent<PlayerHealth>();
            }

            health.SetTrainingBotMode(true);

            EnsureCh36Appearance(root);
            SetupTrainingBotRemotePresentation(root);
            DisableLocalGameplayComponents(root);
            HideFirstPersonOnlyRenderers(root);
            OptimizeTrainingBotAnimators(root);
        }

        private static void OptimizeTrainingBotAnimators(GameObject root)
        {
            var animators = root.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                {
                    continue;
                }

                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
        }

        private static void ConfigureDuelBotAnimators(GameObject root)
        {
            var animators = root.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                {
                    continue;
                }

                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        private static void ConfigureDuelBotLocomotionDriver(GameObject root)
        {
            RemotePlayerLocomotionUtility.EnsureSyntyLocomotionDriver(root);
        }

        private static void ConfigureDuelBotRemoteAudio(GameObject root)
        {
            RemotePlayerLocomotionUtility.EnsureRemoteAudio(root);
        }

        public static void RefreshDuelBotPresentation(GameObject root)
        {
            RemotePlayerLocomotionUtility.FinalizeDuelBotPresentation(root);
            EnableDuelBotPresentation(root);
        }

        private static void EnsureCh36Appearance(GameObject root)
        {
            if (CharacterModelApplier.HasCharacterBody(root))
            {
                return;
            }

            var modelAsset = Resources.Load<GameObject>(Ch36ModelResourcePath);
            if (modelAsset != null)
            {
                CharacterModelApplier.TryApplyToPlayer(root, modelAsset);
            }
        }

        private static void SetupTrainingBotRemotePresentation(GameObject root)
        {
            var bootstrap = root.GetComponent<RemoteThirdPersonPlayerBootstrap>();
            if (bootstrap == null)
            {
                bootstrap = root.AddComponent<RemoteThirdPersonPlayerBootstrap>();
            }

            bootstrap.ApplyRemoteThirdPersonMode();

            var holsterPresentation = root.GetComponent<RemoteAnimatorHolsterPresentation>();
            holsterPresentation?.SetWeaponEquipped(false);

            var locomotionRig = root.GetComponentInChildren<ProceduralLocomotionRig>(true);
            if (locomotionRig != null)
            {
                RemotePlayerLocomotionUtility.ConfigureLocomotionRig(root);
            }

            RemotePlayerLocomotionUtility.EnsureSyntyLocomotionDriver(root);

            if (root.GetComponent<TrainingBotLocomotionPresenter>() == null)
            {
                root.AddComponent<TrainingBotLocomotionPresenter>();
            }

            var characterController = root.GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterController.enabled = false;
            }
        }

        private static void HideFirstPersonOnlyRenderers(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.gameObject.name.IndexOf("_FirstPersonArms", System.StringComparison.Ordinal) >= 0)
                {
                    renderer.gameObject.SetActive(false);
                }
            }
        }

        private static void StripHeavyVisualComponents(GameObject root)
        {
            DestroyAll<RemoteResourceClothingApplier>(root);
            DestroyAll<SyntyFirstPersonArmsPresenter>(root);
            DestroyAll<SyntyWeaponHandBinder>(root);
            DestroyAll<RemoteLeftHandIkBinder>(root);
            DestroyAll<SyntySplitBodyPresentation>(root);
            DestroyAll<SyntyFirstPersonArmLocomotionGate>(root);
        }

        private static void ConfigureNavMeshAgent(GameObject root)
        {
            var agent = root.GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                agent = root.AddComponent<NavMeshAgent>();
            }

            agent.height = 1.8f;
            agent.radius = 0.32f;
            agent.speed = 3.2f;
            agent.angularSpeed = 360f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0.3f;
            agent.autoBraking = true;
            agent.updateRotation = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.baseOffset = 0f;
            agent.enabled = false;
        }

        private static void DisableLocalGameplayComponents(GameObject root)
        {
            SetEnabled<FpsCharacterController>(root, false);
            SetEnabled<PlayerWeaponController>(root, false);
            SetEnabled<PlayerWeaponLoadoutController>(root, false);
            SetEnabled<PlayerPickupController>(root, false);
            SetEnabled<PlayerWeaponHolsterController>(root, false);
            SetEnabled<RemoteLookPitchPosture>(root, false);
            SetEnabled<PlayerAudioController>(root, false);
            SetEnabled<RemotePlayerShotEffects>(root, false);
            SetEnabled<RemoteMedkitPresentation>(root, false);
            SetEnabled<RemoteWeaponPresentation>(root, false);
            SetEnabled<MatchPresenceSync>(root, false);
        }

        private static void RemoveLocalOnlyObjects(GameObject root)
        {
            var cameras = root.GetComponentsInChildren<Camera>(true);
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                {
                    Object.Destroy(cameras[i].gameObject);
                }
            }

            var listeners = root.GetComponentsInChildren<AudioListener>(true);
            for (var i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null)
                {
                    Object.Destroy(listeners[i]);
                }
            }

            var localMarker = root.GetComponent<LocalPlayerMarker>();
            if (localMarker != null)
            {
                Object.Destroy(localMarker);
            }
        }

        private static void DestroyAll<T>(GameObject root) where T : Component
        {
            var components = root.GetComponentsInChildren<T>(true);
            for (var i = components.Length - 1; i >= 0; i--)
            {
                if (components[i] != null)
                {
                    Object.Destroy(components[i]);
                }
            }
        }

        private static void SetEnabled<T>(GameObject root, bool enabled) where T : Behaviour
        {
            var components = root.GetComponentsInChildren<T>(true);
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                {
                    components[i].enabled = enabled;
                }
            }
        }

        private static GameObject ResolveBotVisualPrefab()
        {
            if (WeaponCatalog.IsAlive(cachedBotPrefab))
            {
                return cachedBotPrefab;
            }

            var spawnManager = Object.FindFirstObjectByType<PlayerSpawnManager>();
            var fromSpawnManager = spawnManager != null
                ? spawnManager.GetRemotePlayerPrefabForPreview()
                : null;
            if (WeaponCatalog.IsAlive(fromSpawnManager))
            {
                cachedBotPrefab = fromSpawnManager;
                return cachedBotPrefab;
            }

#if UNITY_EDITOR
            var fromAssets = AssetDatabase.LoadAssetAtPath<GameObject>(RemotePlayerPrefabPath);
            if (fromAssets != null)
            {
                cachedBotPrefab = fromAssets;
                return cachedBotPrefab;
            }
#endif

            cachedBotPrefab = Resources.Load<GameObject>("Player/PlayerCleanRemote");
            return cachedBotPrefab;
        }

        private static GameObject CreateFallbackCapsuleBot(Vector3 position, Quaternion rotation, int botNumber)
        {
            var root = new GameObject($"TrainingBot_{botNumber}");
            root.transform.SetPositionAndRotation(position, rotation);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 1f, 0f);
            visual.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
            Object.Destroy(visual.GetComponent<Collider>());

            CreateHitSphere(root.transform, "HeadHitbox", PlayerBoneHitZone.Head, new Vector3(0f, 1.55f, 0f), 0.24f);
            CreateHitCapsule(root.transform, "BodyHitbox", PlayerBoneHitZone.Body, new Vector3(0f, 1f, 0f), 0.32f, 1.1f);

            var identity = root.AddComponent<PlayerNetworkIdentity>();
            identity.Configure($"training_bot_{botNumber}", false);

            var health = root.AddComponent<PlayerHealth>();
            health.SetTrainingBotMode(true);

            return root;
        }

        private static void CreateHitSphere(
            Transform parent,
            string name,
            PlayerBoneHitZone zone,
            Vector3 localPosition,
            float radius)
        {
            var hitObject = new GameObject(name);
            hitObject.transform.SetParent(parent, false);
            hitObject.transform.localPosition = localPosition;

            var collider = hitObject.AddComponent<SphereCollider>();
            collider.radius = radius;
            collider.isTrigger = false;

            var marker = hitObject.AddComponent<PlayerBoneHitbox>();
            marker.Configure(zone);
        }

        private static void CreateHitCapsule(
            Transform parent,
            string name,
            PlayerBoneHitZone zone,
            Vector3 localPosition,
            float radius,
            float height)
        {
            var hitObject = new GameObject(name);
            hitObject.transform.SetParent(parent, false);
            hitObject.transform.localPosition = localPosition;

            var collider = hitObject.AddComponent<CapsuleCollider>();
            collider.radius = radius;
            collider.height = height;
            collider.direction = 1;
            collider.isTrigger = false;

            var marker = hitObject.AddComponent<PlayerBoneHitbox>();
            marker.Configure(zone);
        }

        public static DuelNavBotController CreateDuelBot(
            Vector3 position,
            Quaternion rotation,
            string nickname,
            float skill,
            bool offlineDmMode = false)
        {
            var prefab = ResolveBotVisualPrefab();
            GameObject root;
            if (prefab != null)
            {
                root = Object.Instantiate(prefab, position, rotation);
                root.name = "DuelNavBot";
                PrepareDuelCharacterBot(root, 1);
            }
            else
            {
                root = CreateFallbackCapsuleBot(position, rotation, 1);
                root.name = "DuelNavBot";
                PrepareDuelCharacterBot(root, 1);
            }

            var identity = root.GetComponent<PlayerNetworkIdentity>();
            if (identity != null)
            {
                identity.Configure("duel_nav_bot", false);
            }

            ConfigureDuelNavMeshAgent(root);
            Object.Destroy(root.GetComponent<TrainingBotController>());

            var skinState = PlayerSkinSelectionService.RollRandomBotNetworkState();
            PlayerSkinSelectionService.ApplyNetworkStateToPlayer(root, skinState, forceReapply: true);
            RefreshDuelBotPresentation(root);

            var bot = root.GetComponent<DuelNavBotController>();
            if (bot == null)
            {
                bot = root.AddComponent<DuelNavBotController>();
            }

            bot.Initialize(nickname, skill, skinState);
            if (offlineDmMode)
            {
                root.GetComponent<PlayerHealth>()?.ConfigureOfflineDmBot();
            }

            return bot;
        }

        private static void StripHeavyVisualComponentsForDuel(GameObject root)
        {
            DestroyAll<SyntyFirstPersonArmsPresenter>(root);
            DestroyAll<SyntyWeaponHandBinder>(root);
            DestroyAll<RemoteLeftHandIkBinder>(root);
            DestroyAll<SyntySplitBodyPresentation>(root);
            DestroyAll<SyntyFirstPersonArmLocomotionGate>(root);
        }

        private static void PrepareDuelCharacterBot(GameObject root, int botNumber)
        {
            StripHeavyVisualComponentsForDuel(root);
            RemoveLocalOnlyObjects(root);

            var identity = root.GetComponent<PlayerNetworkIdentity>();
            if (identity == null)
            {
                identity = root.AddComponent<PlayerNetworkIdentity>();
            }

            identity.Configure($"duel_bot_{botNumber}", false);

            var health = root.GetComponent<PlayerHealth>();
            if (health == null)
            {
                health = root.AddComponent<PlayerHealth>();
            }

            health.SetDuelBotEliminationMode(true);

            EnsureCh36Appearance(root);
            SetupTrainingBotRemotePresentation(root);
            DisableLocalGameplayComponents(root);
            HideFirstPersonOnlyRenderers(root);
            EnableDuelBotPresentation(root);
        }

        private static void EnableDuelBotPresentation(GameObject root)
        {
            SetEnabled<RemoteWeaponPresentation>(root, true);
            SetEnabled<RemotePlayerShotEffects>(root, true);
            SetEnabled<PlayerAudioController>(root, true);
            SetEnabled<RemoteLookPitchPosture>(root, true);
            SetEnabled<RemoteAnimatorHolsterPresentation>(root, true);

            var holsterPresentation = root.GetComponent<RemoteAnimatorHolsterPresentation>();
            holsterPresentation?.SetWeaponEquipped(false);
        }

        private static void ConfigureDuelNavMeshAgent(GameObject root)
        {
            ConfigureNavMeshAgent(root);

            var agent = root.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.updateRotation = false;
            }
        }
    }
}
