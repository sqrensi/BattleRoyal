using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class ShotTracerSpawner
    {
        private static readonly Stack<LineRenderer> Pool = new Stack<LineRenderer>(32);
        private static Transform poolRoot;
        private static Material sharedMaterial;
        private static int activeTracers;

        public static bool TrySpawn(
            MonoBehaviour host,
            Vector3 from,
            Vector3 to,
            float duration,
            float width,
            Color color,
            Material materialOverride = null)
        {
            if (host == null)
            {
                return false;
            }

            if (activeTracers >= GameplayPerformanceOptions.MaxActiveShotTracers)
            {
                return false;
            }

            host.StartCoroutine(PlayTracer(from, to, duration, width, color, materialOverride));
            return true;
        }

        private static IEnumerator PlayTracer(
            Vector3 from,
            Vector3 to,
            float duration,
            float width,
            Color color,
            Material materialOverride)
        {
            activeTracers++;
            try
            {
                var line = AcquireLineRenderer();
                ConfigureLine(line, width, color, materialOverride);
                line.SetPosition(0, from);
                line.SetPosition(1, to);

                yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, duration));

                ReleaseLineRenderer(line);
            }
            finally
            {
                activeTracers = Mathf.Max(0, activeTracers - 1);
            }
        }

        private static LineRenderer AcquireLineRenderer()
        {
            EnsurePoolRoot();
            while (Pool.Count > 0)
            {
                var pooled = Pool.Pop();
                if (pooled != null)
                {
                    pooled.gameObject.SetActive(true);
                    return pooled;
                }
            }

            var tracerObject = new GameObject("ShotTracer");
            tracerObject.transform.SetParent(poolRoot, false);
            return tracerObject.AddComponent<LineRenderer>();
        }

        private static void ReleaseLineRenderer(LineRenderer line)
        {
            if (line == null)
            {
                return;
            }

            line.gameObject.SetActive(false);
            Pool.Push(line);
        }

        private static void ConfigureLine(
            LineRenderer line,
            float width,
            Color color,
            Material materialOverride)
        {
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            line.sortingOrder = 40;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.sharedMaterial = materialOverride != null ? materialOverride : ResolveSharedMaterial(color);
        }

        private static Material ResolveSharedMaterial(Color color)
        {
            if (sharedMaterial != null)
            {
                if (sharedMaterial.HasProperty("_Color"))
                {
                    sharedMaterial.color = color;
                }

                return sharedMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                return null;
            }

            sharedMaterial = new Material(shader);
            if (sharedMaterial.HasProperty("_Color"))
            {
                sharedMaterial.color = color;
            }

            return sharedMaterial;
        }

        private static void EnsurePoolRoot()
        {
            if (poolRoot != null)
            {
                return;
            }

            var rootObject = new GameObject("ShotTracerPool");
            Object.DontDestroyOnLoad(rootObject);
            poolRoot = rootObject.transform;
        }

        /// <summary>
        /// Destroys pooled tracers. Call when leaving a match.
        /// </summary>
        public static void ResetSession()
        {
            activeTracers = 0;

            while (Pool.Count > 0)
            {
                var line = Pool.Pop();
                if (line != null)
                {
                    Object.Destroy(line.gameObject);
                }
            }

            if (poolRoot != null)
            {
                for (var i = poolRoot.childCount - 1; i >= 0; i--)
                {
                    var child = poolRoot.GetChild(i);
                    if (child != null)
                    {
                        Object.Destroy(child.gameObject);
                    }
                }
            }
        }
    }
}
