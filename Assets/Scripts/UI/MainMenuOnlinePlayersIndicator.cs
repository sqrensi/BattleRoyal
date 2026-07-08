using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShooterPrototype.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuOnlinePlayersIndicator : MonoBehaviour
    {
        [SerializeField] private float width = 132f;
        [SerializeField] private float height = 28f;
        [SerializeField] private float gapFromStartButton = 16f;
        [SerializeField] private float dotSize = 12f;
        [SerializeField] private float fontSize = 20f;

        private CanvasGroup canvasGroup;
        private TextMeshProUGUI dotLabel;
        private TextMeshProUGUI valueLabel;
        private bool built;

        public CanvasGroup CanvasGroup => canvasGroup;

        public void Build(RectTransform canvasRect, RectTransform startButtonRect)
        {
            if (built || canvasRect == null || startButtonRect == null)
            {
                return;
            }

            var rootObject = new GameObject("MainMenuOnlinePlayersIndicator");
            rootObject.transform.SetParent(canvasRect, false);

            var rootRect = rootObject.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 0f);
            rootRect.anchorMax = new Vector2(0f, 0f);
            rootRect.pivot = new Vector2(0f, 0.5f);
            rootRect.anchoredPosition = new Vector2(
                startButtonRect.anchoredPosition.x + startButtonRect.sizeDelta.x + gapFromStartButton,
                startButtonRect.anchoredPosition.y + startButtonRect.sizeDelta.y * 0.5f);
            rootRect.sizeDelta = new Vector2(width, height);

            var layout = rootObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = 8f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(0, 0, 0, 0);

            var dotObject = new GameObject("Dot", typeof(RectTransform));
            dotObject.transform.SetParent(rootObject.transform, false);
            var dotRect = dotObject.GetComponent<RectTransform>();
            dotRect.sizeDelta = new Vector2(dotSize, dotSize);

            dotLabel = dotObject.AddComponent<TextMeshProUGUI>();
            dotLabel.text = "●";
            dotLabel.fontSize = dotSize * 1.45f;
            dotLabel.alignment = TextAlignmentOptions.Center;
            dotLabel.enableWordWrapping = false;
            dotLabel.overflowMode = TextOverflowModes.Overflow;
            dotLabel.raycastTarget = false;
            UiTheme.ApplyTmp(dotLabel, UiTextRole.Success);

            var dotLayout = dotObject.AddComponent<LayoutElement>();
            dotLayout.preferredWidth = dotSize;
            dotLayout.preferredHeight = dotSize;
            dotLayout.minWidth = dotSize;
            dotLayout.minHeight = dotSize;

            var textObject = new GameObject("Value", typeof(RectTransform));
            textObject.transform.SetParent(rootObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(width - dotSize - 8f, height);

            valueLabel = textObject.AddComponent<TextMeshProUGUI>();
            valueLabel.text = "--";
            valueLabel.fontSize = fontSize;
            valueLabel.alignment = TextAlignmentOptions.MidlineLeft;
            valueLabel.enableWordWrapping = false;
            valueLabel.overflowMode = TextOverflowModes.Overflow;
            valueLabel.raycastTarget = false;
            UiTheme.ApplyTmp(valueLabel, UiTextRole.Success);

            canvasGroup = rootObject.AddComponent<CanvasGroup>();
            built = true;
            SetOnlineCount(-1, false);
        }

        public void SetOnlineCount(int playerCount, bool available)
        {
            if (!built || canvasGroup == null || dotLabel == null || valueLabel == null)
            {
                return;
            }

            canvasGroup.alpha = available ? 1f : 0.7f;
            valueLabel.text = available ? Mathf.Max(0, playerCount).ToString() : "--";
            UiTheme.ApplyTmp(dotLabel, available ? UiTextRole.Success : UiTextRole.Muted);
            UiTheme.ApplyTmp(valueLabel, available ? UiTextRole.Success : UiTextRole.Muted);
        }
    }
}
