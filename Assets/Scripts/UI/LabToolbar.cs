using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Top-left tool strip: save/load labs, missions, console, simulation speed/pause/step and sound.
public class LabToolbar : MonoBehaviour
{
    public static LabToolbar Instance { get; private set; }

    private Slider speedSlider;
    private TextMeshProUGUI speedLabel;
    private TextMeshProUGUI pauseLabel;
    private TextMeshProUGUI soundLabel;
    private TextMeshProUGUI statusLabel;
    private RectTransform loadMenu;
    private Coroutine statusRoutine;

    void Awake()
    {
        Instance = this;
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        pauseLabel.text = SimulationClock.Paused ? "RESUME" : "PAUSE";
    }

    public void SyncSpeed()
    {
        speedSlider.SetValueWithoutNotify(SimulationClock.Speed);
        speedLabel.text = $"{SimulationClock.Speed:0.##}x";
    }

    public void ShowStatus(string message, Color color)
    {
        if (statusRoutine != null) StopCoroutine(statusRoutine);
        statusRoutine = StartCoroutine(StatusRoutine(message, color));
    }

    IEnumerator StatusRoutine(string message, Color color)
    {
        statusLabel.text = message;
        statusLabel.color = color;
        yield return new WaitForSeconds(4f);
        statusLabel.text = "";
    }

    // ---------- actions ----------

    void Save()
    {
        string path = TopologyIO.Save();
        ShowStatus("Saved lab  →  " + System.IO.Path.GetFileName(path), UITheme.Success);
    }

    void ToggleLoadMenu()
    {
        bool open = !loadMenu.gameObject.activeSelf;
        if (open) RebuildLoadMenu();
        loadMenu.gameObject.SetActive(open);
        LabUI.CursorRequests = Mathf.Max(0, LabUI.CursorRequests + (open ? 1 : -1));
    }

    void CloseLoadMenu()
    {
        if (!loadMenu.gameObject.activeSelf) return;
        loadMenu.gameObject.SetActive(false);
        LabUI.CursorRequests = Mathf.Max(0, LabUI.CursorRequests - 1);
    }

    void LoadLab(TopologyData data, string title)
    {
        CloseLoadMenu();
        if (data == null || TopologyIO.Instance == null) return;
        if (MissionManager.Instance != null) MissionManager.Instance.StopMission();
        TopologyIO.Instance.Load(data);
        ShowStatus("Loaded  " + title, UITheme.Accent);
    }

    void RebuildLoadMenu()
    {
        for (int i = loadMenu.childCount - 1; i >= 0; i--) Destroy(loadMenu.GetChild(i).gameObject);

        LabUI.Label(loadMenu, "LOAD LAB", 15f, UITheme.Accent, TextAlignmentOptions.Left, 22f).fontStyle = FontStyles.Bold;

        Button saved = LabUI.Button(loadMenu, "My saved lab", UITheme.ButtonBlue, () => LoadLab(TopologyIO.ReadFile(), "my saved lab"));
        saved.interactable = TopologyIO.HasQuickSave;
        saved.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;

        foreach (var example in TopologyLibrary.Examples)
        {
            var build = example.build;
            string title = example.title;
            LabUI.Button(loadMenu, title, UITheme.ButtonGrey, () => LoadLab(build(), title)).gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
        }

        LabUI.Button(loadMenu, "Empty lab", UITheme.ButtonGrey, () => LoadLab(TopologyLibrary.Empty(), "empty lab")).gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
        LabUI.Button(loadMenu, "Cancel", new Color(0.18f, 0.2f, 0.24f), CloseLoadMenu).gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
    }

    // ---------- UI ----------

    void Build()
    {
        Canvas canvas = LabUI.CreateCanvas("ToolbarCanvas", 44);
        canvas.transform.SetParent(transform, false);

        RectTransform bar = LabUI.Panel(canvas.transform, "Toolbar", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(360f, 128f));
        LabUI.Vertical(bar, 6f, 8);

        RectTransform row1 = LabUI.Row(bar, 34f);
        LabUI.Button(row1, "SAVE", UITheme.ButtonBlue, Save);
        LabUI.Button(row1, "LOAD", UITheme.ButtonBlue, ToggleLoadMenu);
        LabUI.Button(row1, "MISSIONS", new Color(0.45f, 0.3f, 0.8f), () => { if (MissionManager.Instance != null) MissionManager.Instance.TogglePanel(); });
        LabUI.Button(row1, "CONSOLE", new Color(0.12f, 0.5f, 0.45f), () => { if (LabConsole.Instance != null) LabConsole.Instance.Toggle(); });

        RectTransform row2 = LabUI.Row(bar, 30f);
        row2.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        pauseLabel = LabUI.Button(row2, "PAUSE", UITheme.ButtonGrey, SimulationClock.TogglePause, 72f).GetComponentInChildren<TextMeshProUGUI>();
        LabUI.Button(row2, "STEP", UITheme.ButtonGrey, SimulationClock.Step, 56f);

        speedSlider = LabUI.Slider(row2, 0.25f, 4f, 1f, value =>
        {
            SimulationClock.Speed = Mathf.Round(value * 4f) / 4f;
            speedLabel.text = $"{SimulationClock.Speed:0.##}x";
        });
        speedSlider.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        speedLabel = LabUI.Label(row2, "1x", 15f, UITheme.TextPrimary, TextAlignmentOptions.Center);
        speedLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;

        soundLabel = LabUI.Button(row2, "SOUND", UITheme.ButtonGrey, () =>
        {
            LabSounds.Muted = !LabSounds.Muted;
            soundLabel.text = LabSounds.Muted ? "MUTED" : "SOUND";
        }, 64f).GetComponentInChildren<TextMeshProUGUI>();

        statusLabel = LabUI.Label(bar, "", 14f, UITheme.TextSecondary, TextAlignmentOptions.Left, 22f);
        statusLabel.textWrappingMode = TextWrappingModes.NoWrap;

        loadMenu = LabUI.Panel(canvas.transform, "LoadMenu", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -152f), new Vector2(300f, 0f));
        VerticalLayoutGroup menuLayout = LabUI.Vertical(loadMenu, 6f, 10);
        menuLayout.childForceExpandHeight = false;
        loadMenu.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        loadMenu.gameObject.SetActive(false);
    }
}
