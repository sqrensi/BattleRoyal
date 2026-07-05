using System.Text.RegularExpressions;
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

        private static readonly Regex RankPrefixRegex = new(
            @"^(LEGEND|PRO)(?:X(\d+))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex VipRankPrefixRegex = new(
            @"^VIP-(LEGEND|PRO)(?:X(\d+))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex FormattedPlainRegex = new(
            @"^\[([^\]]+)\]\s*(.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
            if (!TryParseRankPrefix(rankPrefix, out var rankBase, out var rankCount))
            {
                rankBase = string.Empty;
                rankCount = 0;
            }

            var rankValue = string.IsNullOrEmpty(rankBase)
                ? string.Empty
                : FormatRankPrefixWithCount(rankBase, rankCount);

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

        public static string FormatRankPrefixWithCount(string rankPrefix, int count)
        {
            var basePrefix = (rankPrefix ?? string.Empty).Trim().ToUpperInvariant();
            if (basePrefix != Legend && basePrefix != Pro)
            {
                return string.Empty;
            }

            return count > 1 ? $"{basePrefix}x{count}" : basePrefix;
        }

        public static bool TryParseRankPrefix(string prefix, out string rankBase, out int count)
        {
            rankBase = string.Empty;
            count = 0;

            var value = (prefix ?? string.Empty).Trim().ToUpperInvariant();
            var match = RankPrefixRegex.Match(value);
            if (!match.Success)
            {
                return false;
            }

            rankBase = match.Groups[1].Value;
            count = match.Groups[2].Success
                ? Mathf.Max(1, int.Parse(match.Groups[2].Value))
                : 1;
            return true;
        }

        public static string Normalize(string prefix)
        {
            var value = (prefix ?? string.Empty).Trim().ToUpperInvariant();
            if (value == Vip)
            {
                return Vip;
            }

            var vipMatch = VipRankPrefixRegex.Match(value);
            if (vipMatch.Success)
            {
                var rankBase = vipMatch.Groups[1].Value;
                var rankCount = vipMatch.Groups[2].Success
                    ? Mathf.Max(1, int.Parse(vipMatch.Groups[2].Value))
                    : 1;
                return $"{Vip}-{FormatRankPrefixWithCount(rankBase, rankCount)}";
            }

            if (TryParseRankPrefix(value, out var basePrefix, out var count))
            {
                return FormatRankPrefixWithCount(basePrefix, count);
            }

            return string.Empty;
        }

        public static string FormatPlain(string prefix, string nickname)
        {
            var nick = string.IsNullOrWhiteSpace(nickname) ? "Игрок" : nickname.Trim();
            var normalized = Normalize(prefix);
            return string.IsNullOrEmpty(normalized) ? nick : $"[{normalized}] {nick}";
        }

        public static string FormatRichPrefixLabel(string prefix)
        {
            var normalized = Normalize(prefix);
            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            return FormatRichBracketLabel(normalized);
        }

        public static string FormatPlainPrefixLabel(string prefix)
        {
            var normalized = Normalize(prefix);
            return string.IsNullOrEmpty(normalized) ? string.Empty : $"[{normalized}]";
        }

        public static string FormatRich(string prefix, string nickname)
        {
            var nick = string.IsNullOrWhiteSpace(nickname) ? "Игрок" : nickname.Trim();
            var normalized = Normalize(prefix);
            if (string.IsNullOrEmpty(normalized))
            {
                return nick;
            }

            return $"{FormatRichBracketLabel(normalized)} {nick}";
        }

        public static bool TrySplitFormattedPlain(string formatted, out string prefix, out string nickname)
        {
            prefix = string.Empty;
            nickname = formatted ?? string.Empty;

            var value = (formatted ?? string.Empty).Trim();
            var match = FormattedPlainRegex.Match(value);
            if (!match.Success)
            {
                return false;
            }

            prefix = match.Groups[1].Value.Trim();
            nickname = match.Groups[2].Value.Trim();
            return true;
        }

        public static string FormatRichFromFormattedPlain(string formattedPlain)
        {
            var value = string.IsNullOrWhiteSpace(formattedPlain) ? "Игрок" : formattedPlain.Trim();
            if (TrySplitFormattedPlain(value, out var prefix, out var nick))
            {
                return FormatRich(prefix, string.IsNullOrWhiteSpace(nick) ? "Игрок" : nick);
            }

            return value;
        }

        public static Color GetPrefixColor(string prefix)
        {
            var normalized = Normalize(prefix);
            if (string.IsNullOrEmpty(normalized))
            {
                return new Color(0.72f, 0.55f, 1f, 0.98f);
            }

            if (normalized.StartsWith($"{Vip}-"))
            {
                var rankPart = normalized.Substring(Vip.Length + 1);
                if (TryParseRankPrefix(rankPart, out var rankBase, out _))
                {
                    return rankBase == Legend
                        ? new Color(1f, 0.48f, 0.27f, 0.98f)
                        : new Color(0.91f, 0.75f, 0.29f, 0.98f);
                }

                return new Color(0.72f, 0.55f, 1f, 0.98f);
            }

            if (TryParseRankPrefix(normalized, out var basePrefix, out _))
            {
                return basePrefix == Legend
                    ? new Color(1f, 0.48f, 0.27f, 0.98f)
                    : new Color(0.91f, 0.75f, 0.29f, 0.98f);
            }

            return new Color(0.72f, 0.55f, 1f, 0.98f);
        }

        private static string FormatRichBracketLabel(string normalized)
        {
            var vipMatch = VipRankPrefixRegex.Match(normalized);
            if (vipMatch.Success)
            {
                var rankBase = vipMatch.Groups[1].Value;
                var rankCount = vipMatch.Groups[2].Success
                    ? Mathf.Max(1, int.Parse(vipMatch.Groups[2].Value))
                    : 1;
                var rankLabel = FormatRankPrefixWithCount(rankBase, rankCount);
                var rankColor = rankBase == Legend ? "#FF7A45" : "#E8C04A";
                return $"<color=#B88CFF>[VIP</color><color={rankColor}>-{rankLabel}</color><color=#B88CFF>]</color>";
            }

            if (TryParseRankPrefix(normalized, out var basePrefix, out var count))
            {
                var label = FormatRankPrefixWithCount(basePrefix, count);
                var color = basePrefix == Legend ? "#FF7A45" : "#E8C04A";
                return $"<color={color}>[{label}]</color>";
            }

            return $"<color=#B88CFF>[{normalized}]</color>";
        }
    }
}
