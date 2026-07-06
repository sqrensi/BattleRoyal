using System.Collections;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuDailyRewardPrompt : MonoBehaviour
    {
        private GameObject overlayRoot;
        private CanvasGroup overlayGroup;
        private TMP_Text rewardTitle;
        private MainMenuUiSoundController uiSound;
        private bool built;

        public void Configure(MainMenuUiSoundController sound)
        {
            uiSound = sound;
        }

        public void TryShow(RectTransform canvasRect)
        {
            if (!DailyRewardService.ShouldShowLoginPrompt())
            {
                return;
            }

            EnsureBuilt(canvasRect);
            RefreshRewardPreview();
            DailyRewardService.MarkPromptShownThisSession();
            overlayGroup.alpha = 1f;
            overlayGroup.interactable = true;
            overlayGroup.blocksRaycasts = true;
            overlayRoot.SetActive(true);
        }

        private void EnsureBuilt(RectTransform canvasRect)
        {
            if (built || canvasRect == null)
            {
                return;
            }

            overlayRoot = new GameObject("MainMenuDailyRewardPrompt");
            overlayRoot.transform.SetParent(canvasRect, false);
            var rootRect = overlayRoot.AddComponent<RectTransform>();
            StretchFull(rootRect);

            var dim = overlayRoot.AddComponent<Image>();
            UiTheme.ApplyFlatFill(dim, UiTheme.CanvasDim);
            dim.raycastTarget = true;

            overlayGroup = overlayRoot.AddComponent<CanvasGroup>();

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(overlayRoot.transform, false);
            var panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(620f, 420f);

            var panelImage = panelObject.AddComponent<Image>();
            UiTheme.ApplyPanel(panelImage, UiPanelStyle.Heavy);

            var titleObject = new GameObject("Title");
            titleObject.transform.SetParent(panelObject.transform, false);
            var titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -24f);
            titleRect.sizeDelta = new Vector2(540f, 48f);
            var titleText = titleObject.AddComponent<TextMeshProUGUI>();
            titleText.text = "ЕЖЕДНЕВНАЯ НАГРАДА";
            titleText.fontSize = 32f;
            titleText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyMilitaryHeader(titleText, UiTextRole.Title);

            var subtitleObject = new GameObject("Subtitle");
            subtitleObject.transform.SetParent(panelObject.transform, false);
            var subtitleRect = subtitleObject.AddComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0.5f, 1f);
            subtitleRect.anchorMax = new Vector2(0.5f, 1f);
            subtitleRect.pivot = new Vector2(0.5f, 1f);
            subtitleRect.anchoredPosition = new Vector2(0f, -78f);
            subtitleRect.sizeDelta = new Vector2(540f, 32f);
            var subtitleText = subtitleObject.AddComponent<TextMeshProUGUI>();
            subtitleText.text = "Заходи каждый день и получай награды";
            subtitleText.fontSize = 18f;
            subtitleText.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(subtitleText, UiTextRole.Muted);

            var rewardObject = new GameObject("RewardPreview");
            rewardObject.transform.SetParent(panelObject.transform, false);
            var rewardRect = rewardObject.AddComponent<RectTransform>();
            rewardRect.anchorMin = new Vector2(0.5f, 0.5f);
            rewardRect.anchorMax = new Vector2(0.5f, 0.5f);
            rewardRect.pivot = new Vector2(0.5f, 0.5f);
            rewardRect.sizeDelta = new Vector2(480f, 140f);
            rewardRect.anchoredPosition = new Vector2(0f, 12f);
            var rewardBg = rewardObject.AddComponent<Image>();
            UiTheme.ApplyPanel(rewardBg, UiPanelStyle.Standard);

            var rewardLabelObject = new GameObject("RewardLabel");
            rewardLabelObject.transform.SetParent(rewardObject.transform, false);
            var rewardLabelRect = rewardLabelObject.AddComponent<RectTransform>();
            StretchFull(rewardLabelRect);
            rewardLabelRect.offsetMin = new Vector2(20f, 16f);
            rewardLabelRect.offsetMax = new Vector2(-20f, -16f);
            rewardTitle = rewardLabelObject.AddComponent<TextMeshProUGUI>();
            rewardTitle.fontSize = 28f;
            rewardTitle.alignment = TextAlignmentOptions.Center;
            rewardTitle.enableWordWrapping = true;
            UiTheme.ApplyMilitaryHeader(rewardTitle, UiTextRole.Accent);

            var claimButton = CreateButton(panelObject.transform, "ЗАБРАТЬ", new Vector2(0f, 72f), primary: true);
            claimButton.onClick.AddListener(OnClaimClicked);
            if (uiSound != null)
            {
                claimButton.onClick.AddListener(uiSound.PlayStart);
            }

            var closeButton = CreateButton(panelObject.transform, "ЗАКРЫТЬ", new Vector2(0f, 16f), primary: false);
            closeButton.onClick.AddListener(Hide);
            if (uiSound != null)
            {
                closeButton.onClick.AddListener(uiSound.PlayButton);
            }

            built = true;
            overlayRoot.SetActive(false);
        }

        private void RefreshRewardPreview()
        {
            var index = DailyRewardService.GetNextClaimGlobalIndex();
            if (DailyRewardCatalogService.TryGetReward(index, out var reward))
            {
                rewardTitle.text = DailyRewardCatalogService.FormatRewardTitle(reward);
                return;
            }

            rewardTitle.text = "Награда дня";
        }

        private void OnClaimClicked()
        {
            StartCoroutine(ClaimDailyRewardRoutine());
        }

        private IEnumerator ClaimDailyRewardRoutine()
        {
            var playerId = PlayerIdentityService.GetOrCreatePlayerId();
            var completed = false;
            var success = false;
            var error = string.Empty;
            DailyRewardEntry claimedReward = null;

            yield return PlayerProgressSyncService.ClaimDailyRewardRoutine(
                this,
                null,
                playerId,
                (ok, reward, message) =>
                {
                    completed = true;
                    success = ok;
                    claimedReward = reward;
                    error = message;
                });

            if (!completed || !success)
            {
                if (!string.IsNullOrWhiteSpace(error) && rewardTitle != null)
                {
                    rewardTitle.text = error;
                }

                yield break;
            }

            uiSound?.PlayStart();
            MainMenuDailyRewardsPanel.NotifyClaimedReward(claimedReward);
            MatchAchievementReporter.ReportEvent(this, AchievementEventTypes.DailyRewardClaim, 1);
            DailyRewardService.MarkLoginPromptShown();
            FindFirstObjectByType<MainMenuController>()?.ScheduleProgressFlush();
            Hide();
        }

        private void Hide()
        {
            if (overlayGroup == null || overlayRoot == null)
            {
                return;
            }

            DailyRewardService.MarkLoginPromptShown();
            FindFirstObjectByType<MainMenuController>()?.ScheduleProgressFlush();
            overlayGroup.alpha = 0f;
            overlayGroup.interactable = false;
            overlayGroup.blocksRaycasts = false;
            overlayRoot.SetActive(false);
        }

        private static Button CreateButton(Transform parent, string label, Vector2 bottom, bool primary)
        {
            var buttonObject = new GameObject(label + "Button");
            buttonObject.transform.SetParent(parent, false);
            var rect = buttonObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = bottom;
            rect.sizeDelta = new Vector2(260f, 48f);

            var image = buttonObject.AddComponent<Image>();
            var button = buttonObject.AddComponent<Button>();
            UiTheme.StyleButton(button, primary ? UiButtonStyle.Primary : UiButtonStyle.Standard);
            image.sprite = primary ? UiTheme.PrimaryButtonSprite : UiTheme.ButtonSprite;
            image.type = Image.Type.Sliced;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);
            var text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 18f;
            text.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyMilitaryHeader(text, primary ? UiTextRole.PrimaryButton : UiTextRole.Body);
            return button;
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
