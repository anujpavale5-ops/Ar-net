using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Restyles the scene's existing uGUI at startup into a dark, high-contrast "operations console"
// look so panels stay readable over the bright 3D room. Applied by LabBootstrap.
public static class UITheme
{
    public static readonly Color PanelColor = new Color(0.055f, 0.07f, 0.095f, 0.92f);
    public static readonly Color PanelEdge = new Color(0.35f, 0.75f, 1f, 0.35f);
    public static readonly Color Accent = new Color(0.4f, 0.82f, 1f);
    public static readonly Color TextPrimary = new Color(0.95f, 0.97f, 1f);
    public static readonly Color TextSecondary = new Color(0.68f, 0.74f, 0.82f);
    public static readonly Color FieldColor = new Color(0.11f, 0.135f, 0.17f, 1f);
    public static readonly Color ButtonBlue = new Color(0.16f, 0.45f, 0.86f);
    public static readonly Color ButtonRed = new Color(0.78f, 0.22f, 0.22f);
    public static readonly Color ButtonAmber = new Color(0.85f, 0.55f, 0.12f);
    public static readonly Color ButtonGrey = new Color(0.24f, 0.27f, 0.32f);
    public static readonly Color Success = new Color(0.32f, 0.9f, 0.5f);
    public static readonly Color Failure = new Color(1f, 0.42f, 0.36f);
    public static readonly Color Warning = new Color(1f, 0.75f, 0.3f);

    public static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

