#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShooterPrototype.EditorTools
{
    public static class ClothingIconGenerator
    {
        private const string OutputFolder = "Assets/Resources/Pictures";
        private const string VersionFilePath = "Assets/Resources/Pictures/clothes_icons.version";
        private const string ExpectedVersion = "4";
        private const int IconSize = 512;
        private const float CaptureWorldOffset = -250f;

        private static readonly Color BackgroundColor = new Color(0.12f, 0.14f, 0.17f, 1f);

        private struct CaptureEntry
        {
            public string MeshResourcePath;
            public string MaterialResourcePath;
            public string OutputFileName;
            public Vector3 RotationEuler;
            public float OrthoPadding;
        }

        private static readonly CaptureEntry[] Entries =
        {
            new CaptureEntry
            {
                MeshResourcePath = "4",
                MaterialResourcePath = "gloves",
                OutputFileName = "clothes_gloves.png",
                RotationEuler = new Vector3(-18f, 165f, 0f),
                OrthoPadding = 1.16f
            },
            new CaptureEntry
            {
                MeshResourcePath = "2",
                MaterialResourcePath = "pants",
                OutputFileName = "clothes_pants.png",
                RotationEuler = new Vector3(-12f, 180f, 0f),
                OrthoPadding = 1.12f
            },
            new CaptureEntry
            {
                MeshResourcePath = "1",
                MaterialResourcePath = "tshirt",
                OutputFileName = "clothes_tshirt.png",
                RotationEuler = new Vector3(-10f, 180f, 0f),
                OrthoPadding = 1.12f
            },
            new CaptureEntry
            {
                MeshResourcePath = "3",
                MaterialResourcePath = "feets",
                OutputFileName = "clothes_shoes.png",
                RotationEuler = new Vector3(18f, 210f, 0f),
                OrthoPadding = 1.18f
            }
        };

        [MenuItem("Shooter Prototype/UI/Generate Clothing Inventory Icons")]
        public static void GenerateFromMenu()
        {
            GenerateAll(force: true);
        }

        [InitializeOnLoad]
        private static class MissingIconBootstrap
        {
            static MissingIconBootstrap()
            {
                EditorApplication.delayCall += TryGenerateMissingIcons;
            }

            private static void TryGenerateMissingIcons()
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                if (ShouldRegenerate())
                {
                    GenerateAll(force: true);
                }
            }
        }

        public static void GenerateAllBatch()
        {
            GenerateAll(force: true);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        public static void GenerateAll(bool force = false)
        {
            if (!force && !ShouldRegenerate())
            {
                return;
            }

            Directory.CreateDirectory(OutputFolder);

            var captureRoot = new GameObject("ClothingIconCaptureRoot");
            captureRoot.hideFlags = HideFlags.HideAndDontSave;
            captureRoot.transform.position = new Vector3(0f, CaptureWorldOffset, 0f);

            var cameraObject = new GameObject("ClothingIconCamera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.transform.SetParent(captureRoot.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            ConfigureCamera(camera);

            var keyLightObject = new GameObject("KeyLight");
            keyLightObject.hideFlags = HideFlags.HideAndDontSave;
            keyLightObject.transform.SetParent(captureRoot.transform, false);
            var keyLight = keyLightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.05f;
            keyLight.transform.rotation = Quaternion.Euler(42f, -34f, 0f);

            var fillLightObject = new GameObject("FillLight");
            fillLightObject.hideFlags = HideFlags.HideAndDontSave;
            fillLightObject.transform.SetParent(captureRoot.transform, false);
            var fillLight = fillLightObject.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.45f;
            fillLight.transform.rotation = Quaternion.Euler(18f, 148f, 0f);

            var renderTexture = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = renderTexture;

            try
            {
                for (var i = 0; i < Entries.Length; i++)
                {
                    GenerateIcon(camera, captureRoot.transform, Entries[i]);
                }

                File.WriteAllText(VersionFilePath, ExpectedVersion);
            }
            finally
            {
                camera.targetTexture = null;
                renderTexture.Release();
                Object.DestroyImmediate(captureRoot);
            }

            ShooterPrototype.UI.InventoryIconCatalog.ClearCache();
            AssetDatabase.Refresh();
            Debug.Log("[ClothingIconGenerator] Generated clothing inventory icons in Resources/Pictures.");
        }

        private static bool ShouldRegenerate()
        {
            if (!File.Exists(VersionFilePath))
            {
                return true;
            }

            var version = File.ReadAllText(VersionFilePath).Trim();
            if (!string.Equals(version, ExpectedVersion, System.StringComparison.Ordinal))
            {
                return true;
            }

            for (var i = 0; i < Entries.Length; i++)
            {
                var outputPath = Path.Combine(OutputFolder, Entries[i].OutputFileName);
                if (!File.Exists(outputPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static void GenerateIcon(Camera camera, Transform captureRoot, CaptureEntry entry)
        {
            var meshPrefab = Resources.Load<GameObject>(entry.MeshResourcePath);
            if (meshPrefab == null)
            {
                Debug.LogError($"[ClothingIconGenerator] Mesh not found at Resources/{entry.MeshResourcePath}");
                return;
            }

            var material = Resources.Load<Material>(entry.MaterialResourcePath);
            if (material == null)
            {
                Debug.LogError($"[ClothingIconGenerator] Material not found at Resources/{entry.MaterialResourcePath}");
                return;
            }

            var instance = Object.Instantiate(meshPrefab, captureRoot);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(entry.RotationEuler);
            instance.transform.localScale = Vector3.one;

            var captureMeshRoot = BuildCaptureMeshRoot(instance, material);
            if (captureMeshRoot == null)
            {
                Debug.LogError($"[ClothingIconGenerator] Could not build capture mesh for {entry.OutputFileName}");
                Object.DestroyImmediate(instance);
                return;
            }

            captureMeshRoot.transform.SetParent(instance.transform, false);
            DisableOriginalRenderers(instance, captureMeshRoot.transform);

            var bounds = CalculateBounds(captureMeshRoot);
            if (bounds.size.sqrMagnitude <= 0.0001f)
            {
                Debug.LogError($"[ClothingIconGenerator] Empty bounds for {entry.OutputFileName}");
                Object.DestroyImmediate(instance);
                return;
            }

            FrameCamera(camera, bounds, entry.OrthoPadding);
            camera.Render();

            SaveRenderTexture(camera.targetTexture, Path.Combine(OutputFolder, entry.OutputFileName));
            Object.DestroyImmediate(instance);
        }

        private static void ConfigureCamera(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.orthographic = true;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.aspect = 1f;
            camera.enabled = true;

            var additionalData = camera.GetUniversalAdditionalCameraData();
            if (additionalData != null)
            {
                additionalData.renderType = CameraRenderType.Base;
            }
        }

        private static GameObject BuildCaptureMeshRoot(GameObject source, Material sourceMaterial)
        {
            var captureRoot = new GameObject("CaptureMeshRoot");
            captureRoot.hideFlags = HideFlags.HideAndDontSave;

            var skinnedRenderers = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var addedAny = false;

            for (var i = 0; i < skinnedRenderers.Length; i++)
            {
                var skinnedRenderer = skinnedRenderers[i];
                if (skinnedRenderer == null || skinnedRenderer.sharedMesh == null)
                {
                    continue;
                }

                var bakedMesh = new Mesh();
                skinnedRenderer.BakeMesh(bakedMesh, true);

                var meshObject = new GameObject(skinnedRenderer.name + "_Baked");
                meshObject.hideFlags = HideFlags.HideAndDontSave;
                meshObject.transform.SetParent(captureRoot.transform, false);
                meshObject.transform.localPosition = skinnedRenderer.transform.localPosition;
                meshObject.transform.localRotation = skinnedRenderer.transform.localRotation;
                meshObject.transform.localScale = skinnedRenderer.transform.localScale;

                var meshFilter = meshObject.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = bakedMesh;
                var meshRenderer = meshObject.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = sourceMaterial;
                addedAny = true;
            }

            if (addedAny)
            {
                return captureRoot;
            }

            var staticRenderers = source.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < staticRenderers.Length; i++)
            {
                var meshRenderer = staticRenderers[i];
                var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    continue;
                }

                var meshObject = Object.Instantiate(meshRenderer.gameObject, captureRoot.transform);
                meshObject.hideFlags = HideFlags.HideAndDontSave;
                var clonedRenderer = meshObject.GetComponent<MeshRenderer>();
                if (clonedRenderer != null)
                {
                    clonedRenderer.sharedMaterial = sourceMaterial;
                }

                addedAny = true;
            }

            return addedAny ? captureRoot : null;
        }

        private static void DisableOriginalRenderers(GameObject instance, Transform keepRoot)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (keepRoot != null && renderer.transform.IsChildOf(keepRoot))
                {
                    continue;
                }

                renderer.enabled = false;
            }
        }

        private static void FrameCamera(Camera camera, Bounds bounds, float padding)
        {
            var center = bounds.center;
            camera.transform.SetPositionAndRotation(
                center + new Vector3(0f, 0f, -8f),
                Quaternion.identity);
            camera.transform.LookAt(center, Vector3.up);
            camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z * 0.35f) * padding;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.zero);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static void SaveRenderTexture(RenderTexture source, string assetPath)
        {
            var previousActive = RenderTexture.active;
            var readable = RenderTexture.GetTemporary(IconSize, IconSize, 24, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, readable);
            RenderTexture.active = readable;

            var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, IconSize, IconSize), 0, 0);
            texture.Apply(false, false);

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(readable);

            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
#endif
