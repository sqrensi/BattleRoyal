using System.Collections;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuNicknameEditor : MonoBehaviour
    {
        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float bottomOffset = 28f;
        [SerializeField] private float panelWidth = 340f;
        [SerializeField] private float fieldHeight = 44f;
        [SerializeField] private float panelPadding = 16f;
        [SerializeField] private float labelFontSize = 16f;
        [SerializeField] private float inputFontSize = 19f;
        [SerializeField] private float statusFontSize = 14f;
        [SerializeField] private float actionButtonWidth = 104f;

        private CanvasGroup canvasGroup;
        private TMP_Text ratingText;
        private TMP_InputField nicknameInput;
        private TMP_Text statusText;
        private TMP_Text actionButtonLabel;
        private Button actionButton;
        private MainMenuController menuController;
        private PlayerProfileApiClient profileApiClient;
        private MainMenuUiSoundController uiSound;
        private Coroutine saveCoroutine;
        private bool built;
        private bool isEditing;

        public CanvasGroup CanvasGroup => canvasGroup;
        public RectTransform RootRect { get; private set; }

        public void Configure(MainMenuController controller, PlayerProfileApiClient apiClient, MainMenuUiSoundController sound)
        {
            menuController = controller;
            profileApiClient = apiClient;
            uiSound = sound;
            RefreshFromProfile();
        }

        public void Build(RectTransform canvasRect, RectTransform stackParent = null)
        {
            if (built || canvasRect == null)
            {
                return;
            }

            var parent = stackParent != null ? stackParent : canvasRect;
            var rootObject = new GameObject("MainMenuNicknameEditor");
            rootObject.transform.SetParent(parent, false);
            RootRect = rootObject.AddComponent<RectTransform>();
            var rootRect = RootRect;
            if (stackParent != null)
            {
                var layoutElement = rootObject.AddComponent<LayoutElement>();
                layoutElement.preferredWidth = panelWidth;
                layoutElement.minWidth = panelWidth;
                rootRect.anchorMin = new Vector2(0f, 1f);
                rootRect.anchorMax = new Vector2(1f, 1f);
                rootRect.pivot = new Vector2(0.5f, 1f);
                rootRect.sizeDelta = Vector2.zero;
            }
            else
            {
                rootRect.anchorMin = new Vector2(1f, 0f);
                rootRect.anchorMax = new Vector2(1f, 0f);
                rootRect.pivot = new Vector2(1f, 0f);
                rootRect.anchoredPosition = new Vector2(-edgeMargin, bottomOffset);
                rootRect.sizeDelta = new Vector2(panelWidth, 0f);
            }

            var background = rootObject.AddComponent<Image>();
            UiTheme.ApplyPanel(background, UiPanelStyle.Standard);

            canvasGroup = rootObject.AddComponent<CanvasGroup>();

            var layout = rootObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperRight;
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

            var fitter = rootObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var ratingLabelObject = new GameObject("RatingLabel");
            ratingLabelObject.transform.SetParent(rootObject.transform, false);
            var ratingLabelLayout = ratingLabelObject.AddComponent<LayoutElement>();
            ratingLabelLayout.preferredHeight = 20f;
            var ratingLabelText = ratingLabelObject.AddComponent<TextMeshProUGUI>();
            ratingLabelText.text = "Рейтинг";
            ratingLabelText.fontSize = labelFontSize;
            ratingLabelText.alignment = TextAlignmentOptions.MidlineLeft;
            UiTheme.ApplyTmp(ratingLabelText, UiTextRole.Label);

            var ratingObject = new GameObject("RatingValue");
            ratingObject.transform.SetParent(rootObject.transform, false);
            var ratingLayout = ratingObject.AddComponent<LayoutElement>();
            ratingLayout.preferredHeight = 28f;
            ratingText = ratingObject.AddComponent<TextMeshProUGUI>();
            ratingText.fontSize = 24f;
            ratingText.alignment = TextAlignmentOptions.MidlineLeft;
            UiTheme.ApplyTmp(ratingText, UiTextRole.Accent);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(rootObject.transform, false);
            var labelLayout = labelObject.AddComponent<LayoutElement>();
            labelLayout.preferredHeight = 20f;
            var labelText = labelObject.AddComponent<TextMeshProUGUI>();
            labelText.text = "Ник";
            labelText.fontSize = labelFontSize;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            UiTheme.ApplyTmp(labelText, UiTextRole.Label);

            var fieldContainer = new GameObject("NicknameField");
            fieldContainer.transform.SetParent(rootObject.transform, false);
            var fieldLayout = fieldContainer.AddComponent<LayoutElement>();
            fieldLayout.preferredHeight = fieldHeight;
            fieldLayout.minHeight = fieldHeight;

            nicknameInput = CreateInputField(fieldContainer.transform, actionButtonWidth + 8f);
            actionButton = CreateActionButton(fieldContainer.transform);
            actionButton.onClick.AddListener(OnActionPressed);
            if (uiSound != null)
            {
                actionButton.onClick.AddListener(uiSound.PlayButton);
            }

            var statusObject = new GameObject("Status");
            statusObject.transform.SetParent(rootObject.transform, false);
            var statusLayout = statusObject.AddComponent<LayoutElement>();
            statusLayout.preferredHeight = 18f;
            statusLayout.minHeight = 18f;
            statusText = statusObject.AddComponent<TextMeshProUGUI>();
            statusText.fontSize = statusFontSize;
            statusText.alignment = TextAlignmentOptions.TopLeft;
            statusText.enableWordWrapping = true;
            statusText.overflowMode = TextOverflowModes.Overflow;
            UiTheme.ApplyTmp(statusText, UiTextRole.Danger);
            statusText.text = string.Empty;

            built = true;
            SetEditMode(false);
            RefreshFromProfile();
        }

        private void OnEnable()
        {
            PlayerProfileService.ProfileSynced += RefreshFromProfile;
            RefreshFromProfile();
        }

        private void OnDisable()
        {
            PlayerProfileService.ProfileSynced -= RefreshFromProfile;
        }

        private void RefreshFromProfile()
        {
            if (nicknameInput == null)
            {
                return;
            }

            RefreshRatingFromProfile();

            if (!isEditing)
            {
                RefreshNicknameFromProfile();
            }

            if (actionButton != null)
            {
                actionButton.interactable = PlayerProfileService.IsServerSynced;
            }
        }

        private void RefreshRatingFromProfile()
        {
            if (ratingText == null)
            {
                return;
            }

            ratingText.text = PlayerProfileService.Rating.ToString("N0");
        }

        private void RefreshNicknameFromProfile()
        {
            var nickname = PlayerProfileService.Nickname;
            if (!string.IsNullOrWhiteSpace(nickname))
            {
                nicknameInput.SetTextWithoutNotify(nickname);
            }
        }

        private void OnActionPressed()
        {
            if (!PlayerProfileService.IsServerSynced || profileApiClient == null)
            {
                SetStatus("Сервер недоступен — никнейм нельзя изменить.", UiTheme.Danger);
                return;
            }

            if (isEditing)
            {
                OnSavePressed();
                return;
            }

            SetEditMode(true);
        }

        private void SetEditMode(bool editing)
        {
            isEditing = editing;
            if (nicknameInput != null)
            {
                nicknameInput.interactable = editing;
            }

            if (actionButtonLabel != null)
            {
                actionButtonLabel.text = editing ? "Сохранить" : "Изменить";
            }

            if (editing)
            {
                ClearStatus();
                nicknameInput?.Select();
                nicknameInput?.ActivateInputField();
                return;
            }

            RefreshNicknameFromProfile();
        }

        private string ResolvePlayerId()
        {
            return PlayerIdentityService.GetOrCreatePlayerId();
        }

        private MonoBehaviour ResolveCoroutineRunner()
        {
            if (menuController != null)
            {
                return menuController;
            }

            return this;
        }

        private void OnSavePressed()
        {
            if (!PlayerProfileService.IsServerSynced || profileApiClient == null)
            {
                SetStatus("Сервер недоступен — никнейм нельзя изменить.", UiTheme.Danger);
                return;
            }

            if (saveCoroutine != null)
            {
                StopCoroutine(saveCoroutine);
            }

            saveCoroutine = StartCoroutine(SaveNicknameRoutine());
        }

        private IEnumerator SaveNicknameRoutine()
        {
            if (actionButton != null)
            {
                actionButton.interactable = false;
            }

            var trimmed = nicknameInput != null ? nicknameInput.text.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                SetStatus("Введите никнейм.", UiTheme.Danger);
                if (actionButton != null)
                {
                    actionButton.interactable = PlayerProfileService.IsServerSynced;
                }

                yield break;
            }

            if (string.Equals(trimmed, PlayerProfileService.Nickname, System.StringComparison.OrdinalIgnoreCase))
            {
                SetEditMode(false);
                if (actionButton != null)
                {
                    actionButton.interactable = PlayerProfileService.IsServerSynced;
                }

                yield break;
            }

            var completed = false;
            var success = false;
            var error = string.Empty;

            yield return PlayerProfileService.SetNickname(
                ResolveCoroutineRunner(),
                profileApiClient,
                ResolvePlayerId(),
                trimmed,
                (ok, responseError) =>
                {
                    completed = true;
                    success = ok;
                    error = responseError;
                });

            if (actionButton != null)
            {
                actionButton.interactable = PlayerProfileService.IsServerSynced;
            }

            if (!completed)
            {
                yield break;
            }

            if (success)
            {
                SetEditMode(false);
                nicknameInput.SetTextWithoutNotify(PlayerProfileService.Nickname);
                SetStatus("Никнейм сохранён.", UiTheme.Success);
                yield break;
            }

            SetStatus(string.IsNullOrWhiteSpace(error) ? "Не удалось сохранить никнейм." : error, UiTheme.Danger);
        }

        private void ClearStatus()
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text = string.Empty;
        }

        private void SetStatus(string message, Color color)
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text = message ?? string.Empty;
            statusText.color = color;
        }

        private TMP_InputField CreateInputField(Transform parent, float rightPadding)
        {
            var inputObject = new GameObject("NicknameInput");
            inputObject.transform.SetParent(parent, false);

            var inputRect = inputObject.AddComponent<RectTransform>();
            StretchFull(inputRect);

            var background = inputObject.AddComponent<Image>();
            UiTheme.ApplyFlatFill(background, UiTheme.SlotFill);

            var inputField = inputObject.AddComponent<TMP_InputField>();
            inputField.characterLimit = 16;
            inputField.interactable = false;

            var textAreaObject = new GameObject("Text Area");
            textAreaObject.transform.SetParent(inputObject.transform, false);
            var textAreaRect = textAreaObject.AddComponent<RectTransform>();
            StretchFull(textAreaRect);
            textAreaRect.offsetMin = new Vector2(12f, 7f);
            textAreaRect.offsetMax = new Vector2(-rightPadding, -7f);
            textAreaObject.AddComponent<RectMask2D>();

            var placeholderObject = new GameObject("Placeholder");
            placeholderObject.transform.SetParent(textAreaObject.transform, false);
            var placeholderRect = placeholderObject.AddComponent<RectTransform>();
            StretchFull(placeholderRect);
            var placeholder = placeholderObject.AddComponent<TextMeshProUGUI>();
            placeholder.text = "Введите никнейм";
            placeholder.fontSize = inputFontSize;
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            UiTheme.ApplyTmp(placeholder, UiTextRole.Muted);
            placeholder.color = new Color(UiTheme.TextMuted.r, UiTheme.TextMuted.g, UiTheme.TextMuted.b, 0.72f);

            var textObject = new GameObject("Text");
            textObject.transform.SetParent(textAreaObject.transform, false);
            var textRect = textObject.AddComponent<RectTransform>();
            StretchFull(textRect);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = inputFontSize;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            UiTheme.ApplyTmp(text, UiTextRole.Body);

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            inputField.lineType = TMP_InputField.LineType.SingleLine;

            return inputField;
        }

        private Button CreateActionButton(Transform parent)
        {
            var buttonObject = new GameObject("ActionButton");
            buttonObject.transform.SetParent(parent, false);

            var buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.sizeDelta = new Vector2(actionButtonWidth, 0f);
            buttonRect.anchoredPosition = new Vector2(-4f, 0f);

            buttonObject.AddComponent<Image>();

            var button = buttonObject.AddComponent<Button>();
            UiTheme.StyleButton(button, UiButtonStyle.Primary);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);
            actionButtonLabel = labelObject.AddComponent<TextMeshProUGUI>();
            actionButtonLabel.text = "Изменить";
            actionButtonLabel.fontSize = 15f;
            actionButtonLabel.alignment = TextAlignmentOptions.Center;
            UiTheme.ApplyTmp(actionButtonLabel, UiTextRole.PrimaryButton);

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
