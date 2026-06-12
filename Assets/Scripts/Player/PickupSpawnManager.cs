using System;

using System.Collections;

using System.Collections.Generic;

using ShooterPrototype.Network;

using UnityEngine;

using UnityEngine.Serialization;



namespace ShooterPrototype.Player

{

    public sealed class PickupSpawnManager : MonoBehaviour

    {

        [System.Serializable]

        public sealed class PickupSpawnSlot

        {

            public string spawnId;

            public Transform spawnPoint;

            [SerializeField] private PickupSpawnZone zone;

            [SerializeField] private int zoneSlotIndex = -1;

            public bool IsZoneSlot => zone != null;

            public PickupSpawnZone Zone => zone;

            public int ZoneSlotIndex => zoneSlotIndex;

            public void BindZone(PickupSpawnZone sourceZone, int slotIndex)
            {
                zone = sourceZone;
                zoneSlotIndex = slotIndex;
            }

            public void TryRestoreZoneBinding()
            {
                if (zone != null || spawnPoint == null)
                {
                    return;
                }

                zone = spawnPoint.GetComponentInParent<PickupSpawnZone>(true);
                if (zone == null)
                {
                    return;
                }

                if (zoneSlotIndex < 0 &&
                    PickupSpawnZone.TryParseSpawnSlotIndex(spawnId, out var parsedIndex))
                {
                    zoneSlotIndex = parsedIndex;
                }
            }



            [FormerlySerializedAs("weaponPrefabOverride")]

            [SerializeField] private GameObject pickupVisualOverride;

            [SerializeField] private PickupKind pickupKindOverride = PickupKind.Weapon;

            [SerializeField] private string itemIdOverride;

            [SerializeField] private int amountOverride = 1;

            [SerializeField] private int magAmmoOverride = -1;



            public PickupItemDefinition ResolveDefinition(in PickupItemDefinition fallbackDefault)
            {
                var visual = pickupVisualOverride != null
                    ? pickupVisualOverride
                    : fallbackDefault.VisualPrefab;
                if (visual == null)
                {
                    return default;
                }

                var itemId = !string.IsNullOrWhiteSpace(itemIdOverride)
                    ? itemIdOverride
                    : ResolveImplicitItemId(pickupKindOverride, visual, fallbackDefault);
                var amount = amountOverride > 0 ? amountOverride : fallbackDefault.Amount;
                var definition = PickupItemDefinition.Create(pickupKindOverride, visual, itemId, amount);
                if (magAmmoOverride >= 0)
                {
                    definition = definition.WithMagAmmo(magAmmoOverride);
                }
                else if (pickupKindOverride == PickupKind.Weapon)
                {
                    definition = definition.WithMagAmmo(0);
                }

                RegisterWeaponPrefab(definition);
                return definition;
            }

            private static string ResolveImplicitItemId(
                PickupKind pickupKind,
                GameObject visual,
                in PickupItemDefinition fallbackDefault)
            {
                if (pickupKind == PickupKind.Weapon && visual != null)
                {
                    return visual.name;
                }

                return pickupKind == fallbackDefault.Kind
                    ? fallbackDefault.ResolvedItemId
                    : PickupKindUtility.ToProtocol(pickupKind);
            }

            private static void RegisterWeaponPrefab(in PickupItemDefinition definition)
            {
                if (definition.Kind != PickupKind.Weapon || definition.VisualPrefab == null)
                {
                    return;
                }

                var profile = definition.VisualPrefab.GetComponent<WeaponProfile>() ??
                              definition.VisualPrefab.GetComponentInChildren<WeaponProfile>(true);
                var kind = profile != null
                    ? profile.Kind
                    : WeaponCatalog.ResolveKindFromItemId(definition.ResolvedItemId);
                WeaponCatalog.RegisterWeaponPrefab(kind, definition.VisualPrefab);
            }

            public void ConfigureWeaponDrop(GameObject visualPrefab, string itemId, int magAmmo = -1)
            {
                pickupVisualOverride = visualPrefab;
                itemIdOverride = itemId ?? string.Empty;
                pickupKindOverride = PickupKind.Weapon;
                amountOverride = 1;
                magAmmoOverride = magAmmo;
            }

            public void ConfigureFromDefinition(in PickupItemDefinition definition)
            {
                pickupVisualOverride = definition.VisualPrefab;
                pickupKindOverride = definition.Kind;
                itemIdOverride = definition.ResolvedItemId;
                amountOverride = definition.Amount;
                magAmmoOverride = definition.MagAmmo;
            }

            public bool HasExplicitOverride => pickupVisualOverride != null;

        }



        private enum SpawnCollectionMode
        {
            Auto = 0,
            PointsOnly = 1,
            ZonesOnly = 2,
            Both = 3
        }



        [Header("Spawn Points")]

        [SerializeField] private Transform spawnPointsRoot;

        [SerializeField] private bool autoCollectSpawnPointsFromRoot = true;



        [Header("Spawn Zones")]

        [Tooltip("Optional parent with PickupSpawnZone children. When empty, zones are collected from the whole scene.")]
        [SerializeField] private Transform spawnZonesRoot;

        [SerializeField] private bool autoCollectSpawnZonesFromScene = true;

        [SerializeField] private SpawnCollectionMode spawnCollectionMode = SpawnCollectionMode.Auto;



