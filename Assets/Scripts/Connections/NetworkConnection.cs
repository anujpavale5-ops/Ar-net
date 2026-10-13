using System.Collections.Generic;
using UnityEngine;

public class NetworkConnection : MonoBehaviour
{
    public static readonly List<NetworkConnection> All = new List<NetworkConnection>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    public NetworkDevice deviceA;
    public NetworkDevice deviceB;

    public bool isConnected = true;

    [Header("Realistic Cable")]
    [Tooltip("Draw a patch cable that drops to the floor and runs along it, instead of the straight prefab tube")]
    public bool realisticCable = true;
    public float cableWidth = 0.035f;
    public Color pcCableColor = new Color(0.12f, 0.32f, 0.85f);
    public Color uplinkCableColor = new Color(0.95f, 0.75f, 0.1f);
    public Color serverCableColor = new Color(0.15f, 0.65f, 0.3f);

    [Header("Cable Thickness")]
    public float normalWidth = 0.05f;
    public float activeWidth = 0.08f;

    [Header("Pulse & Glow Settings")]
    public float pulseSpeed = 6f;
    public float pulseAmount = 0.02f;

    // HDR colors so the emission actually blooms
    [ColorUsage(true, true)]
    public Color normalColor = Color.black;
    [ColorUsage(true, true)]
    public Color glowColor = Color.cyan;

    [Header("Offset")]
    public float heightOffset = 0.0f;

    // Number of packets currently travelling on this cable; glow stays on while any are in flight
    private int activePackets = 0;
    private MeshRenderer cableRenderer;
    private Material cableMaterial;

    private LineRenderer cableLine;
    private Material lineMaterial;
    private readonly List<Vector3> path = new List<Vector3>();
    private Vector3 builtFrom, builtTo;
    private bool builtConnected;
    private bool highlighted;
    private float floorHeight;

    public bool IsCarryingTraffic => activePackets > 0;

    void Awake()
    {
        cableRenderer = GetComponentInChildren<MeshRenderer>();

        if (cableRenderer != null)
        {
            cableRenderer.enabled = isConnected && !realisticCable;

            // Per-cable material instance so cables glow independently
            cableMaterial = cableRenderer.material;
            cableMaterial.EnableKeyword("_EMISSION");
            cableMaterial.SetColor("_EmissionColor", normalColor);
        }
    }

    void Start()
    {
        DevicePlacementManager placement = FindAnyObjectByType<DevicePlacementManager>();
        if (placement != null) floorHeight = placement.floorHeight;
    }

    void OnEnable()
    {
        All.Add(this);
        NetworkDevice.MarkTopologyChanged();
    }

    void OnDisable()
    {
        All.Remove(this);
        NetworkDevice.MarkTopologyChanged();
    }

    void Update()
    {
        if (deviceA == null || deviceB == null)
        {
            Destroy(gameObject);
            return;
        }

        if (realisticCable)
        {
            if (cableRenderer != null) cableRenderer.enabled = false;
            UpdateRealisticCable();
            return;
        }

        if (cableRenderer != null) cableRenderer.enabled = isConnected;
        if (!isConnected) return;

        Update3DVisuals();
    }

    // ---------- realistic patch cable ----------

    Color CableColor
    {
        get
        {
            if (deviceA.IsRouter || deviceB.IsRouter) return uplinkCableColor;
            if (deviceA.CompareTag("Server") || deviceB.CompareTag("Server")) return serverCableColor;
            return pcCableColor;
        }
    }

