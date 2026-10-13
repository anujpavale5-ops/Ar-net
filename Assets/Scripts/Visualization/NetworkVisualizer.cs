using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Visual overlay for the simulator:
//  • floor rings colour-coded by subnet (switches take the colour of the LAN they serve)
//  • a legend bar with subnets and the currently previewed route
//  • animated route preview / failed-route line
//  • pulses on packet hops, delivery and failure
//  • dashed red lines for unplugged cables and a rubber-band line while connecting
// Press V to toggle the overlay. Added automatically by LabBootstrap.
public class NetworkVisualizer : MonoBehaviour
{
    public static NetworkVisualizer Instance { get; private set; }

    [Header("Colors")]
    [ColorUsage(true, true)] public Color hopColor = new Color(0.2f, 0.85f, 1f) * 2f;
    [ColorUsage(true, true)] public Color successColor = new Color(0.25f, 1f, 0.45f) * 2.5f;
    [ColorUsage(true, true)] public Color failureColor = new Color(1f, 0.25f, 0.2f) * 2.5f;
    [ColorUsage(true, true)] public Color routeColor = new Color(0.3f, 0.9f, 1f) * 2.2f;
    [ColorUsage(true, true)] public Color routerColor = new Color(1f, 0.75f, 0.25f) * 1.8f;
    [ColorUsage(true, true)] public Color neutralColor = new Color(0.8f, 0.85f, 0.95f) * 1.2f;
    public Color unconfiguredColor = new Color(0.35f, 0.37f, 0.4f);

    [Header("Overlay")]
    public bool overlayVisible = true;
    public float ringWidth = 0.035f;
    public float routeWidth = 0.06f;
    public float routeLift = 0.12f;
    public float flowSpeed = 1.6f;
    public float pulseDuration = 0.7f;

    private Transform root;
    private Material ringTemplate;
    private Material dashMaterial;
    private Material routeMaterial;

    private readonly Dictionary<NetworkDevice, LineRenderer> rings = new Dictionary<NetworkDevice, LineRenderer>();
    private readonly Dictionary<uint, int> subnetOrder = new Dictionary<uint, int>();
    private readonly List<LineRenderer> brokenLinks = new List<LineRenderer>();
    private readonly List<PulseRing> pulses = new List<PulseRing>();
    private readonly Stack<LineRenderer> pulsePool = new Stack<LineRenderer>();

    private LineRenderer routeLine;
    private LineRenderer pendingLine;
    private List<NetworkDevice> routePath;
    private bool routeOk;
    private string routeReason;
    private float emphasisUntil;

    private GameObject legendObject;
    private TextMeshProUGUI legendText;
    private string subnetSummary = "";

    private int builtVersion = -1;
    private Camera mainCamera;
    private DevicePlacementManager placement;
    private NetworkManager networkManager;
    private DeviceConnector connector;

    private const int RingSegments = 48;

    class PulseRing
    {
        public LineRenderer line;
        public Vector3 center;
        public Color color;
        public float age;
        public float scale;
    }

    float FloorHeight => placement != null ? placement.floorHeight : 0f;

    float PacketHeight => networkManager != null ? networkManager.packetHeight : 0.3f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        root = new GameObject("VisualizerLines").transform;
        root.SetParent(transform, false);

        ringTemplate = LabMaterials.Opaque(Color.black, neutralColor, 0f);

        Texture2D dash = LabMaterials.DashTexture();
        dashMaterial = LabMaterials.Transparent(new Color(1f, 1f, 1f, 0.9f), failureColor);
        LabMaterials.SetTexture(dashMaterial, dash, Vector2.one, true);

        routeMaterial = LabMaterials.Transparent(new Color(1f, 1f, 1f, 0.95f), routeColor);
        LabMaterials.SetTexture(routeMaterial, dash, Vector2.one, true);

        routeLine = CreateLine("RoutePreview", routeMaterial, routeWidth);
        routeLine.textureMode = LineTextureMode.Tile;
        routeLine.gameObject.SetActive(false);

        pendingLine = CreateLine("PendingCable", routeMaterial, routeWidth * 0.8f);
        pendingLine.textureMode = LineTextureMode.Tile;
        pendingLine.gameObject.SetActive(false);