        [SerializeField] private List<PickupSpawnSlot> spawnSlots = new List<PickupSpawnSlot>();



        [Header("Default Pickup")]

        [SerializeField] private PickupKind defaultPickupKind = PickupKind.Weapon;

        [FormerlySerializedAs("defaultWeaponPrefab")]

        [SerializeField] private GameObject defaultVisualPrefab;

        [SerializeField] private string defaultItemId;

        [SerializeField] private int defaultAmount = 1;



        [Header("Pickup Visual")]

        [SerializeField] private Vector3 worldPickupEulerOffset = new Vector3(0f, 0f, 90f);

        [SerializeField] private Vector3 worldPickupLocalScale = Vector3.one;

        [SerializeField] private float pickupYOffset = 0.08f;

        [Header("Weapon Drop Placement")]
        [SerializeField] private LayerMask dropGroundMask = ~0;
        [SerializeField] private float dropGroundProbeHeight = 3f;
        [SerializeField] private float dropGroundProbeDistance = 6f;
        [SerializeField] private LayerMask dropWallMask = ~0;
        [SerializeField] private float dropWallProbeHeight = 0.6f;
        [SerializeField] private float dropWallClearance = 0.35f;
        [SerializeField] private float weaponDropSurfaceOffset = 0.14f;

        [SerializeField] private bool spawnOnStart = true;

        [SerializeField] private float respawnDelaySeconds;



        [Header("Random Pool")]

        [SerializeField] private PickupSpawnRandomPool randomPickupPool = new PickupSpawnRandomPool();



        private readonly Dictionary<Transform, WorldPickup> activePickupsBySpawnPoint =

            new Dictionary<Transform, WorldPickup>();

        private readonly Dictionary<string, PickupSpawnSlot> slotsBySpawnId =

            new Dictionary<string, PickupSpawnSlot>();

        private readonly Dictionary<string, WorldPickup> activePickupsBySpawnId =

            new Dictionary<string, WorldPickup>();

        private readonly Dictionary<string, PickupItemDefinition> spawnedDefinitionsBySpawnId =

            new Dictionary<string, PickupItemDefinition>(StringComparer.Ordinal);

        private readonly Dictionary<string, Vector3> serverPositionsBySpawnId =

            new Dictionary<string, Vector3>(StringComparer.Ordinal);

        private bool registeredWithServer;

        [SerializeField] private bool waitForServerPickupSync = true;



        private void Awake()

        {

            if (GetComponent<MatchPickupSync>() == null)

            {

                gameObject.AddComponent<MatchPickupSync>();

            }

            randomPickupPool?.EnsureDefaultEntries();
            randomPickupPool?.NormalizeEntryWeights();

        }



        private void Start()

        {

            RebuildSpawnSlotsFromScene();

            EnsureSlotZoneBindings();

            RebuildSlotLookup();

            if (spawnOnStart && !ShouldDeferSpawnUntilServerSync())

            {

                SpawnAll();

            }

        }

        private bool ShouldDeferSpawnUntilServerSync()
        {
            if (!waitForServerPickupSync)
            {
                return false;
            }

            return GetComponent<MatchPickupSync>() != null ||
                   FindFirstObjectByType<RealtimeTransportClient>() != null;
        }

        [ContextMenu("Rebuild Spawn Zones From Root")]

        [ContextMenu("Rebuild Spawn Zones From Scene")]

        public void RebuildSpawnZonesFromRoot()

        {

            if (spawnSlots == null)

            {

                spawnSlots = new List<PickupSpawnSlot>();

            }



            if (spawnCollectionMode != SpawnCollectionMode.Both)

            {

                spawnSlots.Clear();

            }



            var zones = CollectSpawnZones();

            if (zones.Length == 0)

            {

                Debug.LogWarning(
                    "[PickupSpawnManager] No active PickupSpawnZone found. " +
                    "Add PickupSpawnZone + BoxCollider on building floors and save the scene.");

                RebuildSlotLookup();

                return;

            }

            var createdSlots = 0;

            for (var zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++)

            {

                var zone = zones[zoneIndex];

                if (zone == null)

                {

                    continue;

                }



                var pickupCount = zone.PickupCount;

                for (var slotIndex = 0; slotIndex < pickupCount; slotIndex++)

                {

                    var slot = new PickupSpawnSlot

                    {

                        spawnId = zone.BuildSpawnId(slotIndex),

                        spawnPoint = zone.GetOrCreateAnchor(slotIndex)

                    };

                    slot.BindZone(zone, slotIndex);

                    spawnSlots.Add(slot);

                    createdSlots++;

                }

            }



            Debug.Log(
                $"[PickupSpawnManager] Collected {zones.Length} zone(s), created {createdSlots} spawn slot(s).");

            RebuildSlotLookup();

        }



        [ContextMenu("Rebuild Spawn Points From Root")]

        public void RebuildSpawnPointsFromRoot()

        {

            if (spawnSlots == null)

            {

                spawnSlots = new List<PickupSpawnSlot>();

            }



            if (spawnCollectionMode != SpawnCollectionMode.Both)

            {

                spawnSlots.Clear();

            }



            if (spawnPointsRoot == null)

            {

                RebuildSlotLookup();

                return;

            }



            for (var i = 0; i < spawnPointsRoot.childCount; i++)

            {

                var child = spawnPointsRoot.GetChild(i);

                if (child == null)

                {

                    continue;

                }



                spawnSlots.Add(new PickupSpawnSlot

                {

                    spawnId = ResolveSpawnId(null, child, i),

                    spawnPoint = child

                });

            }



            RebuildSlotLookup();

        }



