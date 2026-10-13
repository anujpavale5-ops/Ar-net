using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Header("UI Panels")]
    [Tooltip("Drag the Add Device Panel here in the Inspector")]
    public GameObject deviceSelectionPanel;

    [Header("Device Placement")]
    [Tooltip("Drag the DevicePlacementManager object here")]
    public DevicePlacementManager devicePlacementManager;

    private void Start()
    {
        if (deviceSelectionPanel != null)
        {
            deviceSelectionPanel.SetActive(false);
        }
    }

    public void OpenDevicePanel()
    {
        // --- NEW: Force the user to drop their deletion/connection tools when opening this menu ---
        ClearActiveModes();

        if (deviceSelectionPanel != null)
        {
            deviceSelectionPanel.SetActive(true);
        }
    }

    public void CloseDevicePanel()
    {
        if (deviceSelectionPanel != null)
        {
            deviceSelectionPanel.SetActive(false);
        }
    }

    public void SelectPC()
    {
        if (devicePlacementManager != null)
        {
            devicePlacementManager.SelectPC();
            CloseDevicePanel();
        }
    }

    public void SelectSwitch()
    {
        if (devicePlacementManager != null)
        {
            devicePlacementManager.SelectSwitch();
            CloseDevicePanel();
        }
    }

    public void SelectRouter()
    {
        if (devicePlacementManager != null)
        {
            devicePlacementManager.SelectRouter();
            CloseDevicePanel();
        }
    }

    public void SelectServer()
    {
        if (devicePlacementManager != null)
        {
            devicePlacementManager.SelectServer();
            CloseDevicePanel();
        }
    }

    // --- NEW HELPER METHOD ---
  private void ClearActiveModes()
    {
        // 1. Check for Deletion Mode and turn it off
        DeviceDeletionManager deleteManager = FindAnyObjectByType<DeviceDeletionManager>();
        if (deleteManager != null && deleteManager.isDeletionMode)
        {
            deleteManager.ToggleDeletionMode(); 
        }

        // 2. Check for Connection Mode and turn it off
        DeviceConnector connectManager = FindAnyObjectByType<DeviceConnector>();
        
        // --- FIXED: Now using the exact names from your DeviceConnector script ---
        if (connectManager != null && connectManager.isConnectingMode)
        {
            connectManager.ToggleConnectingMode(); 
        }
    }
}