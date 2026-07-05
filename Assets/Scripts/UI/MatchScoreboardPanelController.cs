using System;
using System.Collections.Generic;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Network;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(640)]
    public sealed class MatchScoreboardPanelController : MonoBehaviour
    {
        private const int PanelLayoutVersion = 4;
        private const float RowHeight = 28f;
        private const float StatColumnWidth = 52f;
        private const float DamageColumnWidth = 72f;
        private const float TableFontSize = 20f;

        private Canvas targetCanvas;
        private GameObject panelRoot;
        private Transform tableBody;
        private bool isOpen;
        private bool sceneActive;
        private RealtimeTransportClient transportClient;
        private string localTicketId = "offline-local";
        private int lastRenderedRevision = -1;

        public bool IsOpen => isOpen;

        public void EnsureOnCanvas(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            targetCanvas = canvas;
            var existing = canvas.transform.Find("MatchScoreboardPanel");
            if (existing != null)
            {
                var marker = existing.GetComponent<ScoreboardLayoutMarker>();
                if (marker != null && marker.Version >= PanelLayoutVersion)
                {
                    panelRoot = existing.gameObject;
                    BindTableReferences();
                    if (tableBody != null)
                    {
                        return;
                    }
                }

                Destroy(existing.gameObject);
            }

            panelRoot = new GameObject("MatchScoreboardPanel");
            panelRoot.transform.SetParent(canvas.transform, false);
            panelRoot.AddComponent<ScoreboardLayoutMarker>().Version = PanelLayoutVersion;

            var panelRect = panelRoot.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(760f, 400f);
            panelRect.anchoredPosition = Vector2.zero;

            var background = panelRoot.AddComponent<Image>();
            UiTheme.ApplyPanel(background, UiPanelStyle.Hud);
            background.raycastTarget = false;

            var contentObject = new GameObject("TableContent");
            contentObject.transform.SetParent(panelRoot.transform, false);
            var contentRect = contentObject.AddComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(16f, 14f);
            contentRect.offsetMax = new Vector2(-16f, -14f);

            var contentLayout = contentObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 4f;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            CreateTableRow(
                contentObject.transform,
                "HeaderRow",
                "НИК",
                "K",
                "D",
                "K/D",
                "УРОН",
                "PING",
                header: true);

            var dividerObject = new GameObject("Divider");
            dividerObject.transform.SetParent(contentObject.transform, false);
            var dividerLayout = dividerObject.AddComponent<LayoutElement>();
            dividerLayout.preferredHeight = 1f;
            dividerLayout.minHeight = 1f;
            var dividerImage = dividerObject.AddComponent<Image>();
            dividerImage.color = new Color(1f, 1f, 1f, 0.12f);
            dividerImage.raycastTarget = false;

            var bodyObject = new GameObject("TableBody");
            bodyObject.transform.SetParent(contentObject.transform, false);
            var bodyLayout = bodyObject.AddComponent<VerticalLayoutGroup>();
            bodyLayout.spacing = 2f;
            bodyLayout.childAlignment = TextAnchor.UpperLeft;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = false;
            var bodyElement = bodyObject.AddComponent<LayoutElement>();
            bodyElement.flexibleHeight = 1f;
            tableBody = bodyObject.transform;

            panelRoot.SetActive(false);
        }

        private void BindTableReferences()
        {
            tableBody = null;
            if (panelRoot == null)
            {
                return;
            }

            var content = panelRoot.transform.Find("TableContent");
            tableBody = content != null ? content.Find("TableBody") : null;
        }

        private void EnsurePanelReady()
        {
            if (panelRoot != null && tableBody != null)
            {
                return;
            }

            if (targetCanvas == null)
            {
                var hud = GetComponent<GameHudController>();
                if (hud != null && hud.TryGetHudCanvas(out var hudCanvas))
                {
                    targetCanvas = hudCanvas;
                }
            }

            if (targetCanvas != null)
            {
                EnsureOnCanvas(targetCanvas);
            }
        }

        public void SetActiveForScene(bool active)
        {
            sceneActive = active;
            if (!active)
            {
                SetOpen(false);
                UnbindTransport();
                MatchScoreboardTracker.Reset();
                MatchStatsTracker.ResetForNewMatch();
            }
            else
            {
                EnsurePanelReady();
                BindTransport();
                if (string.IsNullOrWhiteSpace(localTicketId))
                {
                    localTicketId = "offline-local";
                }

                MatchScoreboardTracker.Reset(localTicketId);
            }
        }

        public void SetLocalTicketId(string ticketId)
        {
            localTicketId = ticketId ?? string.Empty;
            MatchScoreboardTracker.SetLocalTicket(localTicketId);
        }

        private void Update()
        {
            if (!sceneActive)
            {
                return;
            }

            EnsurePanelReady();
            if (panelRoot == null)
            {
                return;
            }

            if (ReadTabHeld())
            {
                if (!isOpen)
                {
                    SetOpen(true);
                }

                if (lastRenderedRevision != MatchScoreboardTracker.DataRevision)
                {
                    RefreshTable();
                    lastRenderedRevision = MatchScoreboardTracker.DataRevision;
                }

                return;
            }

            if (isOpen)
            {
                SetOpen(false);
            }
        }

        public void SetOpen(bool open)
        {
            if (panelRoot == null)
            {
                isOpen = false;
                return;
            }

            isOpen = open;
            panelRoot.SetActive(open);
            if (open)
            {
                RefreshTable();
                lastRenderedRevision = MatchScoreboardTracker.DataRevision;
            }
        }

        private void BindTransport()
        {
            transportClient = RealtimeTransportClient.Active != null
                ? RealtimeTransportClient.Active
                : FindFirstObjectByType<RealtimeTransportClient>();
            if (transportClient == null)
            {
                return;
            }

            transportClient.KillFeedReceived -= HandleKillFeed;
            transportClient.KillFeedReceived += HandleKillFeed;
        }

        private void UnbindTransport()
        {
            if (transportClient != null)
            {
                transportClient.KillFeedReceived -= HandleKillFeed;
            }
        }

        private void OnDestroy()
        {
            UnbindTransport();
        }

        private void HandleKillFeed(RealtimeTransportClient.KillFeedMessage message)
        {
            if (message == null)
            {
                return;
            }

            MatchScoreboardTracker.RecordKill(
                message.killerTicketId,
                message.victimTicketId,
                message.killerNickname,
                message.victimNickname);
        }

        private void RefreshTable()
        {
            if (tableBody == null)
            {
                return;
            }

            SyncRowsFromContext();
            RebuildTableRows(MatchScoreboardTracker.GetSortedRows());
        }

        private void RebuildTableRows(List<MatchScoreboardEntry> rows)
        {
            for (var i = tableBody.childCount - 1; i >= 0; i--)
            {
                Destroy(tableBody.GetChild(i).gameObject);
            }

            if (rows.Count == 0)
            {
                CreateTableRow(
                    tableBody,
                    "EmptyRow",
                    "—",
                    "0",
                    "0",
                    "0.0",
                    "0",
                    "—",
                    header: false);
                return;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var nickname = FormatDisplayNickname(row);
                var kd = row.Deaths > 0
                    ? (row.Kills / (float)row.Deaths).ToString("0.0")
                    : row.Kills.ToString();
                var ping = row.PingMs >= 0 ? row.PingMs.ToString() : "—";
                CreateTableRow(
                    tableBody,
                    $"Row_{i}",
                    nickname,
                    row.Kills.ToString(),
                    row.Deaths.ToString(),
                    kd,
                    row.Damage.ToString(),
                    ping,
                    header: false);
            }
        }

        private void SyncRowsFromContext()
        {
            if (transportClient != null &&
                transportClient.TryGetLatestMatchState(out var matchState) &&
                string.Equals(matchState.matchMode, "deathmatch", StringComparison.OrdinalIgnoreCase) &&
                matchState.dmScoreboard != null &&
                matchState.dmScoreboard.Length > 0)
            {
                MatchScoreboardTracker.SyncFromDeathmatchState(matchState.dmScoreboard);
            }
            else if (transportClient != null && transportClient.TryGetLatestSnapshot(out var snapshot))
            {
                MatchScoreboardTracker.SyncFromSnapshot(snapshot.players);
            }

            if (transportClient != null)
            {
                var ping = Mathf.RoundToInt(transportClient.SmoothedRoundTripMs);
                MatchScoreboardTracker.SetLocalPing(ping);
            }

            if (!string.IsNullOrWhiteSpace(localTicketId))
            {
                MatchScoreboardTracker.Upsert(localTicketId, PlayerProfileService.LocalDisplayNickname);
            }

            if (ActiveMatchContext.IsOfflineDuelSession && MatchOfflineDuelController.Active != null)
            {
                var duel = MatchOfflineDuelController.Active;
                MatchScoreboardTracker.SetOfflineRow(
                    localTicketId,
                    PlayerProfileService.LocalDisplayNickname,
                    duel.LocalRoundWins,
                    duel.BotRoundWins,
                    MatchStatsTracker.DamageDealtThisMatch,
                    0);
                MatchScoreboardTracker.SetOfflineRow(
                    "offline-bot",
                    duel.OpponentNickname,
                    duel.BotRoundWins,
                    duel.LocalRoundWins,
                    duel.DamageDealtToOpponent,
                    -1);
            }
            else if (ActiveMatchContext.IsOfflineDeathmatchSession &&
                     MatchOfflineDeathmatchController.Active != null)
            {
                MatchOfflineDeathmatchController.Active.EnsureScoreboardParticipants();
            }
            else if (MatchTrainingController.Active != null)
            {
                var training = MatchTrainingController.Active;
                MatchScoreboardTracker.SetOfflineRow(
                    localTicketId,
                    PlayerProfileService.LocalDisplayNickname,
                    training.KillCount,
                    0,
                    MatchStatsTracker.DamageDealtThisMatch,
                    0);
            }
        }

        private void CreateTableRow(
            Transform parent,
            string rowName,
            string nick,
            string kills,
            string deaths,
            string kd,
            string damage,
            string ping,
            bool header)
        {
            var rowObject = new GameObject(rowName);
            rowObject.transform.SetParent(parent, false);
            var rowLayout = rowObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = RowHeight;
            rowLayout.minHeight = RowHeight;

            var horizontal = rowObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.spacing = 6f;
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;

            CreateCell(
                rowObject.transform,
                "Nick",
                nick,
                flexibleWidth: 1f,
                minWidth: 180f,
                columnWidth: 0f,
                rightAligned: false,
                header: header);
            CreateCell(rowObject.transform, "Kills", kills, 0f, 0f, StatColumnWidth, true, header);
            CreateCell(rowObject.transform, "Deaths", deaths, 0f, 0f, StatColumnWidth, true, header);
            CreateCell(rowObject.transform, "Kd", kd, 0f, 0f, StatColumnWidth, true, header);
            CreateCell(rowObject.transform, "Damage", damage, 0f, 0f, DamageColumnWidth, true, header);
            CreateCell(rowObject.transform, "Ping", ping, 0f, 0f, StatColumnWidth, true, header);
        }

        private static void CreateCell(
            Transform parent,
            string name,
            string value,
            float flexibleWidth,
            float minWidth,
            float columnWidth,
            bool rightAligned,
            bool header)
        {
            var cellObject = new GameObject(name);
            cellObject.transform.SetParent(parent, false);
            var layout = cellObject.AddComponent<LayoutElement>();
            if (flexibleWidth > 0f)
            {
                layout.flexibleWidth = flexibleWidth;
                layout.minWidth = minWidth;
            }
            else
            {
                layout.preferredWidth = columnWidth;
                layout.minWidth = columnWidth;
            }

            var text = cellObject.AddComponent<TextMeshProUGUI>();
            text.text = value ?? string.Empty;
            text.fontSize = TableFontSize;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = rightAligned
                ? TextAlignmentOptions.MidlineRight
                : TextAlignmentOptions.MidlineLeft;
            UiTheme.ApplyTmp(text, header ? UiTextRole.Muted : UiTextRole.Body);
            if (header)
            {
                text.fontStyle = FontStyles.Bold;
            }
        }

        private string FormatDisplayNickname(MatchScoreboardEntry row)
        {
            if (row == null)
            {
                return "Игрок";
            }

            if (ShouldShowLocalAsYou(row.TicketId))
            {
                return "Вы";
            }

            return string.IsNullOrWhiteSpace(row.Nickname) ? "Игрок" : row.Nickname;
        }

        private bool ShouldShowLocalAsYou(string ticketId)
        {
            if (string.IsNullOrWhiteSpace(ticketId) || string.IsNullOrWhiteSpace(localTicketId))
            {
                return false;
            }

            if (!string.Equals(ticketId, localTicketId, StringComparison.Ordinal))
            {
                return false;
            }

            return ActiveMatchContext.IsOfflineDuelSession ||
                   ActiveMatchContext.IsOfflineDeathmatchSession ||
                   ActiveMatchContext.IsOfflineSoloSession;
        }

        private static bool ReadTabHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.tabKey.isPressed;
#else
            return Input.GetKey(KeyCode.Tab);
#endif
        }

        private sealed class ScoreboardLayoutMarker : MonoBehaviour
        {
            public int Version;
        }
    }
}
