using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Command prompt for the device you last clicked: ping, tracert, ipconfig, power, router ACLs.
// Toggle with the ` key or the CONSOLE button.
public class LabConsole : MonoBehaviour
{
    public static LabConsole Instance { get; private set; }

    // Raised when a ping or tracert finishes (used by missions)
    public static event System.Action<NetworkDevice, NetworkDevice, bool> PingCompleted;
    public static event System.Action<NetworkDevice, NetworkDevice, bool> TraceCompleted;

    [Tooltip("Ping and tracert packets travel faster than normal traffic")]
    public float probeSpeedMultiplier = 2.5f;
    public int maxLines = 14;

    public bool IsOpen => panel != null && panel.gameObject.activeSelf;

    private RectTransform panel;
    private TextMeshProUGUI titleLabel;
    private TextMeshProUGUI promptLabel;
    private TextMeshProUGUI outputLabel;
    private TMP_InputField input;
    private readonly List<string> lines = new List<string>();
    private readonly List<string> history = new List<string>();
    private int historyIndex;

    private NetworkDevice device;
    private NetworkManager manager;
    private bool busy;

    void Awake()
    {
        Instance = this;
        Build();
        Print($"<color={UITheme.Hex(UITheme.Accent)}>Ar-Net command prompt.</color> Click a device, then type <b>help</b>.");
        panel.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        manager = FindAnyObjectByType<NetworkManager>();
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.backquoteKey.wasPressedThisFrame)
        {
            if (IsOpen && input.isFocused) input.text = input.text.Replace("`", "");
            Toggle();
            return;
        }

        if (!IsOpen || !input.isFocused || history.Count == 0) return;

