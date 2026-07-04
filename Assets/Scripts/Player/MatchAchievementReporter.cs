using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.UI;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class MatchAchievementReporter
    {
        private static readonly HashSet<string> ShownToastAchievementIds =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public static void ReportEvent(MonoBehaviour runner, string eventType, int amount = 1)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return;
            }

            if (ShouldUseOfflineAchievementTracking())
            {
                PlayerProfileService.ReportOfflineAchievementEvent(eventType, amount);
                return;
            }

            if (runner == null)
            {
                return;
            }

            runner.StartCoroutine(ReportEventRoutine(runner, eventType, amount));
        }

        private static bool ShouldUseOfflineAchievementTracking()
        {
            if (!PlayerProfileService.IsServerSynced)
            {
                return true;
            }

            return ActiveMatchContext.IsOfflineDuelSession ||
                   ActiveMatchContext.IsOfflineDeathmatchSession ||
                   ActiveMatchContext.IsOfflineTrainingSession ||
                   ActiveMatchContext.IsOfflineChallengeSession;
        }

        private static IEnumerator ReportEventRoutine(MonoBehaviour runner, string eventType, int amount)
        {
            var apiClient = Object.FindFirstObjectByType<PlayerProfileApiClient>();
            var playerId = PlayerIdentityService.GetOrCreatePlayerId();
            if (apiClient == null || string.IsNullOrWhiteSpace(playerId))
            {
                yield break;
            }

            var success = false;
            AchievementCompletedEntry[] newlyCompleted = null;
            yield return PlayerProfileService.ReportAchievementEvent(
                runner,
                apiClient,
                playerId,
                eventType,
                amount,
                (ok, completed, _) =>
                {
                    success = ok;
                    newlyCompleted = completed;
                });

            if (!success || newlyCompleted == null || newlyCompleted.Length == 0)
            {
                yield break;
            }

            for (var i = 0; i < newlyCompleted.Length; i++)
            {
                var entry = newlyCompleted[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.achievementId))
                {
                    continue;
                }

                if (!ShownToastAchievementIds.Add(entry.achievementId))
                {
                    continue;
                }

                AchievementToastController.Show(entry.title, entry.description);
            }
        }
    }
}