        private void RebuildSpawnSlotsFromScene()

        {

            var useZones = ShouldUseSpawnZones();

            var usePoints = ShouldUseSpawnPoints();



            if (useZones && usePoints && spawnCollectionMode == SpawnCollectionMode.Both)

            {

                spawnSlots = new List<PickupSpawnSlot>();

                RebuildSpawnZonesFromRoot();

                RebuildSpawnPointsFromRoot();

                return;

            }



            if (useZones)

            {

                RebuildSpawnZonesFromRoot();

                return;

            }



            if (usePoints &&

                (autoCollectSpawnPointsFromRoot || spawnSlots == null || spawnSlots.Count == 0))

            {

                RebuildSpawnPointsFromRoot();

            }

        }



        private bool ShouldUseSpawnZones()

        {

            if (spawnCollectionMode == SpawnCollectionMode.PointsOnly)

            {

                return false;

            }



            if (spawnCollectionMode == SpawnCollectionMode.ZonesOnly ||

                spawnCollectionMode == SpawnCollectionMode.Both)

            {

                return spawnZonesRoot != null || autoCollectSpawnZonesFromScene;

            }



            return HasSpawnZonesInScene();

        }



        private PickupSpawnZone[] CollectSpawnZones()

        {

            if (spawnZonesRoot != null)

            {

                var fromRoot = FilterActiveZones(spawnZonesRoot.GetComponentsInChildren<PickupSpawnZone>(true));

                if (fromRoot.Length > 0)

                {

                    return fromRoot;

                }

            }



            if (!autoCollectSpawnZonesFromScene)

            {

                return Array.Empty<PickupSpawnZone>();

            }



            var fromScene = FindObjectsByType<PickupSpawnZone>(

                FindObjectsInactive.Include,

                FindObjectsSortMode.None);

            if (fromScene == null || fromScene.Length == 0)

            {

                return Array.Empty<PickupSpawnZone>();

            }



            var result = FilterActiveZones(fromScene);

            if (result.Length == 0 && fromScene.Length > 0)

            {

                Debug.LogWarning(

                    $"[PickupSpawnManager] Found {fromScene.Length} PickupSpawnZone(s), but all are inactive/disabled.");

            }



            Array.Sort(result, CompareSpawnZones);

            return result;

        }



        private static PickupSpawnZone[] FilterActiveZones(PickupSpawnZone[] zones)

        {

            if (zones == null || zones.Length == 0)

            {

                return Array.Empty<PickupSpawnZone>();

            }



            var activeZones = new List<PickupSpawnZone>(zones.Length);

            for (var i = 0; i < zones.Length; i++)

            {

                var zone = zones[i];

                if (zone != null && zone.isActiveAndEnabled)

                {

                    activeZones.Add(zone);

                }

            }



            return activeZones.ToArray();

        }



        private bool HasSpawnZonesInScene()

        {

            return CollectSpawnZones().Length > 0;

        }



        private static int CompareSpawnZones(PickupSpawnZone left, PickupSpawnZone right)

        {

            if (left == null && right == null)

            {

                return 0;

            }



            if (left == null)

            {

                return 1;

            }



            if (right == null)

            {

                return -1;

            }



            var keyCompare = string.Compare(
                left.BuildSpawnId(0),
                right.BuildSpawnId(0),
                StringComparison.Ordinal);

            if (keyCompare != 0)

            {

                return keyCompare;

            }



            return left.GetInstanceID().CompareTo(right.GetInstanceID());

        }



        private bool ShouldUseSpawnPoints()

        {

            if (spawnCollectionMode == SpawnCollectionMode.ZonesOnly)

            {

                return false;

            }



            if (spawnCollectionMode == SpawnCollectionMode.PointsOnly ||

                spawnCollectionMode == SpawnCollectionMode.Both)

            {

                return spawnPointsRoot != null;

            }



            if (spawnPointsRoot == null)

            {

                return false;

            }



            return !ShouldUseSpawnZones() || spawnCollectionMode == SpawnCollectionMode.Both;

        }



        [ContextMenu("Spawn All Pickups")]

        public void SpawnAll()

        {

            RebuildSpawnSlotsFromScene();

            EnsureSlotZoneBindings();

            RebuildSlotLookup();

            if (spawnSlots == null || spawnSlots.Count == 0)

            {

                Debug.LogWarning("[PickupSpawnManager] SpawnAll skipped: no spawn slots.");

                return;

            }



            var spawnedCount = 0;

            var skippedCount = 0;

            for (var i = 0; i < spawnSlots.Count; i++)

            {

                var slot = spawnSlots[i];

                if (slot == null || slot.spawnPoint == null)

                {

                    skippedCount++;

                    continue;

                }



                if (string.IsNullOrWhiteSpace(slot.spawnId))

                {

                    slot.spawnId = ResolveSpawnId(slot, slot.spawnPoint, i);

                }



                var beforeCount = activePickupsBySpawnId.Count;

                SpawnAtSlot(slot);

                if (activePickupsBySpawnId.Count > beforeCount)

                {

                    spawnedCount++;

                }

                else

                {

                    skippedCount++;

                }

            }



            Debug.Log(

                $"[PickupSpawnManager] SpawnAll finished: {spawnedCount} spawned, {skippedCount} skipped " +

                $"(slots={spawnSlots.Count}).");

        }



