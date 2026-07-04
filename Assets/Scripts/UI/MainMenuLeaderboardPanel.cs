using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Matchmaking;
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
        private TMP_Text titleText;
        private MainMenuController menuController;
        private PlayerProfileApiClient profileApiClient;
        private Coroutine fetchCoroutine;
        private bool built;
        private MainMenuGameMode leaderboardMode = MainMenuGameMode.Duel1v1;

        public RectTransform RootRect { get; private set; }

        public void Configure(MainMenuController controller, PlayerProfileApiClient apiClient)
        {
            menuController = controller;
            profileApiClient = apiClient;
        }

        public void SetLeaderboardMode(MainMenuGameMode mode)
        {
            leaderboardMode = mode;
            if (titleText != null)
            {
                titleText.text = ResolveTitle(mode);
            }

            if (UsesEmptyLeaderboardPlaceholder)
            {
                RenderEmptyLeaderboardPlaceholder();
                return;
            }

            RequestRefresh();
        }

        private bool UsesEmptyLeaderboardPlaceholder => false;

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
            var titleTextComponent = titleObject.AddComponent<TextMeshProUGUI>();
            titleText = titleTextComponent;
            titleText.text = ResolveTitle(leaderboardMode);
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

            if (UsesEmptyLeaderboardPlaceholder)
            {
                RenderEmptyLeaderboardPlaceholder();
                yield break;
            }

            if (!PlayerProfileService.IsServerSynced)
            {
                RenderEntries(System.Array.Empty<LeaderboardEntryDto>());
                yield break;
            }

            if (profileApiClient == null)
            {
                ShowMessage("Сервер недоступен.");
                RenderEntries(System.Array.Empty<LeaderboardEntryDto>());
                yield break;
            }

            var completed = false;
            var success = false;
            LeaderboardEntryDto[] entries = null;
            var error = string.Empty;

            yield return profileApiClient.FetchLeaderboard(
                entryLimit,
                MainMenuGameModeUtility.GetLeaderboardModeKey(leaderboardMode),
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
                MaybeCreatePinnedSelfRow(null);
                return;
            }

            MaybeCreatePinnedSelfRow(entries[0]);

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

        private static string ResolveTitle(MainMenuGameMode mode)
        {
            return mode switch
            {
                MainMenuGameMode.Training => "Топ 25 (тренировка)",
                MainMenuGameMode.Duel1v1 => "Топ 25 (1v1)",
                MainMenuGameMode.Challenge => "Топ 25 (челлендж)",
                MainMenuGameMode.Deathmatch => "Топ 25 (Бой насмерть)",
                _ => "Топ 25 (BR)",
            };
        }

        private bool IsTrainingMode => leaderboardMode == MainMenuGameMode.Training;

        private bool IsDeathmatchMode => leaderboardMode == MainMenuGameMode.Deathmatch;

        private void RenderEmptyLeaderboardPlaceholder()
        {
            ClearContent();
            for (var rank = 1; rank <= 5; rank++)
            {
                CreateDashEntryRow(rank);
            }
        }

        private void CreateDashEntryRow(int rank)
        {
            var rowObject = new GameObject("Entry_" + rank);
            rowObject.transform.SetParent(contentRect, false);

            var rowLayout = rowObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = rowHeight;
            rowLayout.minHeight = rowHeight;

            var background = rowObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.SlotFill);
            background.raycastTarget = false;

            var horizontal = rowObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.padding = new RectOffset(8, 8, 0, 0);
            horizontal.spacing = 6f;
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;

            CreateDashCell(rowObject.transform, rank.ToString(), 28f, TextAlignmentOptions.MidlineRight, UiTextRole.Muted);
            CreateDashCell(rowObject.transform, "—", 0f, TextAlignmentOptions.MidlineLeft, UiTextRole.Body, flexible: true);
            CreateDashCell(rowObject.transform, "—", 52f, TextAlignmentOptions.MidlineRight, UiTextRole.Muted);
        }

        private void CreateDashCell(
            Transform parent,
            string text,
            float width,
            TextAlignmentOptions alignment,
            UiTextRole role,
            bool flexible = false)
        {
            var cellObject = new GameObject("Cell");
            cellObject.transform.SetParent(parent, false);
            var cellLayout = cellObject.AddComponent<LayoutElement>();
            if (flexible)
            {
                cellLayout.flexibleWidth = 1f;
            }
            else
            {
                cellLayout.preferredWidth = width;
                cellLayout.minWidth = width;
            }

            var cellText = cellObject.AddComponent<TextMeshProUGUI>();
            cellText.text = text;
            cellText.fontSize = rowFontSize;
            cellText.alignment = alignment;
            cellText.enableWordWrapping = false;
            cellText.overflowMode = TextOverflowModes.Overflow;
            UiTheme.ApplyTmp(cellText, role);
        }

        private bool IsChallengeMode => leaderboardMode == MainMenuGameMode.Challenge;

        private void MaybeCreatePinnedSelfRow(LeaderboardEntryDto topEntry)
        {
            if (!ShouldPinSelfAboveTop(topEntry))
            {
                return;
            }

            var pinned = BuildPinnedSelfEntry();
            if (pinned != null)
            {
                CreateEntryRow(pinned, true);
            }
        }

        private bool ShouldPinSelfAboveTop(LeaderboardEntryDto topEntry)
        {
            if (IsTrainingMode || IsDeathmatchMode)
            {
                return false;
            }

            var selfValue = ResolveSelfSortValue();
            if (selfValue < 0)
            {
                return false;
            }

            if (topEntry == null)
            {
                return true;
            }

            if (IsChallengeMode)
            {
                var topTime = topEntry.challengeTimeMs > 0 ? topEntry.challengeTimeMs : topEntry.rating;
                return topTime <= 0 || selfValue < topTime;
            }

            return selfValue > topEntry.rating;
        }

        private int ResolveSelfSortValue()
        {
            return leaderboardMode switch
            {
                MainMenuGameMode.Duel1v1 => PlayerProfileService.DuelRating,
                MainMenuGameMode.Challenge => PlayerProfileService.ChallengeBestTimeMs,
                _ => -1,
            };
        }

        private LeaderboardEntryDto BuildPinnedSelfEntry()
        {
            var selfValue = ResolveSelfSortValue();
            if (selfValue < 0)
            {
                return null;
            }

            return new LeaderboardEntryDto
            {
                rank = 0,
                nickname = string.IsNullOrWhiteSpace(PlayerProfileService.Nickname)
                    ? "Вы"
                    : PlayerProfileService.Nickname.Trim(),
                playerId = PlayerIdentityService.GetOrCreatePlayerId(),
                rating = IsChallengeMode ? 0 : selfValue,
                challengeTimeMs = IsChallengeMode ? selfValue : -1,
            };
        }

        private static string FormatTrainingTime(int totalSeconds)
        {
            if (totalSeconds <= 0)
            {
                return "—";
            }

            var hours = totalSeconds / 3600;
            var minutes = (totalSeconds % 3600) / 60;
            var seconds = totalSeconds % 60;
            return hours > 0
                ? $"{hours}:{minutes:00}:{seconds:00}"
                : $"{minutes}:{seconds:00}";
        }

        private static string FormatDeathmatchKd(LeaderboardEntryDto entry)
        {
            if (entry == null)
            {
                return "—";
            }

            if (entry.kdRatio > 0.001f)
            {
                return entry.kdRatio.ToString("0.0");
            }

            if (entry.deaths > 0)
            {
                return (entry.kills / (float)entry.deaths).ToString("0.0");
            }

            return entry.kills > 0 ? entry.kills.ToString("0.0") : "0.0";
        }

        private static string FormatChallengeTime(int timeMs)
        {
            if (timeMs < 0)
            {
                return "—";
            }

            var totalSeconds = timeMs / 1000f;
            var minutes = Mathf.FloorToInt(totalSeconds / 60f);
            var seconds = Mathf.FloorToInt(totalSeconds % 60f);
            var tenths = Mathf.FloorToInt((totalSeconds - Mathf.Floor(totalSeconds)) * 10f);
            return $"{minutes}:{seconds:00}.{tenths}";
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
            rankText.text = entry.rank > 0 ? entry.rank.ToString() : "—";
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

            if (IsDeathmatchMode)
            {
                CreateStatCell(rowObject.transform, entry.kills.ToString(), 40f);
                CreateStatCell(rowObject.transform, FormatDeathmatchKd(entry), 44f);
                return;
            }

            var ratingObject = new GameObject("Rating");
            ratingObject.transform.SetParent(rowObject.transform, false);
            var ratingLayout = ratingObject.AddComponent<LayoutElement>();
            ratingLayout.preferredWidth = IsChallengeMode || IsTrainingMode ? 72f : 52f;
            ratingLayout.minWidth = IsChallengeMode || IsTrainingMode ? 72f : 52f;
            var ratingText = ratingObject.AddComponent<TextMeshProUGUI>();
            ratingText.text = IsChallengeMode
                ? FormatChallengeTime(entry.challengeTimeMs > 0 ? entry.challengeTimeMs : entry.rating)
                : IsTrainingMode
                    ? FormatTrainingTime(entry.trainingTimeSeconds > 0 ? entry.trainingTimeSeconds : entry.rating)
                    : entry.rating.ToString("N0");
            ratingText.fontSize = rowFontSize;
            ratingText.alignment = TextAlignmentOptions.MidlineRight;
            ratingText.enableWordWrapping = false;
            ratingText.overflowMode = TextOverflowModes.Overflow;
            UiTheme.ApplyTmp(ratingText, UiTextRole.Accent);
        }

        private void CreateStatCell(Transform parent, string text, float width)
        {
            var cellObject = new GameObject("Stat");
            cellObject.transform.SetParent(parent, false);
            var cellLayout = cellObject.AddComponent<LayoutElement>();
            cellLayout.preferredWidth = width;
            cellLayout.minWidth = width;
            var cellText = cellObject.AddComponent<TextMeshProUGUI>();
            cellText.text = text ?? string.Empty;
            cellText.fontSize = rowFontSize;
            cellText.alignment = TextAlignmentOptions.MidlineRight;
            cellText.enableWordWrapping = false;
            cellText.overflowMode = TextOverflowModes.Overflow;
            UiTheme.ApplyTmp(cellText, UiTextRole.Accent);
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
