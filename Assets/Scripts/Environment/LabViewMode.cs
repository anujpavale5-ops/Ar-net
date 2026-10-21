using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Switches the main camera between the orbit overview and first-person walk mode (Tab).
// In walk mode the cursor is captured for mouse-look and a crosshair does the aiming;
// holding Alt (or opening a panel / typing) frees the cursor for the UI.
// E lays a cable between two aimed-at devices; H shows the controls panel.
[RequireComponent(typeof(Camera))]
public class LabViewMode : MonoBehaviour
{
    public static LabViewMode Instance { get; private set; }

    public bool IsFirstPerson { get; private set; }

    // Cursor locked and hidden, mouse movement turns the head
    public bool CursorCaptured { get; private set; }

    // Two-step walk-mode actions: aim at the first device, press the key, aim at the second, press again
    public enum PendingAction { None, Cable, Unplug, Packet }
    public PendingAction Pending { get; private set; }

    // First device of the pending action (the rubber-band line starts here)
    public NetworkDevice PendingCableFrom { get; private set; }

    [Tooltip("Furthest distance the crosshair can use a device from")]
    public float reach = 20f;
    [Tooltip("Degrees around the crosshair that still count as aiming at a device")]
    public float aimAssistAngle = 4f;
    public bool showControlsOnStart = true;

    private LabCameraController orbit;
    private FirstPersonController walker;
    private FirstPersonHands hands;
    private Camera cam;

    private DevicePlacementManager placement;
    private DeviceConfigUI configUI;
    private UIManager uiManager;
    private NetworkManager networkManager;
    private DeviceConnector connector;

    private GameObject crosshairCanvas;
    private Image crosshairDot;
    private TextMeshProUGUI targetLabel;
    private TextMeshProUGUI toastLabel;
    private NetworkDevice aimedDevice;
    private Coroutine toastRoutine;

    private GameObject helpCanvas;
    private RectTransform helpPanel;
    private RectTransform helpTag;
    private bool helpVisible;

    void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();

        orbit = GetComponent<LabCameraController>();
        if (orbit == null) orbit = gameObject.AddComponent<LabCameraController>();

        walker = GetComponent<FirstPersonController>();
        if (walker == null) walker = gameObject.AddComponent<FirstPersonController>();

        hands = GetComponent<FirstPersonHands>();
        if (hands == null) hands = gameObject.AddComponent<FirstPersonHands>();

        walker.enabled = false;
        hands.enabled = false;
        helpVisible = showControlsOnStart;

