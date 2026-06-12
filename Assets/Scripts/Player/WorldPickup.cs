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
        [SerializeField] private int magAmmo = -1;
        [SerializeField] private float pickupVerticalHalfHeight = 3f;

        private PickupSpawnManager owner;
        private Transform spawnPoint;
        private string spawnId;

        public PickupKind Kind => kind;
        public GameObject VisualPrefab => visualPrefab;
        public string ItemId => string.IsNullOrWhiteSpace(itemId)
            ? visualPrefab != null ? visualPrefab.name : string.Empty
            : itemId.Trim();
        public int Amount => Mathf.Max(1, amount);
        public string SpawnId => spawnId;
        public Transform SpawnPoint => spawnPoint;

        public PickupItemDefinition Definition
        {
            get
            {
                var definition = PickupItemDefinition.Create(kind, visualPrefab, ItemId, Amount);
                if (kind == PickupKind.Weapon && magAmmo >= 0)
                {
                    definition = definition.WithMagAmmo(magAmmo);
                }

                return definition;
            }
        }

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
            magAmmo = definition.MagAmmo;
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
            Vector3 lookOrigin,
            Vector3 lookForward,
            float maxRadius,
            float minFacingDot,
            out float distanceSqr)
        {
            distanceSqr = float.MaxValue;
            if (!Definition.IsValid)
            {
                return false;
            }

            var center = transform.position;
            var horizontalOffset = playerPosition - center;
            if (Mathf.Abs(horizontalOffset.y) > pickupVerticalHalfHeight + 0.35f)
            {
                return false;
            }

            horizontalOffset.y = 0f;
            distanceSqr = horizontalOffset.sqrMagnitude;
            if (maxRadius > 0.01f && distanceSqr > maxRadius * maxRadius)
            {
                return false;
            }

            if (!PickupApplyRules.RequiresFacingCheck(kind))
            {
                return true;
            }

            if (lookForward.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            var toPickup = center - lookOrigin;
            toPickup.y = 0f;
            if (toPickup.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            var facing = Vector3.Dot(lookForward.normalized, toPickup.normalized);
            return facing >= minFacingDot;
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
    }
}
