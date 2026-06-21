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

        [Header("Ammo Visual Prefabs")]
        [Tooltip("World pickup prefab for assault rifle ammo.")]
        [SerializeField] private GameObject assaultAmmoPrefab;
        [Tooltip("World pickup prefab for sniper rifle ammo.")]
        [SerializeField] private GameObject sniperAmmoPrefab;
        [Tooltip("World pickup prefab for pistol ammo.")]
        [SerializeField] private GameObject pistolAmmoPrefab;
        [Tooltip("World pickup prefab for MP7 ammo.")]
        [SerializeField] private GameObject mp7AmmoPrefab;

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
                return RollInternal();
            }
            finally
            {
                UnityEngine.Random.state = previousState;
            }
        }

        public WeaponKind RollStandaloneAmmoKind(string deterministicSpawnId)
        {
            var previousState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(ComputeStableSeed($"{deterministicSpawnId}_standalone_ammo_kind"));
            try
            {
                return (WeaponKind)UnityEngine.Random.Range(0, 4);
            }
            finally
            {
                UnityEngine.Random.state = previousState;
            }
        }

        private PickupItemDefinition RollInternal()
        {
            var totalWeight = 0f;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.weight <= 0f || entry.kind != PickupKind.Weapon)
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
                if (entry == null || entry.weight <= 0f || entry.kind != PickupKind.Weapon)
                {
                    continue;
                }

                roll -= entry.weight;
                if (roll > 0f)
                {
                    continue;
                }

                return BuildDefinition(entry);
            }

            return BuildDefinition(entries[entries.Count - 1]);
        }

        public static int ComputeStableSeed(string value)
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

        public PickupItemDefinition BuildAmmoDefinition(WeaponKind weaponKind)
        {
            var ammoVisual = ResolveAmmoVisualPrefab(weaponKind);
            if (ammoVisual == null)
            {
                return default;
            }

            return PickupItemDefinition.Create(
                PickupKind.Ammo,
                ammoVisual,
                AmmoCatalog.GetAmmoItemId(weaponKind),
                AmmoCatalog.GetDefaultPickupAmount(weaponKind));
        }

        public GameObject ResolveAmmoVisualPrefab(WeaponKind weaponKind)
        {
            var assigned = GetAssignedAmmoPrefab(weaponKind);
            if (assigned != null && !IsWeaponLikePrefab(assigned))
            {
                return assigned;
            }

            return CreateFallbackAmmoTemplate(weaponKind);
        }

        public GameObject GetAssignedAmmoPrefab(WeaponKind weaponKind)
        {
            switch (weaponKind)
            {
                case WeaponKind.SniperRifle:
                    return sniperAmmoPrefab;
                case WeaponKind.Pistol:
                    return pistolAmmoPrefab;
                case WeaponKind.Mp7:
                    return mp7AmmoPrefab;
                default:
                    return assaultAmmoPrefab;
            }
        }

        private GameObject CreateFallbackAmmoTemplate(WeaponKind weaponKind)
        {
            var templateName = $"AmmoPickupTemplate_{weaponKind}";
            var existing = GameObject.Find(templateName);
            if (existing != null)
            {
                return existing;
            }

            var template = new GameObject(templateName);
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
                renderer.sharedMaterial = CreateAmmoMaterial(AmmoCatalog.GetAmmoColor(weaponKind));
            }

            return template;
        }

        public void ApplyInspectorAmmoPrefabs(
            GameObject assaultPrefab,
            GameObject sniperPrefab,
            GameObject pistolPrefab,
            GameObject mp7Prefab)
        {
            if (assaultPrefab != null)
            {
                assaultAmmoPrefab = assaultPrefab;
            }

            if (sniperPrefab != null)
            {
                sniperAmmoPrefab = sniperPrefab;
            }

            if (pistolPrefab != null)
            {
                pistolAmmoPrefab = pistolPrefab;
            }

            if (mp7Prefab != null)
            {
                mp7AmmoPrefab = mp7Prefab;
            }
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
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null || entry.kind != PickupKind.Weapon)
                    {
                        continue;
                    }

                    if (entry.visualPrefab == null)
                    {
                        entry.visualPrefab = WeaponCatalog.GetWeaponPrefab(entry.weaponKind);
                    }

                    if (string.IsNullOrWhiteSpace(entry.itemId))
                    {
                        entry.itemId = WeaponCatalog.GetDefaultItemId(entry.weaponKind);
                    }
                }

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
        }

        private PickupItemDefinition BuildDefinition(Entry entry)
        {
            if (entry == null || entry.kind != PickupKind.Weapon)
            {
                return default;
            }

            var visual = entry.visualPrefab ?? WeaponCatalog.GetWeaponPrefab(entry.weaponKind);
            if (visual == null)
            {
                return default;
            }

            var itemId = !string.IsNullOrWhiteSpace(entry.itemId)
                ? entry.itemId.Trim()
                : WeaponCatalog.GetDefaultItemId(entry.weaponKind);
            var amount = entry.amount > 0 ? entry.amount : 1;
            return PickupItemDefinition.Create(PickupKind.Weapon, visual, itemId, amount).WithMagAmmo(0);
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

        private static Material CreateAmmoMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(shader) { color = color };
        }
    }
}
