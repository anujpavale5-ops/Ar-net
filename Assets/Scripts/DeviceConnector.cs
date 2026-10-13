using UnityEngine;
using TMPro;

public class DeviceConnector : MonoBehaviour
{
    [Header("Core References")]
    public NetworkManager networkManager;
    public GameObject cablePrefab;

    [Header("UI Elements")]
    public TMP_Text connectButtonText;

    [Header("State")]
    public bool isConnectingMode = false;
    private NetworkDevice selectedDeviceA;

    public NetworkDevice PendingDevice => isConnectingMode ? selectedDeviceA : null;

    void Start()
    {
        if (networkManager == null) networkManager = FindAnyObjectByType<NetworkManager>();
    }

    public void ToggleConnectingMode()
    {
        isConnectingMode = !isConnectingMode;
        selectedDeviceA = null;

        // Only one tool mode at a time
        DeviceDeletionManager deleteManager = FindAnyObjectByType<DeviceDeletionManager>();
        if (deleteManager != null && isConnectingMode && deleteManager.isDeletionMode)
        {
            deleteManager.ToggleDeletionMode();
        }

        UpdateUIButton();
    }

    public void UpdateUIButton()
    {
        if (connectButtonText == null) return;

        connectButtonText.text = !isConnectingMode ? "CONNECT DEVICES"
            : selectedDeviceA == null ? "CANCEL CONNECT" : "SELECT 2ND DEVICE";
        UITheme.SetButtonActive(connectButtonText, isConnectingMode, UITheme.ButtonAmber);
    }

    public void SelectDevice(NetworkDevice device)
    {
        if (!isConnectingMode || device == null) return;

        if (selectedDeviceA == null)
        {
            selectedDeviceA = device;
            UpdateUIButton();
            return;
        }

        if (selectedDeviceA == device) return;

        if (networkManager != null && cablePrefab != null)
        {
            networkManager.ConnectDevices(selectedDeviceA, device, cablePrefab);
        }
        else
        {
            Debug.LogError("DeviceConnector: NetworkManager or cable prefab not assigned.");
        }

        // Stay in connect mode so several cables can be laid in a row
        selectedDeviceA = null;
        UpdateUIButton();
    }
}
