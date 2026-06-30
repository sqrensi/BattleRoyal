using System;
using System.Collections.Generic;
using ShooterPrototype.Network;
using UnityEngine;

namespace ShooterPrototype.Player
{
    public sealed class MatchScoreboardEntry
    {
        public string TicketId = string.Empty;
        public string Nickname = string.Empty;
        public int Kills;
        public int Deaths;
        public int Damage;
        public int PingMs = -1;
    }

    public static class MatchScoreboardTracker
    {
        private static readonly Dictionary<string, MatchScoreboardEntry> ByTicket =
            new Dictionary<string, MatchScoreboardEntry>(16);

        private static string localTicketId = string.Empty;

        public static void Reset(string localTicket = null)
        {
            ByTicket.Clear();
            localTicketId = localTicket ?? string.Empty;
        }

        public static void SetLocalTicket(string ticketId)
        {
            localTicketId = ticketId ?? string.Empty;
        }

        public static void Upsert(string ticketId, string nickname)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                return;
            }

            if (!ByTicket.TryGetValue(ticketId, out var entry))
            {
                entry = new MatchScoreboardEntry { TicketId = ticketId };
                ByTicket[ticketId] = entry;
            }

            if (!string.IsNullOrWhiteSpace(nickname))
            {
                entry.Nickname = nickname.Trim();
            }
        }

        public static void RecordKill(
            string killerTicketId,
            string victimTicketId,
            string killerNickname,
            string victimNickname)
        {
            var hasKiller = !string.IsNullOrWhiteSpace(killerTicketId) &&
                            !string.Equals(killerTicketId, "world", StringComparison.Ordinal);

            if (hasKiller)
            {
                Upsert(killerTicketId, killerNickname);
                if (ByTicket.TryGetValue(killerTicketId, out var killer))
                {
                    killer.Kills++;
                }
            }

            if (!string.IsNullOrWhiteSpace(victimTicketId))
            {
                Upsert(victimTicketId, victimNickname);
                if (ByTicket.TryGetValue(victimTicketId, out var victim))
                {
                    victim.Deaths++;
                }
            }
        }

        public static bool TryGetEntry(string ticketId, out MatchScoreboardEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                return false;
            }

            return ByTicket.TryGetValue(ticketId, out entry);
        }

        public static void AddDamageDealt(string ticketId, int damage)
        {
            AddDamage(ticketId, damage);
        }

        public static void SyncFromSnapshot(RealtimeTransportClient.RealtimePlayerState[] players)
        {
            if (players == null)
            {
                return;
            }

            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null || string.IsNullOrWhiteSpace(player.ticketId))
                {
                    continue;
                }

                Upsert(player.ticketId, player.nickname);
                if (ByTicket.TryGetValue(player.ticketId, out var entry) && player.killCount > entry.Kills)
                {
                    entry.Kills = player.killCount;
                }
            }
        }

        public static void AddDamage(string ticketId, int damage)
        {
            if (damage <= 0 || string.IsNullOrWhiteSpace(ticketId))
            {
                return;
            }

            Upsert(ticketId, null);
            if (ByTicket.TryGetValue(ticketId, out var entry))
            {
                entry.Damage += damage;
            }
        }

        public static void AddLocalDamage(int damage)
        {
            if (!string.IsNullOrWhiteSpace(localTicketId))
            {
                AddDamage(localTicketId, damage);
            }
        }

        public static void SetOfflineRow(
            string ticketId,
            string nickname,
            int kills,
            int deaths,
            int damage,
            int pingMs = -1)
        {
            Upsert(ticketId, nickname);
            if (!ByTicket.TryGetValue(ticketId, out var entry))
            {
                return;
            }

            entry.Kills = Mathf.Max(0, kills);
            entry.Deaths = Mathf.Max(0, deaths);
            entry.Damage = Mathf.Max(0, damage);
            entry.PingMs = pingMs;
        }

        public static void SetLocalPing(int pingMs)
        {
            if (string.IsNullOrWhiteSpace(localTicketId) ||
                !ByTicket.TryGetValue(localTicketId, out var entry))
            {
                return;
            }

            entry.PingMs = pingMs >= 0 ? pingMs : -1;
        }

        public static List<MatchScoreboardEntry> GetSortedRows()
        {
            var rows = new List<MatchScoreboardEntry>(ByTicket.Count);
            foreach (var entry in ByTicket.Values)
            {
                if (ShouldHideScoreboardRow(entry))
                {
                    continue;
                }

                rows.Add(entry);
            }

            rows.Sort((a, b) =>
            {
                var byKills = b.Kills.CompareTo(a.Kills);
                if (byKills != 0)
                {
                    return byKills;
                }

                return string.Compare(a.Nickname, b.Nickname, StringComparison.OrdinalIgnoreCase);
            });

            return rows;
        }

        private static bool ShouldHideScoreboardRow(MatchScoreboardEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.TicketId))
            {
                return true;
            }

            return string.Equals(entry.TicketId, "world", StringComparison.Ordinal);
        }
    }
}
