#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShooterPrototype.EditorTools
{
    public static class UiPrefabBuilder
    {
        private const string OutputDirectory = "Assets/Resources/UI";

        [MenuItem("Tools/ShooterPrototype/Build UI Prefabs")]
        public static void BuildUiPrefabs()
        {
            Directory.CreateDirectory(OutputDirectory);

            SavePrefab("NavTab", BuildNavTabTemplate());
            SavePrefab("ItemSlot", BuildItemSlotTemplate());
            SavePrefab("PriceBadge", BuildPriceBadgeTemplate());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UiPrefabBuilder] UI prefabs exported to " + OutputDirectory);
        }

        private static GameObject BuildNavTabTemplate()
        {
            var host = new GameObject("NavTabHost");
            var tab = ShooterPrototype.UI.UiPrefabLibrary.BuildNavTab(host.transform, "TAB", selected: false);
            var root = tab.Root;
            root.transform.SetParent(null, false);
            Object.DestroyImmediate(host);
            return root;
        }

        private static GameObject BuildItemSlotTemplate()
        {
            var host = new GameObject("ItemSlotHost");
            var slot = ShooterPrototype.UI.UiPrefabLibrary.BuildItemSlot(host.transform, 160f, 160f);
            var root = slot.Root;
            root.transform.SetParent(null, false);
            Object.DestroyImmediate(host);
            return root;
        }

        private static GameObject BuildPriceBadgeTemplate()
        {
            var host = new GameObject("PriceBadgeHost");
            var badge = ShooterPrototype.UI.UiPrefabLibrary.BuildPriceBadge(host.transform, 96f, 32f);
            var root = badge.Root;
            root.transform.SetParent(null, false);
            Object.DestroyImmediate(host);
            return root;
        }

        private static void SavePrefab(string name, GameObject root)
        {
            var path = Path.Combine(OutputDirectory, name + ".prefab").Replace('\\', '/');
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }
    }
}
#endif
