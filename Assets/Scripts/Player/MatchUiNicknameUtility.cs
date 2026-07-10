using System;
using ShooterPrototype.Matchmaking;

namespace ShooterPrototype.Player
{
    public static class MatchUiNicknameUtility
    {
        public const string OfflineLocalTicketId = "offline-local";

        public static string FormatKillFeedParticipant(string ticketId, string nickname)
        {
            if (IsOfflineLocalTicket(ticketId))
            {
                return FormatLocalKillFeedPlain();
            }

            return string.IsNullOrWhiteSpace(nickname) ? "Игрок" : nickname.Trim();
        }

        public static string FormatLocalKillFeedPlain()
        {
            var allowRankPrefix = ActiveMatchContext.IsDuel || ActiveMatchContext.IsOfflineDuelSession;
            return allowRankPrefix
                ? PlayerProfileService.LocalDuelDisplayNickname
                : PlayerProfileService.LocalDisplayNickname;
        }

        public static string FormatLocalScoreboardRich(bool preferYouLabelWhenNoNick)
        {
            var allowRankPrefix = ActiveMatchContext.IsDuel || ActiveMatchContext.IsOfflineDuelSession;
            var prefix = NicknamePrefixUtility.GetContextualPrefix(
                PlayerProfileService.NicknamePrefix,
                allowRankPrefix);
            var nick = PlayerProfileService.Nickname?.Trim();
            if (!string.IsNullOrEmpty(prefix))
            {
                var label = preferYouLabelWhenNoNick && string.IsNullOrWhiteSpace(nick)
                    ? "Вы"
                    : nick;
                return NicknamePrefixUtility.FormatRich(prefix, label);
            }

            if (!string.IsNullOrWhiteSpace(nick))
            {
                return nick;
            }

            return preferYouLabelWhenNoNick ? "Вы" : PlayerProfileService.LocalDisplayNickname;
        }

        public static string FormatScoreboardRich(string ticketId, string localTicketId, string nickname)
        {
            if (!string.IsNullOrWhiteSpace(ticketId) &&
                !string.IsNullOrWhiteSpace(localTicketId) &&
                string.Equals(ticketId, localTicketId, StringComparison.Ordinal))
            {
                var preferYou = string.Equals(localTicketId, OfflineLocalTicketId, StringComparison.Ordinal);
                return FormatLocalScoreboardRich(preferYou);
            }

            var allowRankPrefix = ActiveMatchContext.IsDuel || ActiveMatchContext.IsOfflineDuelSession;
            if (!allowRankPrefix && NicknamePrefixUtility.TrySplitFormattedPlain(nickname, out var prefix, out var nick))
            {
                var contextualPrefix = NicknamePrefixUtility.GetContextualPrefix(prefix, false);
                if (!string.IsNullOrEmpty(contextualPrefix))
                {
                    return NicknamePrefixUtility.FormatRich(contextualPrefix, string.IsNullOrWhiteSpace(nick) ? "Игрок" : nick);
                }

                return string.IsNullOrWhiteSpace(nick) ? "Игрок" : nick.Trim();
            }

            return NicknamePrefixUtility.FormatRichFromFormattedPlain(nickname);
        }

        private static bool IsOfflineLocalTicket(string ticketId)
        {
            return !string.IsNullOrWhiteSpace(ticketId) &&
                   string.Equals(ticketId, OfflineLocalTicketId, StringComparison.Ordinal);
        }
    }
}
