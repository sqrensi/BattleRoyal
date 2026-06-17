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
        Gloves = 3,
        Face = 4,
        Hair = 5
        // Future: Hat, Mask, Backpack, etc.
    }

    public readonly struct PlayerSkinDefinition
    {
        public PlayerSkinDefinition(
            string id,
            string displayName,
            string prefabResourcePath,
            string materialResourcePath,
            string pictureResourcePath = "")
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            PrefabResourcePath = prefabResourcePath ?? string.Empty;
            MaterialResourcePath = materialResourcePath ?? string.Empty;
            PictureResourcePath = pictureResourcePath ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string PrefabResourcePath { get; }
        public string MaterialResourcePath { get; }
        public string PictureResourcePath { get; }
        [Obsolete("Use PrefabResourcePath.")]
        public string MeshResourcePath => PrefabResourcePath;

        [Obsolete("Use PictureResourcePath.")]
        public string IconFileName => PictureResourcePath;

        public bool IsValid => !string.IsNullOrWhiteSpace(PrefabResourcePath);
    }

    public static class PlayerSkinSelectionService
    {
        private const string PrefKeyPrefix = "player_skin_";
        private const string LegacyUnequippedSkinId = "__none__";
        private const int MaxVariantProbeCount = 32;
        private const int MaxAttachmentProbeCount = 64;

        private static readonly Dictionary<PlayerSkinSlot, List<PlayerSkinDefinition>> Catalog = BuildCatalog();

        private static readonly PlayerSkinSlot[] OwnedItemDisplayOrder =
        {
            PlayerSkinSlot.Face,
            PlayerSkinSlot.Hair,
            PlayerSkinSlot.Gloves,
            PlayerSkinSlot.Pants,
            PlayerSkinSlot.Shirt,
            PlayerSkinSlot.Boots
        };

        public static IReadOnlyList<PlayerSkinDefinition> GetOptions(PlayerSkinSlot slot)
        {
            return Catalog.TryGetValue(slot, out var options) ? options : Array.Empty<PlayerSkinDefinition>();
        }

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

            var savedId = NormalizeSavedSkinId(PlayerPrefs.GetString(BuildPrefKey(slot), string.Empty));
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

            var currentId = NormalizeSavedSkinId(PlayerPrefs.GetString(BuildPrefKey(slot), string.Empty));
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
            ApplyAttachmentsToPlayer(playerRoot, forceReapply);

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

        public static void ApplyAttachmentsToPlayer(GameObject playerRoot, bool forceReapply = true)
        {
            if (playerRoot == null)
            {
                return;
            }

            var attachmentApplier = playerRoot.GetComponent<PlayerAttachmentApplier>();
            if (attachmentApplier == null)
            {
                attachmentApplier = playerRoot.AddComponent<PlayerAttachmentApplier>();
            }

            attachmentApplier.Apply(forceReapply);
        }

        private static Dictionary<PlayerSkinSlot, List<PlayerSkinDefinition>> BuildCatalog()
        {
            var catalog = new Dictionary<PlayerSkinSlot, List<PlayerSkinDefinition>>();
            foreach (PlayerSkinSlot slot in Enum.GetValues(typeof(PlayerSkinSlot)))
            {
                if (PlayerSkinResourcePaths.IsAttachmentSlot(slot))
                {
                    catalog[slot] = DiscoverAttachmentsForCategory(
                        slot,
                        PlayerSkinResourcePaths.GetAttachmentCategory(slot));
                    continue;
                }

                var category = PlayerSkinResourcePaths.GetCategoryFolder(slot);
                catalog[slot] = DiscoverSkinsForCategory(slot, category);
            }

            return catalog;
        }

        private static List<PlayerSkinDefinition> DiscoverAttachmentsForCategory(
            PlayerSkinSlot slot,
            string categoryFolder)
        {
            var results = new List<PlayerSkinDefinition>(16);
            if (string.IsNullOrWhiteSpace(categoryFolder))
            {
                return results;
            }

            var displayBase = GetDefaultDisplayName(slot);
            for (var i = 1; i <= MaxAttachmentProbeCount; i++)
            {
                var variantId = i.ToString("000");
                var prefabPath = PlayerSkinResourcePaths.BuildAttachmentPrefabPath(categoryFolder, variantId);
                if (Resources.Load<GameObject>(prefabPath) == null)
                {
                    continue;
                }

                var skinId = PlayerSkinResourcePaths.BuildAttachmentSkinId(categoryFolder, variantId);
                var displayName = results.Count == 0 ? displayBase : $"{displayBase} {variantId}";
                results.Add(new PlayerSkinDefinition(
                    skinId,
                    displayName,
                    prefabPath,
                    string.Empty,
                    PlayerSkinResourcePaths.BuildAttachmentPicturePath(categoryFolder, variantId)));
            }

            return results;
        }

        private static List<PlayerSkinDefinition> DiscoverSkinsForCategory(PlayerSkinSlot slot, string categoryFolder)
        {
            var results = new List<PlayerSkinDefinition>(4);
            if (string.IsNullOrWhiteSpace(categoryFolder))
            {
                return results;
            }

            var displayBase = GetDefaultDisplayName(slot);
            for (var i = 1; i <= MaxVariantProbeCount; i++)
            {
                var variantId = i.ToString("000");
                var prefabPath = PlayerSkinResourcePaths.BuildPrefabPath(categoryFolder, variantId);
                if (Resources.Load<GameObject>(prefabPath) == null)
                {
                    continue;
                }

                var skinId = PlayerSkinResourcePaths.BuildSkinId(categoryFolder, variantId);
                var displayName = results.Count == 0 ? displayBase : $"{displayBase} {variantId}";
                results.Add(new PlayerSkinDefinition(
                    skinId,
                    displayName,
                    prefabPath,
                    PlayerSkinResourcePaths.BuildMaterialPath(categoryFolder, variantId),
                    PlayerSkinResourcePaths.BuildPicturePath(categoryFolder, variantId)));
            }

            if (results.Count == 0)
            {
                const string fallbackVariant = "001";
                results.Add(new PlayerSkinDefinition(
                    PlayerSkinResourcePaths.BuildSkinId(categoryFolder, fallbackVariant),
                    displayBase,
                    PlayerSkinResourcePaths.BuildPrefabPath(categoryFolder, fallbackVariant),
                    PlayerSkinResourcePaths.BuildMaterialPath(categoryFolder, fallbackVariant),
                    PlayerSkinResourcePaths.BuildPicturePath(categoryFolder, fallbackVariant)));
            }

            return results;
        }

        private static string GetDefaultDisplayName(PlayerSkinSlot slot)
        {
            switch (slot)
            {
                case PlayerSkinSlot.Shirt:
                    return "Футболка";
                case PlayerSkinSlot.Pants:
                    return "Штаны";
                case PlayerSkinSlot.Boots:
                    return "Ботинки";
                case PlayerSkinSlot.Gloves:
                    return "Перчатки";
                case PlayerSkinSlot.Face:
                    return "Лицо";
                case PlayerSkinSlot.Hair:
                    return "Волосы";
                default:
                    return "Скин";
            }
        }

        private static void ResolveSkinPaths(PlayerSkinSlot slot, out string meshPath, out string materialPath)
        {
            if (TryGetAppliedSkin(slot, out var definition) && definition.IsValid)
            {
                meshPath = definition.PrefabResourcePath;
                materialPath = definition.MaterialResourcePath;
                return;
            }

            meshPath = string.Empty;
            materialPath = string.Empty;
        }

        private static string NormalizeSavedSkinId(string savedId)
        {
            if (string.IsNullOrWhiteSpace(savedId))
            {
                return savedId;
            }

            switch (savedId.Trim().ToLowerInvariant())
            {
                case "default_shirt":
                    return PlayerSkinResourcePaths.BuildSkinId("tshirts", "001");
                case "default_pants":
                    return PlayerSkinResourcePaths.BuildSkinId("pants", "001");
                case "default_boots":
                    return PlayerSkinResourcePaths.BuildSkinId("shoes", "001");
                case "default_gloves":
                    return PlayerSkinResourcePaths.BuildSkinId("gloves", "001");
                default:
                    return savedId.Trim();
            }
        }

        private static string BuildPrefKey(PlayerSkinSlot slot)
        {
            return PrefKeyPrefix + slot.ToString().ToLowerInvariant();
        }
    }
}