        public void ResetServerRegistration()

        {

            registeredWithServer = false;

        }



        public void TryRegisterWithServer(RealtimeTransportClient transportClient)

        {

            if (registeredWithServer || transportClient == null || !transportClient.IsReady)

            {

                return;

            }

            RebuildSpawnSlotsFromScene();

            EnsureSlotZoneBindings();

            RebuildSlotLookup();

            var entries = BuildNetworkRegistrationEntries();

            if (entries == null || entries.Length == 0)

            {

                return;

            }



            transportClient.SendRegisterPickups(entries);

            registeredWithServer = true;

        }



        public RealtimeTransportClient.PickupSpawnRegistration[] BuildNetworkRegistrationEntries()

        {

            RebuildSlotLookup();

            if (spawnSlots == null || spawnSlots.Count == 0)

            {

                return null;

            }



            var defaultDefinition = ResolveDefaultPickupDefinition();

            var entries = new List<RealtimeTransportClient.PickupSpawnRegistration>();

            for (var i = 0; i < spawnSlots.Count; i++)

            {

                var slot = spawnSlots[i];

                if (slot == null || slot.spawnPoint == null)

                {

                    continue;

                }



                if (string.IsNullOrWhiteSpace(slot.spawnId))

                {

                    slot.spawnId = ResolveSpawnId(slot, slot.spawnPoint, i);

                }



                var definition = ResolveSpawnedDefinition(slot);

                if (!definition.IsValid)

                {

                    continue;

                }



                var position = ResolveRegistrationPosition(slot);

                var itemId = definition.ResolvedItemId;

                entries.Add(new RealtimeTransportClient.PickupSpawnRegistration

                {

                    spawnId = slot.spawnId,

                    pickupKind = PickupKindUtility.ToProtocol(definition.Kind),

                    itemId = itemId,

                    weaponId = itemId,

                    amount = definition.Amount,

                    magAmmo = definition.Kind == PickupKind.Weapon
                        ? Mathf.Max(0, definition.MagAmmo)
                        : -1,

                    x = position.x,

                    y = position.y,

                    z = position.z,

                    respawnDelaySeconds = respawnDelaySeconds

                });

            }



            return entries.Count > 0 ? entries.ToArray() : null;

        }



        public void ApplyServerPickupState(RealtimeTransportClient.PickupSpawnState[] spawns)

        {

            if (spawns == null || spawns.Length == 0)

            {

                return;

            }



            RebuildSpawnSlotsFromScene();

            EnsureSlotZoneBindings();

            RebuildSlotLookup();

            for (var i = 0; i < spawns.Length; i++)

            {

                var spawn = spawns[i];

                if (spawn == null || string.IsNullOrWhiteSpace(spawn.spawnId))

                {

                    continue;

                }



                var itemId = !string.IsNullOrWhiteSpace(spawn.itemId)
                    ? spawn.itemId
                    : spawn.weaponId;
                var kind = PickupKindUtility.FromProtocol(spawn.pickupKind);
                var magAmmo = spawn.magAmmo;
                var definition = ResolveDefinitionFromProtocol(
                    kind,
                    itemId,
                    spawn.amount > 0 ? spawn.amount : 1,
                    magAmmo);
                if (definition.IsValid)
                {
                    spawnedDefinitionsBySpawnId[spawn.spawnId] = definition;
                    if (slotsBySpawnId.TryGetValue(spawn.spawnId, out var slot) && slot != null)
                    {
                        slot.ConfigureFromDefinition(definition);
                    }
                }

                if (TryReadServerPosition(spawn, out var serverPosition))
                {
                    serverPositionsBySpawnId[spawn.spawnId] = serverPosition;
                    ApplyPositionToSlot(spawn.spawnId, serverPosition);
                }



                if (spawn.available)

                {

                    ApplyServerPickupRespawn(spawn.spawnId, true);

                }

                else

                {

                    ApplyServerPickupTaken(spawn.spawnId);

                }

            }

        }



        public void ApplyServerPickupTaken(string spawnId)

        {

            if (string.IsNullOrWhiteSpace(spawnId))

            {

                return;

            }



            if (activePickupsBySpawnId.TryGetValue(spawnId, out var pickup) && pickup != null)

            {

                DestroyPickupVisual(pickup);

                return;

            }



            if (slotsBySpawnId.TryGetValue(spawnId, out var slot))

            {

                if (slot?.spawnPoint != null &&

                    activePickupsBySpawnPoint.TryGetValue(slot.spawnPoint, out pickup) &&

                    pickup != null)

                {

                    DestroyPickupVisual(pickup);

                }

            }

        }



        public void ApplyServerPickupRespawn(string spawnId, bool forceResync = false)

        {

            if (string.IsNullOrWhiteSpace(spawnId) ||

                !slotsBySpawnId.TryGetValue(spawnId, out var slot))

            {

                return;

            }



            if (slot == null || slot.spawnPoint == null)

            {

                return;

            }



            if (activePickupsBySpawnId.TryGetValue(spawnId, out var existing) && existing != null)

            {

                if (!forceResync && PickupMatchesCachedDefinition(existing, spawnId))

                {

                    return;

                }



                DestroyPickupVisual(existing);

            }



            SpawnAtSlot(slot);

        }



