using UnityEngine;
using TMPro;

public class FloatingLabel : MonoBehaviour
{
    private Camera mainCamera;

    [Header("References")]
    public TMP_Text labelText;
    public NetworkDevice networkDevice;

    void Start()
    {
        mainCamera = Camera.main;

        if (networkDevice == null)
        {
            networkDevice = GetComponentInParent<NetworkDevice>();
        }

        if (networkDevice != null && networkDevice.isPreviewModel)
        {
            gameObject.SetActive(false); 
            return; 
        }

        // Dark outline keeps white labels readable against the light floor
        if (labelText != null)
        {
            labelText.outlineWidth = 0.25f;
            labelText.outlineColor = new Color32(15, 18, 24, 255);
        }

        RefreshText();
    }

    // Extracted to a public method so the Terminal can call it directly!
    public void RefreshText()
    {
        if (networkDevice != null && labelText != null)
        {
            if (!networkDevice.isOnline)
            {
                labelText.text = networkDevice.deviceName + "\n<size=70%><color=#FF6A5E>OFFLINE</color></size>";
                return;
            }

            // 1. Check if the device is a Switch (make sure your prefab's Device Type is "Switch")
            if (networkDevice.IsSwitch)
            {
                // Switches don't need IPs, so just show the name
                labelText.text = networkDevice.deviceName;
            }
            else
            {
                // 2. For PCs, Routers, and Servers, check if the IP is configured
                if (string.IsNullOrEmpty(networkDevice.ipAddress))
                {
                    // No IP yet, show the yellow warning
                    labelText.text = networkDevice.deviceName + "\n<size=70%><color=yellow>Unconfigured</color></size>";
                }
                else
                {
                    // IP is configured, show the name and the IP
                    labelText.text = networkDevice.deviceName + "\n<size=70%>" + networkDevice.ipAddress + "</size>";
                }
            }
        }
    }

    void LateUpdate()
    {
        if (mainCamera != null)
        {
            transform.rotation = mainCamera.transform.rotation;
        }
    }
}