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



            [FormerlySerializedAs("weaponPrefabOverride")]

            [SerializeField] private GameObject pickupVisualOverride;

            [SerializeField] private PickupKind pickupKindOverride = PickupKind.Weapon;

            [SerializeField] private string itemIdOverride;

            [SerializeField] private int amountOverride = 1;



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
                    : pickupKindOverride == fallbackDefault.Kind
                        ? fallbackDefault.ResolvedItemId
                        : PickupKindUtility.ToProtocol(pickupKindOverride);
                var amount = amountOverride > 0 ? amountOverride : fallbackDefault.Amount;
                return PickupItemDefinition.Create(pickupKindOverride, visual, itemId, amount);
            }

        }



        [Header("Spawn Points")]

        [SerializeField] private Transform spawnPointsRoot;

        [SerializeField] private bool autoCollectSpawnPointsFromRoot = true;

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

        [SerializeField] private bool spawnOnStart = true;

        [SerializeField] private float respawnDelaySeconds;



        private readonly Dictionary<Transform, WorldPickup> activePickupsBySpawnPoint =

            new Dictionary<Transform, WorldPickup>();

        private readonly Dictionary<string, PickupSpawnSlot> slotsBySpawnId =

            new Dictionary<string, PickupSpawnSlot>();

        private readonly Dictionary<string, WorldPickup> activePickupsBySpawnId =

            new Dictionary<string, WorldPickup>();

        private bool registeredWithServer;



        private void Awake()

        {

            if (GetComponent<MatchPickupSync>() == null)

            {

                gameObject.AddComponent<MatchPickupSync>();

            }

        }



        private void Start()

        {

            RebuildSlotLookup();

            if (spawnOnStart)

            {

                SpawnAll();

            }

        }

        [ContextMenu("Rebuild Spawn Points From Root")]

        public void RebuildSpawnPointsFromRoot()

        {

            if (spawnSlots == null)

            {

                spawnSlots = new List<PickupSpawnSlot>();

            }



            spawnSlots.Clear();

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



        [ContextMenu("Spawn All Pickups")]

        public void SpawnAll()

        {

            if (autoCollectSpawnPointsFromRoot && spawnPointsRoot != null &&

                (spawnSlots == null || spawnSlots.Count == 0))

            {

                RebuildSpawnPointsFromRoot();

            }



            RebuildSlotLookup();

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



                SpawnAtSlot(slot);

            }

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



                var definition = ResolvePickupDefinition(slot);

                if (!definition.IsValid)

                {

                    continue;

                }



                var position = slot.spawnPoint.position + Vector3.up * pickupYOffset;

                var itemId = definition.ResolvedItemId;

                entries.Add(new RealtimeTransportClient.PickupSpawnRegistration

                {

                    spawnId = slot.spawnId,

                    pickupKind = PickupKindUtility.ToProtocol(definition.Kind),

                    itemId = itemId,

                    weaponId = itemId,

                    amount = definition.Amount,

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



            RebuildSlotLookup();

            for (var i = 0; i < spawns.Length; i++)

            {

                var spawn = spawns[i];

                if (spawn == null || string.IsNullOrWhiteSpace(spawn.spawnId))

                {

                    continue;

                }



                if (spawn.available)

                {

                    ApplyServerPickupRespawn(spawn.spawnId);

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



        public void ApplyServerPickupRespawn(string spawnId)

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



            if (activePickupsBySpawnId.ContainsKey(spawnId))

            {

                return;

            }



            SpawnAtSlot(slot);

        }



        public PickupItemDefinition ResolveDefinitionForSpawnId(string spawnId)

        {

            if (string.IsNullOrWhiteSpace(spawnId) ||

                !slotsBySpawnId.TryGetValue(spawnId, out var slot))

            {

                return ResolveDefaultPickupDefinition();

            }



            return ResolvePickupDefinition(slot);

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



            var definition = ResolvePickupDefinition(slot);

            if (!definition.IsValid)

            {

                Debug.LogWarning("[PickupSpawnManager] No pickup definition assigned for spawn slot.");

                return;

            }



            var spawnPosition = slot.spawnPoint.position + Vector3.up * pickupYOffset;

            var spawnRotation = slot.spawnPoint.rotation * Quaternion.Euler(worldPickupEulerOffset);

            var pickupRoot = new GameObject($"{definition.ResolvedItemId}_Pickup");

            pickupRoot.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

            pickupRoot.transform.localScale = worldPickupLocalScale;

            pickupRoot.transform.SetParent(slot.spawnPoint, true);



            var pickupVisual = Instantiate(definition.VisualPrefab, pickupRoot.transform);

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

            return PickupItemDefinition.Create(

                defaultPickupKind,

                defaultVisualPrefab,

                defaultItemId,

                defaultAmount);

        }



        private PickupItemDefinition ResolvePickupDefinition(PickupSpawnSlot slot)

        {

            return slot != null

                ? slot.ResolveDefinition(ResolveDefaultPickupDefinition())

                : ResolveDefaultPickupDefinition();

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

    }

}


