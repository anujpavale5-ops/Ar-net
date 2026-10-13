using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Builds themed runtime UI (panels, labels, buttons, inputs, sliders) for the lab's extra tools.
public static class LabUI
{
    // Set by panels that need a free mouse cursor in walk mode (console, menus)
    public static int CursorRequests;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => CursorRequests = 0;

    public static Canvas CreateCanvas(string canvasName, int sortingOrder)
    {
        GameObject canvasObject = new GameObject(canvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    public static RectTransform Panel(Transform parent, string panelName, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        GameObject panel = new GameObject(panelName, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Shadow));
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        panel.GetComponent<Image>().color = UITheme.PanelColor;
        Outline outline = panel.GetComponent<Outline>();
        outline.effectColor = UITheme.PanelEdge;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        Shadow shadow = panel.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(4f, -5f);
        return rect;
    }

    public static VerticalLayoutGroup Vertical(RectTransform rect, float spacing, int padding)
    {
        VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }

    public static RectTransform Row(Transform parent, float height, float spacing = 6f)
    {
        GameObject row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        row.GetComponent<LayoutElement>().preferredHeight = height;
        return row.GetComponent<RectTransform>();
    }

    public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Left, float preferredHeight = -1f)
    {
        GameObject labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(parent, false);
        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;

        if (preferredHeight > 0f) labelObject.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
        return label;
    }

    public static Button Button(Transform parent, string text, Color color, UnityAction onClick, float preferredWidth = -1f)
    {
        GameObject buttonObject = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.2f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.8f);
        colors.colorMultiplier = 1.2f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(() => LabSounds.Play(LabSound.Click));
        if (onClick != null) button.onClick.AddListener(onClick);

        TextMeshProUGUI label = Label(buttonObject.transform, text, 15f, UITheme.TextPrimary, TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(label.rectTransform);

        if (preferredWidth > 0f) buttonObject.AddComponent<LayoutElement>().preferredWidth = preferredWidth;
        return button;
    }

    public static TMP_InputField Input(Transform parent, string placeholder, float height)
    {
        GameObject field = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        field.transform.SetParent(parent, false);
        field.AddComponent<LayoutElement>().preferredHeight = height;

        TMP_InputField input = field.GetComponent<TMP_InputField>();
        input.image.color = UITheme.FieldColor;
        input.textComponent.color = UITheme.TextPrimary;
        input.textComponent.fontSize = 17f;
        input.caretColor = UITheme.Accent;
        input.customCaretColor = true;
        input.selectionColor = new Color(0.3f, 0.6f, 1f, 0.45f);
        input.richText = false;

        if (input.placeholder is TMP_Text hint)
        {
            hint.text = placeholder;
            hint.color = new Color(0.5f, 0.55f, 0.62f);
            hint.fontSize = 17f;
            hint.fontStyle = FontStyles.Italic;
        }
        return input;
    }

    public static Slider Slider(Transform parent, float min, float max, float value, UnityAction<float> onChange)
    {
        GameObject sliderObject = DefaultControls.CreateSlider(new DefaultControls.Resources());
        sliderObject.transform.SetParent(parent, false);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.onValueChanged.AddListener(onChange);

        foreach (Image image in sliderObject.GetComponentsInChildren<Image>(true))
        {
            if (image.name == "Background") image.color = UITheme.FieldColor;
            else if (image.name == "Fill") image.color = UITheme.ButtonBlue;
            else if (image.name == "Handle") image.color = UITheme.Accent;
        }

        RectTransform background = sliderObject.transform.Find("Background") as RectTransform;
        if (background != null)
        {
            background.anchorMin = new Vector2(0f, 0.35f);
            background.anchorMax = new Vector2(1f, 0.65f);
        }
        return slider;
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
