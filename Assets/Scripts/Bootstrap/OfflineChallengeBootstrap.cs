using System.Collections;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Player;
using UnityEngine;

namespace ShooterPrototype.Bootstrap
{
    public static class OfflineChallengeBootstrap
    {
        public static IEnumerator StartWhenPlayerReady(MonoBehaviour host)
        {
            if (!ActiveMatchContext.IsChallenge)
            {
                yield break;
            }

            const float timeoutSeconds = 8f;
            var deadline = Time.unscaledTime + timeoutSeconds;
            while (Object.FindFirstObjectByType<LocalPlayerMarker>() == null && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            var controller = Object.FindFirstObjectByType<MatchChallengeController>();
            if (controller == null)
            {
                Debug.LogError("[OfflineChallengeBootstrap] MatchChallengeController not found.");
                yield break;
            }

            yield return controller.StartChallengeSessionRoutine();
        }
    }
}