        public void ApplyServerSpawnPosition(string spawnId, Vector3 worldPosition)
        {
            if (string.IsNullOrWhiteSpace(spawnId))
            {
                return;
            }

            serverPositionsBySpawnId[spawnId] = worldPosition;
            ApplyPositionToSlot(spawnId, worldPosition);
        }

        public bool HasRegisteredSlot(string spawnId)
        {
            return !string.IsNullOrWhiteSpace(spawnId) && slotsBySpawnId.ContainsKey(spawnId);
        }

        public void CacheServerDefinition(string spawnId, in PickupItemDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(spawnId) || !definition.IsValid)
            {
                return;
            }

            spawnedDefinitionsBySpawnId[spawnId] = definition;
            if (slotsBySpawnId.TryGetValue(spawnId, out var slot) && slot != null)
            {
                slot.ConfigureFromDefinition(definition);
            }
        }

        public PickupItemDefinition ResolveDefinitionForSpawnId(string spawnId)

        {

            if (!string.IsNullOrWhiteSpace(spawnId) &&
                spawnedDefinitionsBySpawnId.TryGetValue(spawnId, out var cached) &&
                cached.IsValid)
            {
                return cached;
            }

            if (string.IsNullOrWhiteSpace(spawnId) ||

                !slotsBySpawnId.TryGetValue(spawnId, out var slot))

            {

                return ResolveDefaultPickupDefinition();

            }



            return ResolveSpawnedDefinition(slot);

        }



        public void NotifyPickupCollected(Transform spawnPoint, in PickupItemDefinition definition)

        {

            if (spawnPoint != null)

            {

                activePickupsBySpawnPoint.Remove(spawnPoint);

            }



            if (respawnDelaySeconds <= 0.01f)

            {

                return;

            }



            PickupSpawnSlot slot = null;

            for (var i = 0; i < spawnSlots.Count; i++)

            {

                var candidate = spawnSlots[i];

                if (candidate != null && candidate.spawnPoint == spawnPoint)

                {

                    slot = candidate;

                    break;

                }

            }



            if (slot == null && spawnPoint != null)

            {

                slot = new PickupSpawnSlot

                {

                    spawnPoint = spawnPoint,

                    spawnId = ResolveSpawnId(null, spawnPoint, spawnSlots.Count)

                };

                spawnSlots.Add(slot);

                RebuildSlotLookup();

            }



            if (slot != null)

            {

                StartCoroutine(RespawnAfterDelay(slot, respawnDelaySeconds));

            }

        }



        private IEnumerator RespawnAfterDelay(PickupSpawnSlot slot, float delaySeconds)

        {

            yield return new WaitForSeconds(Mathf.Max(0.01f, delaySeconds));

            SpawnAtSlot(slot);

            registeredWithServer = false;

        }



        private void SpawnAtSlot(PickupSpawnSlot slot)

        {

            if (slot == null || slot.spawnPoint == null)

            {

                return;

            }



            if (string.IsNullOrWhiteSpace(slot.spawnId))

            {

                slot.spawnId = ResolveSpawnId(slot, slot.spawnPoint, 0);

            }



            if (activePickupsBySpawnId.TryGetValue(slot.spawnId, out var existingById) &&

                existingById != null)

            {

                return;

            }



            if (activePickupsBySpawnPoint.TryGetValue(slot.spawnPoint, out var existing) &&

                existing != null)

            {

                return;

            }



            if (!string.IsNullOrWhiteSpace(slot.spawnId))

            {

                spawnedDefinitionsBySpawnId.Remove(slot.spawnId);

            }



            var definition = ResolveSpawnedDefinition(slot);

            if (!definition.IsValid)

            {

                Debug.LogWarning("[PickupSpawnManager] No pickup definition assigned for spawn slot.");

                return;

            }



            spawnedDefinitionsBySpawnId[slot.spawnId] = definition;

            if (!TryResolveSlotSpawnPose(slot, out var spawnPosition, out var spawnRotation, out var spawnAnchor))

            {

                Debug.LogWarning(

                    $"[PickupSpawnManager] Failed to resolve spawn pose for '{slot.spawnId}' " +

                    $"(zone='{slot.Zone?.name ?? "n/a"}'). Check PickupSpawnZone BoxCollider size/position.");

                return;

            }



            var pickupRoot = new GameObject($"{definition.ResolvedItemId}_Pickup");

            pickupRoot.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

            pickupRoot.transform.localScale = worldPickupLocalScale;

            pickupRoot.transform.SetParent(spawnAnchor, true);



            var pickupVisual = Instantiate(definition.VisualPrefab, pickupRoot.transform);

            pickupVisual.SetActive(true);

            pickupVisual.name = "PickupVisual";

            pickupVisual.transform.localPosition = Vector3.zero;

            pickupVisual.transform.localRotation = Quaternion.identity;



            PreparePickupVisual(pickupVisual);



            var pickup = pickupRoot.AddComponent<WorldPickup>();

            pickup.Initialize(this, slot.spawnPoint, slot.spawnId, definition);

            activePickupsBySpawnPoint[slot.spawnPoint] = pickup;

            activePickupsBySpawnId[slot.spawnId] = pickup;

        }



        private void DestroyPickupVisual(WorldPickup pickup)

