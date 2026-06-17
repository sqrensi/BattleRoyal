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
        private static Sprite whiteSprite;

        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.1f, 0.62f);
        private static readonly Color InputColor = new Color(0.1f, 0.12f, 0.15f, 0.92f);
        private static readonly Color LabelColor = new Color(0.94f, 0.96f, 0.98f, 0.98f);
        private static readonly Color StatusOkColor = new Color(0.62f, 0.9f, 0.72f, 0.95f);
        private static readonly Color StatusErrorColor = new Color(0.98f, 0.58f, 0.58f, 0.95f);
        private static readonly Color StatusHintColor = new Color(0.82f, 0.88f, 0.92f, 0.88f);
        private static readonly Color ButtonNormalColor = new Color(0.18f, 0.48f, 0.42f, 0.96f);
        private static readonly Color ButtonHighlightedColor = new Color(0.22f, 0.58f, 0.5f, 1f);
        private static readonly Color ButtonPressedColor = new Color(0.14f, 0.38f, 0.34f, 1f);

        [SerializeField] private float edgeMargin = 28f;
        [SerializeField] private float bottomOffset = 28f;
        [SerializeField] private float panelWidth = 380f;
        [SerializeField] private float rowHeight = 44f;
        [SerializeField] private float panelPadding = 14f;
        [SerializeField] private float rowSpacing = 8f;
        [SerializeField] private float labelFontSize = 17f;
        [SerializeField] private float inputFontSize = 19f;
        [SerializeField] private float statusFontSize = 15f;
        [SerializeField] private float buttonWidth = 112f;

        private CanvasGroup canvasGroup;
        private TMP_InputField nicknameInput;
        private TMP_Text statusText;
        private Button saveButton;
        private MainMenuController menuController;
        private PlayerProfileApiClient profileApiClient;
        private MainMenuUiSoundController uiSound;
        private Coroutine saveCoroutine;
        private Coroutine validateCoroutine;
        private bool built;

        public CanvasGroup CanvasGroup => canvasGroup;

        public void Configure(MainMenuController controller, PlayerProfileApiClient apiClient, MainMenuUiSoundController sound)
        {
            menuController = controller;
            profileApiClient = apiClient;
            uiSound = sound;
            RefreshFromProfile();
        }

        public void Build(RectTransform canvasRect)
        {
            if (built || canvasRect == null)
            {
                return;
            }

            var rootObject = new GameObject("MainMenuNicknameEditor");
            rootObject.transform.SetParent(canvasRect, false);

            var rootRect = rootObject.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 0f);
            rootRect.anchorMax = new Vector2(1f, 0f);
            rootRect.pivot = new Vector2(1f, 0f);
            rootRect.anchoredPosition = new Vector2(-edgeMargin, bottomOffset);
            rootRect.sizeDelta = new Vector2(panelWidth, 0f);

            var background = rootObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.type = Image.Type.Simple;
            background.color = PanelColor;
            background.raycastTarget = true;

            canvasGroup = rootObject.AddComponent<CanvasGroup>();

            var layout = rootObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerRight;
            layout.spacing = rowSpacing;
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

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(rootObject.transform, false);
            var labelLayout = labelObject.AddComponent<LayoutElement>();
            labelLayout.preferredHeight = 22f;
            var labelText = labelObject.AddComponent<TextMeshProUGUI>();
            labelText.text = "Никнейм";
            labelText.fontSize = labelFontSize;
            labelText.fontStyle = FontStyles.Bold;
            labelText.alignment = TextAlignmentOptions.MidlineRight;
            labelText.color = LabelColor;
            labelText.raycastTarget = false;

            var rowObject = new GameObject("InputRow");
            rowObject.transform.SetParent(rootObject.transform, false);
            var rowLayout = rowObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleRight;
            rowLayout.spacing = rowSpacing;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            var rowElement = rowObject.AddComponent<LayoutElement>();
            rowElement.preferredHeight = rowHeight;
            rowElement.minHeight = rowHeight;

            nicknameInput = CreateInputField(rowObject.transform);
            nicknameInput.onEndEdit.AddListener(OnNicknameEndEdit);

            saveButton = CreateButton(rowObject.transform, "Сохранить", buttonWidth, rowHeight);
            saveButton.onClick.AddListener(OnSavePressed);
            if (uiSound != null)
            {
                saveButton.onClick.AddListener(uiSound.PlayButton);
            }

            var statusObject = new GameObject("Status");
            statusObject.transform.SetParent(rootObject.transform, false);
            var statusLayout = statusObject.AddComponent<LayoutElement>();
            statusLayout.preferredHeight = 20f;
            statusLayout.minHeight = 20f;
            statusText = statusObject.AddComponent<TextMeshProUGUI>();
            statusText.fontSize = statusFontSize;
            statusText.alignment = TextAlignmentOptions.MidlineRight;
            statusText.enableWordWrapping = true;
            statusText.overflowMode = TextOverflowModes.Ellipsis;
            statusText.color = StatusHintColor;
            statusText.raycastTarget = false;
            statusText.text = string.Empty;

            built = true;
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

            var nickname = PlayerProfileService.Nickname;
            if (!string.IsNullOrWhiteSpace(nickname))
            {
                nicknameInput.SetTextWithoutNotify(nickname);
            }

            if (PlayerProfileService.IsServerSynced)
            {
                SetStatus("Можно сменить никнейм.", StatusHintColor);
                if (saveButton != null)
                {
                    saveButton.interactable = true;
                }
            }
            else
            {
                SetStatus("Сервер недоступен — никнейм нельзя изменить.", StatusErrorColor);
                if (saveButton != null)
                {
                    saveButton.interactable = false;
                }
            }
        }

        private void OnNicknameEndEdit(string value)
        {
            if (!PlayerProfileService.IsServerSynced || profileApiClient == null)
            {
                return;
            }

            if (validateCoroutine != null)
            {
                StopCoroutine(validateCoroutine);
            }

            validateCoroutine = StartCoroutine(ValidateNicknameRoutine(value));
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

        private IEnumerator ValidateNicknameRoutine(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                SetStatus("Введите никнейм.", StatusErrorColor);
                yield break;
            }

            if (string.Equals(trimmed, PlayerProfileService.Nickname, System.StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Это ваш текущий никнейм.", StatusHintColor);
                yield break;
            }

            var completed = false;
            var success = false;
            var available = false;
            var message = string.Empty;

            yield return profileApiClient.CheckNicknameAvailable(ResolvePlayerId(), trimmed, (ok, isAvailable, responseMessage, _) =>
            {
                completed = true;
                success = ok;
                available = isAvailable;
                message = responseMessage;
            });

            if (!completed)
            {
                yield break;
            }

            if (!success)
            {
                SetStatus(PlayerProfileService.IsServerSynced
                    ? "Не удалось проверить никнейм."
                    : message, StatusErrorColor);
                yield break;
            }

            if (available)
            {
                SetStatus("Никнейм свободен.", StatusOkColor);
                yield break;
            }

            if (message.IndexOf("Invalid", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("3-16", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetStatus("Никнейм: 3–16 символов, буквы, цифры, _ или -.", StatusErrorColor);
            }
            else
            {
                SetStatus("Этот никнейм уже занят.", StatusErrorColor);
            }
        }

        private void OnSavePressed()
        {
            if (!PlayerProfileService.IsServerSynced || profileApiClient == null)
            {
                SetStatus("Сервер недоступен — никнейм нельзя изменить.", StatusErrorColor);
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
            if (saveButton != null)
            {
                saveButton.interactable = false;
            }

            var trimmed = nicknameInput != null ? nicknameInput.text.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                SetStatus("Введите никнейм.", StatusErrorColor);
                if (saveButton != null)
                {
                    saveButton.interactable = true;
                }

                yield break;
            }

            if (string.Equals(trimmed, PlayerProfileService.Nickname, System.StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Никнейм не изменился.", StatusHintColor);
                if (saveButton != null)
                {
                    saveButton.interactable = true;
                }

                yield break;
            }

            SetStatus("Сохранение...", StatusHintColor);

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

            if (saveButton != null)
            {
                saveButton.interactable = PlayerProfileService.IsServerSynced;
            }

            if (!completed)
            {
                yield break;
            }

            if (success)
            {
                nicknameInput.SetTextWithoutNotify(PlayerProfileService.Nickname);
                SetStatus("Никнейм сохранён.", StatusOkColor);
                yield break;
            }

            SetStatus(string.IsNullOrWhiteSpace(error) ? "Не удалось сохранить никнейм." : error, StatusErrorColor);
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

        private TMP_InputField CreateInputField(Transform parent)
        {
            var inputObject = new GameObject("NicknameInput");
            inputObject.transform.SetParent(parent, false);

            var layoutElement = inputObject.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.minWidth = 140f;
            layoutElement.preferredHeight = rowHeight;

            var background = inputObject.AddComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.type = Image.Type.Simple;
            background.color = InputColor;

            var inputField = inputObject.AddComponent<TMP_InputField>();
            inputField.characterLimit = 16;

            var textAreaObject = new GameObject("Text Area");
            textAreaObject.transform.SetParent(inputObject.transform, false);
            var textAreaRect = textAreaObject.AddComponent<RectTransform>();
            StretchFull(textAreaRect);
            textAreaRect.offsetMin = new Vector2(12f, 7f);
            textAreaRect.offsetMax = new Vector2(-12f, -7f);
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
            placeholder.color = new Color(0.72f, 0.76f, 0.8f, 0.72f);
            placeholder.raycastTarget = false;

            var textObject = new GameObject("Text");
            textObject.transform.SetParent(textAreaObject.transform, false);
            var textRect = textObject.AddComponent<RectTransform>();
            StretchFull(textRect);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = inputFontSize;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.color = LabelColor;
            text.raycastTarget = false;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            inputField.lineType = TMP_InputField.LineType.SingleLine;

            return inputField;
        }

        private Button CreateButton(Transform parent, string label, float width, float height)
        {
            var buttonObject = new GameObject("SaveNicknameButton");
            buttonObject.transform.SetParent(parent, false);

            var layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = width;
            layoutElement.minWidth = width;
            layoutElement.preferredHeight = height;

            var image = buttonObject.AddComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.type = Image.Type.Simple;
            image.color = ButtonNormalColor;

            var button = buttonObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = ButtonNormalColor;
            colors.highlightedColor = ButtonHighlightedColor;
            colors.pressedColor = ButtonPressedColor;
            colors.selectedColor = ButtonHighlightedColor;
            colors.disabledColor = new Color(0.12f, 0.14f, 0.16f, 0.55f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            button.targetGraphic = image;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            StretchFull(labelRect);
            var labelText = labelObject.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 17f;
            labelText.fontStyle = FontStyles.Bold;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.color = LabelColor;
            labelText.raycastTarget = false;

            return button;
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