        if (keyboard.upArrowKey.wasPressedThisFrame) RecallHistory(-1);
        else if (keyboard.downArrowKey.wasPressedThisFrame) RecallHistory(1);
    }

    void RecallHistory(int direction)
    {
        historyIndex = Mathf.Clamp(historyIndex + direction, 0, history.Count);
        input.text = historyIndex < history.Count ? history[historyIndex] : "";
        input.caretPosition = input.text.Length;
    }

    // ---------- public API ----------

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen) return;
        panel.gameObject.SetActive(true);
        LabUI.CursorRequests++;
        if (device == null) PickDefaultDevice();
        RefreshPrompt();
        StartCoroutine(FocusNextFrame());
    }

    public void Close()
    {
        if (!IsOpen) return;
        panel.gameObject.SetActive(false);
        LabUI.CursorRequests = Mathf.Max(0, LabUI.CursorRequests - 1);
    }

    IEnumerator FocusNextFrame()
    {
        yield return null;
        input.text = "";
        input.ActivateInputField();
        input.Select();
    }

    public void SetDevice(NetworkDevice newDevice)
    {
        if (newDevice == null || newDevice.isPreviewModel) return;
        device = newDevice;
        RefreshPrompt();
    }

    // ---------- commands ----------

    void Submit(string text)
    {
        text = text.Trim();
        input.text = "";

        if (text.Length > 0)
        {
            history.Add(text);
            historyIndex = history.Count;
            Print($"<color={UITheme.Hex(UITheme.Accent)}>{PromptText()}</color> {Escape(text)}");
            Execute(text);
        }

        StartCoroutine(FocusNextFrame());
    }

    void Execute(string text)
    {
        string[] args = text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        string command = args[0].ToLowerInvariant();

        if (busy && command != "clear" && command != "cls")
        {
            Print(Warn("Busy - wait for the current command to finish."));
            return;
        }

        switch (command)
        {
            case "help": Help(); break;
            case "clear": case "cls": lines.Clear(); RenderOutput(); break;
            case "devices": ListDevices(); break;
            case "use": Use(args); break;
            case "ipconfig": IpConfig(); break;
            case "ping": if (RequireDevice()) StartCoroutine(Ping(args)); break;
            case "tracert": case "traceroute": if (RequireDevice()) StartCoroutine(Trace(args)); break;
            case "shutdown": SetPower(args, false); break;
            case "poweron": SetPower(args, true); break;
            case "acl": Acl(text, args); break;
            case "speed": Speed(args); break;
            case "pause": SimulationClock.TogglePause(); Print(SimulationClock.Paused ? "Simulation paused." : "Simulation resumed."); break;
            case "exit": Close(); break;
            default: Print(Fail($"'{Escape(args[0])}' is not a command. Type help.")); break;
        }
    }

    void Help()
    {
        Print("<b>help</b>  devices  use <i>name</i>  ipconfig  clear  exit");
        Print("<b>ping</b> <i>ip|name</i> [-n count]    <b>tracert</b> <i>ip|name</i>");
        Print("<b>shutdown</b> / <b>poweron</b> [<i>name</i>]    <b>speed</b> <i>0.25-4</i>    <b>pause</b>");
        Print("<b>acl</b> deny|permit <i>src</i> <i>dst</i>   acl list   acl remove <i>n</i>   acl clear   (routers)");
        Print(Dim("src/dst: any, 192.168.1.10 or 192.168.1.0/24"));
    }

    void ListDevices()
    {
        foreach (NetworkDevice d in SortedDevices())
        {
            string ip = d.IsSwitch ? Dim("switch") : string.IsNullOrEmpty(d.ipAddress) ? Warn("unconfigured") : d.ipAddress;
            string state = d.isOnline ? Good("up") : Fail("down");
            Print($"  {d.deviceName,-9} {ip}   {state}");
        }
    }

    void Use(string[] args)
    {
        if (args.Length < 2) { Print("usage: use <device name>"); return; }
        NetworkDevice target = FindByName(args[1]);
        if (target == null) { Print(Fail($"No device called {Escape(args[1])}")); return; }
        SetDevice(target);
        Print($"Now using {target.deviceName}.");
    }

    void IpConfig()
    {
        if (!RequireDevice()) return;
        Print($"{device.deviceName} ({device.deviceType})  {(device.isOnline ? Good("online") : Fail("offline"))}");
        Print($"   IPv4 Address . . : {Or(device.ipAddress, "not set")}");
        Print($"   Subnet Mask  . . : {Or(device.subnetMask, "not set")}");
        Print($"   Default Gateway  : {Or(device.gateway, device.IsRouter ? "(router)" : "not set")}");
        if (device.TryGetMask(out uint mask) && device.Addresses.Count > 0)
            Print(Dim($"   Network          : {IpUtil.ToString(device.Addresses[0] & mask)}/{IpUtil.PrefixLength(mask)}"));
    }

    IEnumerator Ping(string[] args)
    {
        if (args.Length < 2) { Print("usage: ping <ip|name> [-n count]"); yield break; }

        NetworkDevice target = ResolveTarget(args[1], out string targetIp);
        int count = 3;
        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "-n" && int.TryParse(args[i + 1], out int n)) count = Mathf.Clamp(n, 1, 10);
        }

        busy = true;
        NetworkDevice source = device;
        Print($"Pinging {Escape(args[1])} [{targetIp}] with 32 bytes of data:");

        if (target == null)
        {
            for (int i = 0; i < count; i++) Print(Fail("Request timed out."));
            Print($"Packets: Sent = {count}, Received = 0, Lost = {count} (100% loss)");
            busy = false;
            PingCompleted?.Invoke(source, null, false);
            yield break;
        }

        int received = 0;
        for (int i = 0; i < count; i++)
        {
            ProbeResult outbound = new ProbeResult();
            yield return SendProbe(source, target, outbound, null);

            if (!outbound.delivered)
            {
                Print(Fail(outbound.unreachable ? $"Reply from {PrimaryIp(source)}: Destination host unreachable. ({outbound.reason})" : "Request timed out."));
                continue;
            }

            ProbeResult reply = new ProbeResult();
            yield return SendProbe(target, source, reply, null);

            if (!reply.delivered)
            {
                Print(Fail($"Request timed out. (no reply path: {reply.reason})"));
                continue;
            }

            received++;
            int routers = CountRouters(outbound.route);
            int time = 1 + routers * 2 + Random.Range(0, 2);
            Print(Good($"Reply from {targetIp}: bytes=32 time={time}ms TTL={128 - routers}"));
        }

        int lost = count - received;
        Print($"Packets: Sent = {count}, Received = {received}, Lost = {lost} ({lost * 100 / count}% loss)");
        busy = false;
        PingCompleted?.Invoke(source, target, received > 0);
    }

    IEnumerator Trace(string[] args)
    {
        if (args.Length < 2) { Print("usage: tracert <ip|name>"); yield break; }

        NetworkDevice target = ResolveTarget(args[1], out string targetIp);
        NetworkDevice source = device;
        busy = true;
        Print($"Tracing route to {Escape(args[1])} [{targetIp}] over a maximum of 30 hops:");

        if (target == null)
        {
            Print(Fail("  1     *        Request timed out."));
            Print("Trace complete.");
            busy = false;
            TraceCompleted?.Invoke(source, null, false);
            yield break;
        }

        int hop = 0;
        ProbeResult result = new ProbeResult();
        yield return SendProbe(source, target, result, reached =>
        {
            // Switches are invisible at layer 3, so only routers and the destination appear
            if (!reached.IsRouter && reached != target) return;
            hop++;
            Print($"  {hop,-3} {1 + hop * 2,3} ms   {reached.deviceName} [{IpFacing(reached, source)}]");
        });

        if (!result.delivered)
        {
            Print(Fail($"  {hop + 1,-3}   *      {(result.unreachable ? "Destination unreachable: " + result.reason : "Request timed out.")}"));
        }

        Print("Trace complete.");
        busy = false;
        TraceCompleted?.Invoke(source, target, result.delivered);
    }

    class ProbeResult
    {
        public bool delivered;
        public bool unreachable;
        public string reason;
        public IReadOnlyList<NetworkDevice> route;
    }

    // Sends one animated packet and waits for it to arrive or fail
    IEnumerator SendProbe(NetworkDevice from, NetworkDevice to, ProbeResult result, System.Action<NetworkDevice> onHop)
    {
        List<NetworkDevice> route = manager.BuildRoute(from, to, out string reason);
        if (route == null)
        {
            result.unreachable = true;
            result.reason = reason;
            manager.HandleFailedPacket(from, to, reason);
            yield return new WaitForSeconds(0.4f);
            yield break;
        }

        GameObject packetObject = manager.CreatePacket(from, to, route);
        Packet packet = packetObject != null ? packetObject.GetComponent<Packet>() : null;
        if (packet == null)
        {
            result.reason = "packet could not be created";
            yield break;
        }

        packet.speed *= probeSpeedMultiplier;
        bool done = false;
        packet.Finished += (p, ok) => { done = true; result.delivered = ok; };
        if (onHop != null) packet.HopReached += (p, reached) => onHop(reached);
        result.route = route;

        while (!done && packet != null) yield return null;
        yield return new WaitForSeconds(0.15f);
    }

    void SetPower(string[] args, bool on)
    {
        NetworkDevice target = args.Length > 1 ? FindByName(args[1]) : device;
        if (target == null) { Print(Fail(args.Length > 1 ? $"No device called {Escape(args[1])}" : "Select a device first.")); return; }

        target.SetStatus(on);
        Print(on ? Good($"{target.deviceName} powered on.") : Warn($"{target.deviceName} shut down - traffic through it will fail."));
    }

    void Acl(string text, string[] args)
    {
        if (!RequireDevice()) return;
        if (!device.IsRouter) { Print(Fail($"{device.deviceName} is not a router. Use a router first (e.g. use Router1).")); return; }

        string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "list";
        switch (sub)
        {
            case "list":
                if (device.accessList.Count == 0) Print(Dim("No access rules - all traffic is permitted."));
                for (int i = 0; i < device.accessList.Count; i++) Print($"  {i + 1}. {device.accessList[i]}");
                break;

            case "clear":
                device.accessList.Clear();
                NetworkDevice.MarkTopologyChanged();
                Print("Access list cleared.");
                break;

            case "remove":
                if (args.Length > 2 && int.TryParse(args[2], out int index) && index >= 1 && index <= device.accessList.Count)
                {
                    Print($"Removed: {device.accessList[index - 1]}");
                    device.accessList.RemoveAt(index - 1);
                    NetworkDevice.MarkTopologyChanged();
                }
                else Print("usage: acl remove <rule number>");
                break;

            default:
                if (AclRule.TryParse(text.Substring(text.IndexOf(' ') + 1), out AclRule rule, out string error))
                {
                    device.accessList.Add(rule);
                    NetworkDevice.MarkTopologyChanged();
                    Print(Good($"{device.deviceName}: added rule {device.accessList.Count}: {rule}"));
                }
                else Print(Fail(error));
                break;
        }
    }

    void Speed(string[] args)
    {
        if (args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float speed))
        {
            SimulationClock.Speed = Mathf.Clamp(speed, 0.25f, 4f);
            if (LabToolbar.Instance != null) LabToolbar.Instance.SyncSpeed();
        }
        Print($"Simulation speed: {SimulationClock.Speed:0.##}x");
    }

    // ---------- helpers ----------

    bool RequireDevice()
    {
        if (device != null && device.isActiveAndEnabled) return true;
        Print(Fail("No device selected. Click a device or type: use PC1"));
        return false;
    }

    void PickDefaultDevice()
    {
        foreach (NetworkDevice d in SortedDevices())
        {
            if (!d.IsSwitch) { device = d; return; }
        }
    }

    static List<NetworkDevice> SortedDevices()
    {
        List<NetworkDevice> list = new List<NetworkDevice>();
        foreach (NetworkDevice d in NetworkDevice.All) if (d != null && !d.isPreviewModel) list.Add(d);
        list.Sort((a, b) => string.CompareOrdinal(a.deviceName, b.deviceName));
        return list;
    }

    static NetworkDevice FindByName(string deviceName)
    {
        foreach (NetworkDevice d in NetworkDevice.All)
        {
            if (d != null && !d.isPreviewModel && string.Equals(d.deviceName, deviceName, System.StringComparison.OrdinalIgnoreCase)) return d;
        }
        return null;
    }

    static NetworkDevice ResolveTarget(string text, out string ip)
    {
        NetworkDevice byName = FindByName(text);
        if (byName != null)
        {
            ip = byName.Addresses.Count > 0 ? IpUtil.ToString(byName.Addresses[0]) : "no address";
            return byName;
        }

        ip = text;
        if (!IpUtil.TryParse(text, out uint address)) return null;

        foreach (NetworkDevice d in NetworkDevice.All)
        {
            if (d != null && !d.isPreviewModel && d.HasAddress(address)) return d;
        }
        return null;
    }

    static string PrimaryIp(NetworkDevice d) => d.Addresses.Count > 0 ? IpUtil.ToString(d.Addresses[0]) : d.deviceName;

    // The router interface address on the source's side, like a real traceroute shows
    static string IpFacing(NetworkDevice hop, NetworkDevice source)
    {
        foreach (uint address in hop.Addresses)
        {
            if (source.IsInSameSubnet(address)) return IpUtil.ToString(address);
        }
        return PrimaryIp(hop);
    }

    static int CountRouters(IReadOnlyList<NetworkDevice> route)
    {
        int routers = 0;
        if (route == null) return 0;
        for (int i = 1; i < route.Count - 1; i++) if (route[i].IsRouter) routers++;
        return routers;
    }

    string PromptText() => (device != null ? device.deviceName : "lab") + ">";

    void RefreshPrompt()
    {
        promptLabel.text = PromptText();
        titleLabel.text = "COMMAND PROMPT" + (device != null ? $"  ·  {device.deviceName}" : "");
    }

    static string Or(string value, string fallback) => string.IsNullOrEmpty(value) ? Dim(fallback) : value;
    static string Escape(string text) => text.Replace("<", "&lt;");
    static string Good(string text) => $"<color={UITheme.Hex(UITheme.Success)}>{text}</color>";
    static string Fail(string text) => $"<color={UITheme.Hex(UITheme.Failure)}>{text}</color>";
    static string Warn(string text) => $"<color={UITheme.Hex(UITheme.Warning)}>{text}</color>";
    static string Dim(string text) => $"<color={UITheme.Hex(UITheme.TextSecondary)}>{text}</color>";

    public void Print(string line)
    {
        lines.Add(line);
        while (lines.Count > maxLines) lines.RemoveAt(0);
        RenderOutput();
    }

    void RenderOutput()
    {
        StringBuilder text = new StringBuilder();
        foreach (string line in lines) text.AppendLine(line);
        outputLabel.text = text.ToString();
    }

    // ---------- UI ----------

    void Build()
    {
        Canvas canvas = LabUI.CreateCanvas("ConsoleCanvas", 45);
        canvas.transform.SetParent(transform, false);

        panel = LabUI.Panel(canvas.transform, "ConsolePanel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 250f), new Vector2(780f, 380f));
        panel.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.055f, 0.96f);
        LabUI.Vertical(panel, 6f, 12);

        RectTransform header = LabUI.Row(panel, 26f);
        header.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        titleLabel = LabUI.Label(header, "COMMAND PROMPT", 17f, UITheme.Accent);
        titleLabel.fontStyle = FontStyles.Bold;
        Button close = LabUI.Button(header, "X", UITheme.ButtonGrey, Close, 30f);
        close.GetComponent<LayoutElement>().flexibleWidth = 0f;
        titleLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        outputLabel = LabUI.Label(panel, "", 15f, UITheme.TextPrimary, TextAlignmentOptions.TopLeft);
        outputLabel.textWrappingMode = TextWrappingModes.Normal;
        outputLabel.overflowMode = TextOverflowModes.Truncate;
        outputLabel.verticalAlignment = VerticalAlignmentOptions.Bottom;
        outputLabel.lineSpacing = -4f;
        LayoutElement outputLayout = outputLabel.gameObject.AddComponent<LayoutElement>();
        outputLayout.flexibleHeight = 1f;
        outputLayout.minHeight = 200f;

        RectTransform inputRow = LabUI.Row(panel, 34f, 8f);
        inputRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        promptLabel = LabUI.Label(inputRow, "lab>", 17f, UITheme.Accent, TextAlignmentOptions.Right);
        promptLabel.textWrappingMode = TextWrappingModes.NoWrap;
        promptLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;

        input = LabUI.Input(inputRow, "type a command, e.g. ping Server1", 34f);
        input.GetComponent<LayoutElement>().flexibleWidth = 1f;
        input.onSubmit.AddListener(Submit);
    }
}