        {

            if (pickup == null)

            {

                return;

            }



            if (pickup.SpawnPoint != null)

            {

                activePickupsBySpawnPoint.Remove(pickup.SpawnPoint);

            }



            if (!string.IsNullOrWhiteSpace(pickup.SpawnId))

            {

                activePickupsBySpawnId.Remove(pickup.SpawnId);

            }



            Destroy(pickup.gameObject);

        }



        private void EnsureSlotZoneBindings()

        {

            if (spawnSlots == null)

            {

                return;

            }



            for (var i = 0; i < spawnSlots.Count; i++)

            {

                spawnSlots[i]?.TryRestoreZoneBinding();

            }

        }



        private void RebuildSlotLookup()

        {

            slotsBySpawnId.Clear();

            if (spawnSlots == null)

            {

                return;

            }



            for (var i = 0; i < spawnSlots.Count; i++)

            {

                var slot = spawnSlots[i];

                if (slot == null || slot.spawnPoint == null)

                {

                    continue;

                }



                if (string.IsNullOrWhiteSpace(slot.spawnId))

                {

                    slot.spawnId = ResolveSpawnId(slot, slot.spawnPoint, i);

                }



                slotsBySpawnId[slot.spawnId] = slot;

            }

        }



        private PickupItemDefinition ResolveDefaultPickupDefinition()

        {

            var definition = PickupItemDefinition.Create(

                defaultPickupKind,

                defaultVisualPrefab,

                defaultItemId,

                defaultAmount);

            if (defaultPickupKind == PickupKind.Weapon)

            {

                definition = definition.WithMagAmmo(0);

            }

            return definition;

        }



        private PickupItemDefinition ResolvePickupDefinition(PickupSpawnSlot slot)

        {

            return slot != null

                ? slot.ResolveDefinition(ResolveDefaultPickupDefinition())

                : ResolveDefaultPickupDefinition();

        }



        private PickupItemDefinition ResolveSpawnedDefinition(PickupSpawnSlot slot)

        {

            if (slot == null)

            {

                return default;

            }



            if (!string.IsNullOrWhiteSpace(slot.spawnId) &&

                spawnedDefinitionsBySpawnId.TryGetValue(slot.spawnId, out var existing) &&

                existing.IsValid)

            {

                return existing;

            }



            if (slot.HasExplicitOverride)

            {

                return ResolvePickupDefinition(slot);

            }



            if (randomPickupPool != null && randomPickupPool.Enabled)

            {

                var rolled = randomPickupPool.Roll(transform, slot.spawnId);

                if (rolled.IsValid)

                {

                    return rolled;

                }

            }



            return ResolvePickupDefinition(slot);

        }



        private bool TryResolveSlotSpawnPose(
            PickupSpawnSlot slot,
            out Vector3 spawnPosition,
            out Quaternion spawnRotation,
            out Transform spawnAnchor)
        {
            spawnPosition = Vector3.zero;
            spawnRotation = Quaternion.identity;
            spawnAnchor = null;

            if (slot == null || slot.spawnPoint == null)
            {
                return false;
            }

            if (slot.IsZoneSlot && slot.Zone != null)
            {
                if (!string.IsNullOrWhiteSpace(slot.spawnId) &&
                    serverPositionsBySpawnId.TryGetValue(slot.spawnId, out var serverPosition))
                {
                    spawnPosition = serverPosition;
                    spawnRotation = Quaternion.Euler(worldPickupEulerOffset);
                    spawnAnchor = slot.Zone.GetOrCreateAnchor(slot.ZoneSlotIndex);
                    spawnAnchor.SetPositionAndRotation(spawnPosition, spawnRotation);
                    slot.spawnPoint = spawnAnchor;
                    return true;
                }

                var avoidPositions = BuildDeterministicAvoidPositions(slot);
                if (!slot.Zone.TryResolveSpawnPose(
                        slot.spawnId,
                        slot.ZoneSlotIndex,
                        pickupYOffset,
                        worldPickupEulerOffset,
                        avoidPositions,
                        out spawnPosition,
                        out spawnRotation))
                {
                    return false;
                }

                spawnAnchor = slot.Zone.GetOrCreateAnchor(slot.ZoneSlotIndex);
                spawnAnchor.SetPositionAndRotation(spawnPosition, spawnRotation);
                slot.spawnPoint = spawnAnchor;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(slot.spawnId) &&
                serverPositionsBySpawnId.TryGetValue(slot.spawnId, out var dynamicServerPosition))
            {
                spawnPosition = dynamicServerPosition;
            }
            else
            {
                spawnPosition = IsWeaponDropSpawnId(slot.spawnId)
                    ? slot.spawnPoint.position
                    : ResolveGroundedDropPosition(slot.spawnPoint.position);
            }
            spawnRotation = slot.spawnPoint.rotation * Quaternion.Euler(worldPickupEulerOffset);
            spawnAnchor = slot.spawnPoint;
            return true;
        }

