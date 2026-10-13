using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class DeviceDeletionManager : MonoBehaviour
{
    [Header("State")]
    public bool isDeletionMode = false;
    public Camera mainCamera;

    [Header("UI Elements")]
    public TMP_Text deleteButtonText;

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;
    }

    public void ToggleDeletionMode()
    {
        isDeletionMode = !isDeletionMode;

        // Only one tool mode at a time
        DeviceConnector connector = FindAnyObjectByType<DeviceConnector>();
        if (connector != null && isDeletionMode && connector.isConnectingMode)
        {
            connector.ToggleConnectingMode();
        }

        UpdateUIButton();
    }

    public void UpdateUIButton()
    {
        if (deleteButtonText == null) return;

        deleteButtonText.text = isDeletionMode ? "CANCEL DELETE" : "DELETE DEVICE";
        UITheme.SetButtonActive(deleteButtonText, isDeletionMode, new Color(0.55f, 0.1f, 0.1f));
    }

    void Update()
    {
        // Desktop shortcut: Delete key removes the hovered device. Clicks are handled by
        // NetworkDevice.OnPointerClick so UI buttons on top of a device block them.
        if (Keyboard.current == null || !Keyboard.current.deleteKey.wasPressedThisFrame) return;
        if (PointerUtil.IsTyping()) return;

        TryDeleteDeviceUnderPointer();
    }

    void TryDeleteDeviceUnderPointer()
    {
        if (mainCamera == null || Mouse.current == null) return;

        Vector2 mousePosition = PointerUtil.ScreenPoint;
        if (PointerUtil.IsOverUI(mousePosition)) return;

        Ray ray = mainCamera.ScreenPointToRay(mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            NetworkDevice device = hit.collider.GetComponentInParent<NetworkDevice>();
            if (device != null && !device.isPreviewModel)
            {
                ExecuteDeletion(device);
            }
        }
    }

    public void DeleteDevice(NetworkDevice device)
    {
        if (!isDeletionMode || device == null) return;
        ExecuteDeletion(device);
    }

    private void ExecuteDeletion(NetworkDevice device)
    {
        string deletedName = device.deviceName;

        DeviceConfigUI configUI = FindAnyObjectByType<DeviceConfigUI>();
        if (configUI != null) configUI.CloseIfEditing(device);

        for (int i = NetworkConnection.All.Count - 1; i >= 0; i--)
        {
            NetworkConnection cable = NetworkConnection.All[i];
            if (cable != null && (cable.deviceA == device || cable.deviceB == device))
            {
                Destroy(cable.gameObject);
            }
        }

        Destroy(device.gameObject);
        Debug.Log("Deleted device: " + deletedName);
    }
}
