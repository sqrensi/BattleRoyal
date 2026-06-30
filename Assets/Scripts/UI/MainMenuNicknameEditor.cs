using System.Collections;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuNicknameEditor : MonoBehaviour
    {
        private const float TextAreaLeftPadding = 12f;
        private const float CaretVisibleSeconds = 0.55f;
        private const float CaretHiddenSeconds = 0.45f;

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
        private TMP_Text ratingLabelText;
        private TMP_Text ratingText;
        private TMP_InputField nicknameInput;
        private TMP_Text statusText;
        private TMP_Text actionButtonLabel;
        private Button actionButton;
        private MainMenuController menuController;
        private PlayerProfileApiClient profileApiClient;
        private MainMenuUiSoundController uiSound;
        private Coroutine saveCoroutine;
        private RectTransform customCaretRect;
        private Image customCaretImage;
        private TMP_Text nicknameInputText;
        private bool built;
        private bool isEditing;
        private bool caretVisible = true;
        private float caretBlinkPhaseStart;
        private int lastCaretStringPosition = -1;

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
            var ratingLabelTextComponent = ratingLabelObject.AddComponent<TextMeshProUGUI>();
            ratingLabelText = ratingLabelTextComponent;
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
            HideCaret();
        }

        private void Update()
        {
            if (!isEditing || customCaretImage == null || !customCaretImage.enabled)
            {
                return;
            }

            var cycleSeconds = CaretVisibleSeconds + CaretHiddenSeconds;
            var phase = (Time.unscaledTime - caretBlinkPhaseStart) % cycleSeconds;
            caretVisible = phase < CaretVisibleSeconds;
            var alpha = caretVisible ? 1f : 0f;
            if (!Mathf.Approximately(customCaretImage.color.a, alpha))
            {
                customCaretImage.color = new Color(1f, 1f, 1f, alpha);
            }
        }

        private void LateUpdate()
        {
            if (!isEditing || nicknameInput == null || customCaretImage == null || !customCaretImage.enabled)
            {
                return;
            }

            var stringPosition = nicknameInput.stringPosition;
            if (stringPosition != lastCaretStringPosition)
            {
                lastCaretStringPosition = stringPosition;
                caretBlinkPhaseStart = Time.unscaledTime;
                caretVisible = true;
                customCaretImage.color = Color.white;
            }

            UpdateCustomCaretPosition();
        }

        public void RefreshFromProfile()
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

            var mode = MainMenuGameModeSelector.SelectedMode;
            var showDuelRating = mode == MainMenuGameMode.Duel1v1;

            if (ratingLabelText != null)
            {
                ratingLabelText.gameObject.SetActive(showDuelRating);
                if (showDuelRating)
                {
                    ratingLabelText.text = "Рейтинг 1v1";
                }
            }

            ratingText.gameObject.SetActive(showDuelRating);
            if (!showDuelRating)
            {
                return;
            }

            ratingText.text = PlayerProfileService.IsServerSynced
                ? PlayerProfileService.DuelRating.ToString("N0")
                : "—";
        }

        private static string FormatChallengeBestTime(int timeMs)
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
                lastCaretStringPosition = -1;
                nicknameInput?.Select();
                nicknameInput?.ActivateInputField();
                if (nicknameInput != null)
                {
                    nicknameInput.MoveTextEnd(false);
                }

                ShowCaret();
                return;
            }

            HideCaret();
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
            inputField.resetOnDeActivation = false;

            var textAreaObject = new GameObject("Text Area");
            textAreaObject.transform.SetParent(inputObject.transform, false);
            var textAreaRect = textAreaObject.AddComponent<RectTransform>();
            StretchFull(textAreaRect);
            textAreaRect.offsetMin = new Vector2(TextAreaLeftPadding, 7f);
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
            inputField.customCaretColor = true;
            inputField.caretColor = new Color(1f, 1f, 1f, 0f);
            inputField.caretBlinkRate = 0f;
            inputField.caretWidth = 1;
            inputField.selectionColor = new Color(1f, 1f, 1f, 0.22f);

            nicknameInputText = text;
            CreateCustomCaret(inputObject.transform);

            return inputField;
        }

        private void CreateCustomCaret(Transform inputRoot)
        {
            var caretObject = new GameObject("CustomCaret");
            caretObject.transform.SetParent(inputRoot, false);
            caretObject.transform.SetAsLastSibling();

            customCaretRect = caretObject.AddComponent<RectTransform>();
            customCaretRect.anchorMin = new Vector2(0f, 0.5f);
            customCaretRect.anchorMax = new Vector2(0f, 0.5f);
            customCaretRect.pivot = new Vector2(0f, 0.5f);
            customCaretRect.anchoredPosition = new Vector2(TextAreaLeftPadding, 0f);
            customCaretRect.sizeDelta = new Vector2(3f, Mathf.Max(22f, inputFontSize + 4f));

            customCaretImage = caretObject.AddComponent<Image>();
            customCaretImage.sprite = UiTheme.WhiteSprite;
            customCaretImage.type = Image.Type.Simple;
            customCaretImage.color = Color.white;
            customCaretImage.raycastTarget = false;
            customCaretImage.maskable = false;
            customCaretImage.enabled = false;
        }

        private void ShowCaret()
        {
            if (customCaretImage == null)
            {
                return;
            }

            customCaretImage.enabled = true;
            customCaretImage.color = Color.white;
            customCaretImage.transform.SetAsLastSibling();
            caretVisible = true;
            caretBlinkPhaseStart = Time.unscaledTime;
            UpdateCustomCaretPosition();
        }

        private void HideCaret()
        {
            if (customCaretImage != null)
            {
                customCaretImage.enabled = false;
            }
        }

        private void UpdateCustomCaretPosition()
        {
            if (!isEditing || nicknameInput == null || customCaretRect == null || nicknameInputText == null)
            {
                return;
            }

            var caretIndex = Mathf.Clamp(nicknameInput.stringPosition, 0, nicknameInput.text.Length);
            var textBeforeCaret = nicknameInput.text.Substring(0, caretIndex);
            nicknameInputText.ForceMeshUpdate();
            var textOffset = ResolveCaretOffsetX(nicknameInputText, textBeforeCaret, caretIndex);
            customCaretRect.anchoredPosition = new Vector2(TextAreaLeftPadding + textOffset, 0f);
        }

        private static float ResolveCaretOffsetX(TMP_Text text, string textBeforeCaret, int caretIndex)
        {
            if (text == null)
            {
                return 0f;
            }

            if (string.IsNullOrEmpty(textBeforeCaret))
            {
                return 0f;
            }

            var preferred = text.GetPreferredValues(textBeforeCaret);
            if (preferred.x > 0.01f)
            {
                return preferred.x;
            }

            var textInfo = text.textInfo;
            if (textInfo == null || textInfo.characterCount == 0)
            {
                return 0f;
            }

            if (caretIndex >= textInfo.characterCount)
            {
                var last = textInfo.characterInfo[textInfo.characterCount - 1];
                return last.origin + last.xAdvance;
            }

            return textInfo.characterInfo[Mathf.Clamp(caretIndex, 0, textInfo.characterCount - 1)].origin;
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