        BuildCrosshair();
        BuildHelp();
    }

    void Start()
    {
        placement = FindAnyObjectByType<DevicePlacementManager>();
        configUI = FindAnyObjectByType<DeviceConfigUI>();
        uiManager = FindAnyObjectByType<UIManager>();
        networkManager = FindAnyObjectByType<NetworkManager>();
        connector = FindAnyObjectByType<DeviceConnector>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ReleaseCursor();
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        bool typing = PointerUtil.IsTyping();

        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame && !typing) Toggle();

        if (!IsFirstPerson) return;

        UpdateCursorCapture(keyboard, typing);
        UpdateAim();

        if (keyboard != null && !typing)
        {
            HandleDeviceHotkeys(keyboard);
            HandleActionKeys(keyboard);
            if (keyboard.hKey.wasPressedThisFrame) SetHelpVisible(!helpVisible);
        }

        // Crosshair click on a device (placement clicks are handled by DevicePlacementManager)
        Mouse mouse = Mouse.current;
        if (CursorCaptured && mouse != null && mouse.leftButton.wasPressedThisFrame && aimedDevice != null &&
            (placement == null || !placement.IsPlacing))
        {
            if (Pending != PendingAction.None) FinishPending(aimedDevice);
            else aimedDevice.HandleClick();
        }
    }

    // ---------- walk-mode action keys ----------

    void HandleActionKeys(Keyboard keyboard)
    {
        if (Pending != PendingAction.None && (keyboard.qKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame))
        {
            CancelPending();
            return;
        }

        if (keyboard.eKey.wasPressedThisFrame) TwoStep(PendingAction.Cable, "E");
        else if (keyboard.xKey.wasPressedThisFrame) TwoStep(PendingAction.Unplug, "X");
        else if (keyboard.pKey.wasPressedThisFrame) TwoStep(PendingAction.Packet, "P");
        else if (keyboard.fKey.wasPressedThisFrame) OpenSettings();
        else if (keyboard.cKey.wasPressedThisFrame) OpenConsole();
        else if (keyboard.gKey.wasPressedThisFrame) TogglePower();
        else if (keyboard.deleteKey.wasPressedThisFrame) RemoveAimed();
    }

    void TwoStep(PendingAction action, string key)
    {
        if (aimedDevice == null)
        {
            Toast(Pending == PendingAction.None ? $"Aim the dot at a device, then press {key}" : $"Aim at the second device and press {key}", UITheme.Warning);
            return;
        }

        if (Pending == PendingAction.None)
        {
            if (placement != null && placement.IsPlacing) placement.CancelPlacement();
            Pending = action;
            PendingCableFrom = aimedDevice;
            LabSounds.Play(LabSound.Click);
            Toast(StartMessage(action, aimedDevice), ActionColor(action));
        }
        else if (Pending == action) FinishPending(aimedDevice);
        else Toast($"Finish or cancel (Q) the current {ActionName(Pending)} first", UITheme.Warning);
    }

    void FinishPending(NetworkDevice to)
    {
        NetworkDevice from = PendingCableFrom;
        PendingAction action = Pending;
        if (from == null || action == PendingAction.None) return;

        if (to == from)
        {
            Toast("Aim at a different device for the second end", UITheme.Warning);
            return;
        }

        Pending = PendingAction.None;
        PendingCableFrom = null;

        switch (action)
        {
            case PendingAction.Cable: FinishCable(from, to); break;
            case PendingAction.Unplug: FinishUnplug(from, to); break;
            case PendingAction.Packet: FinishPacket(from, to); break;
        }
    }

    void CancelPending()
    {
        Toast($"{ActionName(Pending)} cancelled", UITheme.TextSecondary);
        Pending = PendingAction.None;
        PendingCableFrom = null;
    }

    void FinishCable(NetworkDevice from, NetworkDevice to)
    {
        NetworkConnection existing = NetworkConnection.Between(from, to);
        if (existing != null && existing.isConnected)
        {
            Toast($"{from.deviceName} and {to.deviceName} are already connected", UITheme.Warning);
            return;
        }

        if (networkManager != null && connector != null && connector.cablePrefab != null)
        {
            networkManager.ConnectDevices(from, to, connector.cablePrefab);
            Toast($"Connected {from.deviceName} ↔ {to.deviceName}", UITheme.Success);
        }
        else Toast("No cable prefab assigned on DeviceConnector", UITheme.Failure);
    }

    void FinishUnplug(NetworkDevice from, NetworkDevice to)
    {
        NetworkConnection cable = NetworkConnection.Between(from, to);
        if (cable == null)
        {
            Toast($"No cable between {from.deviceName} and {to.deviceName} (use E to add one)", UITheme.Warning);
            return;
        }

        if (cable.isConnected)
        {
            cable.Disconnect();
            Toast($"Unplugged {from.deviceName} × {to.deviceName}", UITheme.Warning);
        }
        else
        {
            cable.Reconnect();
            LabSounds.Play(LabSound.Plug);
            Toast($"Plugged back in: {from.deviceName} ↔ {to.deviceName}", UITheme.Success);
        }
    }

    void FinishPacket(NetworkDevice from, NetworkDevice to)
    {
        if (networkManager == null) return;

        // Same rules as the Send Packet panel; the reason is shown if it can't be delivered
        if (networkManager.BuildRoute(from, to, out string reason) == null)
        {
            networkManager.SendPacket(from, to);
            Toast($"Can't send: {reason}", UITheme.Failure);
            return;
        }

        networkManager.SendPacket(from, to);
        Toast($"Packet sent {from.deviceName} → {to.deviceName}", UITheme.Success);
    }

    void OpenSettings()
    {
        if (aimedDevice == null) { Toast("Aim at a device, then press F", UITheme.Warning); return; }
        if (aimedDevice.IsSwitch) { Toast("Switches have no IP settings", UITheme.TextSecondary); return; }

        if (LabConsole.Instance != null) LabConsole.Instance.SetDevice(aimedDevice);
        if (configUI != null) configUI.OpenTerminal(aimedDevice);
    }

    void OpenConsole()
    {
        if (LabConsole.Instance == null) return;
        if (aimedDevice != null) LabConsole.Instance.SetDevice(aimedDevice);
        LabConsole.Instance.Open();
    }

    void TogglePower()
    {
        if (aimedDevice == null) { Toast("Aim at a device, then press G", UITheme.Warning); return; }

        bool on = !aimedDevice.isOnline;
        aimedDevice.SetStatus(on);
        LabSounds.Play(on ? LabSound.Plug : LabSound.Unplug);
        Toast(on ? $"{aimedDevice.deviceName} powered on" : $"{aimedDevice.deviceName} powered off", on ? UITheme.Success : UITheme.Warning);
    }

    void RemoveAimed()
    {
        if (aimedDevice == null) return;

        DeviceDeletionManager deletion = FindAnyObjectByType<DeviceDeletionManager>();
        string removed = aimedDevice.deviceName;
        if (PendingCableFrom == aimedDevice) { Pending = PendingAction.None; PendingCableFrom = null; }
        if (deletion != null && deletion.RemoveDevice(aimedDevice)) Toast($"Removed {removed}", UITheme.Failure);
    }

    static string ActionName(PendingAction action)
    {
        switch (action)
        {
            case PendingAction.Cable: return "Cable";
            case PendingAction.Unplug: return "Unplug";
            case PendingAction.Packet: return "Packet";
            default: return "Action";
        }
    }

    static string ActionKey(PendingAction action) => action == PendingAction.Cable ? "E" : action == PendingAction.Unplug ? "X" : "P";

    static Color ActionColor(PendingAction action)
    {
        switch (action)
        {
            case PendingAction.Unplug: return UITheme.Failure;
            case PendingAction.Packet: return UITheme.Accent;
            default: return UITheme.Warning;
        }
    }

    static string StartMessage(PendingAction action, NetworkDevice from)
    {
        switch (action)
        {
            case PendingAction.Unplug: return $"Unplug from {from.deviceName}: aim at the other end, press X";
            case PendingAction.Packet: return $"Packet from {from.deviceName}: aim at the destination, press P";
            default: return $"Cable plugged into {from.deviceName}: aim at the other end, press E";
        }
    }

    // ---------- cursor ----------

    void UpdateCursorCapture(Keyboard keyboard, bool typing)
    {
        bool altHeld = keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
        bool wantsCapture = !altHeld && !typing && !PanelNeedsCursor() && Application.isFocused;

        if (wantsCapture != CursorCaptured)
        {
            CursorCaptured = wantsCapture;
            if (CursorCaptured)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else ReleaseCursor();
        }
        else if (CursorCaptured && Cursor.lockState != CursorLockMode.Locked)
        {
            // The editor drops the lock on Escape; take it back on the next click
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
        }

        crosshairCanvas.SetActive(CursorCaptured);
    }

    bool PanelNeedsCursor()
    {
        if (LabUI.CursorRequests > 0) return true;
        if (configUI != null && configUI.configPanel != null && configUI.configPanel.activeInHierarchy) return true;
        if (uiManager != null && uiManager.deviceSelectionPanel != null && uiManager.deviceSelectionPanel.activeInHierarchy) return true;
        return false;
    }

    void ReleaseCursor()
    {
        CursorCaptured = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // ---------- aiming ----------

    void UpdateAim()
    {
        aimedDevice = null;

        if (CursorCaptured && (placement == null || !placement.IsPlacing))
        {
            aimedDevice = FindAimedDevice();
        }

        bool aiming = aimedDevice != null;
        bool pending = Pending != PendingAction.None && PendingCableFrom != null;
        Color pendingColor = ActionColor(Pending);

        crosshairDot.color = pending ? pendingColor : aiming ? UITheme.Accent : new Color(1f, 1f, 1f, 0.85f);
        crosshairDot.rectTransform.sizeDelta = Vector2.one * (aiming ? 11f : 6f);

        string text = null;
        if (pending)
        {
            string hex = UITheme.Hex(pendingColor);
            string key = ActionKey(Pending);
            string verb = Pending == PendingAction.Cable ? "plug the cable in here"
                : Pending == PendingAction.Unplug ? "unplug / replug the cable to here"
                : "send the packet here";

            text = aiming && aimedDevice != PendingCableFrom
                ? $"<b>{aimedDevice.deviceName}</b>\n<size=80%><color={hex}>{key} / click: {verb}</color>  ·  Q: cancel</size>"
                : $"<color={hex}>{ActionName(Pending)} from <b>{PendingCableFrom.deviceName}</b></color>\n<size=80%>aim at another device and press {key}  ·  Q: cancel</size>";
        }
        else if (aiming)
        {
            string ip = aimedDevice.IsSwitch ? "Layer-2 switch"
                : !aimedDevice.isOnline ? "<color=#FF6A5E>OFFLINE</color>"
                : string.IsNullOrEmpty(aimedDevice.ipAddress) ? "<color=#FFC04D>Unconfigured</color>"
                : aimedDevice.ipAddress;
            text = $"<b>{aimedDevice.deviceName}</b>  <size=80%>{ip}</size>\n<size=80%>E: cable  ·  P: send packet  ·  F: settings  ·  C: console</size>";
        }

        targetLabel.gameObject.SetActive(text != null);
        if (text != null) targetLabel.text = text;
    }

    // Exact hit first; otherwise the device closest to the crosshair within a small cone
    NetworkDevice FindAimedDevice()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, reach))
        {
            NetworkDevice hitDevice = hit.collider.GetComponentInParent<NetworkDevice>();
            if (hitDevice != null && !hitDevice.isPreviewModel) return hitDevice;
        }

        NetworkDevice best = null;
        float bestAngle = aimAssistAngle;

        foreach (NetworkDevice device in NetworkDevice.All)
        {
            if (device == null || device.isPreviewModel) continue;

            Vector3 toDevice = device.ModelCenter - ray.origin;
            float distance = toDevice.magnitude;
            if (distance > reach || distance < 0.1f) continue;

            // Bigger devices and closer ones get a slightly wider target
            float allowance = Mathf.Atan2(device.FootprintRadius, distance) * Mathf.Rad2Deg * 0.5f;
            float angle = Vector3.Angle(ray.direction, toDevice) - allowance;
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = device;
            }
        }
        return best;
    }

    // 1-4 pick up a device to place without leaving walk mode
    void HandleDeviceHotkeys(Keyboard keyboard)
    {
        if (placement == null) return;

        GameObject before = placement.SelectedPrefab;
        if (keyboard.digit1Key.wasPressedThisFrame) placement.SelectPC();
        else if (keyboard.digit2Key.wasPressedThisFrame) placement.SelectSwitch();
        else if (keyboard.digit3Key.wasPressedThisFrame) placement.SelectRouter();
        else if (keyboard.digit4Key.wasPressedThisFrame) placement.SelectServer();

        if (placement.SelectedPrefab != before && placement.SelectedPrefab != null)
        {
            Pending = PendingAction.None;
            PendingCableFrom = null;
            Toast("Aim at the floor and click to place  ·  Esc: cancel", UITheme.Accent);
        }
    }

    // ---------- modes ----------

    public void Toggle()
    {
        if (IsFirstPerson) EnterOverview();
        else EnterWalkMode();
    }

    public void EnterWalkMode()
    {
        if (IsFirstPerson) return;

        // Start a few steps back from what the overview was looking at, facing the same way
        float yaw = orbit.Yaw;
        Vector3 facing = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 start = orbit.Pivot - facing * 3.5f;

        orbit.enabled = false;
        walker.enabled = true;
        walker.Enter(start, yaw);
        hands.enabled = true;

        IsFirstPerson = true;
        helpCanvas.SetActive(true);
        SetHelpVisible(helpVisible);
        if (NetworkVisualizer.Instance != null) NetworkVisualizer.Instance.RefreshLegend();
    }

    public void EnterOverview()
    {
        if (!IsFirstPerson) return;

        ReleaseCursor();
        crosshairCanvas.SetActive(false);
        helpCanvas.SetActive(false);
        aimedDevice = null;
        Pending = PendingAction.None;
        PendingCableFrom = null;

        hands.enabled = false;
        walker.enabled = false;
        orbit.enabled = true;
        orbit.ResumeFromCurrentTransform();

        IsFirstPerson = false;
        if (NetworkVisualizer.Instance != null) NetworkVisualizer.Instance.RefreshLegend();
    }

    // ---------- crosshair UI ----------

    void Toast(string message, Color color)
    {
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(ToastRoutine(message, color));
    }

    IEnumerator ToastRoutine(string message, Color color)
    {
        toastLabel.text = message;
        toastLabel.color = color;
        toastLabel.gameObject.SetActive(true);
        yield return new WaitForSecondsRealtime(2f);
        toastLabel.gameObject.SetActive(false);
    }

    void BuildCrosshair()
    {
        crosshairCanvas = new GameObject("Crosshair", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = crosshairCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;

        CanvasScaler scaler = crosshairCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject dot = new GameObject("Dot", typeof(RectTransform), typeof(Image), typeof(Outline));
        dot.transform.SetParent(crosshairCanvas.transform, false);
        crosshairDot = dot.GetComponent<Image>();
        crosshairDot.raycastTarget = false;
        crosshairDot.rectTransform.sizeDelta = new Vector2(6f, 6f);
        Outline outline = dot.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        targetLabel = CrosshairText("Target", new Vector2(0f, -48f), 22f);
        toastLabel = CrosshairText("Toast", new Vector2(0f, 70f), 20f);

        crosshairCanvas.SetActive(false);
    }

    TextMeshProUGUI CrosshairText(string objectName, Vector2 position, float size)
    {
        GameObject label = new GameObject(objectName, typeof(RectTransform));
        label.transform.SetParent(crosshairCanvas.transform, false);
        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(700f, 64f);

        TextMeshProUGUI text = label.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = size;
        text.color = UITheme.TextPrimary;
        text.raycastTarget = false;
        text.outlineWidth = 0.2f;
        text.outlineColor = new Color32(10, 14, 20, 255);
        label.SetActive(false);
        return text;
    }

    // ---------- controls help ----------

    static readonly (string keys, string action)[] Controls =
    {
        ("Mouse  WASD", "Look  ·  walk   (Shift: run)"),
        ("E", "Connect cable: 1st device, then 2nd"),
        ("X", "Unplug / replug cable: 1st, then 2nd"),
        ("P", "Send packet: source, then destination"),
        ("Q / Esc", "Cancel cable / packet / placing"),
        ("F  or  Click", "Device settings (IP, mask, gateway)"),
        ("C", "Console on aimed device (ping…)"),
        ("G", "Power aimed device off / on"),
        ("1 2 3 4", "Place PC / Switch / Router / Server"),
        ("Delete", "Remove aimed device"),
        ("Alt (hold)", "Free mouse to use panels"),
        ("V  ·  Tab", "Overlay  ·  overview camera"),
        ("H", "Hide these controls"),
    };

    void SetHelpVisible(bool visible)
    {
        helpVisible = visible;
        helpPanel.gameObject.SetActive(visible);
        helpTag.gameObject.SetActive(!visible);
    }

    void BuildHelp()
    {
        Canvas canvas = LabUI.CreateCanvas("WalkControlsCanvas", 42);
        helpCanvas = canvas.gameObject;
        helpCanvas.transform.SetParent(transform, false);

        helpPanel = LabUI.Panel(canvas.transform, "WalkControls", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -560f), new Vector2(350f, 0f));
        VerticalLayoutGroup layout = LabUI.Vertical(helpPanel, 0f, 9);
        layout.childForceExpandHeight = false;
        helpPanel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = LabUI.Label(helpPanel, "WALK MODE CONTROLS", 14f, UITheme.Accent, TextAlignmentOptions.Left, 22f);
        title.fontStyle = FontStyles.Bold;

        foreach (var (keys, action) in Controls)
        {
            RectTransform row = LabUI.Row(helpPanel, 17f, 8f);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;

            TextMeshProUGUI key = LabUI.Label(row, keys, 13f, UITheme.Warning, TextAlignmentOptions.Right);
            key.fontStyle = FontStyles.Bold;
            key.textWrappingMode = TextWrappingModes.NoWrap;
            key.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;

            TextMeshProUGUI text = LabUI.Label(row, action, 13f, UITheme.TextPrimary);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        helpTag = LabUI.Panel(canvas.transform, "WalkControlsTag", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -560f), new Vector2(190f, 30f));
        TextMeshProUGUI tag = LabUI.Label(helpTag, "<b>H</b>  show controls", 14f, UITheme.TextSecondary, TextAlignmentOptions.Center);
        LabUI.Stretch(tag.rectTransform);

        helpCanvas.SetActive(false);
    }
}
