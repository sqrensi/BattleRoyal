using System;

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
            return PlayerProfileService.LocalDisplayNickname;
        }

        public static string FormatLocalScoreboardRich(bool preferYouLabelWhenNoNick)
        {
            var prefix = PlayerProfileService.NicknamePrefix;
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

            return NicknamePrefixUtility.FormatRichFromFormattedPlain(nickname);
        }

        private static bool IsOfflineLocalTicket(string ticketId)
        {
            return !string.IsNullOrWhiteSpace(ticketId) &&
                   string.Equals(ticketId, OfflineLocalTicketId, StringComparison.Ordinal);
        }
    }
}
