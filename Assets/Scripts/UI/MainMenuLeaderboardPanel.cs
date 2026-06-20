using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuLeaderboardPanel : MonoBehaviour
    {
        private static Sprite whiteSprite;

        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.1f, 0.72f);
        private static readonly Color TitleColor = new Color(0.94f, 0.96f, 0.98f, 0.98f);
        private static readonly Color RowColor = new Color(0.1f, 0.12f, 0.15f, 0.88f);
        private static readonly Color SelfRowColor = new Color(0.16f, 0.28f, 0.26f, 0.92f);
        private static readonly Color RankColor = new Color(0.72f, 0.78f, 0.84f, 0.92f);
        private static readonly Color NicknameColor = new Color(0.94f, 0.96f, 0.98f, 0.96f);
        private static readonly Color SelfNicknameColor = new Color(0.92f, 0.84f, 0.55f, 0.98f);
        private static readonly Color RatingColor = new Color(0.92f, 0.84f, 0.55f, 0.98f);
        private static readonly Color MutedColor = new Color(0.72f, 0.76f, 0.8f, 0.88f);
        private static readonly Color ScrollTrackColor = new Color(0.1f, 0.12f, 0.14f, 0.55f);
        private static readonly Color ScrollHandleColor = new Color(0.24f, 0.28f, 0.32f, 0.92f);

        [SerializeField] private float panelWidth = 340f;
        [SerializeField] private float panelPadding = 14f;
        [SerializeField] private float scrollHeight = 280f;
        [SerializeField] private float rowHeight = 24f;
        [SerializeField] private float rowSpacing = 4f;
        [SerializeField] private float titleFontSize = 18f;
        [SerializeField] private float rowFontSize = 14f;
        [SerializeField] private int entryLimit = 25;

        private RectTransform contentRect;
        private MainMenuController menuController;
        private PlayerProfileApiClient profileApiClient;
        private Coroutine fetchCoroutine;
        private bool built;

        public RectTransform RootRect { get; private set; }

        public void Configure(MainMenuController controller, PlayerProfileApiClient apiClient)
        {
            menuController = controller;
            profileApiClient = apiClient;
        }

        public void Build(RectTransform stackParent)
        {
            if (built || stackParent == null)
            {
                return;
            }

            var rootObject = new GameObject("MainMenuLeaderboardPanel");
            rootObject.transform.SetParent(stackParent, false);

            RootRect = rootObject.AddComponent<RectTransform>();

            var layoutElement = rootObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = panelWidth;
            layoutElement.minWidth = panelWidth;

            var background = rootObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.type = Image.Type.Simple;
            background.color = PanelColor;
            background.raycastTarget = true;

            var layout = rootObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.spacing = 8f;
            layout.padding = new RectOffset(
                Mathf.RoundToInt(panelPadding),
                Mathf.RoundToInt(panelPadding),
                Mathf.RoundToInt(panelPadding),
                Mathf.RoundToInt(panelPadding));
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(rootObject.transform, false);
            var titleLayout = titleObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 24f;
            var titleText = titleObject.AddComponent<TextMeshProUGUI>();
            titleText.text = "Топ 25";
            titleText.fontSize = titleFontSize;
            titleText.fontStyle = FontStyles.Bold;
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
            titleText.color = TitleColor;
            titleText.raycastTarget = false;

            BuildScrollArea(rootObject.transform);

            var rootFitter = rootObject.AddComponent<ContentSizeFitter>();
            rootFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            built = true;
            ShowMessage("Загрузка...");
            RequestRefresh();
        }

        private void OnEnable()
        {
            PlayerProfileService.ProfileSynced += RequestRefresh;
            if (built)
            {
                RequestRefresh();
            }
        }

        private void OnDisable()
        {
            PlayerProfileService.ProfileSynced -= RequestRefresh;
        }

        public void RequestRefresh()
        {
            if (!built)
            {
                return;
            }

            if (fetchCoroutine != null)
            {
                StopCoroutine(fetchCoroutine);
            }

            fetchCoroutine = StartCoroutine(FetchLeaderboardRoutine());
        }

        private IEnumerator FetchLeaderboardRoutine()
        {
            if (profileApiClient == null)
            {
                profileApiClient = menuController != null ? menuController.ProfileApiClient : null;
            }

            if (profileApiClient == null)
            {
                ShowMessage("Сервер недоступен.");
                yield break;
            }

            var completed = false;
            var success = false;
            LeaderboardEntryDto[] entries = null;
            var error = string.Empty;

            yield return profileApiClient.FetchLeaderboard(
                entryLimit,
                (ok, responseEntries, responseError) =>
                {
                    completed = true;
                    success = ok;
                    entries = responseEntries;
                    error = responseError;
                });

            if (!completed)
            {
                yield break;
            }

            if (!success || entries == null)
            {
                ShowMessage(string.IsNullOrWhiteSpace(error) ? "Не удалось загрузить." : error);
                yield break;
            }

            RenderEntries(entries);
        }

        private void BuildScrollArea(Transform parent)
        {
            var scrollContainer = new GameObject("ScrollContainer");
            scrollContainer.transform.SetParent(parent, false);
            var scrollContainerLayout = scrollContainer.AddComponent<LayoutElement>();
            scrollContainerLayout.preferredHeight = scrollHeight;
            scrollContainerLayout.minHeight = scrollHeight;

            var scrollObject = new GameObject("Scroll");
            scrollObject.transform.SetParent(scrollContainer.transform, false);
            var scrollRectTransform = scrollObject.AddComponent<RectTransform>();
            StretchFull(scrollRectTransform);

            var scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            var viewportObject = new GameObject("Viewport");
            viewportObject.transform.SetParent(scrollObject.transform, false);
            var viewportRect = viewportObject.AddComponent<RectTransform>();
            StretchFull(viewportRect);
            viewportRect.offsetMax = new Vector2(-12f, 0f);
            viewportObject.AddComponent<RectMask2D>();

            var scrollbar = CreateVerticalScrollbar(scrollObject.transform);
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            var contentObject = new GameObject("Content");
            contentObject.transform.SetParent(viewportObject.transform, false);
            contentRect = contentObject.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var contentLayout = contentObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = rowSpacing;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;
        }

        private void ShowMessage(string message)
        {
            ClearContent();
            CreateMessageRow(message);
        }

        private void RenderEntries(IReadOnlyList<LeaderboardEntryDto> entries)
        {
            ClearContent();

            if (entries == null || entries.Count == 0)
            {
                CreateMessageRow("Пока нет игроков.");
                return;
            }

            var localPlayerId = PlayerIdentityService.GetOrCreatePlayerId();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                var isSelf = !string.IsNullOrWhiteSpace(localPlayerId) &&
                             string.Equals(entry.playerId, localPlayerId, System.StringComparison.Ordinal);
                CreateEntryRow(entry, isSelf);
            }
        }

        private void ClearContent()
        {
            if (contentRect == null)
            {
                return;
            }

            for (var i = contentRect.childCount - 1; i >= 0; i--)
            {
                var child = contentRect.GetChild(i);
                if (child != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }

        private void CreateMessageRow(string message)
        {
            var rowObject = new GameObject("MessageRow");
            rowObject.transform.SetParent(contentRect, false);
            var rowLayout = rowObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = rowHeight;

            var text = rowObject.AddComponent<TextMeshProUGUI>();
            text.text = message ?? string.Empty;
            text.fontSize = rowFontSize;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.color = MutedColor;
            text.raycastTarget = false;
        }

        private void CreateEntryRow(LeaderboardEntryDto entry, bool isSelf)
        {
            var rowObject = new GameObject("Entry_" + entry.rank);
            rowObject.transform.SetParent(contentRect, false);

            var rowLayout = rowObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = rowHeight;
            rowLayout.minHeight = rowHeight;

            var background = rowObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.type = Image.Type.Simple;
            background.color = isSelf ? SelfRowColor : RowColor;
            background.raycastTarget = false;

            var horizontal = rowObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.padding = new RectOffset(8, 8, 0, 0);
            horizontal.spacing = 6f;
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;

            var rankObject = new GameObject("Rank");
            rankObject.transform.SetParent(rowObject.transform, false);
            var rankLayout = rankObject.AddComponent<LayoutElement>();
            rankLayout.preferredWidth = 28f;
            rankLayout.minWidth = 28f;
            var rankText = rankObject.AddComponent<TextMeshProUGUI>();
            rankText.text = entry.rank.ToString();
            rankText.fontSize = rowFontSize;
            rankText.fontStyle = FontStyles.Bold;
            rankText.alignment = TextAlignmentOptions.MidlineRight;
            rankText.color = RankColor;
            rankText.raycastTarget = false;

            var nicknameObject = new GameObject("Nickname");
            nicknameObject.transform.SetParent(rowObject.transform, false);
            var nicknameLayout = nicknameObject.AddComponent<LayoutElement>();
            nicknameLayout.flexibleWidth = 1f;
            var nicknameText = nicknameObject.AddComponent<TextMeshProUGUI>();
            nicknameText.text = string.IsNullOrWhiteSpace(entry.nickname) ? "Игрок" : entry.nickname.Trim();
            nicknameText.fontSize = rowFontSize;
            nicknameText.fontStyle = isSelf ? FontStyles.Bold : FontStyles.Normal;
            nicknameText.alignment = TextAlignmentOptions.MidlineLeft;
            nicknameText.color = isSelf ? SelfNicknameColor : NicknameColor;
            nicknameText.overflowMode = TextOverflowModes.Ellipsis;
            nicknameText.raycastTarget = false;

            var ratingObject = new GameObject("Rating");
            ratingObject.transform.SetParent(rowObject.transform, false);
            var ratingLayout = ratingObject.AddComponent<LayoutElement>();
            ratingLayout.preferredWidth = 52f;
            ratingLayout.minWidth = 52f;
            var ratingText = ratingObject.AddComponent<TextMeshProUGUI>();
            ratingText.text = entry.rating.ToString("N0");
            ratingText.fontSize = rowFontSize;
            ratingText.fontStyle = FontStyles.Bold;
            ratingText.alignment = TextAlignmentOptions.MidlineRight;
            ratingText.color = RatingColor;
            ratingText.raycastTarget = false;
        }

        private static Scrollbar CreateVerticalScrollbar(Transform parent)
        {
            var scrollbarObject = new GameObject("Scrollbar");
            scrollbarObject.transform.SetParent(parent, false);

            var scrollbarRect = scrollbarObject.AddComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(8f, 0f);
            scrollbarRect.anchoredPosition = Vector2.zero;

            var trackImage = scrollbarObject.AddComponent<Image>();
            trackImage.sprite = GetWhiteSprite();
            trackImage.type = Image.Type.Simple;
            trackImage.color = ScrollTrackColor;

            var scrollbar = scrollbarObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var handleObject = new GameObject("Handle");
            handleObject.transform.SetParent(scrollbarObject.transform, false);
            var handleRect = handleObject.AddComponent<RectTransform>();
            StretchFull(handleRect);

            var handleImage = handleObject.AddComponent<Image>();
            handleImage.sprite = GetWhiteSprite();
            handleImage.type = Image.Type.Simple;
            handleImage.color = ScrollHandleColor;

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            return scrollbar;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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
