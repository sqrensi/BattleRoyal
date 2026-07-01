using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShooterPrototype.Bootstrap
{
    [DefaultExecutionOrder(-90)]
    [DisallowMultipleComponent]
    public sealed class SceneOcclusionBootstrap : MonoBehaviour
    {
        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ApplyToAllCameras();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyToAllCameras();
        }

        private static void ApplyToAllCameras()
        {
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (camera == null)
                {
                    continue;
                }

                camera.useOcclusionCulling = true;
            }
        }
    }
}
