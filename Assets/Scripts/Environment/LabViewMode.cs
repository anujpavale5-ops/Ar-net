using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Switches the main camera between the orbit overview and first-person walk mode (Tab).
// In walk mode the cursor is captured for mouse-look and a crosshair does the aiming;
// holding Alt (or opening a panel / typing) frees the cursor for the UI.
[RequireComponent(typeof(Camera))]
public class LabViewMode : MonoBehaviour
{
    public static LabViewMode Instance { get; private set; }

    public bool IsFirstPerson { get; private set; }

    // Cursor locked and hidden, mouse movement turns the head
    public bool CursorCaptured { get; private set; }

    [Tooltip("Furthest distance the crosshair can use a device from")]
    public float reach = 12f;

    private LabCameraController orbit;
    private FirstPersonController walker;
    private FirstPersonHands hands;
    private Camera cam;

    private DevicePlacementManager placement;
    private DeviceConfigUI configUI;
    private UIManager uiManager;

    private GameObject crosshairCanvas;
    private Image crosshairDot;
    private TextMeshProUGUI targetLabel;
    private NetworkDevice aimedDevice;

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

        BuildCrosshair();
    }

    void Start()
    {
        placement = FindAnyObjectByType<DevicePlacementManager>();
        configUI = FindAnyObjectByType<DeviceConfigUI>();
        uiManager = FindAnyObjectByType<UIManager>();
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

        if (keyboard != null && !typing) HandleDeviceHotkeys(keyboard);

        // Crosshair click on a device (placement clicks are handled by DevicePlacementManager)
        Mouse mouse = Mouse.current;
        if (CursorCaptured && mouse != null && mouse.leftButton.wasPressedThisFrame && aimedDevice != null &&
            (placement == null || !placement.IsPlacing))
        {
            aimedDevice.HandleClick();
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
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, reach))
            {
                NetworkDevice device = hit.collider.GetComponentInParent<NetworkDevice>();
                if (device != null && !device.isPreviewModel) aimedDevice = device;
            }
        }

        bool aiming = aimedDevice != null;
        crosshairDot.color = aiming ? UITheme.Accent : new Color(1f, 1f, 1f, 0.85f);
        crosshairDot.rectTransform.sizeDelta = Vector2.one * (aiming ? 10f : 6f);

        if (aiming)
        {
            string ip = aimedDevice.IsSwitch ? "Layer-2 switch"
                : string.IsNullOrEmpty(aimedDevice.ipAddress) ? "<color=#FFC04D>Unconfigured</color>"
                : aimedDevice.ipAddress;
            targetLabel.text = $"<b>{aimedDevice.deviceName}</b>\n<size=80%>{ip}  ·  click to use</size>";
        }
        targetLabel.gameObject.SetActive(aiming);
    }

    // 1-4 pick up a device to place without leaving walk mode
    void HandleDeviceHotkeys(Keyboard keyboard)
    {
        if (placement == null) return;

        if (keyboard.digit1Key.wasPressedThisFrame) placement.SelectPC();
        else if (keyboard.digit2Key.wasPressedThisFrame) placement.SelectSwitch();
        else if (keyboard.digit3Key.wasPressedThisFrame) placement.SelectRouter();
        else if (keyboard.digit4Key.wasPressedThisFrame) placement.SelectServer();
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
        if (NetworkVisualizer.Instance != null) NetworkVisualizer.Instance.RefreshLegend();
    }

    public void EnterOverview()
    {
        if (!IsFirstPerson) return;

        ReleaseCursor();
        crosshairCanvas.SetActive(false);
        aimedDevice = null;

        hands.enabled = false;
        walker.enabled = false;
        orbit.enabled = true;
        orbit.ResumeFromCurrentTransform();

        IsFirstPerson = false;
        if (NetworkVisualizer.Instance != null) NetworkVisualizer.Instance.RefreshLegend();
    }

    // ---------- crosshair UI ----------

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

        GameObject label = new GameObject("Target", typeof(RectTransform));
        label.transform.SetParent(crosshairCanvas.transform, false);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchoredPosition = new Vector2(0f, -42f);
        labelRect.sizeDelta = new Vector2(500f, 60f);

        targetLabel = label.AddComponent<TextMeshProUGUI>();
        targetLabel.alignment = TextAlignmentOptions.Center;
        targetLabel.fontSize = 22f;
        targetLabel.color = UITheme.TextPrimary;
        targetLabel.raycastTarget = false;
        targetLabel.outlineWidth = 0.2f;
        targetLabel.outlineColor = new Color32(10, 14, 20, 255);
        label.SetActive(false);

        crosshairCanvas.SetActive(false);
    }
}
