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
        Hair = 5,
        WeaponAssaultRifle = 6,
        WeaponSniperRifle = 7,
        WeaponPistol = 8,
        WeaponMp7 = 9
    }

    public readonly struct PlayerSkinDefinition
    {
        public PlayerSkinDefinition(
            string id,
            string displayName,
            string prefabResourcePath,
            string materialResourcePath,
            string pictureResourcePath = "",
            WeaponKind? weaponKind = null)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            PrefabResourcePath = prefabResourcePath ?? string.Empty;
            MaterialResourcePath = materialResourcePath ?? string.Empty;
            PictureResourcePath = pictureResourcePath ?? string.Empty;
            WeaponKind = weaponKind;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string PrefabResourcePath { get; }
        public string MaterialResourcePath { get; }
        public string PictureResourcePath { get; }
        public WeaponKind? WeaponKind { get; }
        [Obsolete("Use PrefabResourcePath.")]
        public string MeshResourcePath => PrefabResourcePath;

        [Obsolete("Use PictureResourcePath.")]
        public string IconFileName => PictureResourcePath;

        public bool IsWeaponSkin => WeaponKind.HasValue;

        public bool IsValid =>
            IsWeaponSkin
                ? !string.IsNullOrWhiteSpace(PictureResourcePath)
                : !string.IsNullOrWhiteSpace(PrefabResourcePath);
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
            PlayerSkinSlot.Boots,
            PlayerSkinSlot.WeaponAssaultRifle,
            PlayerSkinSlot.WeaponSniperRifle,
            PlayerSkinSlot.WeaponPistol,
            PlayerSkinSlot.WeaponMp7
        };

        public static IReadOnlyList<PlayerSkinDefinition> GetOptions(PlayerSkinSlot slot)
        {
            return Catalog.TryGetValue(slot, out var options) ? options : Array.Empty<PlayerSkinDefinition>();
        }

        public static IReadOnlyList<PlayerSkinDefinition> GetCatalogOptions(PlayerSkinSlot slot)
        {
            return GetOptions(slot);
        }

        public static PlayerSkinSlot[] GetDisplaySlotOrder()
        {
            return OwnedItemDisplayOrder;
        }

        public static IReadOnlyList<PlayerSkinDefinition> GetOwnedItems()
        {
            PlayerSkinOwnershipService.EnsureInitialized();
            return PlayerSkinOwnershipService.GetOwnedCatalogItems();
        }

        public static bool TryResolveWeaponKind(PlayerSkinDefinition item, out WeaponKind kind)
        {
            if (item.WeaponKind.HasValue)
            {
                kind = item.WeaponKind.Value;
                return true;
            }

            kind = default;
            return false;
        }

        public static bool TryGetEquippedWeaponSkin(WeaponKind kind, out PlayerSkinDefinition definition)
        {
            if (!WeaponSkinResourcePaths.TryGetSlotForKind(kind, out var slot))
            {
                definition = default;
                return false;
            }

            return TryGetAppliedSkin(slot, out definition);
        }

        public static bool TryGetDefinitionById(string skinId, out PlayerSkinDefinition definition)
        {
            definition = default;
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return false;
            }

            foreach (PlayerSkinSlot slot in Enum.GetValues(typeof(PlayerSkinSlot)))
            {
                var options = GetOptions(slot);
                for (var i = 0; i < options.Count; i++)
                {
                    if (string.Equals(options[i].Id, skinId, StringComparison.OrdinalIgnoreCase))
                    {
                        definition = options[i];
                        return true;
                    }
                }
            }

            return false;
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
            PlayerSkinOwnershipService.EnsureInitialized();
            definition = default;

            if (PlayerSkinResourcePaths.IsAttachmentSlot(slot))
            {
                var attachmentId = ResolveEquippedSkinId(slot);
                if (string.IsNullOrWhiteSpace(attachmentId))
                {
                    return false;
                }

                return TryGetDefinitionById(attachmentId, out definition);
            }

            var skinId = ResolveEquippedSkinId(slot);
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return false;
            }

            return TryGetDefinitionById(skinId, out definition);
        }

        public static bool SupportsUnequip(PlayerSkinSlot slot)
        {
            return PlayerSkinResourcePaths.IsAttachmentSlot(slot) ||
                   WeaponSkinResourcePaths.IsWeaponSkinSlot(slot);
        }

        public static string GetSavedSkinId(PlayerSkinSlot slot)
        {
            return NormalizeSavedSkinId(PlayerPrefs.GetString(BuildPrefKey(slot), string.Empty));
        }

        /// <summary>
        /// Authoritative equipped skin id for UI, visuals, and network capture.
        /// When server-synced, profile DTO wins; otherwise PlayerPrefs with catalog defaults.
        /// </summary>
        public static string ResolveEquippedSkinId(PlayerSkinSlot slot)
        {
            PlayerSkinOwnershipService.EnsureInitialized();

            if (WeaponSkinResourcePaths.IsWeaponSkinSlot(slot))
            {
                return ResolveEquippedWeaponSkinId(slot);
            }

            if (PlayerSkinResourcePaths.IsAttachmentSlot(slot))
            {
                return ResolveEquippedAttachmentSkinId(slot);
            }

            return ResolveEquippedClothingSkinId(slot);
        }

        public static bool IsSlotApplied(PlayerSkinSlot slot)
        {
            return TryGetAppliedSkin(slot, out _);
        }

        public static bool IsEquipped(PlayerSkinDefinition item)
        {
            if (!TryResolveSlot(item, out var slot))
            {
                return false;
            }

            var savedId = ResolveEquippedSkinId(slot);
            if (WeaponSkinResourcePaths.IsWeaponSkinSlot(slot))
            {
                if (string.IsNullOrWhiteSpace(savedId))
                {
                    return false;
                }

                if (WeaponSkinResourcePaths.TryParseSkinId(savedId, out _, out var variantId) &&
                    string.Equals(variantId, "000", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return string.Equals(savedId, item.Id, StringComparison.OrdinalIgnoreCase);
            }

            if (PlayerSkinResourcePaths.IsAttachmentSlot(slot) &&
                string.Equals(savedId, LegacyUnequippedSkinId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(savedId) &&
                   string.Equals(savedId, item.Id, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryEquip(PlayerSkinDefinition item)
        {
            PlayerSkinOwnershipService.EnsureInitialized();
            if (!PlayerSkinOwnershipService.IsOwned(item) ||
                !TryResolveSlot(item, out var slot) ||
                !item.IsValid)
            {
                return false;
            }

            if (PlayerProfileService.IsServerSynced)
            {
                return false;
            }

            if (IsEquipped(item))
            {
                if (!SupportsUnequip(slot))
                {
                    return false;
                }

                if (WeaponSkinResourcePaths.IsWeaponSkinSlot(slot) &&
                    WeaponSkinResourcePaths.TryGetWeaponKind(slot, out var weaponKind))
                {
                    SaveSelected(slot, WeaponSkinResourcePaths.BuildSkinId(weaponKind, "000"));
                }
                else
                {
                    SaveSelected(slot, LegacyUnequippedSkinId);
                }

                return true;
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
            ApplyTo(applier, CaptureLocalNetworkState());
        }

        public static PlayerSkinNetworkState CaptureLocalNetworkState()
        {
            return BuildResolvedNetworkState();
        }

        public static PlayerSkinNetworkState CaptureFromEquipped(PlayerProfileEquippedDto equipped)
        {
            if (equipped == null)
            {
                return BuildResolvedNetworkState();
            }

            return new PlayerSkinNetworkState(
                ResolveEquippedClothingSkinId(PlayerSkinSlot.Shirt, equipped.shirt),
                ResolveEquippedClothingSkinId(PlayerSkinSlot.Pants, equipped.pants),
                ResolveEquippedClothingSkinId(PlayerSkinSlot.Boots, equipped.boots),
                ResolveEquippedClothingSkinId(PlayerSkinSlot.Gloves, equipped.gloves),
                ResolveEquippedAttachmentSkinId(PlayerSkinSlot.Face, equipped.face),
                ResolveEquippedAttachmentSkinId(PlayerSkinSlot.Hair, equipped.hair),
                ResolveEquippedWeaponSkinId(PlayerSkinSlot.WeaponAssaultRifle, equipped.weaponAssault),
                ResolveEquippedWeaponSkinId(PlayerSkinSlot.WeaponSniperRifle, equipped.weaponSniper),
                ResolveEquippedWeaponSkinId(PlayerSkinSlot.WeaponPistol, equipped.weaponPistol),
                ResolveEquippedWeaponSkinId(PlayerSkinSlot.WeaponMp7, equipped.weaponMp7));
        }

        private static PlayerSkinNetworkState BuildResolvedNetworkState()
        {
            return new PlayerSkinNetworkState(
                ResolveEquippedSkinId(PlayerSkinSlot.Shirt),
                ResolveEquippedSkinId(PlayerSkinSlot.Pants),
                ResolveEquippedSkinId(PlayerSkinSlot.Boots),
                ResolveEquippedSkinId(PlayerSkinSlot.Gloves),
                ResolveEquippedSkinId(PlayerSkinSlot.Face),
                ResolveEquippedSkinId(PlayerSkinSlot.Hair),
                ResolveEquippedSkinId(PlayerSkinSlot.WeaponAssaultRifle),
                ResolveEquippedSkinId(PlayerSkinSlot.WeaponSniperRifle),
                ResolveEquippedSkinId(PlayerSkinSlot.WeaponPistol),
                ResolveEquippedSkinId(PlayerSkinSlot.WeaponMp7));
        }

        public static void ApplyNetworkStateToPlayer(
            GameObject playerRoot,
            in PlayerSkinNetworkState state,
            bool forceReapply = true)
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

            ApplyTo(clothingApplier, state);

            var attachmentApplier = playerRoot.GetComponent<PlayerAttachmentApplier>();
            if (attachmentApplier == null)
            {
                attachmentApplier = playerRoot.AddComponent<PlayerAttachmentApplier>();
            }

            attachmentApplier.ApplyFromNetwork(state, forceReapply);

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

        public static void ApplyTo(RemoteResourceClothingApplier applier, in PlayerSkinNetworkState state)
        {
            if (applier == null)
            {
                return;
            }

            ResolveSkinPathsFromId(state.ShirtId, out var shirtMesh, out var shirtMaterial);
            ResolveSkinPathsFromId(state.PantsId, out var pantsMesh, out var pantsMaterial);
            ResolveSkinPathsFromId(state.BootsId, out var bootsMesh, out var bootsMaterial);
            ResolveSkinPathsFromId(state.GlovesId, out var glovesMesh, out var glovesMaterial);

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

            ApplyNetworkStateToPlayer(playerRoot, CaptureLocalNetworkState(), forceReapply);
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
                if (WeaponSkinResourcePaths.IsWeaponSkinSlot(slot))
                {
                    if (WeaponSkinResourcePaths.TryGetWeaponKind(slot, out var weaponKind))
                    {
                        catalog[slot] = DiscoverWeaponSkinsForKind(weaponKind);
                    }
                    else
                    {
                        catalog[slot] = new List<PlayerSkinDefinition>(0);
                    }

                    continue;
                }

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

        private static List<PlayerSkinDefinition> DiscoverWeaponSkinsForKind(WeaponKind kind)
        {
            var results = new List<PlayerSkinDefinition>(8);
            var displayBase = GetWeaponDisplayName(kind);

            for (var i = 0; i <= MaxVariantProbeCount; i++)
            {
                var variantId = i.ToString("000");
                var picturePath = WeaponSkinResourcePaths.BuildPicturePath(kind, variantId);
                if (!HasResourcePicture(picturePath))
                {
                    if (results.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                var materialPath = string.Empty;
                if (i > 0)
                {
                    var candidateMaterialPath = WeaponSkinResourcePaths.BuildMaterialPath(kind, variantId);
                    if (Resources.Load<Material>(candidateMaterialPath) != null)
                    {
                        materialPath = candidateMaterialPath;
                    }
                }

                var skinId = WeaponSkinResourcePaths.BuildSkinId(kind, variantId);
                var displayName = i == 0
                    ? $"{displayBase} (стандарт)"
                    : $"{displayBase} {variantId}";
                results.Add(new PlayerSkinDefinition(
                    skinId,
                    displayName,
                    string.Empty,
                    materialPath,
                    picturePath,
                    kind));
            }

            return results;
        }

        private static bool HasResourcePicture(string picturePath)
        {
            return Resources.Load<Sprite>(picturePath) != null ||
                   Resources.Load<Texture2D>(picturePath) != null;
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
                case PlayerSkinSlot.WeaponAssaultRifle:
                    return "AK-47";
                case PlayerSkinSlot.WeaponSniperRifle:
                    return "Снайперка";
                case PlayerSkinSlot.WeaponPistol:
                    return "Пистолет";
                case PlayerSkinSlot.WeaponMp7:
                    return "MP7";
                default:
                    return "Скин";
            }
        }

        private static string GetWeaponDisplayName(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.AssaultRifle:
                    return "AK-47";
                case WeaponKind.SniperRifle:
                    return "Снайперка";
                case WeaponKind.Pistol:
                    return "Пистолет";
                case WeaponKind.Mp7:
                    return "MP7";
                default:
                    return "Оружие";
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

        private static void ResolveSkinPathsFromId(string skinId, out string meshPath, out string materialPath)
        {
            meshPath = string.Empty;
            materialPath = string.Empty;
            if (string.IsNullOrWhiteSpace(skinId))
            {
                return;
            }

            if (TryGetDefinitionById(skinId, out var definition) && definition.IsValid)
            {
                meshPath = definition.PrefabResourcePath;
                materialPath = definition.MaterialResourcePath;
            }
        }

        private static string GetAppliedSkinId(PlayerSkinSlot slot)
        {
            return ResolveEquippedSkinId(slot);
        }

        private static string ReadRawEquippedId(PlayerSkinSlot slot)
        {
            if (PlayerProfileService.IsServerSynced &&
                PlayerProfileService.CurrentProfile?.equipped != null)
            {
                var fromProfile = ReadEquippedIdFromProfile(
                    slot,
                    PlayerProfileService.CurrentProfile.equipped);
                if (!string.IsNullOrWhiteSpace(fromProfile))
                {
                    return fromProfile;
                }
            }

            return PlayerPrefs.GetString(BuildPrefKey(slot), string.Empty);
        }

        private static string ReadEquippedIdFromProfile(
            PlayerSkinSlot slot,
            PlayerProfileEquippedDto equipped)
        {
            if (equipped == null)
            {
                return string.Empty;
            }

            switch (slot)
            {
                case PlayerSkinSlot.Shirt:
                    return equipped.shirt;
                case PlayerSkinSlot.Pants:
                    return equipped.pants;
                case PlayerSkinSlot.Boots:
                    return equipped.boots;
                case PlayerSkinSlot.Gloves:
                    return equipped.gloves;
                case PlayerSkinSlot.Face:
                    return equipped.face;
                case PlayerSkinSlot.Hair:
                    return equipped.hair;
                case PlayerSkinSlot.WeaponAssaultRifle:
                    return equipped.weaponAssault;
                case PlayerSkinSlot.WeaponSniperRifle:
                    return equipped.weaponSniper;
                case PlayerSkinSlot.WeaponPistol:
                    return equipped.weaponPistol;
                case PlayerSkinSlot.WeaponMp7:
                    return equipped.weaponMp7;
                default:
                    return string.Empty;
            }
        }

        private static string ResolveEquippedClothingSkinId(PlayerSkinSlot slot)
        {
            return ResolveEquippedClothingSkinId(slot, ReadRawEquippedId(slot));
        }

        private static string ResolveEquippedClothingSkinId(PlayerSkinSlot slot, string rawId)
        {
            var normalized = NormalizeSavedSkinId(rawId);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                TryGetDefinitionById(normalized, out _))
            {
                return normalized;
            }

            if (PlayerSkinOwnershipService.TryGetDefaultEquipped(slot, out var definition) &&
                definition.IsValid)
            {
                return definition.Id;
            }

            return string.Empty;
        }

        private static string ResolveEquippedAttachmentSkinId(PlayerSkinSlot slot)
        {
            return ResolveEquippedAttachmentSkinId(slot, ReadRawEquippedId(slot));
        }

        private static string ResolveEquippedAttachmentSkinId(PlayerSkinSlot slot, string rawId)
        {
            var normalized = NormalizeSavedSkinId(rawId);
            if (string.IsNullOrWhiteSpace(normalized) ||
                string.Equals(normalized, LegacyUnequippedSkinId, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return TryGetDefinitionById(normalized, out _) ? normalized : string.Empty;
        }

        private static string ResolveEquippedWeaponSkinId(PlayerSkinSlot slot)
        {
            return ResolveEquippedWeaponSkinId(slot, ReadRawEquippedId(slot));
        }

        private static string ResolveEquippedWeaponSkinId(PlayerSkinSlot slot, string rawId)
        {
            var normalized = NormalizeSavedSkinId(rawId);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                TryGetDefinitionById(normalized, out _))
            {
                return normalized;
            }

            if (WeaponSkinResourcePaths.TryGetWeaponKind(slot, out var kind))
            {
                return WeaponSkinResourcePaths.BuildSkinId(kind, "000");
            }

            return string.Empty;
        }

        private static bool TryResolveDefaultAppliedSkin(
            PlayerSkinSlot slot,
            IReadOnlyList<PlayerSkinDefinition> options,
            out PlayerSkinDefinition definition)
        {
            if (WeaponSkinResourcePaths.IsWeaponSkinSlot(slot) &&
                TryGetDefaultWeaponSkinDefinition(slot, out definition))
            {
                return true;
            }

            if (PlayerSkinOwnershipService.TryGetDefaultEquipped(slot, out definition))
            {
                return true;
            }

            if (options.Count > 0)
            {
                definition = options[0];
                return true;
            }

            definition = default;
            return false;
        }

        private static bool TryGetDefaultWeaponSkinDefinition(
            PlayerSkinSlot slot,
            out PlayerSkinDefinition definition)
        {
            if (WeaponSkinResourcePaths.TryGetWeaponKind(slot, out var kind))
            {
                return TryGetDefinitionById(
                    WeaponSkinResourcePaths.BuildSkinId(kind, "000"),
                    out definition);
            }

            definition = default;
            return false;
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
