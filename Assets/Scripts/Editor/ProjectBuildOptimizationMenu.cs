#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class ProjectBuildOptimizationMenu
    {
        private const string WebGlBuildTarget = "WebGL";

        [MenuItem("Shooter Prototype/Build Optimization/Compress All Textures For WebGL")]
        public static void CompressAllTexturesForWebGl()
        {
            var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });
            var changed = 0;

            for (var i = 0; i < textureGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
                if (ShouldSkipTexturePath(path))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                var maxSize = ResolveMaxTextureSize(path);
                if (!TryApplyWebGlCompression(importer, maxSize))
                {
                    Debug.LogWarning($"[ProjectBuildOptimization] Skipped incompatible texture: {path}");
                    continue;
                }

                importer.SaveAndReimport();
                changed++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ProjectBuildOptimization] Reimported {changed} textures with WebGL compression settings.");
        }

        public static void BatchCompressAllForWebGl()
        {
            CompressAllTexturesForWebGl();
            CompressMaterialsForSkinsOnly();
            EditorApplication.Exit(0);
        }

        [MenuItem("Shooter Prototype/Build Optimization/Compress MaterialsForSkins (512px WebGL)")]
        public static void CompressMaterialsForSkinsOnly()
        {
            var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/MaterialsForSkins" });
            for (var i = 0; i < textureGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                if (!TryApplyWebGlCompression(importer, 512))
                {
                    continue;
                }

                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectBuildOptimization] MaterialsForSkins compressed for WebGL at 512px.");
        }

        private static bool ShouldSkipTexturePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            return path.Contains("TextMesh Pro/Examples") ||
                   path.Contains("PluginYourGames/Modules") && path.Contains("/Editor/Icons/");
        }

        private static int ResolveMaxTextureSize(string path)
        {
            var normalized = path.Replace('\\', '/');

            if (normalized.Contains("/Resources/MaterialsForSkins/") ||
                normalized.Contains("/Resources/Skins/") && normalized.EndsWith("/picture.png"))
            {
                return 512;
            }

            if (normalized.Contains("/Pictures/") ||
                normalized.Contains("/UI/") ||
                normalized.Contains("Inventory") && normalized.EndsWith(".png"))
            {
                return 512;
            }

            if (normalized.Contains("/Opsive/") ||
                normalized.Contains("/Synty/") ||
                normalized.Contains("/Characters/") ||
                normalized.Contains("/Resources/Characters/"))
            {
                return 1024;
            }

            return 1024;
        }

        private static bool TryApplyWebGlCompression(TextureImporter importer, int maxSize)
        {
            if (importer == null)
            {
                return false;
            }

            importer.maxTextureSize = Mathf.Min(importer.maxTextureSize, maxSize);
            var useDxt5 = CanUseDxt5(importer);

            if (!ApplyPlatformCompression(importer, WebGlBuildTarget, maxSize, useDxt5))
            {
                return false;
            }

            return ApplyPlatformCompression(importer, "Standalone", maxSize, useDxt5);
        }

        private static bool CanUseDxt5(TextureImporter importer)
        {
            switch (importer.textureType)
            {
                case TextureImporterType.SingleChannel:
                case TextureImporterType.NormalMap:
                case TextureImporterType.Cookie:
                case TextureImporterType.Lightmap:
                case TextureImporterType.DirectionalLightmap:
                case TextureImporterType.Shadowmask:
                    return false;
                default:
                    return true;
            }
        }

        private static bool ApplyPlatformCompression(
            TextureImporter importer,
            string buildTarget,
            int maxSize,
            bool useDxt5)
        {
            var platform = importer.GetPlatformTextureSettings(buildTarget);
            platform.overridden = true;
            platform.maxTextureSize = maxSize;
            platform.textureCompression = TextureImporterCompression.Compressed;
            platform.crunchedCompression = true;
            platform.compressionQuality = 50;

            if (useDxt5)
            {
                platform.format = TextureImporterFormat.DXT5;
            }
            else
            {
                platform.format = TextureImporterFormat.Automatic;
            }

            try
            {
                importer.SetPlatformTextureSettings(platform);
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    $"[ProjectBuildOptimization] Failed to set {buildTarget} settings for " +
                    $"{importer.assetPath}: {exception.Message}");
                return false;
            }
        }
    }
}
#endif