        public bool TryResolveWeaponDropPose(
            Vector3 origin,
            Vector3 forward,
            float maxForwardDistance,
            out Vector3 resolvedPosition,
            out Vector3 resolvedForward)
        {
            resolvedForward = forward;
            resolvedForward.y = 0f;
            if (resolvedForward.sqrMagnitude < 0.001f)
            {
                resolvedForward = Vector3.forward;
            }

            resolvedForward.Normalize();

            var travelDistance = Mathf.Max(0.25f, maxForwardDistance);
            var probeOrigin = origin + Vector3.up * dropWallProbeHeight;
            var target = origin + resolvedForward * travelDistance;
            if (Physics.Raycast(
                    probeOrigin,
                    resolvedForward,
                    out var wallHit,
                    travelDistance,
                    dropWallMask,
                    QueryTriggerInteraction.Ignore))
            {
                target = wallHit.point - resolvedForward * dropWallClearance;
            }

            resolvedPosition = ResolveGroundedDropPosition(target, weaponDropSurfaceOffset);
            return true;
        }

        public Vector3 ResolveGroundedDropPosition(Vector3 approximatePosition)
        {
            return ResolveGroundedDropPosition(approximatePosition, pickupYOffset);
        }

        public Vector3 ResolveGroundedDropPosition(Vector3 approximatePosition, float surfaceOffset)
        {
            var probeOrigin = approximatePosition + Vector3.up * dropGroundProbeHeight;
            if (Physics.Raycast(
                    probeOrigin,
                    Vector3.down,
                    out var groundHit,
                    dropGroundProbeDistance,
                    dropGroundMask,
                    QueryTriggerInteraction.Ignore))
            {
                return groundHit.point + Vector3.up * Mathf.Max(0.01f, surfaceOffset);
            }

            return approximatePosition + Vector3.up * Mathf.Max(0.01f, surfaceOffset);
        }

        private List<Vector3> BuildDeterministicAvoidPositions(PickupSpawnSlot slot)
        {
            var positions = new List<Vector3>(4);
            if (slot?.Zone == null || slot.ZoneSlotIndex <= 0 || spawnSlots == null)
            {
                return positions;
            }

            for (var i = 0; i < spawnSlots.Count; i++)
            {
                var other = spawnSlots[i];
                if (other?.Zone != slot.Zone ||
                    other.ZoneSlotIndex < 0 ||
                    other.ZoneSlotIndex >= slot.ZoneSlotIndex ||
                    string.IsNullOrWhiteSpace(other.spawnId))
                {
                    continue;
                }

                var priorAvoid = BuildDeterministicAvoidPositions(other);
                if (other.Zone.TryResolveSpawnPose(
                        other.spawnId,
                        other.ZoneSlotIndex,
                        pickupYOffset,
                        worldPickupEulerOffset,
                        priorAvoid,
                        out var otherPosition,
                        out _))
                {
                    positions.Add(otherPosition);
                }
            }

            return positions;
        }

        private static bool TryReadServerPosition(
            RealtimeTransportClient.PickupSpawnState spawn,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (spawn == null)
            {
                return false;
            }

            if (Mathf.Abs(spawn.x) < 0.001f &&
                Mathf.Abs(spawn.y) < 0.001f &&
                Mathf.Abs(spawn.z) < 0.001f)
            {
                return false;
            }

            position = new Vector3(spawn.x, spawn.y, spawn.z);
            return true;
        }

        private void ApplyPositionToSlot(string spawnId, Vector3 worldPosition)
        {
            if (string.IsNullOrWhiteSpace(spawnId) ||
                !slotsBySpawnId.TryGetValue(spawnId, out var slot) ||
                slot == null)
            {
                return;
            }

            if (slot.IsZoneSlot && slot.Zone != null)
            {
                var anchor = slot.Zone.GetOrCreateAnchor(Mathf.Max(0, slot.ZoneSlotIndex));
                anchor.position = worldPosition;
                slot.spawnPoint = anchor;
                return;
            }

            if (slot.spawnPoint != null)
            {
                slot.spawnPoint.position = worldPosition;
            }
        }

        private Vector3 ResolveRegistrationPosition(PickupSpawnSlot slot)
        {
            if (slot != null &&
                !string.IsNullOrWhiteSpace(slot.spawnId) &&
                activePickupsBySpawnId.TryGetValue(slot.spawnId, out var activePickup) &&
                activePickup != null)
            {
                return activePickup.transform.position;
            }

            if (slot?.spawnPoint != null)
            {
                if (!string.IsNullOrWhiteSpace(slot.spawnId) &&
                    serverPositionsBySpawnId.TryGetValue(slot.spawnId, out var serverPosition))
                {
                    return serverPosition;
                }

                if (TryResolveSlotSpawnPose(slot, out var spawnPosition, out _, out _))
                {
                    return spawnPosition;
                }
            }

            return Vector3.zero;
        }

        private List<Vector3> CollectActiveZonePositions(PickupSpawnZone zone, string exceptSpawnId)
        {
            var positions = new List<Vector3>(4);
            if (zone == null)
            {
                return positions;
            }

            foreach (var entry in activePickupsBySpawnId)
            {
                if (string.Equals(entry.Key, exceptSpawnId, StringComparison.Ordinal) ||
                    entry.Value == null)
                {
                    continue;
                }

                var pickup = entry.Value;
                if (pickup.transform == null || !pickup.transform.IsChildOf(zone.transform))
                {
                    continue;
                }

                positions.Add(pickup.transform.position);
            }

            return positions;
        }



        private static string ResolveSpawnId(PickupSpawnSlot slot, Transform spawnPoint, int index)

        {

            if (slot != null && !string.IsNullOrWhiteSpace(slot.spawnId))

            {

                return slot.spawnId.Trim();

            }



            if (spawnPoint != null && !string.IsNullOrWhiteSpace(spawnPoint.name))

            {

                return spawnPoint.name.Trim();

            }



            return $"pickup_{index}";

        }



