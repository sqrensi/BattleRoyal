using System.Collections.Generic;
using ShooterPrototype.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class GameKillFeedController : MonoBehaviour
    {
        private const int MaxEntries = 5;
        private const float EntryLifetimeSeconds = 3f;
        private const float EntryIntroSeconds = 0.28f;
        private const float EntrySlidePixels = 48f;
        private const int FeedLayoutVersion = 6;
        private const float TopOffsetBelowCornerStats = 62f;
        private const float EntryHorizontalPadding = 14f;
        private const float EntryVerticalPadding = 6f;
        private const float EntryMinWidth = 120f;
        private const float EntryMaxWidth = 560f;


        [SerializeField] private float edgeMargin = 18f;
        [SerializeField] private float entryHeight = 26f;
        [SerializeField] private float entrySpacing = 5f;
        [SerializeField] private float entryFontSize = 16f;

        private RectTransform feedRoot;
        private RealtimeTransportClient transportClient;
        private readonly List<FeedEntry> activeEntries = new List<FeedEntry>(MaxEntries);
        private long lastSeq = -1;
        private bool built;

        private sealed class FeedEntry
        {
            public GameObject Root;
            public CanvasGroup Group;
            public RectTransform Rect;
            public float ExpireAt;
            public float IntroAt;
        }

        public void EnsureOnCanvas(Canvas targetCanvas)
        {
            if (targetCanvas == null)
            {
                return;
            }

            var existingPanel = targetCanvas.transform.Find("KillFeedPanel");
            if (existingPanel != null)
            {
                var versionMarker = existingPanel.GetComponent<KillFeedLayoutMarker>();
                if (versionMarker != null && versionMarker.Version >= FeedLayoutVersion)
                {
                    feedRoot = existingPanel.GetComponent<RectTransform>();
                    built = true;
                    BindTransportClient();
                    return;
                }

                ClearEntries();
                Destroy(existingPanel.gameObject);
                feedRoot = null;
                built = false;
            }

            if (!built)
            {
                BuildFeedPanel(targetCanvas.transform);
            }

            BindTransportClient();
        }

        public void SetActiveForScene(bool isGameScene)
        {
            if (feedRoot != null)
            {
                feedRoot.gameObject.SetActive(isGameScene);
            }

            if (!isGameScene)
            {
                ClearEntries();
                UnbindTransportClient();
            }
            else
            {
                BindTransportClient();
            }
        }

        private void Update()
        {
            if (activeEntries.Count == 0)
            {
                return;
            }

            var now = Time.unscaledTime;
            var layoutDirty = false;
            for (var i = activeEntries.Count - 1; i >= 0; i--)
            {
                var entry = activeEntries[i];
                if (entry == null || entry.Root == null)
                {
                    activeEntries.RemoveAt(i);
                    layoutDirty = true;
                    continue;
                }

                var remaining = entry.ExpireAt - now;
                if (remaining <= 0f)
                {
                    Destroy(entry.Root);
                    activeEntries.RemoveAt(i);
                    layoutDirty = true;
                    continue;
                }

                if (entry.Group != null)
                {
                    var fade = remaining < 0.45f ? remaining / 0.45f : 1f;
                    var introT = entry.IntroAt > 0f
                        ? Mathf.Clamp01((EntryIntroSeconds - (entry.IntroAt - now)) / EntryIntroSeconds)
                        : 1f;
                    entry.Group.alpha = fade * introT;
                }

                if (entry.Rect != null && entry.IntroAt > now)
                {
                    var introT = Mathf.Clamp01((EntryIntroSeconds - (entry.IntroAt - now)) / EntryIntroSeconds);
                    var eased = 1f - (1f - introT) * (1f - introT);
                    entry.Rect.anchoredPosition = new Vector2(Mathf.Lerp(EntrySlidePixels, 0f, eased), entry.Rect.anchoredPosition.y);
                }
            }

            if (layoutDirty)
            {
                RebuildEntryLayout();
            }
        }

        private void OnDisable()
        {
            UnbindTransportClient();
        }

        private void BindTransportClient()
        {
            if (transportClient == null)
            {
                transportClient = RealtimeTransportClient.Active != null
                    ? RealtimeTransportClient.Active
                    : FindFirstObjectByType<RealtimeTransportClient>();
            }

            if (transportClient == null)
            {
                return;
            }

            transportClient.KillFeedReceived -= HandleKillFeed;
            transportClient.KillFeedReceived += HandleKillFeed;
        }

        private void UnbindTransportClient()
        {
            if (transportClient != null)
            {
                transportClient.KillFeedReceived -= HandleKillFeed;
            }
        }

        private void HandleKillFeed(RealtimeTransportClient.KillFeedMessage message)
        {
            if (message == null || feedRoot == null || !feedRoot.gameObject.activeInHierarchy)
            {
                return;
            }

            if (message.seq > 0 && message.seq <= lastSeq)
            {
                return;
            }

            if (message.seq > 0)
            {
                lastSeq = message.seq;
            }

            PushEntry(FormatKillFeedMessage(message));
        }

        private static string FormatKillFeedMessage(RealtimeTransportClient.KillFeedMessage message)
        {
            var victim = string.IsNullOrWhiteSpace(message.victimNickname) ? "Игрок" : message.victimNickname.Trim();
            if (string.Equals(message.cause, "zone", System.StringComparison.OrdinalIgnoreCase))
            {
                return $"<color=#{ColorToHex(UiTheme.KillFeedVictim)}>{victim}</color> <color=#{ColorToHex(UiTheme.KillFeedWeapon)}>погиб от зоны</color>";
            }

            if (string.Equals(message.cause, "disconnect", System.StringComparison.OrdinalIgnoreCase))
            {
                return $"<color=#{ColorToHex(UiTheme.KillFeedVictim)}>{victim}</color> <color=#{ColorToHex(UiTheme.KillFeedWeapon)}>отключился</color>";
            }

            var killer = string.IsNullOrWhiteSpace(message.killerNickname) ? "Игрок" : message.killerNickname.Trim();
            return $"<color=#{ColorToHex(UiTheme.KillFeedKiller)}>{killer}</color> <color=#{ColorToHex(UiTheme.KillFeedWeapon)}>убил</color> <color=#{ColorToHex(UiTheme.KillFeedVictim)}>{victim}</color>";
        }

        private static string ColorToHex(Color color)
        {
            var r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            var g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            var b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return $"{r:X2}{g:X2}{b:X2}";
        }

        public void PushLocalPlayerKill(string killerNickname, string victimNickname)
        {
            var killer = string.IsNullOrWhiteSpace(killerNickname) ? "Игрок" : killerNickname.Trim();
            var victim = string.IsNullOrWhiteSpace(victimNickname) ? "Игрок" : victimNickname.Trim();
            PushEntry(
                $"<color=#{ColorToHex(UiTheme.KillFeedKiller)}>{killer}</color> " +
                $"<color=#{ColorToHex(UiTheme.KillFeedWeapon)}>убил</color> " +
                $"<color=#{ColorToHex(UiTheme.KillFeedVictim)}>{victim}</color>");
        }

        private void PushEntry(string richText)
        {
            while (activeEntries.Count >= MaxEntries)
            {
                var oldest = activeEntries[0];
                activeEntries.RemoveAt(0);
                if (oldest?.Root != null)
                {
                    Destroy(oldest.Root);
                }
            }

            var entryObject = new GameObject("KillFeedEntry");
            entryObject.transform.SetParent(feedRoot, false);
            entryObject.transform.SetAsLastSibling();

            var entryRect = entryObject.AddComponent<RectTransform>();
            entryRect.anchorMin = new Vector2(1f, 1f);
            entryRect.anchorMax = new Vector2(1f, 1f);
            entryRect.pivot = new Vector2(1f, 1f);

            var innerObject = new GameObject("LabelWrap");
            innerObject.transform.SetParent(entryObject.transform, false);
            var innerRect = innerObject.AddComponent<RectTransform>();
            innerRect.anchorMin = new Vector2(1f, 0.5f);
            innerRect.anchorMax = new Vector2(1f, 0.5f);
            innerRect.pivot = new Vector2(1f, 0.5f);
            innerRect.anchoredPosition = Vector2.zero;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(innerObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(EntryHorizontalPadding, EntryVerticalPadding);
            labelRect.offsetMax = new Vector2(-EntryHorizontalPadding, -EntryVerticalPadding);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = entryFontSize;
            label.alignment = TextAlignmentOptions.MidlineRight;
            label.richText = true;
            label.text = richText;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.lineSpacing = -4f;
            label.characterSpacing = 0.2f;
            label.outlineWidth = 0.16f;
            label.outlineColor = new Color(0.02f, 0.02f, 0.02f, 0.78f);

            label.ForceMeshUpdate(true, true);
            var textWidth = Mathf.Clamp(label.preferredWidth, EntryMinWidth, EntryMaxWidth);
            var textHeight = Mathf.Max(entryHeight, label.preferredHeight + EntryVerticalPadding * 2f);
            innerRect.sizeDelta = new Vector2(textWidth + EntryHorizontalPadding * 2f, textHeight);
            entryRect.sizeDelta = innerRect.sizeDelta;
            entryRect.anchoredPosition = new Vector2(EntrySlidePixels, 0f);

            var group = entryObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            activeEntries.Add(new FeedEntry
            {
                Root = entryObject,
                Group = group,
                Rect = entryRect,
                ExpireAt = Time.unscaledTime + EntryLifetimeSeconds,
                IntroAt = Time.unscaledTime + EntryIntroSeconds
            });

            RebuildEntryLayout();
        }

        private void RebuildEntryLayout()
        {
            var y = 0f;
            for (var i = activeEntries.Count - 1; i >= 0; i--)
            {
                var entry = activeEntries[i];
                if (entry?.Root == null)
                {
                    continue;
                }

                var rect = entry.Root.GetComponent<RectTransform>();
                if (rect == null)
                {
                    continue;
                }

                rect.anchoredPosition = new Vector2(0f, y);
                y -= rect.sizeDelta.y + entrySpacing;
            }
        }

        private void ClearEntries()
        {
            for (var i = 0; i < activeEntries.Count; i++)
            {
                if (activeEntries[i]?.Root != null)
                {
                    Destroy(activeEntries[i].Root);
                }
            }

            activeEntries.Clear();
            lastSeq = -1;
        }

        private void BuildFeedPanel(Transform canvasTransform)
        {
            var rootObject = new GameObject("KillFeedPanel");
            rootObject.transform.SetParent(canvasTransform, false);

            feedRoot = rootObject.AddComponent<RectTransform>();
            feedRoot.anchorMin = new Vector2(1f, 1f);
            feedRoot.anchorMax = new Vector2(1f, 1f);
            feedRoot.pivot = new Vector2(1f, 1f);
            feedRoot.anchoredPosition = new Vector2(-edgeMargin, -(edgeMargin + TopOffsetBelowCornerStats));
            feedRoot.sizeDelta = new Vector2(EntryMaxWidth, 240f);

            rootObject.AddComponent<KillFeedLayoutMarker>().Version = FeedLayoutVersion;
            built = true;
        }

        private sealed class KillFeedLayoutMarker : MonoBehaviour
        {
            public int Version;
        }
    }
}