    void EnsureLine()
    {
        if (cableLine != null) return;

        GameObject lineObject = new GameObject("PatchCable");
        lineObject.transform.SetParent(transform, false);

        lineMaterial = LabMaterials.Opaque(CableColor, Color.black, 0.45f);
        cableLine = lineObject.AddComponent<LineRenderer>();
        cableLine.sharedMaterial = lineMaterial;
        cableLine.useWorldSpace = true;
        cableLine.numCornerVertices = 6;
        cableLine.numCapVertices = 3;
        cableLine.alignment = LineAlignment.View;
        cableLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    void UpdateRealisticCable()
    {
        EnsureLine();

        Vector3 from = deviceA.PortPosition;
        Vector3 to = deviceB.PortPosition;

        if (path.Count == 0 || from != builtFrom || to != builtTo || isConnected != builtConnected)
        {
            BuildPath(from, to);
            cableLine.positionCount = path.Count;
            cableLine.SetPositions(path.ToArray());
        }

        Color color = CableColor;
        float width = cableWidth;
        Color emission = Color.black;
        Color baseColor = color;

        if (!isConnected)
        {
            baseColor = color * 0.55f;
        }
        else if (IsCarryingTraffic)
        {
            float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f;
            emission = color * Mathf.Lerp(0.8f, 2.4f, pulse);
            width = cableWidth * 1.35f;
        }
        else if (highlighted)
        {
            emission = color * 0.9f;
            width = cableWidth * 1.25f;
        }

        LabMaterials.SetColors(lineMaterial, baseColor, emission);
        cableLine.startWidth = width;
        cableLine.endWidth = width;
    }

    // Port → back edge of the desk/cabinet → floor → along the floor → up to the other port.
    // Unplugged cables stop short and lie on the floor.
    void BuildPath(Vector3 from, Vector3 to)
    {
        builtFrom = from;
        builtTo = to;
        builtConnected = isConnected;
        path.Clear();

        float floor = floorHeight + cableWidth / 2f;
        Vector3 flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
        float horizontal = flat.magnitude;
        Vector3 direction = horizontal > 0.001f ? flat / horizontal : Vector3.forward;

        // Spread parallel cables slightly so they don't z-fight along the floor
        int seed = 0;
        foreach (char c in deviceA.deviceName + deviceB.deviceName) seed = seed * 31 + c;
        float lane = (((seed % 7) + 7) % 7 - 3f) * 0.025f;
        Vector3 lateral = Vector3.Cross(Vector3.up, direction) * lane;

        float exitA = Mathf.Min(deviceA.FootprintRadius + 0.05f, horizontal * 0.45f);
        float exitB = Mathf.Min(deviceB.FootprintRadius + 0.05f, horizontal * 0.45f);

        Vector3 aEdge = from + direction * exitA;
        Vector3 bEdge = to - direction * exitB;
        Vector3 aFloor = new Vector3(aEdge.x, floor, aEdge.z) + lateral;
        Vector3 bFloor = new Vector3(bEdge.x, floor, bEdge.z) + lateral;

        path.Add(from);
        if (aEdge.y - floor > 0.05f) path.Add(aEdge + Vector3.down * Mathf.Min(0.05f, aEdge.y - floor));
        path.Add(aFloor);

        if (isConnected)
        {
            path.Add(bFloor);
            if (bEdge.y - floor > 0.05f) path.Add(bEdge + Vector3.down * Mathf.Min(0.05f, bEdge.y - floor));
            path.Add(to);
        }
        else
        {
            // Loose end lying on the floor with a little curl
            Vector3 end = Vector3.Lerp(aFloor, bFloor, 0.75f);
            path.Add(end);
            path.Add(end + Vector3.Cross(Vector3.up, direction) * 0.25f + direction * 0.15f);
        }
    }

    // Points along the cable, ordered starting at the given device
    public List<Vector3> GetPath(NetworkDevice startDevice)
    {
        if (!realisticCable || deviceA == null || deviceB == null)
        {
            return new List<Vector3> { startDevice.transform.position, OtherEnd(startDevice).transform.position };
        }

        if (path.Count == 0 || !builtConnected) BuildPath(deviceA.PortPosition, deviceB.PortPosition);

        List<Vector3> ordered = new List<Vector3>(path);
        if (startDevice == deviceB) ordered.Reverse();
        return ordered;
    }

    public void SetHighlighted(bool value)
    {
        highlighted = value;
    }

    // ---------- classic straight tube ----------

    void Update3DVisuals()
    {
        Vector3 startPoint = deviceA.transform.position + (Vector3.up * heightOffset);
        Vector3 endPoint = deviceB.transform.position + (Vector3.up * heightOffset);
        float distance = Vector3.Distance(startPoint, endPoint);

        transform.position = (startPoint + endPoint) / 2f;

        Vector3 direction = endPoint - startPoint;
        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        float currentWidth = normalWidth;

        if (IsCarryingTraffic)
        {
            float rawSine = Mathf.Sin(Time.time * pulseSpeed);
            currentWidth = Mathf.Max(activeWidth + rawSine * pulseAmount, normalWidth);

            if (cableMaterial != null)
            {
                float colorLerp = (rawSine + 1f) / 2f;
                cableMaterial.SetColor("_EmissionColor", Color.Lerp(normalColor, glowColor, colorLerp));
            }
        }
        else if (cableMaterial != null)
        {
            cableMaterial.SetColor("_EmissionColor", normalColor);
        }

        transform.localScale = new Vector3(currentWidth, currentWidth, distance);
    }

    public void SetActive(bool active)
    {
        activePackets = Mathf.Max(0, activePackets + (active ? 1 : -1));

        if (!IsCarryingTraffic && cableMaterial != null)
        {
            cableMaterial.SetColor("_EmissionColor", normalColor);
        }
    }

    public bool Connects(NetworkDevice device1, NetworkDevice device2)
    {
        return (deviceA == device1 && deviceB == device2) ||
               (deviceA == device2 && deviceB == device1);
    }

    public NetworkDevice OtherEnd(NetworkDevice device)
    {
        if (deviceA == device) return deviceB;
        if (deviceB == device) return deviceA;
        return null;
    }

    public void Disconnect()
    {
        if (!isConnected) return;

        isConnected = false;
        activePackets = 0;
        LabSounds.Play(LabSound.Unplug);

        if (cableRenderer != null) cableRenderer.enabled = false;
        NetworkDevice.MarkTopologyChanged();

        NetworkLogUI logUI = FindAnyObjectByType<NetworkLogUI>();
        if (logUI != null) logUI.LogDisconnection(deviceA, deviceB);
    }

    public void Reconnect()
    {
        isConnected = true;
        activePackets = 0;

        if (cableRenderer != null) cableRenderer.enabled = !realisticCable;
        if (cableMaterial != null) cableMaterial.SetColor("_EmissionColor", normalColor);
        NetworkDevice.MarkTopologyChanged();
    }

    public void ResetVisual()
    {
        activePackets = 0;
        if (cableMaterial != null)
        {
            cableMaterial.SetColor("_EmissionColor", normalColor);
        }
    }

    public static NetworkConnection Between(NetworkDevice a, NetworkDevice b)
    {
        foreach (NetworkConnection connection in All)
        {
            if (connection != null && connection.Connects(a, b)) return connection;
        }
        return null;
    }
}
