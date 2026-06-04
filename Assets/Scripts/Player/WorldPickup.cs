using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class WorldPickup : MonoBehaviour
    {
        private static readonly List<WorldPickup> ActivePickups = new List<WorldPickup>();

        [SerializeField] private PickupKind kind = PickupKind.Weapon;
        [SerializeField] private GameObject visualPrefab;
        [SerializeField] private string itemId;
        [SerializeField] private int amount = 1;
        [SerializeField] private float pickupRadius = 2.5f;
        [SerializeField] private float pickupVerticalHalfHeight = 2f;

        private PickupSpawnManager owner;
        private Transform spawnPoint;
        private string spawnId;
        private SphereCollider triggerCollider;

        public PickupKind Kind => kind;
        public GameObject VisualPrefab => visualPrefab;
        public string ItemId => string.IsNullOrWhiteSpace(itemId)
            ? visualPrefab != null ? visualPrefab.name : string.Empty
            : itemId.Trim();
        public int Amount => Mathf.Max(1, amount);
        public float PickupRadius => pickupRadius;
        public string SpawnId => spawnId;
        public Transform SpawnPoint => spawnPoint;

        public PickupItemDefinition Definition =>
            PickupItemDefinition.Create(kind, visualPrefab, ItemId, Amount);

        public static IReadOnlyList<WorldPickup> Active => ActivePickups;

        public void Initialize(
            PickupSpawnManager spawnManager,
            Transform sourceSpawnPoint,
            string sourceSpawnId,
            in PickupItemDefinition definition)
        {
            owner = spawnManager;
            spawnPoint = sourceSpawnPoint;
            spawnId = string.IsNullOrWhiteSpace(sourceSpawnId)
                ? sourceSpawnPoint != null ? sourceSpawnPoint.name : string.Empty
                : sourceSpawnId.Trim();
            kind = definition.Kind;
            visualPrefab = definition.VisualPrefab;
            itemId = definition.ResolvedItemId;
            amount = definition.Amount;
            EnsureTriggerCollider();
        }

        private void OnEnable()
        {
            if (!ActivePickups.Contains(this))
            {
                ActivePickups.Add(this);
            }
        }

        private void OnDisable()
        {
            ActivePickups.Remove(this);
        }

        public bool IsAvailableForPickup(
            Vector3 playerPosition,
            Vector3 playerForward,
            float minFacingDot,
            out float distanceSqr)
        {
            distanceSqr = float.MaxValue;
            if (!Definition.IsValid)
            {
                return false;
            }

            var center = transform.position;
            var offset = playerPosition - center;
            if (Mathf.Abs(offset.y) > pickupVerticalHalfHeight + 0.35f)
            {
                return false;
            }

            offset.y = 0f;
            distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > pickupRadius * pickupRadius)
            {
                return false;
            }

            if (PickupApplyRules.RequiresFacingCheck(kind) &&
                playerForward.sqrMagnitude > 0.0001f)
            {
                var toPickup = center - playerPosition;
                toPickup.y = 0f;
                if (toPickup.sqrMagnitude > 0.0001f)
                {
                    var facing = Vector3.Dot(playerForward.normalized, toPickup.normalized);
                    if (facing < minFacingDot)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public void Collect(PickupSpawnManager collectorOwner)
        {
            if (collectorOwner != null)
            {
                owner = collectorOwner;
            }

            owner?.NotifyPickupCollected(spawnPoint, Definition);
            Destroy(gameObject);
        }

        private void EnsureTriggerCollider()
        {
            if (TryGetComponent(out triggerCollider))
            {
                triggerCollider.isTrigger = true;
                triggerCollider.radius = pickupRadius;
                return;
            }

            triggerCollider = gameObject.AddComponent<SphereCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.radius = pickupRadius;
        }
    }
}
