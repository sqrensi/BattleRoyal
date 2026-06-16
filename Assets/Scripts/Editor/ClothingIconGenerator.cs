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
        private const int IconSize = 512;
        private const float CaptureWorldOffset = -250f;

        private static readonly Color BackgroundColor = new Color(0.12f, 0.14f, 0.17f, 1f);

        private struct CaptureEntry
        {
            public string PrefabResourcePath;
            public string MaterialResourcePath;
            public string OutputAssetPath;
            public Vector3 RotationEuler;
            public float OrthoPadding;
        }

        private static readonly CaptureEntry[] Entries =
        {
            new CaptureEntry
            {
                PrefabResourcePath = "Skins/gloves/001/prefab",
                MaterialResourcePath = "Skins/gloves/001/material",
                OutputAssetPath = "Assets/Resources/Skins/gloves/001/picture.png",
                RotationEuler = new Vector3(-18f, 165f, 0f),
                OrthoPadding = 1.16f
            },
            new CaptureEntry
            {
                PrefabResourcePath = "Skins/pants/001/prefab",
                MaterialResourcePath = "Skins/pants/001/material",
                OutputAssetPath = "Assets/Resources/Skins/pants/001/picture.png",
                RotationEuler = new Vector3(-12f, 180f, 0f),
                OrthoPadding = 1.12f
            },
            new CaptureEntry
            {
                PrefabResourcePath = "Skins/tshirts/001/prefab",
                MaterialResourcePath = "Skins/tshirts/001/material",
                OutputAssetPath = "Assets/Resources/Skins/tshirts/001/picture.png",
                RotationEuler = new Vector3(-10f, 180f, 0f),
                OrthoPadding = 1.12f
            },
            new CaptureEntry
            {
                PrefabResourcePath = "Skins/shoes/001/prefab",
                MaterialResourcePath = "Skins/shoes/001/material",
                OutputAssetPath = "Assets/Resources/Skins/shoes/001/picture.png",
                RotationEuler = new Vector3(18f, 210f, 0f),
                OrthoPadding = 1.18f
            }
        };

        [MenuItem("Shooter Prototype/UI/Generate Clothing Inventory Icons")]
        public static void GenerateFromMenu()
        {
            GenerateAll();
        }

        public static void GenerateAllBatch()
        {
            GenerateAll();
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        public static void GenerateAll()
        {
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
            }
            finally
            {
                camera.targetTexture = null;
                renderTexture.Release();
                Object.DestroyImmediate(captureRoot);
            }

            ShooterPrototype.UI.InventoryIconCatalog.ClearCache();
            AssetDatabase.Refresh();
            Debug.Log("[ClothingIconGenerator] Generated clothing inventory pictures in Resources/Skins.");
        }

        private static void GenerateIcon(Camera camera, Transform captureRoot, CaptureEntry entry)
        {
            var meshPrefab = Resources.Load<GameObject>(entry.PrefabResourcePath);
            if (meshPrefab == null)
            {
                Debug.LogError($"[ClothingIconGenerator] Prefab not found at Resources/{entry.PrefabResourcePath}");
                return;
            }

            var material = Resources.Load<Material>(entry.MaterialResourcePath);
            if (material == null)
            {
                Debug.LogError($"[ClothingIconGenerator] Material not found at Resources/{entry.MaterialResourcePath}");
                return;
            }

            var outputDirectory = Path.GetDirectoryName(entry.OutputAssetPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var instance = Object.Instantiate(meshPrefab, captureRoot);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(entry.RotationEuler);
            instance.transform.localScale = Vector3.one;

            var captureMeshRoot = BuildCaptureMeshRoot(instance, material);
            if (captureMeshRoot == null)
            {
                Debug.LogError($"[ClothingIconGenerator] Could not build capture mesh for {entry.OutputAssetPath}");
                Object.DestroyImmediate(instance);
                return;
            }

            captureMeshRoot.transform.SetParent(instance.transform, false);
            DisableOriginalRenderers(instance, captureMeshRoot.transform);

            var bounds = CalculateBounds(captureMeshRoot);
            if (bounds.size.sqrMagnitude <= 0.0001f)
            {
                Debug.LogError($"[ClothingIconGenerator] Empty bounds for {entry.OutputAssetPath}");
                Object.DestroyImmediate(instance);
                return;
            }

            FrameCamera(camera, bounds, entry.OrthoPadding);
            camera.Render();

            SaveRenderTexture(camera.targetTexture, entry.OutputAssetPath);
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

            var staticRenderers = source.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < staticRenderers.Length; i++)
            {
                var meshRenderer = staticRenderers[i];
                if (meshRenderer == null)
                {
                    continue;
                }

                var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    continue;
                }

                var meshObject = Object.Instantiate(meshRenderer.gameObject, captureRoot.transform, true);
                meshObject.hideFlags = HideFlags.HideAndDontSave;
                var cloneRenderer = meshObject.GetComponent<MeshRenderer>();
                if (cloneRenderer != null)
                {
                    cloneRenderer.sharedMaterial = sourceMaterial;
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
                if (renderer == null || renderer.transform.IsChildOf(keepRoot))
                {
                    continue;
                }

                renderer.enabled = false;
            }
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            var bounds = new Bounds();
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        private static void FrameCamera(Camera camera, Bounds bounds, float padding)
        {
            var center = bounds.center;
            var extents = bounds.extents;
            var radius = Mathf.Max(extents.x, extents.y, extents.z) * padding;
            camera.transform.position = center + new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.orthographicSize = radius;
        }

        private static void SaveRenderTexture(RenderTexture renderTexture, string assetPath)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = renderTexture;

            var texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
            texture.Apply(false, false);

            RenderTexture.active = previous;

            var png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            File.WriteAllBytes(assetPath, png);
        }
    }
}
#endif
