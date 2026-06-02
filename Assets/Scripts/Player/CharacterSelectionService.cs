using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class CharacterSelectionService
    {
        private const string SelectedCharacterModelKey = "selected_character_model_name";

        public readonly struct CharacterModelOption
        {
            public CharacterModelOption(GameObject modelAsset)
            {
                ModelAsset = modelAsset;
            }

            public GameObject ModelAsset { get; }
            public string DisplayName => ModelAsset != null ? ModelAsset.name : "Default";
        }

        public static List<CharacterModelOption> LoadModelOptions(string resourcesFolder)
        {
            var options = new List<CharacterModelOption>();
            var normalizedFolder = NormalizeFolder(resourcesFolder);
            var models = Resources.LoadAll<GameObject>(normalizedFolder);
            for (var i = 0; i < models.Length; i++)
            {
                if (models[i] == null)
                {
                    continue;
                }

                options.Add(new CharacterModelOption(models[i]));
            }

            options.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));
            return options;
        }

        public static CharacterModelOption ResolveSelectedModel(string resourcesFolder)
        {
            var options = LoadModelOptions(resourcesFolder);
            if (options.Count == 0)
            {
                return default;
            }

            var selectedName = PlayerPrefs.GetString(SelectedCharacterModelKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(selectedName))
            {
                for (var i = 0; i < options.Count; i++)
                {
                    if (string.Equals(options[i].DisplayName, selectedName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return options[i];
                    }
                }
            }

            SaveSelectedModel(options[0].DisplayName);
            return options[0];
        }

        public static string GetSelectedModelName(string resourcesFolder)
        {
            var selected = ResolveSelectedModel(resourcesFolder);
            return selected.ModelAsset != null ? selected.DisplayName : string.Empty;
        }

        public static GameObject ResolveFallbackModel(string resourcesFolder)
        {
            var selected = ResolveSelectedModel(resourcesFolder);
            if (selected.ModelAsset != null)
            {
                return selected.ModelAsset;
            }

            var options = LoadModelOptions(resourcesFolder);
            return options.Count > 0 ? options[0].ModelAsset : null;
        }

        public static CharacterModelOption SelectNextModel(string resourcesFolder)
        {
            var options = LoadModelOptions(resourcesFolder);
            if (options.Count == 0)
            {
                return default;
            }

            var selectedName = PlayerPrefs.GetString(SelectedCharacterModelKey, string.Empty);
            var selectedIndex = -1;
            for (var i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].DisplayName, selectedName, System.StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }

            var nextIndex = (selectedIndex + 1 + options.Count) % options.Count;
            var next = options[nextIndex];
            SaveSelectedModel(next.DisplayName);
            return next;
        }

        public static void SaveSelectedModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                return;
            }

            PlayerPrefs.SetString(SelectedCharacterModelKey, modelName);
            PlayerPrefs.Save();
        }

        public static GameObject FindModelByName(string resourcesFolder, string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                return null;
            }

            var options = LoadModelOptions(resourcesFolder);
            for (var i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].DisplayName, modelName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return options[i].ModelAsset;
                }
            }

            return null;
        }

        private static string NormalizeFolder(string resourcesFolder)
        {
            if (string.IsNullOrWhiteSpace(resourcesFolder))
            {
                return "Characters";
            }

            return resourcesFolder.Trim().Trim('/').Trim('\\');
        }
    }
}
