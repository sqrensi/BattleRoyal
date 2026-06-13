using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    [Serializable]
    public sealed class PickupSpawnRandomPool
    {
        [Serializable]
        public sealed class Entry
        {
            public PickupKind kind = PickupKind.Weapon;
            public WeaponKind weaponKind = WeaponKind.AssaultRifle;
            public GameObject visualPrefab;
            public string itemId;
            public int amount = 1;
            [Min(0f)] public float weight = 1f;
        }

        [SerializeField] private bool enabled = true;
        [SerializeField] private List<Entry> entries = new List<Entry>();
        [SerializeField] private GameObject ammoVisualPrefab;
        [SerializeField] private string ammoItemId = "ammo_pack";
        [SerializeField] private int ammoPickupAmount = 30;

        public bool Enabled => enabled;

        public PickupItemDefinition Roll(Transform owner, string deterministicSpawnId = null)
        {
            EnsureDefaultEntries();
            if (!enabled || entries == null || entries.Count == 0)
            {
                return default;
            }

            var previousState = UnityEngine.Random.state;
            if (!string.IsNullOrWhiteSpace(deterministicSpawnId))
            {
                UnityEngine.Random.InitState(ComputeStableSeed(deterministicSpawnId));
            }

            try
            {
                return RollInternal(owner);
            }
            finally
            {
                UnityEngine.Random.state = previousState;
            }
        }

        private PickupItemDefinition RollInternal(Transform owner)
        {
            var resolvedAmmoVisual = ResolveAmmoVisualPrefab(owner);

            var totalWeight = 0f;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.weight <= 0f)
                {
                    continue;
                }

                totalWeight += entry.weight;
            }

            if (totalWeight <= 0f)
            {
                return default;
            }

            var roll = UnityEngine.Random.value * totalWeight;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.weight <= 0f)
                {
                    continue;
                }

                roll -= entry.weight;
                if (roll > 0f)
                {
                    continue;
                }

                return BuildDefinition(entry, resolvedAmmoVisual);
            }

            return BuildDefinition(entries[entries.Count - 1], resolvedAmmoVisual);
        }

        private static int ComputeStableSeed(string value)
        {
            unchecked
            {
                var hash = 17;
                for (var i = 0; i < value.Length; i++)
                {
                    hash = (hash * 31) + value[i];
                }

                return hash;
            }
        }

        public GameObject ResolveAmmoVisualPrefab(Transform owner)
        {
            if (ammoVisualPrefab != null && !IsWeaponLikePrefab(ammoVisualPrefab))
            {
                return ammoVisualPrefab;
            }

            if (owner == null)
            {
                return null;
            }

            var existing = owner.Find("AmmoPickupTemplate");
            if (existing != null)
            {
                return existing.gameObject;
            }

            var template = new GameObject("AmmoPickupTemplate");
            template.transform.SetParent(owner, false);
            template.SetActive(false);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "AmmoBox";
            cube.transform.SetParent(template.transform, false);
            cube.transform.localPosition = Vector3.zero;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = new Vector3(0.32f, 0.14f, 0.48f);

            var collider = cube.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }

            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateAmmoMaterial();
            }

            return template;
        }

        public void NormalizeEntryWeights()
        {
            if (entries == null || entries.Count == 0)
            {
                return;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && entry.weight <= 0f)
                {
                    entry.weight = 1f;
                }
            }
        }

        public void EnsureDefaultEntries()
        {
            if (entries == null)
            {
                entries = new List<Entry>();
            }

            if (entries.Count > 0)
            {
                NormalizeEntryWeights();
                return;
            }

            entries.Add(new Entry
            {
                kind = PickupKind.Weapon,
                weaponKind = WeaponKind.AssaultRifle,
                visualPrefab = WeaponCatalog.GetWeaponPrefab(WeaponKind.AssaultRifle),
                itemId = WeaponCatalog.GetDefaultItemId(WeaponKind.AssaultRifle),
                amount = 1,
                weight = 1f
            });
            entries.Add(new Entry
            {
                kind = PickupKind.Weapon,
                weaponKind = WeaponKind.SniperRifle,
                visualPrefab = WeaponCatalog.GetWeaponPrefab(WeaponKind.SniperRifle),
                itemId = WeaponCatalog.GetDefaultItemId(WeaponKind.SniperRifle),
                amount = 1,
                weight = 1f
            });
            entries.Add(new Entry
            {
                kind = PickupKind.Weapon,
                weaponKind = WeaponKind.Pistol,
                visualPrefab = WeaponCatalog.GetWeaponPrefab(WeaponKind.Pistol),
                itemId = WeaponCatalog.GetDefaultItemId(WeaponKind.Pistol),
                amount = 1,
                weight = 1f
            });
            entries.Add(new Entry
            {
                kind = PickupKind.Weapon,
                weaponKind = WeaponKind.Mp7,
                visualPrefab = WeaponCatalog.GetWeaponPrefab(WeaponKind.Mp7),
                itemId = WeaponCatalog.GetDefaultItemId(WeaponKind.Mp7),
                amount = 1,
                weight = 1f
            });
            entries.Add(new Entry
            {
                kind = PickupKind.Ammo,
                amount = Mathf.Max(1, ammoPickupAmount),
                itemId = ammoItemId,
                weight = 1f
            });
        }

        private PickupItemDefinition BuildDefinition(Entry entry, GameObject resolvedAmmoVisual)
        {
            if (entry == null)
            {
                return default;
            }

            if (entry.kind == PickupKind.Ammo)
            {
                var ammoVisual = resolvedAmmoVisual;
                if (ammoVisual != null && IsWeaponLikePrefab(ammoVisual))
                {
                    ammoVisual = null;
                }

                if (ammoVisual == null)
                {
                    return default;
                }

                var ammoId = string.IsNullOrWhiteSpace(entry.itemId) ? ammoItemId : entry.itemId.Trim();
                var ammoAmount = entry.amount > 0 ? entry.amount : ammoPickupAmount;
                return PickupItemDefinition.Create(PickupKind.Ammo, ammoVisual, ammoId, ammoAmount);
            }

            var visual = entry.visualPrefab;
            if (visual == null && entry.kind == PickupKind.Weapon)
            {
                visual = WeaponCatalog.GetWeaponPrefab(entry.weaponKind);
            }

            var itemId = !string.IsNullOrWhiteSpace(entry.itemId)
                ? entry.itemId.Trim()
                : entry.kind == PickupKind.Weapon
                    ? WeaponCatalog.GetDefaultItemId(entry.weaponKind)
                    : visual != null ? visual.name : string.Empty;
            var amount = entry.amount > 0 ? entry.amount : 1;

            if (entry.kind == PickupKind.Weapon)
            {
                if (visual == null)
                {
                    return default;
                }

                return PickupItemDefinition.Create(PickupKind.Weapon, visual, itemId, amount).WithMagAmmo(0);
            }

            if (visual == null)
            {
                return default;
            }

            return PickupItemDefinition.Create(entry.kind, visual, itemId, amount);
        }

        private static bool IsWeaponLikePrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            return prefab.GetComponent<WeaponProfile>() != null ||
                   prefab.GetComponentInChildren<WeaponProfile>(true) != null;
        }

        private static Material CreateAmmoMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                color = new Color(0.82f, 0.68f, 0.18f, 1f)
            };
            return material;
        }
    }
}
