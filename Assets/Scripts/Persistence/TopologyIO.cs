using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Saves the current lab to JSON, loads it back, and builds labs from TopologyData (examples, missions).
public class TopologyIO : MonoBehaviour
{
    public static TopologyIO Instance { get; private set; }

    public static string SaveFolder => Path.Combine(Application.persistentDataPath, "ArNetLabs");
    public static string QuickSavePath => Path.Combine(SaveFolder, "my_lab.json");

    public bool IsLoading { get; private set; }
    public event System.Action<TopologyData> Loaded;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ---------- capture / save ----------

    public static TopologyData Capture(string labName = "My Lab")
    {
        TopologyData data = new TopologyData { name = labName };

        foreach (NetworkDevice device in NetworkDevice.All)
        {
            if (device == null || device.isPreviewModel) continue;

            DeviceData entry = data.Add(device.deviceType, device.deviceName, device.transform.position.x, device.transform.position.z,
                device.ipAddress, device.subnetMask, device.gateway);
            entry.online = device.isOnline;
            foreach (AclRule rule in device.accessList) entry.acl.Add(rule.ToString());
        }

        foreach (NetworkConnection connection in NetworkConnection.All)
        {
            if (connection == null || connection.deviceA == null || connection.deviceB == null) continue;
            data.Link(connection.deviceA.deviceName, connection.deviceB.deviceName, connection.isConnected);
        }

        return data;
    }

    public static string Save(string path = null)
    {
        path = path ?? QuickSavePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(Capture(), true));
        return path;
    }

    public static bool HasQuickSave => File.Exists(QuickSavePath);

    public static TopologyData ReadFile(string path = null)
    {
        path = path ?? QuickSavePath;
        if (!File.Exists(path)) return null;
        return JsonUtility.FromJson<TopologyData>(File.ReadAllText(path));
    }

    // ---------- load ----------

    public void Load(TopologyData data)
    {
        if (data == null || IsLoading) return;
        StartCoroutine(LoadRoutine(data));
    }

    public static void ClearLab()
    {
        DevicePlacementManager placement = FindAnyObjectByType<DevicePlacementManager>();
        if (placement != null) placement.CancelPlacement();

        foreach (Packet packet in FindObjectsByType<Packet>()) Destroy(packet.gameObject);

        foreach (NetworkConnection connection in new List<NetworkConnection>(NetworkConnection.All))
        {
            if (connection != null) Destroy(connection.gameObject);
        }

        foreach (NetworkDevice device in new List<NetworkDevice>(NetworkDevice.All))
        {
            if (device != null && !device.isPreviewModel) Destroy(device.gameObject);
        }

        if (NetworkStatistics.Instance != null) NetworkStatistics.Instance.ResetStatistics();

        NetworkLogUI log = FindAnyObjectByType<NetworkLogUI>();
        if (log != null) log.ClearLog();

        if (NetworkVisualizer.Instance != null) NetworkVisualizer.Instance.ClearRoutePreview();
        SimulationClock.Paused = false;
    }

    IEnumerator LoadRoutine(TopologyData data)
    {
        IsLoading = true;
        ClearLab();

        // Let the destroyed objects disappear before re-creating devices with the same names
        yield return null;

        DevicePlacementManager placement = FindAnyObjectByType<DevicePlacementManager>();
        NetworkManager manager = FindAnyObjectByType<NetworkManager>();
        DeviceConnector connector = FindAnyObjectByType<DeviceConnector>();

        Dictionary<string, NetworkDevice> byName = new Dictionary<string, NetworkDevice>();

        foreach (DeviceData entry in data.devices)
        {
            NetworkDevice device = placement != null ? placement.SpawnDevice(entry.type, new Vector3(entry.x, 0f, entry.z), entry.name) : null;
            if (device == null) continue;

            device.SetNetworkConfiguration(entry.ip ?? "", entry.mask ?? "", entry.gateway ?? "");
            device.accessList.Clear();
            foreach (string ruleText in entry.acl)
            {
                if (AclRule.TryParse(ruleText, out AclRule rule, out _)) device.accessList.Add(rule);
            }

            byName[entry.name] = device;
        }

        // Labels and LEDs initialise in Start, so apply power state a frame later
        yield return null;

        foreach (DeviceData entry in data.devices)
        {
            if (!byName.TryGetValue(entry.name, out NetworkDevice device)) continue;

            FloatingLabel label = device.GetComponentInChildren<FloatingLabel>();
            if (label != null) label.RefreshText();
            if (!entry.online) device.SetStatus(false);
        }

        if (manager != null && connector != null)
        {
            foreach (LinkData link in data.links)
            {
                if (!byName.TryGetValue(link.a, out NetworkDevice a) || !byName.TryGetValue(link.b, out NetworkDevice b)) continue;

                NetworkConnection connection = manager.ConnectDevices(a, b, connector.cablePrefab);
                if (connection != null && !link.connected)
                {
                    connection.isConnected = false;
                    NetworkDevice.MarkTopologyChanged();
                }
            }
        }

        LabViewMode viewMode = LabViewMode.Instance;
        if (viewMode != null && !viewMode.IsFirstPerson)
        {
            LabCameraController orbit = viewMode.GetComponent<LabCameraController>();
            if (orbit != null) orbit.FrameDevices();
        }

        IsLoading = false;
        Loaded?.Invoke(data);
    }
}