    public static void ApplyToScene()
    {
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (!canvas.isRootCanvas) continue;
            if (canvas.renderMode == RenderMode.WorldSpace) continue;
            if (canvas.GetComponentInParent<NetworkVisualizer>() != null) continue;
            Apply(canvas.transform);
        }
    }

    public static void Apply(Transform root)
    {
        HashSet<Object> styled = new HashSet<Object>();

        foreach (TMP_Dropdown dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true)) StyleDropdown(dropdown, styled);
        foreach (TMP_InputField field in root.GetComponentsInChildren<TMP_InputField>(true)) StyleInputField(field, styled);
        foreach (Scrollbar scrollbar in root.GetComponentsInChildren<Scrollbar>(true)) StyleScrollbar(scrollbar, styled);
        foreach (Button button in root.GetComponentsInChildren<Button>(true)) StyleButton(button, styled);

        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (styled.Contains(image)) continue;
            if (image.name.Contains("Panel")) StylePanel(image);
        }

        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (styled.Contains(text)) continue;
            StyleText(text);
        }
    }

    static void StylePanel(Image panel)
    {
        panel.color = PanelColor;

        // Explicit null checks: Unity's fake-null objects defeat the ?? operator in the editor
        Outline edge = panel.GetComponent<Outline>();
        if (edge == null) edge = panel.gameObject.AddComponent<Outline>();
        edge.effectColor = PanelEdge;
        edge.effectDistance = new Vector2(1.5f, -1.5f);

        Shadow shadow = panel.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(4f, -5f);
    }

    static bool IsTitle(string objectName)
    {
        return objectName == "Title" || objectName == "TitleText" || objectName == "Title Text" || objectName == "ConnectionTitle";
    }

    static void StyleText(TMP_Text text)
    {
        if (IsTitle(text.name))
        {
            text.color = Accent;
            text.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            text.characterSpacing = 1.5f;

            // Shrink rather than wrap onto the content below
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontSizeMax = text.fontSize;
            text.fontSizeMin = Mathf.Min(12f, text.fontSize);
            text.enableAutoSizing = true;
            return;
        }

        if (text.name == "Subtitle" || text.name.EndsWith("Label") || text.name.EndsWith(" Text"))
        {
            text.color = TextSecondary;
            return;
        }

        if (text.name == "LogText")
        {
            text.color = TextPrimary;
            text.fontSize = Mathf.Min(text.fontSize, 17f);
            text.lineSpacing = 4f;
            return;
        }

        // Anything left that was dark-on-light becomes light-on-dark
        if (text.color.grayscale < 0.6f) text.color = TextPrimary;
    }

    static void StyleButton(Button button, HashSet<Object> styled)
    {
        Image image = button.image;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        string buttonName = button.name.ToLowerInvariant();

        // Device picker tiles in the Add Device panel are already dark with preview images
        bool deviceTile = image != null && image.color.grayscale < 0.3f;

        if (image != null && !deviceTile)
        {
            Color baseColor = ButtonBlue;
            if (buttonName.Contains("delete")) baseColor = ButtonRed;
            else if (buttonName.Contains("disconnect")) baseColor = ButtonAmber;
            else if (buttonName.Contains("cancel") || buttonName.Contains("close")) baseColor = ButtonGrey;

            image.color = baseColor;
            styled.Add(image);
        }

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.2f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.8f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        colors.colorMultiplier = 1.2f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        if (label != null)
        {
            label.color = TextPrimary;
            if (!deviceTile) label.fontStyle |= FontStyles.Bold;
            styled.Add(label);
        }
    }

    static void StyleDropdown(TMP_Dropdown dropdown, HashSet<Object> styled)
    {
        if (dropdown.image != null)
        {
            dropdown.image.color = FieldColor;
            styled.Add(dropdown.image);
        }

        ColorBlock colors = dropdown.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.3f, 1.4f);
        colors.colorMultiplier = 1.3f;
        dropdown.colors = colors;

        if (dropdown.captionText != null)
        {
            dropdown.captionText.color = TextPrimary;
            dropdown.captionText.fontSize = Mathf.Max(dropdown.captionText.fontSize, FieldFontSize(dropdown.transform));
            styled.Add(dropdown.captionText);
        }

        if (dropdown.itemText != null)
        {
            dropdown.itemText.color = TextPrimary;
            dropdown.itemText.fontSize = Mathf.Max(dropdown.itemText.fontSize, 15f);
            styled.Add(dropdown.itemText);
        }

        if (dropdown.template != null)
        {
            foreach (Image image in dropdown.template.GetComponentsInChildren<Image>(true))
            {
                styled.Add(image);
                if (image.name == "Item Checkmark") image.color = Accent;
                else if (image.name == "Item Background") image.color = FieldColor;
                else if (image.name != "Viewport") image.color = new Color(0.08f, 0.1f, 0.13f, 0.98f);
            }

            foreach (Toggle toggle in dropdown.template.GetComponentsInChildren<Toggle>(true))
            {
                ColorBlock itemColors = toggle.colors;
                itemColors.normalColor = Color.white;
                itemColors.highlightedColor = new Color(0.55f, 0.85f, 1.6f);
                itemColors.selectedColor = new Color(0.45f, 0.7f, 1.3f);
                itemColors.colorMultiplier = 1.6f;
                toggle.colors = itemColors;
            }
        }

        foreach (Image image in dropdown.GetComponentsInChildren<Image>(true))
        {
            if (image.name == "Arrow")
            {
                image.color = Accent;
                styled.Add(image);
            }
        }

        foreach (TMP_Text text in dropdown.GetComponentsInChildren<TMP_Text>(true)) styled.Add(text);
    }

    static float FieldFontSize(Transform field)
    {
        float height = ((RectTransform)field).rect.height;
        if (height <= 0f) height = ((RectTransform)field).sizeDelta.y;
        return Mathf.Clamp(height * 0.55f, 14f, 20f);
    }

    static void StyleInputField(TMP_InputField field, HashSet<Object> styled)
    {
        if (field.image != null)
        {
            field.image.color = FieldColor;
            styled.Add(field.image);
        }

        if (field.textComponent != null)
        {
            field.textComponent.color = TextPrimary;
            field.textComponent.fontSize = Mathf.Max(field.textComponent.fontSize, FieldFontSize(field.transform));
            styled.Add(field.textComponent);
        }

        if (field.placeholder is TMP_Text placeholder)
        {
            placeholder.color = new Color(0.5f, 0.55f, 0.62f);
            placeholder.fontSize = Mathf.Max(placeholder.fontSize, FieldFontSize(field.transform));
            placeholder.fontStyle = FontStyles.Italic;
            styled.Add(placeholder);
        }

        field.caretColor = Accent;
        field.customCaretColor = true;
        field.selectionColor = new Color(0.3f, 0.6f, 1f, 0.45f);

        ColorBlock colors = field.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.25f, 1.35f);
        colors.selectedColor = new Color(1.3f, 1.4f, 1.6f);
        colors.colorMultiplier = 1.4f;
        field.colors = colors;
    }

    static void StyleScrollbar(Scrollbar scrollbar, HashSet<Object> styled)
    {
        if (scrollbar.image != null)
        {
            scrollbar.image.color = new Color(0.4f, 0.48f, 0.58f);
            styled.Add(scrollbar.image);
        }

        Image track = scrollbar.GetComponent<Image>();
        if (track != null)
        {
            track.color = new Color(0.1f, 0.12f, 0.15f, 0.9f);
            styled.Add(track);
        }
    }

    // Swaps a button's fill when a tool mode is toggled on/off
    public static void SetButtonActive(TMP_Text buttonLabel, bool active, Color activeColor)
    {
        if (buttonLabel == null) return;

        buttonLabel.color = TextPrimary;
        Button button = buttonLabel.GetComponentInParent<Button>();
        if (button == null || button.image == null) return;

        ToolButtonState state = button.GetComponent<ToolButtonState>();
        if (state == null) state = button.gameObject.AddComponent<ToolButtonState>();
        if (!state.captured)
        {
            state.idleColor = button.image.color;
            state.captured = true;
        }
        button.image.color = active ? activeColor : state.idleColor;
    }
}
