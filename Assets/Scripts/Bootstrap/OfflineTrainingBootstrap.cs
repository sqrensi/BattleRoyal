using System.Collections;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Player;
using UnityEngine;

namespace ShooterPrototype.Bootstrap
{
    /// <summary>
    /// Starts a local training session when the main menu loads Game without queue/server.
    /// </summary>
    public static class OfflineTrainingBootstrap
    {
        public static IEnumerator StartWhenPlayerReady(MonoBehaviour host, float timeoutSeconds = 6f)
        {
            if (!ActiveMatchContext.IsTraining)
            {
                yield break;
            }

            var deadline = Time.unscaledTime + Mathf.Max(1f, timeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                var marker = Object.FindFirstObjectByType<LocalPlayerMarker>();
                if (marker != null)
                {
                    break;
                }

                yield return null;
            }

            var controller = Object.FindFirstObjectByType<MatchTrainingController>();
            if (controller != null)
            {
                yield return controller.StartTrainingSessionRoutine();
            }
        }
    }
}
