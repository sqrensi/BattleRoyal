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

        private static int dataRevision;

        public static int DataRevision => dataRevision;

        private static void BumpRevision()
        {
            dataRevision++;
        }

        public static void Reset(string localTicket = null)
        {
            ByTicket.Clear();
            localTicketId = localTicket ?? string.Empty;
            BumpRevision();
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
                BumpRevision();
            }

            if (!string.IsNullOrWhiteSpace(nickname) &&
                !string.Equals(entry.Nickname, nickname.Trim(), StringComparison.Ordinal))
            {
                entry.Nickname = nickname.Trim();
                BumpRevision();
            }
        }

        public static void RecordKill(
            string killerTicketId,
            string victimTicketId,
            string killerNickname,
            string victimNickname)
        {
            var changed = false;
            var hasKiller = !string.IsNullOrWhiteSpace(killerTicketId) &&
                            !string.Equals(killerTicketId, "world", StringComparison.Ordinal);

            if (hasKiller)
            {
                var revisionBefore = dataRevision;
                Upsert(killerTicketId, killerNickname);
                changed |= dataRevision != revisionBefore;
                if (ByTicket.TryGetValue(killerTicketId, out var killer))
                {
                    killer.Kills++;
                    changed = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(victimTicketId))
            {
                var revisionBefore = dataRevision;
                Upsert(victimTicketId, victimNickname);
                changed |= dataRevision != revisionBefore;
                if (ByTicket.TryGetValue(victimTicketId, out var victim))
                {
                    victim.Deaths++;
                    changed = true;
                }
            }

            if (changed)
            {
                BumpRevision();
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

        public static void SyncFromDeathmatchState(RealtimeTransportClient.DmScoreboardRowMessage[] rows)
        {
            if (rows == null || rows.Length == 0)
            {
                return;
            }

            var changed = false;
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (row == null || string.IsNullOrWhiteSpace(row.ticketId))
                {
                    continue;
                }

                var revisionBefore = dataRevision;
                Upsert(row.ticketId, row.nickname);
                changed |= dataRevision != revisionBefore;
                if (!ByTicket.TryGetValue(row.ticketId, out var entry))
                {
                    continue;
                }

                var kills = Mathf.Max(0, row.kills);
                var deaths = Mathf.Max(0, row.deaths);
                var damage = Mathf.Max(0, row.damage);
                if (entry.Kills != kills || entry.Deaths != deaths || entry.Damage != damage)
                {
                    entry.Kills = kills;
                    entry.Deaths = deaths;
                    entry.Damage = damage;
                    changed = true;
                }
            }

            if (changed)
            {
                BumpRevision();
            }
        }

        public static void SyncFromSnapshot(RealtimeTransportClient.RealtimePlayerState[] players)
        {
            if (players == null)
            {
                return;
            }

            var changed = false;
            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null || string.IsNullOrWhiteSpace(player.ticketId))
                {
                    continue;
                }

                var revisionBefore = dataRevision;
                Upsert(player.ticketId, player.nickname);
                changed |= dataRevision != revisionBefore;
                if (!ByTicket.TryGetValue(player.ticketId, out var entry))
                {
                    continue;
                }

                var kills = Mathf.Max(entry.Kills, Mathf.Max(0, player.killCount));
                var deaths = Mathf.Max(entry.Deaths, player.deathCount);
                var damage = Mathf.Max(entry.Damage, player.damageDealt);
                if (entry.Kills != kills || entry.Deaths != deaths || entry.Damage != damage)
                {
                    entry.Kills = kills;
                    entry.Deaths = deaths;
                    entry.Damage = damage;
                    changed = true;
                }
            }

            if (changed)
            {
                BumpRevision();
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
                BumpRevision();
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

            var killsValue = Mathf.Max(0, kills);
            var deathsValue = Mathf.Max(0, deaths);
            var damageValue = Mathf.Max(0, damage);
            if (entry.Kills == killsValue &&
                entry.Deaths == deathsValue &&
                entry.Damage == damageValue &&
                entry.PingMs == pingMs)
            {
                return;
            }

            entry.Kills = killsValue;
            entry.Deaths = deathsValue;
            entry.Damage = damageValue;
            entry.PingMs = pingMs;
            BumpRevision();
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
