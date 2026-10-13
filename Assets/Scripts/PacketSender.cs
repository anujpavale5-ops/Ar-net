using UnityEngine;

public class PacketSender : MonoBehaviour
{
    [Header("Core References")]
    public NetworkManager networkManager;

    [Header("State")]
    public bool isSendingMode = false;
    private NetworkDevice selectedSource;

    void Start()
    {
        if (networkManager == null) networkManager = FindAnyObjectByType<NetworkManager>();
    }

    public void ToggleSendingMode()
    {
        isSendingMode = !isSendingMode;
        selectedSource = null;
        Debug.Log("Send Packet Mode is now: " + (isSendingMode ? "ON" : "OFF"));
    }

    public void SelectDevice(NetworkDevice device)
    {
        if (!isSendingMode || device == null) return;

        if (selectedSource == null)
        {
            selectedSource = device;
            Debug.Log("Packet Source Selected: " + selectedSource.deviceName);
            return;
        }

        if (selectedSource == device) return;

        if (networkManager != null) networkManager.SendPacket(selectedSource, device);

        selectedSource = null;
        isSendingMode = false;
    }
}
