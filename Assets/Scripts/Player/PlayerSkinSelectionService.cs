using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public enum PlayerSkinSlot
    {
        Shirt = 0,
        Pants = 1,
        Boots = 2,
        Gloves = 3
        // Future: Hat, Mask, Backpack, etc.
    }

    public readonly struct PlayerSkinDefinition
    {
        public PlayerSkinDefinition(
            string id,
            string displayName,
            string meshResourcePath,
            string materialResourcePath,
            string iconFileName = "")
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            MeshResourcePath = meshResourcePath ?? string.Empty;
            MaterialResourcePath = materialResourcePath ?? string.Empty;
            IconFileName = iconFileName ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string MeshResourcePath { get; }
        public string MaterialResourcePath { get; }
        public string IconFileName { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(MeshResourcePath);
    }

    public static class PlayerSkinSelectionService
    {
        private const string PrefKeyPrefix = "player_skin_";
        private const string LegacyUnequippedSkinId = "__none__";

        private static readonly PlayerSkinDefinition DefaultShirt =
            new PlayerSkinDefinition("default_shirt", "Футболка", "1", "tshirt", "clothes_tshirt.png");

        private static readonly PlayerSkinDefinition DefaultPants =
            new PlayerSkinDefinition("default_pants", "Штаны", "2", "pants", "clothes_pants.png");

        private static readonly PlayerSkinDefinition DefaultBoots =
            new PlayerSkinDefinition("default_boots", "Ботинки", "3", "feets", "clothes_shoes.png");

        private static readonly PlayerSkinDefinition DefaultGloves =
            new PlayerSkinDefinition("default_gloves", "Перчатки", "4", "gloves", "clothes_gloves.png");

        private static readonly Dictionary<PlayerSkinSlot, List<PlayerSkinDefinition>> Catalog =
            new Dictionary<PlayerSkinSlot, List<PlayerSkinDefinition>>
            {
                {
                    PlayerSkinSlot.Shirt,
                    new List<PlayerSkinDefinition> { DefaultShirt }
                },
                {
                    PlayerSkinSlot.Pants,
                    new List<PlayerSkinDefinition> { DefaultPants }
                },
                {
                    PlayerSkinSlot.Boots,
                    new List<PlayerSkinDefinition> { DefaultBoots }
                },
                {
                    PlayerSkinSlot.Gloves,
                    new List<PlayerSkinDefinition> { DefaultGloves }
                }
            };

        public static IReadOnlyList<PlayerSkinDefinition> GetOptions(PlayerSkinSlot slot)
        {
            return Catalog.TryGetValue(slot, out var options) ? options : Array.Empty<PlayerSkinDefinition>();
        }

        private static readonly PlayerSkinSlot[] OwnedItemDisplayOrder =
        {
            PlayerSkinSlot.Gloves,
            PlayerSkinSlot.Pants,
            PlayerSkinSlot.Shirt,
            PlayerSkinSlot.Boots
        };

        public static IReadOnlyList<PlayerSkinDefinition> GetOwnedItems()
        {
            var owned = new List<PlayerSkinDefinition>(OwnedItemDisplayOrder.Length);
            for (var i = 0; i < OwnedItemDisplayOrder.Length; i++)
            {
                var options = GetOptions(OwnedItemDisplayOrder[i]);
                for (var j = 0; j < options.Count; j++)
                {
                    owned.Add(options[j]);
                }
            }

            return owned;
        }

        public static bool TryResolveSlot(PlayerSkinDefinition item, out PlayerSkinSlot slot)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                slot = default;
                return false;
            }

            foreach (PlayerSkinSlot candidate in Enum.GetValues(typeof(PlayerSkinSlot)))
            {
                var options = GetOptions(candidate);
                for (var i = 0; i < options.Count; i++)
                {
                    if (string.Equals(options[i].Id, item.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        slot = candidate;
                        return true;
                    }
                }
            }

            slot = default;
            return false;
        }

        public static bool TryGetAppliedSkin(PlayerSkinSlot slot, out PlayerSkinDefinition definition)
        {
            var options = GetOptions(slot);
            if (options.Count == 0)
            {
                definition = default;
                return false;
            }

            var savedId = PlayerPrefs.GetString(BuildPrefKey(slot), string.Empty);
            if (string.IsNullOrWhiteSpace(savedId) ||
                string.Equals(savedId, LegacyUnequippedSkinId, StringComparison.OrdinalIgnoreCase))
            {
                definition = options[0];
                return true;
            }

            for (var i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].Id, savedId, StringComparison.OrdinalIgnoreCase))
                {
                    definition = options[i];
                    return true;
                }
            }

            definition = default;
            return false;
        }

        public static bool IsEquipped(PlayerSkinDefinition item)
        {
            if (!TryResolveSlot(item, out var slot))
            {
                return false;
            }

            return TryGetAppliedSkin(slot, out var applied) &&
                   string.Equals(applied.Id, item.Id, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryEquip(PlayerSkinDefinition item)
        {
            if (!TryResolveSlot(item, out var slot) || !item.IsValid || IsEquipped(item))
            {
                return false;
            }

            SaveSelected(slot, item.Id);
            return true;
        }

        public static PlayerSkinDefinition GetSelected(PlayerSkinSlot slot)
        {
            return TryGetAppliedSkin(slot, out var definition) ? definition : default;
        }

        public static PlayerSkinDefinition SelectNext(PlayerSkinSlot slot)
        {
            var options = GetOptions(slot);
            if (options.Count == 0)
            {
                return default;
            }

            if (options.Count == 1)
            {
                SaveSelected(slot, options[0].Id);
                return options[0];
            }

            var currentId = PlayerPrefs.GetString(BuildPrefKey(slot), string.Empty);
            var currentIndex = -1;
            for (var i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].Id, currentId, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }

            var nextIndex = (currentIndex + 1 + options.Count) % options.Count;
            SaveSelected(slot, options[nextIndex].Id);
            return options[nextIndex];
        }

        public static void SaveSelected(PlayerSkinSlot slot, string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return;
            }

            PlayerPrefs.SetString(BuildPrefKey(slot), skinId.Trim());
            PlayerPrefs.Save();
        }

        public static void ApplyTo(RemoteResourceClothingApplier applier)
        {
            if (applier == null)
            {
                return;
            }

            ResolveSkinPaths(PlayerSkinSlot.Shirt, out var shirtMesh, out var shirtMaterial);
            ResolveSkinPaths(PlayerSkinSlot.Pants, out var pantsMesh, out var pantsMaterial);
            ResolveSkinPaths(PlayerSkinSlot.Boots, out var bootsMesh, out var bootsMaterial);
            ResolveSkinPaths(PlayerSkinSlot.Gloves, out var glovesMesh, out var glovesMaterial);

            applier.ConfigureSkinPaths(
                shirtMesh,
                shirtMaterial,
                pantsMesh,
                pantsMaterial,
                bootsMesh,
                bootsMaterial,
                glovesMesh,
                glovesMaterial);
        }

        public static void ApplyToPlayer(GameObject playerRoot, bool forceReapply = true)
        {
            if (playerRoot == null)
            {
                return;
            }

            var clothingApplier = playerRoot.GetComponent<RemoteResourceClothingApplier>();
            if (clothingApplier == null)
            {
                clothingApplier = playerRoot.AddComponent<RemoteResourceClothingApplier>();
            }

            ApplyTo(clothingApplier);

            var thirdPersonBody = playerRoot.transform.Find("ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            if (syntyVisual == null)
            {
                return;
            }

            var armsPresenter = playerRoot.GetComponent<SyntyFirstPersonArmsPresenter>();
            if (armsPresenter != null && playerRoot.GetComponent<RemoteThirdPersonPlayerBootstrap>() == null)
            {
                clothingApplier.ApplyToLocalVisual(syntyVisual, armsPresenter, forceReapply);
                return;
            }

            clothingApplier.ApplyToRemoteVisual(syntyVisual, forceReapply);
        }

        private static void ResolveSkinPaths(PlayerSkinSlot slot, out string meshPath, out string materialPath)
        {
            if (TryGetAppliedSkin(slot, out var definition) && definition.IsValid)
            {
                meshPath = definition.MeshResourcePath;
                materialPath = definition.MaterialResourcePath;
                return;
            }

            meshPath = string.Empty;
            materialPath = string.Empty;
        }

        private static string BuildPrefKey(PlayerSkinSlot slot)
        {
            return PrefKeyPrefix + slot.ToString().ToLowerInvariant();
        }
    }
}
