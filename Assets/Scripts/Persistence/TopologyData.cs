using System;
using System.Collections.Generic;

// Plain data for saving a lab to JSON (JsonUtility-friendly)
[Serializable]
public class TopologyData
{
    public string name = "My Lab";
    public List<DeviceData> devices = new List<DeviceData>();
    public List<LinkData> links = new List<LinkData>();

    public DeviceData Add(string type, string deviceName, float x, float z, string ip = "", string mask = "", string gateway = "")
    {
        DeviceData device = new DeviceData { type = type, name = deviceName, x = x, z = z, ip = ip, mask = mask, gateway = gateway };
        devices.Add(device);
        return device;
    }

    public TopologyData Link(string a, string b, bool connected = true)
    {
        links.Add(new LinkData { a = a, b = b, connected = connected });
        return this;
    }
}

[Serializable]
public class DeviceData
{
    public string type;
    public string name;
    public float x, z;
    public string ip = "";
    public string mask = "";
    public string gateway = "";
    public bool online = true;
    public List<string> acl = new List<string>();
}

[Serializable]
public class LinkData
{
    public string a;
    public string b;
    public bool connected = true;
}
