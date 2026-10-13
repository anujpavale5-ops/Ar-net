using System.Collections.Generic;
using UnityEngine;

public class Packet : MonoBehaviour
{
    public float speed = 3f;
    [Tooltip("How high the packet arcs on hops that have no cable to follow")]
    public float arcHeight = 0.25f;
    [Tooltip("World size of the packet when riding along realistic cables")]
    public float cableRiderSize = 0.16f;

    private NetworkDevice source;
    private NetworkDevice destination;
    private List<NetworkDevice> path;

    public NetworkDevice Source => source;
    public NetworkDevice Destination => destination;
    public IReadOnlyList<NetworkDevice> Route => path;

    // Raised for every packet, so missions and the console can react to traffic
    public static event System.Action<Packet> AnyDelivered;
    public static event System.Action<Packet, string> AnyFailed;

    public event System.Action<Packet, NetworkDevice> HopReached;
    public event System.Action<Packet, bool> Finished;

    private int currentTargetIndex = 0;
    private float heightOffset = 0.3f;
    private float hopProgress = 0f;
    private bool finished = false;

    // Polyline for the current hop when following a cable
    private List<Vector3> hopPath;
    private float hopPathLength;
    private float hopDistance;
    private float rideHeight;

    private NetworkLogUI logUI;
    private NetworkConnection currentCable;
    private bool holdingCable = false;

    public void Initialize(NetworkDevice src, NetworkDevice dest, List<NetworkDevice> route)
    {
        source = src;
        destination = dest;
        path = route;

        NetworkManager manager = FindAnyObjectByType<NetworkManager>();
        if (manager != null) heightOffset = manager.packetHeight;

        logUI = FindAnyObjectByType<NetworkLogUI>();
        if (logUI != null) logUI.StartPacket(this, source, destination);

        transform.position = HopPoint(path[0]);
        currentTargetIndex = 1;

        if (FollowsCables)
        {
            // Shrink to a data pulse that rides on top of the patch cable
            Bounds bounds = GetComponentInChildren<Renderer>() != null ? GetComponentInChildren<Renderer>().bounds : new Bounds(transform.position, Vector3.one * 0.3f);
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (size > 0.001f) transform.localScale *= cableRiderSize / size;
            rideHeight = cableRiderSize * 0.5f;

            TrailRenderer trail = GetComponentInChildren<TrailRenderer>();
            if (trail != null) trail.widthMultiplier *= cableRiderSize / Mathf.Max(size, 0.001f);
        }

        BlinkData(source);
        AcquireCable();
    }

    Vector3 HopPoint(NetworkDevice device)
    {
        return device.transform.position + Vector3.up * heightOffset;
    }

    void Update()
    {
        if (finished || path == null || currentTargetIndex >= path.Count) return;

        NetworkDevice from = path[currentTargetIndex - 1];
        NetworkDevice to = path[currentTargetIndex];

        if (from == null || to == null)
        {
            Fail("A device on the route was removed", from);
            return;
        }

        if (holdingCable && (currentCable == null || !currentCable.isConnected))
        {
            Fail($"Cable cut while travelling {from.deviceName} → {to.deviceName}", from);
            return;
        }

        if (hopPath != null)
        {
            hopDistance = Mathf.Min(hopDistance + speed * SimulationClock.DeltaTime, hopPathLength);
            transform.position = PointAlong(hopPath, hopDistance) + Vector3.up * rideHeight;
            if (hopDistance >= hopPathLength) ArriveAtHop(from, to);
            return;
        }

        Vector3 start = HopPoint(from);
        Vector3 end = HopPoint(to);
        float hopLength = Mathf.Max(Vector3.Distance(start, end), 0.001f);

        hopProgress = Mathf.MoveTowards(hopProgress, 1f, speed * SimulationClock.DeltaTime / hopLength);

        Vector3 position = Vector3.Lerp(start, end, hopProgress);
        position.y += Mathf.Sin(hopProgress * Mathf.PI) * arcHeight;
        transform.position = position;

        if (hopProgress >= 1f) ArriveAtHop(from, to);
    }