        BuildLegend();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        mainCamera = Camera.main;
        placement = FindAnyObjectByType<DevicePlacementManager>();
        networkManager = FindAnyObjectByType<NetworkManager>();
        connector = FindAnyObjectByType<DeviceConnector>();
    }

    // ---------- public API ----------

    public void ShowRoute(List<NetworkDevice> path, bool ok, float emphasisSeconds = 0f, string reason = null)
    {
        routePath = path != null ? new List<NetworkDevice>(path) : null;
        routeOk = ok;
        routeReason = reason;
        emphasisUntil = Time.time + emphasisSeconds;
        UpdateLegendText();
    }

    public void ClearRoutePreview()
    {
        routePath = null;
        routeReason = null;
        routeLine.gameObject.SetActive(false);
        UpdateLegendText();
    }

    public void Pulse(Vector3 position, Color color, float scale = 1f)
    {
        LineRenderer line = pulsePool.Count > 0 ? pulsePool.Pop() : CreateLine("Pulse", LabMaterials.Transparent(Color.white, color), ringWidth * 1.6f);
        line.loop = true;
        line.positionCount = RingSegments;
        line.gameObject.SetActive(true);

        pulses.Add(new PulseRing
        {
            line = line,
            center = new Vector3(position.x, FloorHeight + 0.03f, position.z),
            color = color,
            age = 0f,
            scale = scale
        });
    }

    // ---------- frame update ----------

    void LateUpdate()
    {
        if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame && !PointerUtil.IsTyping())
        {
            overlayVisible = !overlayVisible;
            if (legendObject != null) legendObject.SetActive(overlayVisible);
        }

        if (builtVersion != NetworkDevice.TopologyVersion) RebuildTopologyVisuals();

        UpdateRings();
        UpdateBrokenLinks();
        UpdateRouteLine();
        UpdatePendingLine();
        UpdatePulses();
    }

    void RebuildTopologyVisuals()
    {
        builtVersion = NetworkDevice.TopologyVersion;

        List<NetworkDevice> stale = new List<NetworkDevice>();
        foreach (KeyValuePair<NetworkDevice, LineRenderer> pair in rings)
        {
            if (pair.Key == null || pair.Key.isPreviewModel || !pair.Key.isActiveAndEnabled) stale.Add(pair.Key);
        }
        foreach (NetworkDevice device in stale)
        {
            if (rings[device] != null) Destroy(rings[device].gameObject);
            rings.Remove(device);
        }

        Dictionary<uint, int> subnetCounts = new Dictionary<uint, int>();
        Dictionary<uint, string> subnetLabels = new Dictionary<uint, string>();
        int unconfigured = 0;

        foreach (NetworkDevice device in NetworkDevice.All)
        {
            if (device == null || device.isPreviewModel) continue;

            if (!rings.TryGetValue(device, out LineRenderer ring))
            {
                ring = CreateLine("SubnetRing", new Material(ringTemplate), ringWidth);
                ring.loop = true;
                ring.positionCount = RingSegments;
                rings[device] = ring;
            }

            Color color = ColorFor(device);
            LabMaterials.SetColors(ring.sharedMaterial, Color.black, color);

            if (device.IsSwitch) continue;

            if (!device.IsConfigured) { unconfigured++; continue; }

            device.TryGetMask(out uint mask);
            foreach (uint address in device.Addresses)
            {
                uint key = SubnetKey(address, mask);
                subnetCounts.TryGetValue(key, out int count);
                subnetCounts[key] = count + 1;
                subnetLabels[key] = IpUtil.ToString(address & mask) + "/" + IpUtil.PrefixLength(mask);
            }
        }

        StringBuilder summary = new StringBuilder();
        foreach (KeyValuePair<uint, int> pair in subnetCounts)
        {
            summary.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(LegendColor(SubnetColor(pair.Key)))).Append(">●</color> ")
                   .Append(subnetLabels[pair.Key]).Append(" <alpha=#88>(").Append(pair.Value).Append(")<alpha=#FF>    ");
        }
        if (unconfigured > 0)
        {
            summary.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(unconfiguredColor * 1.6f)).Append(">○</color> Unconfigured <alpha=#88>(")
                   .Append(unconfigured).Append(")<alpha=#FF>    ");
        }
        if (summary.Length == 0) summary.Append("<alpha=#AA>Place devices and set IPs to see subnets<alpha=#FF>    ");

        subnetSummary = summary.ToString();
        UpdateLegendText();
    }

    void UpdateRings()
    {
        float floorY = FloorHeight + 0.02f;

        foreach (KeyValuePair<NetworkDevice, LineRenderer> pair in rings)
        {
            if (pair.Key == null || pair.Value == null) continue;

            LineRenderer ring = pair.Value;
            ring.gameObject.SetActive(overlayVisible);
            if (!overlayVisible) continue;

            Vector3 center = pair.Key.transform.position;
            float radius = FootprintRadius(pair.Key);
            float breathe = pair.Key.isOnline ? 1f : 0.6f + 0.4f * Mathf.PingPong(Time.time * 2f, 1f);

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                ring.SetPosition(i, new Vector3(center.x + Mathf.Cos(angle) * radius * breathe, floorY, center.z + Mathf.Sin(angle) * radius * breathe));
            }
        }
    }

    float FootprintRadius(NetworkDevice device)
    {
        return device.FootprintRadius + 0.15f;
    }

    void UpdateBrokenLinks()
    {
        int used = 0;

        // Realistic cables already show the loose end on the floor
        if (overlayVisible && !CablesAreRealistic())
        {
            foreach (NetworkConnection connection in NetworkConnection.All)
            {
                if (connection == null || connection.isConnected || connection.deviceA == null || connection.deviceB == null) continue;

                if (used >= brokenLinks.Count)
                {
                    LineRenderer line = CreateLine("UnpluggedCable", dashMaterial, routeWidth * 0.7f);
                    line.textureMode = LineTextureMode.Tile;
                    brokenLinks.Add(line);
                }

                LineRenderer link = brokenLinks[used++];
                link.gameObject.SetActive(true);
                SetStraightLine(link, connection.deviceA.transform.position, connection.deviceB.transform.position, routeLift * 0.5f);
            }
        }

        for (int i = used; i < brokenLinks.Count; i++) brokenLinks[i].gameObject.SetActive(false);

        LabMaterials.SetOffset(dashMaterial, new Vector2(-Time.time * flowSpeed * 0.3f, 0f));
    }

    void UpdateRouteLine()
    {
        bool hasRoute = routePath != null && routePath.Count >= 2 && !routePath.Exists(d => d == null);

        // Successful routes light up the real cables they use
        bool highlightCables = hasRoute && routeOk && CablesAreRealistic();
        foreach (NetworkConnection connection in NetworkConnection.All)
        {
            if (connection != null) connection.SetHighlighted(false);
        }
        if (highlightCables)
        {
            for (int i = 1; i < routePath.Count; i++)
            {
                NetworkConnection cable = NetworkConnection.Between(routePath[i - 1], routePath[i]);
                if (cable != null) cable.SetHighlighted(true);
            }
        }

        if (!hasRoute || highlightCables)
        {
            routeLine.gameObject.SetActive(false);
            return;
        }

        routeLine.gameObject.SetActive(true);

        bool emphasised = Time.time < emphasisUntil;
        Color color = routeOk ? routeColor : failureColor;
        if (emphasised) color *= 1.6f;
        LabMaterials.SetColors(routeMaterial, new Color(1f, 1f, 1f, 0.95f), color);

        float width = routeWidth * (emphasised ? 1.8f : 1f);
        routeLine.startWidth = width;
        routeLine.endWidth = width;

        float height = PacketHeight + routeLift;
        routeLine.positionCount = routePath.Count;
        for (int i = 0; i < routePath.Count; i++)
        {
            routeLine.SetPosition(i, routePath[i].transform.position + Vector3.up * height);
        }

        float direction = routeOk ? -1f : 0f;
        LabMaterials.SetOffset(routeMaterial, new Vector2(direction * Time.time * flowSpeed, 0f));
    }

    void UpdatePendingLine()
    {
        NetworkDevice pending = connector != null ? connector.PendingDevice : null;

        if (pending == null || mainCamera == null || Mouse.current == null)
        {
            pendingLine.gameObject.SetActive(false);
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(PointerUtil.ScreenPoint);
        Vector3 target = pending.transform.position;

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            NetworkDevice hovered = hit.collider.GetComponentInParent<NetworkDevice>();
            target = hovered != null && !hovered.isPreviewModel ? hovered.transform.position : hit.point;
        }
        else
        {
            Plane floor = new Plane(Vector3.up, new Vector3(0f, FloorHeight, 0f));
            if (floor.Raycast(ray, out float distance)) target = ray.GetPoint(distance);
        }

        pendingLine.gameObject.SetActive(true);
        SetStraightLine(pendingLine, pending.transform.position, target, routeLift);
    }

    void UpdatePulses()
    {
        for (int i = pulses.Count - 1; i >= 0; i--)
        {
            PulseRing pulse = pulses[i];
            pulse.age += Time.deltaTime;
            float t = pulse.age / pulseDuration;

            if (t >= 1f)
            {
                pulse.line.gameObject.SetActive(false);
                pulsePool.Push(pulse.line);
                pulses.RemoveAt(i);
                continue;
            }

            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(0.25f, 1.3f, eased) * pulse.scale;
            float fade = 1f - t;

            Material material = pulse.line.sharedMaterial;
            LabMaterials.SetColors(material, new Color(1f, 1f, 1f, fade), pulse.color * fade);
            pulse.line.startWidth = pulse.line.endWidth = ringWidth * 1.6f * (1f + (1f - t));

            for (int s = 0; s < RingSegments; s++)
            {
                float angle = s * Mathf.PI * 2f / RingSegments;
                pulse.line.SetPosition(s, pulse.center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }
    }

    static bool CablesAreRealistic()
    {
        foreach (NetworkConnection connection in NetworkConnection.All)
        {
            if (connection != null) return connection.realisticCable;
        }
        return false;
    }

    // ---------- colours ----------

    Color ColorFor(NetworkDevice device)
    {
        if (!device.isOnline) return unconfiguredColor;
        if (device.IsRouter) return routerColor;

        if (device.IsSwitch)
        {
            HashSet<uint> subnets = SubnetsBehindSwitch(device);
            if (subnets.Count == 1)
            {
                foreach (uint key in subnets) return SubnetColor(key);
            }
            return subnets.Count == 0 ? unconfiguredColor : neutralColor;
        }

        if (!device.IsConfigured) return unconfiguredColor;

        device.TryGetMask(out uint mask);
        return SubnetColor(SubnetKey(device.Addresses[0], mask));
    }

    // Subnets of hosts reachable through this switch without crossing a router
    HashSet<uint> SubnetsBehindSwitch(NetworkDevice start)
    {
        HashSet<uint> subnets = new HashSet<uint>();
        HashSet<NetworkDevice> visited = new HashSet<NetworkDevice> { start };
        Queue<NetworkDevice> queue = new Queue<NetworkDevice>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            NetworkDevice current = queue.Dequeue();

            foreach (NetworkConnection connection in NetworkConnection.All)
            {
                if (connection == null || !connection.isConnected) continue;

                NetworkDevice neighbor = connection.OtherEnd(current);
                if (neighbor == null || !visited.Add(neighbor)) continue;

                if (neighbor.IsSwitch) queue.Enqueue(neighbor);
                else if (!neighbor.IsRouter && neighbor.IsConfigured)
                {
                    neighbor.TryGetMask(out uint mask);
                    subnets.Add(SubnetKey(neighbor.Addresses[0], mask));
                }
            }
        }
        return subnets;
    }

    static uint SubnetKey(uint address, uint mask)
    {
        // Network address mixed with prefix length so 10.0.0.0/8 and 10.0.0.0/16 differ
        return (address & mask) ^ ((uint)IpUtil.PrefixLength(mask) * 2654435761u);
    }

    Color SubnetColor(uint key)
    {
        if (!subnetOrder.TryGetValue(key, out int index))
        {
            index = subnetOrder.Count;
            subnetOrder[key] = index;
        }

        // Golden-ratio hue steps keep neighbouring subnets visually distinct
        float hue = (0.52f + index * 0.618034f) % 1f;
        return Color.HSVToRGB(hue, 0.75f, 1f) * 2f;
    }

    static Color LegendColor(Color hdr)
    {
        float max = Mathf.Max(hdr.maxColorComponent, 1f);
        return new Color(hdr.r / max, hdr.g / max, hdr.b / max);
    }

    // ---------- helpers ----------

    void SetStraightLine(LineRenderer line, Vector3 from, Vector3 to, float lift)
    {
        float height = PacketHeight + lift;
        line.positionCount = 2;
        line.SetPosition(0, from + Vector3.up * height);
        line.SetPosition(1, new Vector3(to.x, Mathf.Max(to.y, from.y) + height, to.z));
    }

    LineRenderer CreateLine(string lineName, Material material, float width)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(root, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.startWidth = width;
        line.endWidth = width;
        line.useWorldSpace = true;
        line.numCornerVertices = 4;
        line.numCapVertices = 4;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    void BuildLegend()
    {
        GameObject canvasObject = new GameObject("VisualizerLegend", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        legendObject = new GameObject("Legend", typeof(RectTransform), typeof(Image));
        legendObject.transform.SetParent(canvasObject.transform, false);

        RectTransform panel = legendObject.GetComponent<RectTransform>();
        // Top-centre sits between the existing side panels without covering any controls
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0f, -12f);
        panel.sizeDelta = new Vector2(1150f, 40f);

        Image background = legendObject.GetComponent<Image>();
        background.color = new Color(0.03f, 0.05f, 0.08f, 0.78f);
        background.raycastTarget = false;

        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(legendObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(16f, 6f);
        textRect.offsetMax = new Vector2(-16f, -6f);

        legendText = textObject.AddComponent<TextMeshProUGUI>();
        legendText.fontSize = 18f;
        legendText.alignment = TextAlignmentOptions.Center;
        legendText.color = new Color(0.85f, 0.9f, 0.97f);
        legendText.raycastTarget = false;
        legendText.textWrappingMode = TextWrappingModes.Normal;
        legendText.overflowMode = TextOverflowModes.Ellipsis;
    }

    public void RefreshLegend() => UpdateLegendText();

    void UpdateLegendText()
    {
        if (legendText == null) return;

        StringBuilder text = new StringBuilder(subnetSummary);
        bool walking = LabViewMode.Instance != null && LabViewMode.Instance.IsFirstPerson;
        text.Append(walking
            ? "\n<size=85%><color=#7FE8FF>WALK MODE</color><alpha=#77>  Mouse: look · WASD: walk · Shift: run · Click: use · 1-4: pick up device · Hold Alt: cursor · Tab: overview<alpha=#FF></size>"
            : "\n<size=85%><alpha=#77>Tab: walk mode · Right-drag: orbit · Middle-drag / WASD: pan · Scroll: zoom · F: frame · V: overlay · `: console<alpha=#FF></size>");

        if (routePath != null && routePath.Count >= 2 && !routePath.Exists(d => d == null))
        {
            text.Append('\n');
            if (routeOk)
            {
                text.Append("<color=#7FE8FF>Route:</color> ");
                for (int i = 0; i < routePath.Count; i++)
                {
                    if (i > 0) text.Append(" → ");
                    text.Append(routePath[i].deviceName);
                }
                text.Append(" <alpha=#88>(").Append(routePath.Count - 1).Append(" hops)<alpha=#FF>");
            }
            else
            {
                text.Append("<color=#FF6A5E>× ").Append(routePath[0].deviceName).Append(" → ")
                    .Append(routePath[routePath.Count - 1].deviceName).Append(":</color> ")
                    .Append(routeReason ?? "unreachable");
            }
        }

        legendText.text = text.ToString();

        // Grow the panel to fit however many lines the legend currently needs
        RectTransform panel = (RectTransform)legendObject.transform;
        float height = legendText.GetPreferredValues(legendText.text, panel.sizeDelta.x - 32f, 0f).y + 14f;
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, Mathf.Max(40f, height));
    }
}
