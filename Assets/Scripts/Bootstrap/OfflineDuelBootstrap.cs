using System.Collections;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Player;
using UnityEngine;

namespace ShooterPrototype.Bootstrap
{
    public static class OfflineDuelBootstrap
    {
        public static IEnumerator StartWhenPlayerReady(MonoBehaviour host, float timeoutSeconds = 6f)
        {
            if (!ActiveMatchContext.IsOfflineDuelSession)
            {
                yield break;
            }

            var deadline = Time.unscaledTime + Mathf.Max(1f, timeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                if (Object.FindFirstObjectByType<LocalPlayerMarker>() != null)
                {
                    break;
                }

                yield return null;
            }

            var controller = Object.FindFirstObjectByType<MatchOfflineDuelController>();
            if (controller != null)
            {
                yield return controller.StartSessionRoutine();
            }
        }
    }
}
