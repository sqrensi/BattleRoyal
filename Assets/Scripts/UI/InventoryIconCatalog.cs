using System.Collections.Generic;
using ShooterPrototype.Player;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShooterPrototype.UI
{
    public static class InventoryIconCatalog
    {
        private const string PicturesFolder = "Assets/Pictures/";
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

        public static Sprite GetWeaponIcon(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return LoadSprite("sniper.PNG");
                case WeaponKind.Pistol:
                    return LoadSprite("pistol.PNG");
                case WeaponKind.Mp7:
                    return LoadSprite("mp7.PNG");
                default:
                    return LoadSprite("ak-47.PNG");
            }
        }

        public static Sprite GetAmmoIcon(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.SniperRifle:
                    return LoadSprite("sniperAmmo.PNG");
                case WeaponKind.Pistol:
                    return LoadSprite("pistolAmmo.PNG");
                case WeaponKind.Mp7:
                    return LoadSprite("mp7Ammo.PNG");
                default:
                    return LoadSprite("ak-47Ammo.PNG");
            }
        }

        public static Sprite GetSkinIcon(string pictureResourcePath)
        {
            return LoadSpriteFromResources(pictureResourcePath);
        }

        public static void ClearCache()
        {
            SpriteCache.Clear();
        }

        public static Sprite GetPickupIcon(PickupKind kind, string itemId)
        {
            switch (kind)
            {
                case PickupKind.Weapon:
                    return GetWeaponIcon(WeaponCatalog.ResolveKindFromItemId(itemId));
                case PickupKind.Ammo:
                    if (AmmoCatalog.TryResolveKindFromItemId(itemId, out var ammoKind))
                    {
                        return GetAmmoIcon(ammoKind);
                    }

                    return GetAmmoIcon(WeaponKind.AssaultRifle);
                default:
                    return null;
            }
        }

        private static Sprite LoadSpriteFromResources(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                return null;
            }

            if (SpriteCache.TryGetValue(resourcePath, out var cached))
            {
                return cached;
            }

            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite != null)
            {
                SpriteCache[resourcePath] = sprite;
                return sprite;
            }

            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                return null;
            }

            sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            SpriteCache[resourcePath] = sprite;
            return sprite;
        }

        private static Sprite LoadSprite(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            if (SpriteCache.TryGetValue(fileName, out var cached))
            {
                return cached;
            }

            var texture = LoadTexture(fileName);
            if (texture == null)
            {
                return null;
            }

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            SpriteCache[fileName] = sprite;
            return sprite;
        }

        private static Texture2D LoadTexture(string fileName)
        {
            var resourceName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var fromResources = Resources.Load<Texture2D>("Pictures/" + resourceName);
            if (fromResources != null)
            {
                return fromResources;
            }

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<Texture2D>(PicturesFolder + fileName);
#else
            return null;
#endif
        }
    }
}
