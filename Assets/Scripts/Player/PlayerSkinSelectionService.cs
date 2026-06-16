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
        public PlayerSkinDefinition(string id, string displayName, string meshResourcePath, string materialResourcePath)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            MeshResourcePath = meshResourcePath ?? string.Empty;
            MaterialResourcePath = materialResourcePath ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string MeshResourcePath { get; }
        public string MaterialResourcePath { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(MeshResourcePath);
    }

    public static class PlayerSkinSelectionService
    {
        private const string PrefKeyPrefix = "player_skin_";

        private static readonly PlayerSkinDefinition DefaultShirt =
            new PlayerSkinDefinition("default_shirt", "Футболка", "1", "tshirt");

        private static readonly PlayerSkinDefinition DefaultPants =
            new PlayerSkinDefinition("default_pants", "Штаны", "2", "pants");

        private static readonly PlayerSkinDefinition DefaultBoots =
            new PlayerSkinDefinition("default_boots", "Ботинки", "3", "feets");

        private static readonly PlayerSkinDefinition DefaultGloves =
            new PlayerSkinDefinition("default_gloves", "Перчатки", "4", "gloves");

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

        public static PlayerSkinDefinition GetSelected(PlayerSkinSlot slot)
        {
            var options = GetOptions(slot);
            if (options.Count == 0)
            {
                return default;
            }

            var savedId = PlayerPrefs.GetString(BuildPrefKey(slot), string.Empty);
            if (!string.IsNullOrWhiteSpace(savedId))
            {
                for (var i = 0; i < options.Count; i++)
                {
                    if (string.Equals(options[i].Id, savedId, StringComparison.OrdinalIgnoreCase))
                    {
                        return options[i];
                    }
                }
            }

            SaveSelected(slot, options[0].Id);
            return options[0];
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

            var shirt = GetSelected(PlayerSkinSlot.Shirt);
            var pants = GetSelected(PlayerSkinSlot.Pants);
            var boots = GetSelected(PlayerSkinSlot.Boots);
            var gloves = GetSelected(PlayerSkinSlot.Gloves);

            applier.ConfigureSkinPaths(
                shirt.MeshResourcePath,
                shirt.MaterialResourcePath,
                pants.MeshResourcePath,
                pants.MaterialResourcePath,
                boots.MeshResourcePath,
                boots.MaterialResourcePath,
                gloves.MeshResourcePath,
                gloves.MaterialResourcePath);
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

        private static string BuildPrefKey(PlayerSkinSlot slot)
        {
            return PrefKeyPrefix + slot.ToString().ToLowerInvariant();
        }
    }
}