    void ArriveAtHop(NetworkDevice from, NetworkDevice to)
    {
        if (logUI != null) logUI.AddHop(this, from, to);
        if (NetworkStatistics.Instance != null) NetworkStatistics.Instance.AddHop();

        ReleaseCable();
        BlinkData(to);
        LabSounds.Play(LabSound.Hop);
        HopReached?.Invoke(this, to);
        SimulationClock.NotifyHop();

        if (NetworkVisualizer.Instance != null)
            NetworkVisualizer.Instance.Pulse(to.transform.position, NetworkVisualizer.Instance.hopColor);

        currentTargetIndex++;
        hopProgress = 0f;

        if (currentTargetIndex >= path.Count) CompleteTransfer();
        else AcquireCable();
    }

    void AcquireCable()
    {
        if (currentTargetIndex >= path.Count) return;

        currentCable = NetworkConnection.Between(path[currentTargetIndex - 1], path[currentTargetIndex]);
        holdingCable = currentCable != null;
        if (holdingCable) currentCable.SetActive(true);

        hopPath = null;
        hopDistance = 0f;

        if (holdingCable && currentCable.realisticCable)
        {
            hopPath = currentCable.GetPath(path[currentTargetIndex - 1]);
            hopPathLength = 0f;
            for (int i = 1; i < hopPath.Count; i++) hopPathLength += Vector3.Distance(hopPath[i - 1], hopPath[i]);
            transform.position = hopPath[0] + Vector3.up * rideHeight;
        }
    }

    bool FollowsCables
    {
        get
        {
            NetworkConnection first = path != null && path.Count > 1 ? NetworkConnection.Between(path[0], path[1]) : null;
            return first != null && first.realisticCable;
        }
    }

    static Vector3 PointAlong(List<Vector3> points, float distance)
    {
        for (int i = 1; i < points.Count; i++)
        {
            float segment = Vector3.Distance(points[i - 1], points[i]);
            if (distance <= segment && segment > 0f) return Vector3.Lerp(points[i - 1], points[i], distance / segment);
            distance -= segment;
        }
        return points[points.Count - 1];
    }

    void ReleaseCable()
    {
        if (holdingCable && currentCable != null) currentCable.SetActive(false);
        holdingCable = false;
        currentCable = null;
    }

    void Fail(string reason, NetworkDevice lastDevice)
    {
        finished = true;
        ReleaseCable();

        if (logUI != null) logUI.FailedPacket(this, reason);
        LabSounds.Play(LabSound.Error);
        Finished?.Invoke(this, false);
        AnyFailed?.Invoke(this, reason);
        if (NetworkStatistics.Instance != null) NetworkStatistics.Instance.PacketFailed();

        if (lastDevice != null)
        {
            DeviceLED errorLED = lastDevice.GetComponentInChildren<DeviceLED>();
            if (errorLED != null) errorLED.BlinkError();
        }

        if (NetworkVisualizer.Instance != null)
            NetworkVisualizer.Instance.Pulse(transform.position, NetworkVisualizer.Instance.failureColor);

        Destroy(gameObject);
    }

    void CompleteTransfer()
    {
        finished = true;

        if (logUI != null) logUI.CompletePacket(this, destination);
        LabSounds.Play(LabSound.Delivered);
        Finished?.Invoke(this, true);
        AnyDelivered?.Invoke(this);
        if (NetworkStatistics.Instance != null) NetworkStatistics.Instance.PacketDelivered();

        if (NetworkVisualizer.Instance != null && destination != null)
            NetworkVisualizer.Instance.Pulse(destination.transform.position, NetworkVisualizer.Instance.successColor, 1.6f);

        Destroy(gameObject);
    }

    void OnDestroy()
    {
        ReleaseCable();
    }

    static void BlinkData(NetworkDevice device)
    {
        if (device == null) return;
        DeviceLED led = device.GetComponentInChildren<DeviceLED>();
        if (led != null) led.BlinkData();
    }
}
