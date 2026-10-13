using TMPro;
using UnityEngine;

public class DeviceInfoPanel : MonoBehaviour
{
    public TMP_Text infoText;

    public RectTransform panelRect;
    public RectTransform textRect;

    public float padding = 40f;
    public float minHeight = 250f;
    public float maxHeight = 450f;

    public void ShowDevice(NetworkDevice device)
    {
        if (device == null)
            return;

        gameObject.SetActive(true);

        var connections = NetworkConnection.All;

        string connectedDevices = "";

        foreach (NetworkConnection connection in connections)
        {
            if (!connection.isConnected)
                continue;

            NetworkDevice connectedDevice = null;

            if (connection.deviceA == device)
            {
                connectedDevice = connection.deviceB;
            }
            else if (connection.deviceB == device)
            {
                connectedDevice = connection.deviceA;
            }

            if (connectedDevice != null)
            {
                connectedDevices +=
                    "→ " +
                    connectedDevice.deviceName +
                    "\n";
            }
        }

        if (connectedDevices == "")
        {
            connectedDevices = "No active connections";
        }

        infoText.text =
            "DEVICE INFORMATION\n\n" +
            "Name: " + device.deviceName + "\n" +
            "IP: " + device.ipAddress + "\n" +
            "Status: " + (device.isOnline ? "● ONLINE" : "○ OFFLINE") + "\n\n" +
            "CONNECTED TO\n" +
            connectedDevices;

        Canvas.ForceUpdateCanvases();

        float textHeight = infoText.preferredHeight;

        float newHeight = Mathf.Clamp(
            textHeight + padding,
            minHeight,
            maxHeight
        );

        panelRect.sizeDelta = new Vector2(
            panelRect.sizeDelta.x,
            newHeight
        );

        textRect.sizeDelta = new Vector2(
            textRect.sizeDelta.x,
            textHeight
        );

        textRect.anchoredPosition = new Vector2(
            textRect.anchoredPosition.x,
            -(newHeight - textHeight) / 2f
        );
    }

    public void HidePanel()
    {
        gameObject.SetActive(false);
    }
}