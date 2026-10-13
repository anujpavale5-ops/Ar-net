using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PacketUIManager : MonoBehaviour
{
    public TMP_Dropdown sourceDropdown;
    public TMP_Dropdown destinationDropdown;
    public NetworkLogUI networkLog;

    private NetworkManager networkManager;
    private readonly List<NetworkDevice> devices = new List<NetworkDevice>();
    private int loadedVersion = -1;

    void Start()
    {
        networkManager = FindAnyObjectByType<NetworkManager>();

        if (networkManager == null)
        {
            Debug.LogError("PacketUIManager: NetworkManager not found.");
            return;
        }

        if (networkLog == null) networkLog = FindAnyObjectByType<NetworkLogUI>();

        if (sourceDropdown != null) sourceDropdown.onValueChanged.AddListener(_ => PreviewSelectedRoute());
        if (destinationDropdown != null) destinationDropdown.onValueChanged.AddListener(_ => PreviewSelectedRoute());

        LoadDevices();
    }

    void OnEnable()
    {
        LoadDevices();
    }

    void LateUpdate()
    {
        // Devices get their final name right after spawning, so refresh lazily once per change
        if (loadedVersion != NetworkDevice.TopologyVersion) LoadDevices();
    }

    public void LoadDevices()
    {
        if (sourceDropdown == null || destinationDropdown == null) return;

        loadedVersion = NetworkDevice.TopologyVersion;

        string currentSource = SelectedName(sourceDropdown);
        string currentDest = SelectedName(destinationDropdown);

        devices.Clear();
        foreach (NetworkDevice device in NetworkDevice.All)
        {
            if (device != null && !device.isPreviewModel && !device.IsSwitch) devices.Add(device);
        }
        devices.Sort((a, b) => string.CompareOrdinal(a.deviceName, b.deviceName));

        List<string> deviceNames = new List<string> { "-- Select Device --" };
        foreach (NetworkDevice device in devices) deviceNames.Add(device.deviceName);

        sourceDropdown.ClearOptions();
        destinationDropdown.ClearOptions();
        sourceDropdown.AddOptions(deviceNames);
        destinationDropdown.AddOptions(deviceNames);

        sourceDropdown.SetValueWithoutNotify(Mathf.Max(0, deviceNames.IndexOf(currentSource)));
        destinationDropdown.SetValueWithoutNotify(Mathf.Max(0, deviceNames.IndexOf(currentDest)));

        sourceDropdown.RefreshShownValue();
        destinationDropdown.RefreshShownValue();

        PreviewSelectedRoute();
    }

    static string SelectedName(TMP_Dropdown dropdown)
    {
        return dropdown.options.Count > dropdown.value ? dropdown.options[dropdown.value].text : "";
    }

    NetworkDevice Selected(TMP_Dropdown dropdown)
    {
        int index = dropdown.value - 1;
        return index >= 0 && index < devices.Count ? devices[index] : null;
    }

    void PreviewSelectedRoute()
    {
        if (NetworkVisualizer.Instance == null || networkManager == null) return;

        NetworkDevice source = Selected(sourceDropdown);
        NetworkDevice destination = Selected(destinationDropdown);

        if (source == null || destination == null || source == destination)
        {
            NetworkVisualizer.Instance.ClearRoutePreview();
            return;
        }

        List<NetworkDevice> route = networkManager.BuildRoute(source, destination, out string reason);
        if (route != null) NetworkVisualizer.Instance.ShowRoute(route, true);
        else NetworkVisualizer.Instance.ShowRoute(new List<NetworkDevice> { source, destination }, false, 0f, reason);
    }

    public void SendPacket()
    {
        if (networkManager == null) return;

        NetworkDevice source = Selected(sourceDropdown);
        NetworkDevice destination = Selected(destinationDropdown);

        if (source == null || destination == null)
        {
            Debug.LogWarning("Please select both a valid source and destination.");
            return;
        }

        networkManager.SendPacket(source, destination);
    }
}
