using System.Collections.Generic;
using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    public GameObject packetPrefab;

    [Tooltip("Height above the device pivot that packets travel at")]
    public float packetHeight = 0.3f;

    private NetworkLogUI networkLog;

    public IEnumerable<NetworkDevice> Devices
    {
        get
        {
            foreach (NetworkDevice device in NetworkDevice.All)
            {
                if (device != null && !device.isPreviewModel) yield return device;
            }
        }
    }

    void Start()
    {
        networkLog = FindAnyObjectByType<NetworkLogUI>();
    }

    NetworkLogUI Log
    {
        get
        {
            if (networkLog == null) networkLog = FindAnyObjectByType<NetworkLogUI>();
            return networkLog;
        }
    }

    public NetworkConnection ConnectDevices(NetworkDevice a, NetworkDevice b, GameObject cablePrefab)
    {
        if (a == null || b == null || a == b)
        {
            Debug.LogWarning("ConnectDevices: Invalid device selection.");
            return null;
        }

        NetworkConnection existing = NetworkConnection.Between(a, b);
        if (existing != null)
        {
            if (!existing.isConnected) existing.Reconnect();
            else Debug.LogWarning($"{a.deviceName} and {b.deviceName} are already connected!");
            return existing;
        }

        if (cablePrefab == null)
        {
            Debug.LogError("ConnectDevices: Cable prefab is not assigned.");
            return null;
        }

        GameObject cableObject = Instantiate(cablePrefab, Vector3.zero, Quaternion.identity);
        NetworkConnection connection = cableObject.GetComponent<NetworkConnection>();

        if (connection == null)
        {
            Debug.LogError("ConnectDevices: Cable prefab is missing the NetworkConnection script!");
            Destroy(cableObject);
            return null;
        }

        connection.deviceA = a;
        connection.deviceB = b;
        connection.isConnected = true;
        connection.ResetVisual();
        NetworkDevice.MarkTopologyChanged();
        LabSounds.Play(LabSound.Plug);

        return connection;
    }

    public bool AreConnected(NetworkDevice deviceA, NetworkDevice deviceB)
    {
        NetworkConnection connection = NetworkConnection.Between(deviceA, deviceB);
        return connection != null && connection.isConnected;
    }

    // Shortest physical path. Only switches (and routers when allowRouters) may forward traffic;
    // PCs and servers are always endpoints.
    public List<NetworkDevice> FindPath(NetworkDevice start, NetworkDevice target, bool allowRouters = true)
    {
        List<NetworkDevice> emptyPath = new List<NetworkDevice>();

        if (start == null || target == null || !start.isOnline || !target.isOnline)
            return emptyPath;

        if (start == target)
            return new List<NetworkDevice> { start };

        Dictionary<NetworkDevice, List<NetworkDevice>> adjacency = BuildAdjacency();
        Dictionary<NetworkDevice, NetworkDevice> previous = new Dictionary<NetworkDevice, NetworkDevice>();
        Queue<NetworkDevice> queue = new Queue<NetworkDevice>();

        queue.Enqueue(start);
        previous[start] = null;

        while (queue.Count > 0)
        {
            NetworkDevice current = queue.Dequeue();
            if (current == target) break;

            bool canForward = current == start || current.IsSwitch || (allowRouters && current.IsRouter);
            if (!canForward || !adjacency.TryGetValue(current, out List<NetworkDevice> neighbors))
                continue;

            foreach (NetworkDevice neighbor in neighbors)
            {
                if (!neighbor.isOnline || previous.ContainsKey(neighbor)) continue;

                previous[neighbor] = current;
                queue.Enqueue(neighbor);
            }
        }

        if (!previous.ContainsKey(target))
            return emptyPath;

        List<NetworkDevice> path = new List<NetworkDevice>();
        for (NetworkDevice node = target; node != null; node = previous[node])
        {
            path.Add(node);
        }
        path.Reverse();
        return path;
    }

    Dictionary<NetworkDevice, List<NetworkDevice>> BuildAdjacency()
    {
        Dictionary<NetworkDevice, List<NetworkDevice>> adjacency = new Dictionary<NetworkDevice, List<NetworkDevice>>();

        foreach (NetworkConnection connection in NetworkConnection.All)
        {
            if (connection == null || !connection.isConnected) continue;
            if (connection.deviceA == null || connection.deviceB == null) continue;

            AddEdge(adjacency, connection.deviceA, connection.deviceB);
            AddEdge(adjacency, connection.deviceB, connection.deviceA);
        }
        return adjacency;
    }

    static void AddEdge(Dictionary<NetworkDevice, List<NetworkDevice>> adjacency, NetworkDevice from, NetworkDevice to)
    {
        if (!adjacency.TryGetValue(from, out List<NetworkDevice> list))
        {
            list = new List<NetworkDevice>();
            adjacency[from] = list;
        }
        list.Add(to);
    }

    // Works out the route a packet would really take: switched within a subnet,
    // via the gateway router(s) between subnets. Returns null and a reason on failure.
    public List<NetworkDevice> BuildRoute(NetworkDevice source, NetworkDevice destination, out string reason)
    {
        reason = null;

        if (source == null || destination == null) { reason = "Select a source and destination"; return null; }
        if (source == destination) { reason = "Source and destination are the same device"; return null; }
        if (!source.isOnline) { reason = source.deviceName + " is offline"; return null; }
        if (!destination.isOnline) { reason = destination.deviceName + " is offline"; return null; }

        if (source.IsSwitch || destination.IsSwitch)
        {
            reason = "Switches are layer-2 devices and cannot send or receive packets";
            return null;
        }

        if (!source.IsConfigured) { reason = source.deviceName + " has no valid IP / subnet mask"; return null; }
        if (!destination.IsConfigured) { reason = destination.deviceName + " has no valid IP / subnet mask"; return null; }

        if (FindAddressConflict(source, out reason) || FindAddressConflict(destination, out reason))
            return null;

        bool sameSubnet = source.SharesSubnetWith(destination) && destination.SharesSubnetWith(source);

        if (sameSubnet)
        {
            List<NetworkDevice> lanPath = FindPath(source, destination, allowRouters: false);
            if (lanPath.Count < 2)
            {
                reason = "No switched path inside the subnet (check cables)";
                return null;
            }
            return lanPath;
        }

        NetworkDevice sourceGateway = ResolveGateway(source, out reason);
        if (sourceGateway == null) { reason = "Destination unreachable: " + reason; return null; }

        NetworkDevice destinationGateway = ResolveGateway(destination, out reason);
        if (destinationGateway == null) { reason = "Request timed out (no reply path): " + reason; return null; }

        List<NetworkDevice> route = new List<NetworkDevice>();

        if (!AppendSegment(route, source, sourceGateway, false))
        {
            reason = $"{source.deviceName} cannot reach its gateway {sourceGateway.deviceName}";
            return null;
        }

        if (!AppendSegment(route, sourceGateway, destinationGateway, true))
        {
            reason = $"No route between routers {sourceGateway.deviceName} and {destinationGateway.deviceName}";
            return null;
        }

        if (!AppendSegment(route, destinationGateway, destination, false))
        {
            reason = $"{destinationGateway.deviceName} cannot reach {destination.deviceName}";
            return null;
        }

        if (BlockedByAcl(route, source, destination, out reason)) return null;

        return route;
    }

    // Any router on the path can drop the traffic with its access list
    static bool BlockedByAcl(List<NetworkDevice> route, NetworkDevice source, NetworkDevice destination, out string reason)
    {
        reason = null;
        uint sourceAddress = source.Addresses[0];
        uint destinationAddress = destination.Addresses[0];

        foreach (NetworkDevice hop in route)
        {
            if (hop.Blocks(sourceAddress, destinationAddress, out AclRule rule))
            {
                reason = $"Blocked by ACL on {hop.deviceName} ({rule})";
                return true;
            }
        }
        return false;
    }

    bool AppendSegment(List<NetworkDevice> route, NetworkDevice from, NetworkDevice to, bool allowRouters)
    {
        if (from == to)
        {
            if (route.Count == 0) route.Add(from);
            return true;
        }

        List<NetworkDevice> segment = FindPath(from, to, allowRouters);
        if (segment.Count < 2) return false;

        int startIndex = route.Count > 0 && route[route.Count - 1] == segment[0] ? 1 : 0;
        for (int i = startIndex; i < segment.Count; i++)
        {
            route.Add(segment[i]);
        }
        return true;
    }

    NetworkDevice ResolveGateway(NetworkDevice device, out string reason)
    {
        reason = null;
        if (device.IsRouter) return device;

        if (string.IsNullOrWhiteSpace(device.gateway))
        {
            reason = device.deviceName + " has no default gateway";
            return null;
        }

        if (!IpUtil.TryParse(device.gateway, out uint gatewayAddress))
        {
            reason = device.deviceName + " has an invalid gateway address";
            return null;
        }

        if (!device.IsInSameSubnet(gatewayAddress))
        {
            reason = $"gateway {device.gateway} is outside {device.deviceName}'s subnet";
            return null;
        }

        foreach (NetworkDevice candidate in Devices)
        {
            if (candidate.HasAddress(gatewayAddress))
            {
                if (candidate.IsRouter) return candidate;

                reason = $"gateway {device.gateway} belongs to {candidate.deviceName}, which is not a router";
                return null;
            }
        }

        reason = $"no router owns gateway {device.gateway}";
        return null;
    }

    bool FindAddressConflict(NetworkDevice device, out string reason)
    {
        reason = null;

        foreach (uint address in device.Addresses)
        {
            foreach (NetworkDevice other in Devices)
            {
                if (other != device && other.HasAddress(address))
                {
                    reason = $"IP conflict: {IpUtil.ToString(address)} is used by {device.deviceName} and {other.deviceName}";
                    return true;
                }
            }
        }
        return false;
    }

    // Single entry point used by every UI that sends packets
    public bool SendPacket(NetworkDevice source, NetworkDevice destination)
    {
        List<NetworkDevice> route = BuildRoute(source, destination, out string reason);

        if (route == null)
        {
            HandleFailedPacket(source, destination, reason);
            return false;
        }

        return CreatePacket(source, destination, route) != null;
    }

    public GameObject CreatePacket(NetworkDevice source, NetworkDevice destination, List<NetworkDevice> path)
    {
        if (source == null || destination == null)
        {
            Debug.LogError("CreatePacket: Source or destination is NULL.");
            return null;
        }

        if (path == null || path.Count < 2)
        {
            HandleFailedPacket(source, destination, "No route available");
            return null;
        }

        if (packetPrefab == null)
        {
            HandleFailedPacket(source, destination, "Packet prefab not assigned");
            return null;
        }

        GameObject packetObject = Instantiate(packetPrefab, source.transform.position + Vector3.up * packetHeight, Quaternion.identity);
        Packet packet = packetObject.GetComponent<Packet>();

        if (packet == null)
        {
            Destroy(packetObject);
            HandleFailedPacket(source, destination, "Packet script missing on prefab");
            return null;
        }

        if (NetworkStatistics.Instance != null) NetworkStatistics.Instance.PacketSent();

        packet.Initialize(source, destination, path);

        if (NetworkVisualizer.Instance != null) NetworkVisualizer.Instance.ShowRoute(path, true, 1.5f);

        return packetObject;
    }

    public void HandleFailedPacket(NetworkDevice source, NetworkDevice destination, string reason)
    {
        if (NetworkStatistics.Instance != null)
        {
            NetworkStatistics.Instance.PacketSent();
            NetworkStatistics.Instance.PacketFailed();
        }

        LabSounds.Play(LabSound.Error);

        string sourceName = source != null ? source.deviceName : "?";
        string destinationName = destination != null ? destination.deviceName : "?";
        Debug.Log($"PACKET FAILED: {sourceName} → {destinationName} | {reason}");

        if (Log != null && source != null && destination != null)
            Log.ShowFailedPacket(source, destination, reason);

        if (source != null)
        {
            DeviceLED led = source.GetComponentInChildren<DeviceLED>();
            if (led != null) led.BlinkError();

            if (NetworkVisualizer.Instance != null)
                NetworkVisualizer.Instance.Pulse(source.transform.position, NetworkVisualizer.Instance.failureColor);
        }
    }
}
