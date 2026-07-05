using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class NicknamePrefixUtility
    {
        public const string Legend = "LEGEND";
        public const string Pro = "PRO";
        public const string Vip = "VIP";
        public const string VipLegend = "VIP-LEGEND";
        public const string VipPro = "VIP-PRO";
        public const int ProTopCount = 5;

        public static string Resolve(string prefix, bool hasVipPrefix, int leaderboardRank)
        {
            var normalized = Normalize(prefix);
            if (!string.IsNullOrEmpty(normalized))
            {
                return normalized;
            }

            return CombinePrefixes(ResolveRankPrefix(leaderboardRank), hasVipPrefix);
        }

        public static string CombinePrefixes(string rankPrefix, bool hasVipPrefix)
        {
            var rank = Normalize(rankPrefix);
            var rankValue = rank == Legend || rank == Pro ? rank : string.Empty;
            if (hasVipPrefix && !string.IsNullOrEmpty(rankValue))
            {
                return $"{Vip}-{rankValue}";
            }

            if (hasVipPrefix)
            {
                return Vip;
            }

            return rankValue;
        }

        public static string ResolveRankPrefix(int leaderboardRank)
        {
            if (leaderboardRank == 1)
            {
                return Legend;
            }

            if (leaderboardRank >= 2 && leaderboardRank <= ProTopCount)
            {
                return Pro;
            }

            return string.Empty;
        }

        public static string Normalize(string prefix)
        {
            var value = (prefix ?? string.Empty).Trim().ToUpperInvariant();
            return value == Legend ||
                   value == Pro ||
                   value == Vip ||
                   value == VipLegend ||
                   value == VipPro
                ? value
                : string.Empty;
        }

        public static string FormatPlain(string prefix, string nickname)
        {
            var nick = string.IsNullOrWhiteSpace(nickname) ? "Игрок" : nickname.Trim();
            var normalized = Normalize(prefix);
            return string.IsNullOrEmpty(normalized) ? nick : $"[{normalized}] {nick}";
        }

        public static string FormatRich(string prefix, string nickname)
        {
            var nick = string.IsNullOrWhiteSpace(nickname) ? "Игрок" : nickname.Trim();
            var normalized = Normalize(prefix);
            if (string.IsNullOrEmpty(normalized))
            {
                return nick;
            }

            if (normalized == VipLegend || normalized == VipPro)
            {
                var rankPart = normalized == VipLegend ? Legend : Pro;
                var rankColor = rankPart == Legend ? "#FF7A45" : "#E8C04A";
                return $"<color=#B88CFF>[VIP</color><color={rankColor}>-{rankPart}</color><color=#B88CFF>]</color> {nick}";
            }

            var color = normalized switch
            {
                Legend => "#FF7A45",
                Pro => "#E8C04A",
                _ => "#B88CFF",
            };
            return $"<color={color}>[{normalized}]</color> {nick}";
        }

        public static Color GetPrefixColor(string prefix)
        {
            var normalized = Normalize(prefix);
            if (normalized == VipLegend || normalized == VipPro)
            {
                return new Color(0.72f, 0.55f, 1f, 0.98f);
            }

            return normalized switch
            {
                Legend => new Color(1f, 0.48f, 0.27f, 0.98f),
                Pro => new Color(0.91f, 0.75f, 0.29f, 0.98f),
                _ => new Color(0.72f, 0.55f, 1f, 0.98f),
            };
        }
    }
}
