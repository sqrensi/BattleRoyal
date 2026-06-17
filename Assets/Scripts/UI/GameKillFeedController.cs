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
        private const float EntryLifetimeSeconds = 5.5f;
        private const float EntryHorizontalPadding = 24f;
        private const float EntryVerticalPadding = 8f;
        private const float EntryMinWidth = 96f;
        private const float EntryMaxWidth = 640f;

        private static Sprite whiteSprite;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.42f);
        private static readonly Color KillerColor = new Color(0.98f, 0.86f, 0.58f, 1f);
        private static readonly Color VictimColor = new Color(0.94f, 0.96f, 0.98f, 1f);
        private static readonly Color ActionColor = new Color(0.78f, 0.84f, 0.88f, 0.95f);

        [SerializeField] private float edgeMargin = 18f;
        [SerializeField] private float entryHeight = 28f;
        [SerializeField] private float entrySpacing = 4f;
        [SerializeField] private float entryFontSize = 18f;

        private RectTransform feedRoot;
        private RealtimeTransportClient transportClient;
        private readonly List<FeedEntry> activeEntries = new List<FeedEntry>(MaxEntries);
        private long lastSeq = -1;
        private bool built;

        private sealed class FeedEntry
        {
            public GameObject Root;
            public CanvasGroup Group;
            public float ExpireAt;
        }

        public void EnsureOnCanvas(Canvas targetCanvas)
        {
            if (targetCanvas == null)
            {
                return;
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
            for (var i = activeEntries.Count - 1; i >= 0; i--)
            {
                var entry = activeEntries[i];
                if (entry == null || entry.Root == null)
                {
                    activeEntries.RemoveAt(i);
                    continue;
                }

                var remaining = entry.ExpireAt - now;
                if (remaining <= 0f)
                {
                    Destroy(entry.Root);
                    activeEntries.RemoveAt(i);
                    continue;
                }

                if (entry.Group != null)
                {
                    entry.Group.alpha = remaining < 1f ? remaining : 1f;
                }
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
                return $"<color=#{ColorToHex(VictimColor)}>{victim}</color> <color=#{ColorToHex(ActionColor)}>погиб от зоны</color>";
            }

            var killer = string.IsNullOrWhiteSpace(message.killerNickname) ? "Игрок" : message.killerNickname.Trim();
            return $"<color=#{ColorToHex(KillerColor)}>{killer}</color> <color=#{ColorToHex(ActionColor)}>убил</color> <color=#{ColorToHex(VictimColor)}>{victim}</color>";
        }

        private static string ColorToHex(Color color)
        {
            var r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            var g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            var b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return $"{r:X2}{g:X2}{b:X2}";
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

            var innerObject = new GameObject("Background");
            innerObject.transform.SetParent(entryObject.transform, false);
            var innerRect = innerObject.AddComponent<RectTransform>();
            innerRect.anchorMin = new Vector2(1f, 0.5f);
            innerRect.anchorMax = new Vector2(1f, 0.5f);
            innerRect.pivot = new Vector2(1f, 0.5f);
            innerRect.anchoredPosition = Vector2.zero;

            var background = innerObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.type = Image.Type.Simple;
            background.color = PanelColor;
            background.raycastTarget = false;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(innerObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(EntryHorizontalPadding * 0.5f, EntryVerticalPadding * 0.5f);
            labelRect.offsetMax = new Vector2(-EntryHorizontalPadding * 0.5f, -EntryVerticalPadding * 0.5f);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = entryFontSize;
            label.alignment = TextAlignmentOptions.MidlineRight;
            label.richText = true;
            label.text = richText;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;

            label.ForceMeshUpdate(true, true);
            var textWidth = Mathf.Clamp(label.preferredWidth, EntryMinWidth, EntryMaxWidth);
            var textHeight = Mathf.Max(entryHeight - EntryVerticalPadding, label.preferredHeight);
            innerRect.sizeDelta = new Vector2(textWidth + EntryHorizontalPadding, textHeight + EntryVerticalPadding);
            entryRect.sizeDelta = innerRect.sizeDelta;

            var group = entryObject.AddComponent<CanvasGroup>();

            activeEntries.Add(new FeedEntry
            {
                Root = entryObject,
                Group = group,
                ExpireAt = Time.unscaledTime + EntryLifetimeSeconds
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
            feedRoot.anchoredPosition = new Vector2(-edgeMargin, -edgeMargin);
            feedRoot.sizeDelta = new Vector2(EntryMaxWidth, 240f);

            built = true;
        }

        private static Sprite GetWhiteSprite()
        {
            if (whiteSprite != null)
            {
                return whiteSprite;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.SetPixel(1, 0, Color.white);
            texture.SetPixel(0, 1, Color.white);
            texture.SetPixel(1, 1, Color.white);
            texture.Apply(false, false);
            whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 100f);
            return whiteSprite;
        }
    }
}
