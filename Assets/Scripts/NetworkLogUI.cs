using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class NetworkLogUI : MonoBehaviour
{
    public TMP_Text logText;
    public RectTransform panelRect;
    public RectTransform textRect;

    public float padding = 40f;
    public float minHeight = 180f;
    public float maxHeight = 450f;

    public int maxPackets = 3;

    private readonly List<string> packetLogs = new List<string>();

    // Packets currently in flight, in the order they were sent
    private readonly List<object> liveOrder = new List<object>();
    private readonly Dictionary<object, string> liveEntries = new Dictionary<object, string>();

    public void ClearLog()
    {
        packetLogs.Clear();
        liveOrder.Clear();
        liveEntries.Clear();
        RefreshLog();
    }

    public void StartPacket(object packet, NetworkDevice source, NetworkDevice destination)
    {
        if (!liveEntries.ContainsKey(packet)) liveOrder.Add(packet);
        liveEntries[packet] = source.deviceName + " → " + destination.deviceName + "\nSTATUS: TRANSMITTING...";
        RefreshLog();
    }

    public void AddHop(object packet, NetworkDevice from, NetworkDevice to)
    {
        if (!liveEntries.ContainsKey(packet)) return;
        liveEntries[packet] += "\n• " + from.deviceName + " → " + to.deviceName;
        RefreshLog();
    }

    public void AddHopFailure(object packet, NetworkDevice from, NetworkDevice to)
    {
        if (!liveEntries.ContainsKey(packet)) return;
        liveEntries[packet] += "\n× " + from.deviceName + " → " + to.deviceName;
        RefreshLog();
    }

    public void CompletePacket(object packet, NetworkDevice destination)
    {
        FinishLive(packet, "\nSTATUS: DELIVERED");
    }

    public void FailedPacket(object packet, string reason)
    {
        FinishLive(packet, "\nSTATUS: FAILED ×\n" + reason);
    }

    void FinishLive(object packet, string suffix)
    {
        if (liveEntries.TryGetValue(packet, out string entry))
        {
            liveEntries.Remove(packet);
            liveOrder.Remove(packet);
            AddToHistory(entry.Replace("\nSTATUS: TRANSMITTING...", "") + suffix);
        }
        RefreshLog();
    }

    public void ShowFailedPacket(NetworkDevice source, NetworkDevice destination, string reason)
    {
        AddToHistory(source.deviceName + " → " + destination.deviceName +
                     "\nSTATUS: FAILED ×" +
                     "\nREASON: " + reason);
        RefreshLog();
    }

    public void LogDisconnection(NetworkDevice deviceA, NetworkDevice deviceB)
    {
        AddToHistory("SYSTEM ALERT\n" +
                     "× CABLE DISCONNECTED\n" +
                     deviceA.deviceName + " × " + deviceB.deviceName);
        RefreshLog();
    }

    void AddToHistory(string entry)
    {
        if (string.IsNullOrEmpty(entry))
            return;

        packetLogs.Add(entry);

        while (packetLogs.Count > Mathf.Max(1, maxPackets))
        {
            packetLogs.RemoveAt(0);
        }
    }

    static string Heading(string text, Color color)
    {
        return $"<size=80%><b><color={UITheme.Hex(color)}>{text}</color></b></size>\n";
    }

    // Colour each log line by what it reports
    static string Colorize(string block)
    {
        string[] lines = block.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            Color color = UITheme.TextPrimary;

            if (line.Contains("DELIVERED")) color = UITheme.Success;
            else if (line.Contains("FAILED") || line.StartsWith("×") || line.StartsWith("REASON")) color = UITheme.Failure;
            else if (line.Contains("SYSTEM ALERT") || line.Contains("DISCONNECTED")) color = UITheme.Warning;
            else if (line.Contains("TRANSMITTING")) color = UITheme.Accent;
            else if (line.StartsWith("•")) color = UITheme.TextSecondary;

            lines[i] = $"<color={UITheme.Hex(color)}>{line}</color>";
        }
        return string.Join("\n", lines);
    }

    void RefreshLog()
    {
        if (logText == null)
            return;

        string finalText = $"<b><color={UITheme.Hex(UITheme.Accent)}>NETWORK ACTIVITY</color></b>\n\n";

        // Live packet first so it is never cut off, then history newest-first
        foreach (object packet in liveOrder)
        {
            finalText += Heading("● LIVE PACKET", UITheme.Accent) + Colorize(liveEntries[packet]) + "\n\n";
        }

        for (int i = packetLogs.Count - 1; i >= 0; i--)
        {
            finalText += Heading("EVENT #" + (i + 1), UITheme.TextSecondary) + Colorize(packetLogs[i]) + "\n\n";
        }

        if (packetLogs.Count == 0 && liveOrder.Count == 0)
        {
            finalText += $"<color={UITheme.Hex(UITheme.TextSecondary)}><i>Waiting for activity...</i></color>";
        }

        logText.text = finalText.TrimEnd('\n');

        Canvas.ForceUpdateCanvases();

        float textHeight = logText.preferredHeight;
        float requiredHeight = textHeight + padding;
        float newHeight = Mathf.Clamp(requiredHeight, minHeight, maxHeight);

        if (panelRect != null)
        {
            panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, newHeight);
        }

        if (textRect != null)
        {
            textRect.sizeDelta = new Vector2(textRect.sizeDelta.x, Mathf.Min(textHeight, maxHeight - padding));
        }

        logText.overflowMode = textHeight > maxHeight - padding
            ? TextOverflowModes.Ellipsis
            : TextOverflowModes.Overflow;
    }
}
