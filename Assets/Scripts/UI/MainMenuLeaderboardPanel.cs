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
        [SerializeField] private float panelWidth = 340f;
        [SerializeField] private float panelPadding = 14f;
        [SerializeField] private float scrollHeight = 280f;
        [SerializeField] private float rowHeight = 24f;
        [SerializeField] private float rowSpacing = 4f;
        [SerializeField] private float titleFontSize = 18f;
        [SerializeField] private float rowFontSize = 14f;
        [SerializeField] private float scrollbarWidth = 10f;
        [SerializeField] private float scrollbarGap = 10f;
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
            UiTheme.ApplyPanel(background, UiPanelStyle.Standard);
            background.raycastTarget = false;

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
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
            UiTheme.ApplyTmp(titleText, UiTextRole.Heading);

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
            MainMenuScrollSupport.ConfigureVerticalScroll(scroll);

            var viewportObject = new GameObject("Viewport");
            viewportObject.transform.SetParent(scrollObject.transform, false);
            var viewportRect = viewportObject.AddComponent<RectTransform>();
            StretchFull(viewportRect);
            viewportRect.offsetMax = new Vector2(-(scrollbarWidth + scrollbarGap), 0f);
            viewportObject.AddComponent<RectMask2D>();
            MainMenuScrollSupport.EnableViewportScrollCapture(viewportObject, UiTheme.WhiteSprite);

            var scrollbar = MainMenuScrollSupport.CreateVerticalScrollbar(
                scrollObject.transform,
                scrollbarWidth,
                UiTheme.ScrollTrack,
                UiTheme.ScrollHandle,
                UiTheme.WhiteSprite);
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
            MainMenuScrollSupport.EnableContentScrollCapture(contentObject, UiTheme.WhiteSprite);

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
            UiTheme.ApplyTmp(text, UiTextRole.Muted);
        }

        private void CreateEntryRow(LeaderboardEntryDto entry, bool isSelf)
        {
            var rowObject = new GameObject("Entry_" + entry.rank);
            rowObject.transform.SetParent(contentRect, false);

            var rowLayout = rowObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = rowHeight;
            rowLayout.minHeight = rowHeight;

            var background = rowObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, isSelf ? UiTheme.SlotHighlight : UiTheme.SlotFill);
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
            rankText.alignment = TextAlignmentOptions.MidlineRight;
            UiTheme.ApplyTmp(rankText, UiTextRole.Muted);

            var nicknameObject = new GameObject("Nickname");
            nicknameObject.transform.SetParent(rowObject.transform, false);
            var nicknameLayout = nicknameObject.AddComponent<LayoutElement>();
            nicknameLayout.flexibleWidth = 1f;
            var nicknameText = nicknameObject.AddComponent<TextMeshProUGUI>();
            nicknameText.text = string.IsNullOrWhiteSpace(entry.nickname) ? "Игрок" : entry.nickname.Trim();
            nicknameText.fontSize = rowFontSize;
            nicknameText.fontStyle = isSelf ? FontStyles.Bold : FontStyles.Normal;
            nicknameText.alignment = TextAlignmentOptions.MidlineLeft;
            nicknameText.overflowMode = TextOverflowModes.Ellipsis;
            UiTheme.ApplyTmp(nicknameText, isSelf ? UiTextRole.Accent : UiTextRole.Body);

            var ratingObject = new GameObject("Rating");
            ratingObject.transform.SetParent(rowObject.transform, false);
            var ratingLayout = ratingObject.AddComponent<LayoutElement>();
            ratingLayout.preferredWidth = 52f;
            ratingLayout.minWidth = 52f;
            var ratingText = ratingObject.AddComponent<TextMeshProUGUI>();
            ratingText.text = entry.rating.ToString("N0");
            ratingText.fontSize = rowFontSize;
            ratingText.alignment = TextAlignmentOptions.MidlineRight;
            UiTheme.ApplyTmp(ratingText, UiTextRole.Accent);
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
