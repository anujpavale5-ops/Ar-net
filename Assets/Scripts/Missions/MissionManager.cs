using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Guided lessons and troubleshooting tickets with live objective checklists.
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    class Objective
    {
        public string text;
        public System.Func<bool> check;
        public bool done;
    }

    class Mission
    {
        public string title;
        public string brief;
        public System.Func<TopologyData> start;
        public List<Objective> objectives = new List<Objective>();
        public string[] hints;

        public Mission Goal(string text, System.Func<bool> check)
        {
            objectives.Add(new Objective { text = text, check = check });
            return this;
        }
    }

    private readonly List<Mission> missions = new List<Mission>();
    private int current;
    private bool started;
    private bool celebrated;
    private int hintIndex = -1;
    private float nextCheck;

    // Traffic seen since the mission started
    private readonly HashSet<string> delivered = new HashSet<string>();
    private readonly HashSet<string> deliveredViaRouter = new HashSet<string>();
    private readonly HashSet<string> pingResults = new HashSet<string>();
    private readonly HashSet<string> traceResults = new HashSet<string>();
    private bool consoleOpened;

    private RectTransform panel;
    private TextMeshProUGUI headerLabel, titleLabel, briefLabel, objectivesLabel, hintLabel, bannerLabel;
    private TextMeshProUGUI startLabel;

    void Awake()
    {
        Instance = this;
        DefineMissions();
        Build();
        panel.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        Packet.AnyDelivered += OnDelivered;
        LabConsole.PingCompleted += OnPing;
        LabConsole.TraceCompleted += OnTrace;
    }

    void OnDisable()
    {
        Packet.AnyDelivered -= OnDelivered;
        LabConsole.PingCompleted -= OnPing;
        LabConsole.TraceCompleted -= OnTrace;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnDelivered(Packet packet)
    {
        if (packet.Source == null || packet.Destination == null) return;
        string key = Key(packet.Source, packet.Destination);
        delivered.Add(key);

        foreach (NetworkDevice hop in packet.Route)
        {
            if (hop != null && hop.IsRouter) deliveredViaRouter.Add(key);
        }
    }

    void OnPing(NetworkDevice from, NetworkDevice to, bool ok)
    {
        if (from != null && to != null) pingResults.Add(Key(from, to) + (ok ? ":ok" : ":fail"));
    }

    void OnTrace(NetworkDevice from, NetworkDevice to, bool ok)
    {
        if (from != null && to != null && ok) traceResults.Add(Key(from, to));
    }

    static string Key(NetworkDevice a, NetworkDevice b) => a.deviceName + ">" + b.deviceName;

    // ---------- flow ----------

    public void TogglePanel()
    {
        panel.gameObject.SetActive(!panel.gameObject.activeSelf);
        Refresh();
    }

    public void StopMission()
    {
        started = false;
        Refresh();
    }

    void StartMission()
    {
        Mission mission = missions[current];
        started = true;
        celebrated = false;
        hintIndex = -1;
        consoleOpened = false;
        delivered.Clear();
        deliveredViaRouter.Clear();
        pingResults.Clear();
        traceResults.Clear();
        foreach (Objective objective in mission.objectives) objective.done = false;

        if (TopologyIO.Instance != null) TopologyIO.Instance.Load(mission.start());
        if (LabToolbar.Instance != null) LabToolbar.Instance.ShowStatus("Mission started: " + mission.title, UITheme.Accent);
        Refresh();
    }

    void Go(int delta)
    {
        current = Mathf.Clamp(current + delta, 0, missions.Count - 1);
        started = false;
        hintIndex = -1;
        Refresh();
    }

    void NextHint()
    {
        string[] hints = missions[current].hints;
        if (hints == null || hints.Length == 0) return;
        hintIndex = (hintIndex + 1) % hints.Length;
        Refresh();
    }

    void Update()
    {
        if (LabConsole.Instance != null && LabConsole.Instance.IsOpen) consoleOpened = true;

        if (!started || Time.unscaledTime < nextCheck) return;
        if (TopologyIO.Instance != null && TopologyIO.Instance.IsLoading) return;
        nextCheck = Time.unscaledTime + 0.3f;

        Mission mission = missions[current];
        bool all = true;
        foreach (Objective objective in mission.objectives)
        {
            bool done;
            try { done = objective.check(); }
            catch { done = false; }
            objective.done = done;
            all &= done;
        }

        if (all && !celebrated)
        {
            celebrated = true;
            LabSounds.Play(LabSound.MissionComplete);
            if (LabToolbar.Instance != null) LabToolbar.Instance.ShowStatus("Mission complete: " + mission.title, UITheme.Success);
        }

        Refresh();
    }

    // ---------- queries used by objectives ----------

    static IEnumerable<NetworkDevice> Devices(string type)
    {
        foreach (NetworkDevice d in NetworkDevice.All)
        {
            if (d != null && !d.isPreviewModel && string.Equals(d.deviceType, type, System.StringComparison.OrdinalIgnoreCase)) yield return d;
        }
    }

    static int Count(string type)
    {
        int count = 0;
        foreach (NetworkDevice _ in Devices(type)) count++;
        return count;
    }

    static NetworkDevice Named(string deviceName)
    {
        foreach (NetworkDevice d in NetworkDevice.All)
        {
            if (d != null && !d.isPreviewModel && d.deviceName == deviceName) return d;
        }
        return null;
    }

    static bool LinkedToSwitch(NetworkDevice device)
    {
        foreach (NetworkConnection c in NetworkConnection.All)
        {
            if (c == null || !c.isConnected) continue;
            NetworkDevice other = c.OtherEnd(device);
            if (other != null && other.IsSwitch) return true;
        }
        return false;
    }

    static bool NoDuplicateAddresses()
    {
        HashSet<uint> seen = new HashSet<uint>();
        foreach (NetworkDevice d in NetworkDevice.All)
        {
            if (d == null || d.isPreviewModel) continue;
            foreach (uint address in d.Addresses)
            {
                if (!seen.Add(address)) return false;
            }
        }
        return true;
    }

    static string RouteFailure(string from, string to)
    {
        NetworkManager manager = FindAnyObjectByType<NetworkManager>();
        NetworkDevice a = Named(from), b = Named(to);
        if (manager == null || a == null || b == null) return "missing";
        return manager.BuildRoute(a, b, out string reason) == null ? reason : null;
    }

    bool Reached(string from, string to) => delivered.Contains(from + ">" + to) || pingResults.Contains(from + ">" + to + ":ok");

    // ---------- missions ----------

    void DefineMissions()
    {
        missions.Add(new Mission
        {
            title = "Build a LAN",
            brief = "Every network starts with devices and cables. Build a tiny office LAN.",
            start = TopologyLibrary.Empty,
            hints = new[]
            {
                "Click + ADD DEVICE and pick PC, then click the floor. In walk mode press 1 for a PC, 2 for a switch.",
                "Press CONNECT DEVICES, click a PC, then click the switch. Repeat for the second PC."
            }
        }
        .Goal("Place two PCs", () => Count("PC") >= 2)
        .Goal("Place a switch", () => Count("Switch") >= 1)
        .Goal("Cable both PCs to the switch", () =>
        {
            int linked = 0;
            foreach (NetworkDevice pc in Devices("PC")) if (LinkedToSwitch(pc)) linked++;
            return linked >= 2;
        }));

        missions.Add(new Mission
        {
            title = "Give them addresses",
            brief = "Devices can only talk once they have IP addresses in the same subnet.",
            start = TopologyLibrary.LanUnconfigured,
            hints = new[]
            {
                "Click PC1 to open its terminal. Try IP 192.168.1.10, mask 255.255.255.0.",
                "Give PC2 192.168.1.11 with the same mask - the first three numbers must match.",
                "In SEND PACKET choose Source PC1, Destination PC2 and press Send."
            }
        }
        .Goal("Give PC1 an IP address and mask", () => Named("PC1") != null && Named("PC1").IsConfigured)
        .Goal("Put PC2 in the same subnet as PC1", () =>
        {
            NetworkDevice pc1 = Named("PC1"), pc2 = Named("PC2");
            return pc1 != null && pc2 != null && pc2.IsConfigured && pc1.SharesSubnetWith(pc2) && pc2.SharesSubnetWith(pc1) && NoDuplicateAddresses();
        })
        .Goal("Deliver a packet from PC1 to PC2", () => Reached("PC1", "PC2")));

        missions.Add(new Mission
        {
            title = "Route between subnets",
            brief = "Two branch offices sit on different subnets. Only a router can connect them.",
            start = TopologyLibrary.TwoLansRouterUnconfigured,
            hints = new[]
            {
                "Click Router1. Routers can have one address per subnet: 192.168.1.1, 192.168.2.1",
                "Use mask 255.255.255.0. The PCs already use .1 as their default gateway.",
                "Send a packet from PC1 to PC3 and watch it pass through Router1."
            }
        }
        .Goal("Give Router1 an address in each subnet", () =>
        {
            NetworkDevice router = Named("Router1"), pc1 = Named("PC1"), pc3 = Named("PC3");
            return router != null && pc1 != null && pc3 != null && router.SharesSubnetWith(pc1) && router.SharesSubnetWith(pc3);
        })
        .Goal("Deliver a packet from PC1 to PC3 through the router", () => deliveredViaRouter.Contains("PC1>PC3") || pingResults.Contains("PC1>PC3:ok")));

        missions.Add(new Mission
        {
            title = "Test with ping and tracert",
            brief = "Network engineers test connectivity from the command line.",
            start = TopologyLibrary.TwoBranches,
            hints = new[]
            {
                "Press ` (the key under Esc) or the CONSOLE button.",
                "Click PC1 first, or type: use PC1. The prompt should read PC1>",
                "Type: ping Server1   then   tracert Server1"
            }
        }
        .Goal("Open the command console", () => consoleOpened)
        .Goal("From PC1 run:  ping Server1", () => pingResults.Contains("PC1>Server1:ok"))
        .Goal("From PC1 run:  tracert Server1", () => traceResults.Contains("PC1>Server1")));

        missions.Add(new Mission
        {
            title = "Ticket #1042: PC1 can't reach the server",
            brief = "A user reports PC1 cannot open anything on Server1. Find and fix every fault.",
            start = TopologyLibrary.BrokenGatewayAndCable,
            hints = new[]
            {
                "Try it first: ping Server1 from PC1 and read the error.",
                "Run ipconfig on PC1. Is the default gateway really the router's address?",
                "Something is still wrong near the server. Check Connection Control for an unplugged cable."
            }
        }
        .Goal("PC1's default gateway points at Router1", () => Named("PC1") != null && Named("PC1").gateway.Trim() == "192.168.1.1")
        .Goal("Every cable is plugged in", () =>
        {
            foreach (NetworkConnection c in NetworkConnection.All) if (c != null && !c.isConnected) return false;
            return true;
        })
        .Goal("PC1 reaches Server1 (send a packet or ping)", () => Reached("PC1", "Server1")));

        missions.Add(new Mission
        {
            title = "Ticket #1057: PC2 keeps dropping off",
            brief = "Users on PC2 lose their connection, and the file server seems to be down too.",
            start = TopologyLibrary.BrokenIpConflict,
            hints = new[]
            {
                "In the console type: devices   Look for two devices with the same IP.",
                "Give PC2 a free address such as 192.168.1.11.",
                "The server is powered off. Type: poweron Server1"
            }
        }
        .Goal("Resolve the duplicate IP address", NoDuplicateAddresses)
        .Goal("Bring Server1 back online", () => Named("Server1") != null && Named("Server1").isOnline)
        .Goal("PC1 reaches Server1", () => Reached("PC1", "Server1"))
        .Goal("PC2 reaches Server1", () => Reached("PC2", "Server1")));

        missions.Add(new Mission
        {
            title = "Lock it down with a firewall",
            brief = "Policy: staff PCs (192.168.1.0/24) must not reach the server, but may still reach PC3.",
            start = TopologyLibrary.TwoBranches,
            hints = new[]
            {
                "Open the console and type: use Router1",
                "Add a rule: acl deny 192.168.1.0/24 192.168.2.20",
                "Check with: use PC1   then   ping Server1   (it should fail)   and   ping PC3"
            }
        }
        .Goal("Staff subnet is blocked from Server1 by Router1", () =>
        {
            string failure = RouteFailure("PC1", "Server1");
            return failure != null && failure.Contains("ACL");
        })
        .Goal("PC1 can still reach PC3", () => RouteFailure("PC1", "PC3") == null)
        .Goal("Prove it: ping Server1 from PC1 and see it blocked", () => pingResults.Contains("PC1>Server1:fail")));
    }

    // ---------- UI ----------

    void Refresh()
    {
        if (panel == null || !panel.gameObject.activeSelf) return;

        Mission mission = missions[current];
        headerLabel.text = $"MISSION {current + 1} / {missions.Count}";
        titleLabel.text = mission.title;
        briefLabel.text = mission.brief;

        StringBuilder text = new StringBuilder();
        foreach (Objective objective in mission.objectives)
        {
            bool done = started && objective.done;
            string mark = done ? $"<color={UITheme.Hex(UITheme.Success)}>●</color>" : $"<color={UITheme.Hex(UITheme.TextSecondary)}>○</color>";
            string color = done ? UITheme.Hex(UITheme.Success) : started ? UITheme.Hex(UITheme.TextPrimary) : UITheme.Hex(UITheme.TextSecondary);
            text.Append(mark).Append("  <color=").Append(color).Append('>').Append(objective.text).Append("</color>\n");
        }
        if (!started) text.Append($"\n<color={UITheme.Hex(UITheme.Warning)}>Press START to load this mission's lab.</color>");
        objectivesLabel.text = text.ToString().TrimEnd('\n');

        bool showHint = hintIndex >= 0 && mission.hints != null && hintIndex < mission.hints.Length;
        hintLabel.gameObject.SetActive(showHint);
        if (showHint) hintLabel.text = $"<b>Hint {hintIndex + 1}/{mission.hints.Length}:</b> {mission.hints[hintIndex]}";

        bannerLabel.gameObject.SetActive(started && celebrated);
        startLabel.text = started ? "RESTART" : "START";
    }

    void Build()
    {
        Canvas canvas = LabUI.CreateCanvas("MissionCanvas", 43);
        canvas.transform.SetParent(transform, false);

        panel = LabUI.Panel(canvas.transform, "MissionPanel", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 215f), new Vector2(420f, 0f));
        VerticalLayoutGroup layout = LabUI.Vertical(panel, 6f, 14);
        layout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform header = LabUI.Row(panel, 24f);
        header.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        headerLabel = LabUI.Label(header, "MISSION", 14f, UITheme.Accent);
        headerLabel.fontStyle = FontStyles.Bold;
        headerLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        LabUI.Button(header, "X", UITheme.ButtonGrey, TogglePanel, 28f).GetComponent<LayoutElement>().flexibleWidth = 0f;

        titleLabel = LabUI.Label(panel, "", 21f, UITheme.TextPrimary);
        titleLabel.fontStyle = FontStyles.Bold;
        briefLabel = LabUI.Label(panel, "", 15f, UITheme.TextSecondary);
        objectivesLabel = LabUI.Label(panel, "", 16f, UITheme.TextPrimary);
        objectivesLabel.lineSpacing = 8f;

        hintLabel = LabUI.Label(panel, "", 15f, UITheme.Warning);
        bannerLabel = LabUI.Label(panel, "MISSION COMPLETE!  Press > for the next one.", 17f, UITheme.Success, TextAlignmentOptions.Center);
        bannerLabel.fontStyle = FontStyles.Bold;

        RectTransform buttons = LabUI.Row(panel, 34f);
        LabUI.Button(buttons, "<", UITheme.ButtonGrey, () => Go(-1));
        startLabel = LabUI.Button(buttons, "START", UITheme.ButtonBlue, StartMission).GetComponentInChildren<TextMeshProUGUI>();
        LabUI.Button(buttons, "HINT", UITheme.ButtonAmber, NextHint);
        LabUI.Button(buttons, ">", UITheme.ButtonGrey, () => Go(1));
    }
}
