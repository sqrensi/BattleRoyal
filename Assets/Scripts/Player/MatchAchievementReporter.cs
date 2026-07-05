using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.UI;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class MatchAchievementReporter
    {
        private const float PendingHeadshotWindowSeconds = 5f;

        private static readonly HashSet<string> ShownToastAchievementIds =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        private static string pendingHeadshotVictimTicket = string.Empty;
        private static float pendingHeadshotUntilUnscaledTime;
        private static int consecutiveKillStreak;

        public static void RegisterPendingHeadshot(string victimTicketId)
        {
            if (IsTrainingSession())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(victimTicketId))
            {
                return;
            }

            pendingHeadshotVictimTicket = victimTicketId.Trim();
            pendingHeadshotUntilUnscaledTime = Time.unscaledTime + PendingHeadshotWindowSeconds;
        }

        public static void ReportPlayerKill(MonoBehaviour runner, string victimTicketId = null)
        {
            if (IsTrainingSession())
            {
                return;
            }

            var normalizedVictim = string.IsNullOrWhiteSpace(victimTicketId)
                ? string.Empty
                : victimTicketId.Trim();
            var isHeadshot = IsPendingHeadshotForVictim(normalizedVictim);
            if (isHeadshot)
            {
                pendingHeadshotVictimTicket = string.Empty;
            }

            ReportEvent(runner, AchievementEventTypes.KillPlayer, 1);
            if (isHeadshot)
            {
                ReportEvent(runner, AchievementEventTypes.HeadshotKill, 1);
            }

            consecutiveKillStreak++;
            ReportEvent(runner, AchievementEventTypes.KillStreak, 1);
        }

        public static void ReportPlayerKills(MonoBehaviour runner, int amount, string victimTicketId = null)
        {
            var normalizedAmount = Mathf.Max(0, amount);
            for (var i = 0; i < normalizedAmount; i++)
            {
                ReportPlayerKill(runner, victimTicketId);
            }
        }

        public static void ResetKillStreak(MonoBehaviour runner)
        {
            if (IsTrainingSession())
            {
                return;
            }

            consecutiveKillStreak = 0;
            pendingHeadshotVictimTicket = string.Empty;
            ReportEvent(runner, AchievementEventTypes.KillStreak, 0);
        }

        public static void ReportMatchEnd(MonoBehaviour runner, bool won)
        {
            if (IsTrainingSession() || !ShouldReportMatchEndOnClient())
            {
                return;
            }

            ReportEvent(runner, AchievementEventTypes.MatchCompleted, 1);
            if (!won)
            {
                return;
            }

            if (ActiveMatchContext.IsDuel)
            {
                ReportEvent(runner, AchievementEventTypes.DuelMatchWin, 1);
            }
            else if (ActiveMatchContext.IsDeathmatch)
            {
                ReportEvent(runner, AchievementEventTypes.DmMatchWin, 1);
            }
        }

        public static void ReportEvent(MonoBehaviour runner, string eventType, int amount = 1)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return;
            }

            if (ShouldUseOfflineAchievementTracking())
            {
                if (amount <= 0)
                {
                    PlayerProfileService.ResetOfflineAchievementEventProgress(eventType);
                }
                else
                {
                    PlayerProfileService.ReportOfflineAchievementEvent(eventType, amount);
                }

                return;
            }

            if (runner == null)
            {
                return;
            }

            runner.StartCoroutine(ReportEventRoutine(runner, eventType, amount));
        }

        private static bool IsPendingHeadshotForVictim(string victimTicketId)
        {
            if (string.IsNullOrWhiteSpace(victimTicketId) ||
                string.IsNullOrWhiteSpace(pendingHeadshotVictimTicket))
            {
                return false;
            }

            if (Time.unscaledTime > pendingHeadshotUntilUnscaledTime)
            {
                pendingHeadshotVictimTicket = string.Empty;
                return false;
            }

            return string.Equals(victimTicketId, pendingHeadshotVictimTicket, System.StringComparison.Ordinal);
        }

        private static bool ShouldUseOfflineAchievementTracking()
        {
            return !PlayerProfileService.IsServerSynced;
        }

        private static bool ShouldReportMatchEndOnClient()
        {
            if (!PlayerProfileService.IsServerSynced)
            {
                return true;
            }

            return ActiveMatchContext.IsOfflineDuelSession ||
                   ActiveMatchContext.IsOfflineDeathmatchSession ||
                   ActiveMatchContext.IsOfflineChallengeSession;
        }

        private static bool IsTrainingSession()
        {
            return ActiveMatchContext.IsTraining ||
                   ActiveMatchContext.IsOfflineTrainingSession ||
                   (MatchTrainingController.Active != null && MatchTrainingController.Active.IsSessionActive);
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