        private bool PickupMatchesCachedDefinition(WorldPickup pickup, string spawnId)
        {
            if (pickup == null ||
                !spawnedDefinitionsBySpawnId.TryGetValue(spawnId, out var expected) ||
                !expected.IsValid)
            {
                return true;
            }

            return pickup.Kind == expected.Kind &&
                   string.Equals(pickup.ItemId, expected.ResolvedItemId, StringComparison.Ordinal);
        }

        public PickupItemDefinition ResolveDefinitionFromProtocol(
            PickupKind kind,
            string itemId,
            int amount,
            int magAmmo = -1)
        {
            var resolvedItemId = string.IsNullOrWhiteSpace(itemId) ? string.Empty : itemId.Trim();
            var resolvedAmount = Mathf.Max(1, amount);

            switch (kind)
            {
                case PickupKind.Weapon:
                {
                    var weaponKind = WeaponCatalog.ResolveKindFromItemId(resolvedItemId);
                    var visual = WeaponCatalog.GetWeaponPrefab(weaponKind);
                    if (visual == null)
                    {
                        return default;
                    }

                    if (string.IsNullOrWhiteSpace(resolvedItemId))
                    {
                        resolvedItemId = WeaponCatalog.GetDefaultItemId(weaponKind);
                    }

                    var definition = PickupItemDefinition.Create(
                        PickupKind.Weapon,
                        visual,
                        resolvedItemId,
                        resolvedAmount);
                    return definition.WithMagAmmo(magAmmo >= 0 ? magAmmo : 0);
                }
                case PickupKind.Ammo:
                {
                    var ammoVisual = randomPickupPool != null
                        ? randomPickupPool.ResolveAmmoVisualPrefab(transform)
                        : null;
                    if (ammoVisual == null)
                    {
                        return default;
                    }

                    if (string.IsNullOrWhiteSpace(resolvedItemId))
                    {
                        resolvedItemId = "ammo_pack";
                    }

                    return PickupItemDefinition.Create(
                        PickupKind.Ammo,
                        ammoVisual,
                        resolvedItemId,
                        resolvedAmount);
                }
                default:
                    return default;
            }
        }



        private static bool IsWeaponDropSpawnId(string spawnId)
        {
            return !string.IsNullOrWhiteSpace(spawnId) &&
                   spawnId.StartsWith("drop_", StringComparison.Ordinal);
        }

        private static void PreparePickupVisual(GameObject pickupVisual)

        {

            if (pickupVisual == null)

            {

                return;

            }



            var nestedPickups = pickupVisual.GetComponentsInChildren<WorldPickup>(true);

            for (var i = 0; i < nestedPickups.Length; i++)

            {

                if (nestedPickups[i] != null)

                {

                    Destroy(nestedPickups[i]);

                }

            }



            DisablePickupColliders(pickupVisual);

        }



        private static void DisablePickupColliders(GameObject pickupRoot)

        {

            if (pickupRoot == null)

            {

                return;

            }



            var colliders = pickupRoot.GetComponentsInChildren<Collider>(true);

            for (var i = 0; i < colliders.Length; i++)

            {

                colliders[i].enabled = false;

            }

        }



        public void SpawnDynamicPickupAtWorld(

            string spawnId,

            Vector3 worldPosition,

            Vector3 forward,

            in PickupItemDefinition definition)

        {

            if (string.IsNullOrWhiteSpace(spawnId) || !definition.IsValid)

            {

                return;

            }



            if (activePickupsBySpawnId.ContainsKey(spawnId))

            {

                return;

            }



            EnsureDynamicSlot(spawnId, worldPosition, forward, definition);

            ApplyServerPickupRespawn(spawnId);

        }



        public void EnsureDynamicSlot(

            string spawnId,

            Vector3 worldPosition,

            Vector3 forward,

            in PickupItemDefinition definition)

        {

            if (string.IsNullOrWhiteSpace(spawnId) || !definition.IsValid)

            {

                return;

            }

            worldPosition = IsWeaponDropSpawnId(spawnId)
                ? worldPosition
                : ResolveGroundedDropPosition(worldPosition);

            spawnedDefinitionsBySpawnId[spawnId] = definition;



            if (slotsBySpawnId.TryGetValue(spawnId, out var existing) && existing != null)

            {

                existing.ConfigureFromDefinition(definition);

                if (existing.spawnPoint != null)

                {

                    existing.spawnPoint.SetPositionAndRotation(

                        worldPosition,

                        forward.sqrMagnitude > 0.001f

                            ? Quaternion.LookRotation(forward.normalized, Vector3.up)

                            : Quaternion.identity);

                }

                return;

            }



            var spawnRoot = new GameObject($"DynamicPickup_{spawnId}");

            spawnRoot.transform.SetPositionAndRotation(

                worldPosition,

                forward.sqrMagnitude > 0.001f

                    ? Quaternion.LookRotation(forward.normalized, Vector3.up)

                    : Quaternion.identity);



            var slot = new PickupSpawnSlot

            {

                spawnId = spawnId,

                spawnPoint = spawnRoot.transform

            };

            slot.ConfigureFromDefinition(definition);



            spawnSlots.Add(slot);

            RebuildSlotLookup();

        }

    }

}
