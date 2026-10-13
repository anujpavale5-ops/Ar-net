using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ConnectionUIManager : MonoBehaviour
{
    public TMP_Dropdown connectionDropdown;
    public Button connectionButton;
    public TMP_Text connectionStatusText;

    private readonly List<NetworkConnection> validConnections = new List<NetworkConnection>();
    private NetworkConnection selectedConnection;
    private int loadedVersion = -1;

    void Start()
    {
        if (connectionButton != null)
        {
            connectionButton.onClick.RemoveAllListeners();
            connectionButton.onClick.AddListener(ToggleConnection);
        }

        if (connectionDropdown != null)
        {
            connectionDropdown.onValueChanged.RemoveAllListeners();
            connectionDropdown.onValueChanged.AddListener(OnConnectionSelected);
        }
    }

    void OnEnable()
    {
        LoadConnections();
    }

    void LateUpdate()
    {
        if (loadedVersion != NetworkDevice.TopologyVersion) LoadConnections();
    }

    public void LoadConnections()
    {
        if (connectionDropdown == null)
        {
            Debug.LogError("ConnectionUIManager: Connection Dropdown reference is NULL.");
            return;
        }

        loadedVersion = NetworkDevice.TopologyVersion;

        NetworkConnection previousSelection = selectedConnection;

        validConnections.Clear();
        List<string> options = new List<string> { "-- Select Connection --" };

        foreach (NetworkConnection connection in NetworkConnection.All)
        {
            if (connection == null || connection.deviceA == null || connection.deviceB == null)
                continue;

            validConnections.Add(connection);
            options.Add(connection.deviceA.deviceName + " ↔ " + connection.deviceB.deviceName);
        }

        connectionDropdown.ClearOptions();
        connectionDropdown.AddOptions(options);

        int previousIndex = previousSelection != null ? validConnections.IndexOf(previousSelection) : -1;

        if (previousIndex >= 0)
        {
            connectionDropdown.SetValueWithoutNotify(previousIndex + 1);
            selectedConnection = previousSelection;
            UpdateUI();
        }
        else
        {
            connectionDropdown.SetValueWithoutNotify(0);
            ShowWaiting();
        }

        connectionDropdown.RefreshShownValue();
    }

    void ShowWaiting()
    {
        selectedConnection = null;
        if (connectionButton != null) connectionButton.gameObject.SetActive(false);
        if (connectionStatusText != null)
        {
            connectionStatusText.text = "Status: WAITING";
            connectionStatusText.color = UITheme.TextSecondary;
        }
    }

    void OnConnectionSelected(int index)
    {
        int adjustedIndex = index - 1;

        if (adjustedIndex < 0 || adjustedIndex >= validConnections.Count)
        {
            ShowWaiting();
            return;
        }

        selectedConnection = validConnections[adjustedIndex];
        UpdateUI();
    }

    void ToggleConnection()
    {
        if (selectedConnection == null)
            return;

        if (selectedConnection.isConnected) selectedConnection.Disconnect();
        else selectedConnection.Reconnect();

        UpdateUI();
    }

    void UpdateUI()
    {
        if (selectedConnection == null)
            return;

        if (connectionButton != null) connectionButton.gameObject.SetActive(true);

        if (connectionStatusText != null)
        {
            connectionStatusText.text = selectedConnection.isConnected ? "Status: ● CONNECTED" : "Status: ○ DISCONNECTED";
            connectionStatusText.color = selectedConnection.isConnected ? UITheme.Success : UITheme.Failure;
        }

        if (connectionButton != null)
        {
            TMP_Text buttonText = connectionButton.GetComponentInChildren<TMP_Text>();
            if (buttonText != null) buttonText.text = selectedConnection.isConnected ? "DISCONNECT" : "RECONNECT";

            // Amber to unplug, blue to plug back in
            if (connectionButton.image != null)
                connectionButton.image.color = selectedConnection.isConnected ? UITheme.ButtonAmber : UITheme.ButtonBlue;
        }
    }
}
