using System.Collections.Generic;

// Ready-made labs: examples for the Load menu and starting points for missions.
public static class TopologyLibrary
{
    public const string Mask24 = "255.255.255.0";

    public static readonly List<(string title, System.Func<TopologyData> build)> Examples = new List<(string, System.Func<TopologyData>)>
    {
        ("Small office (one subnet)", SmallOffice),
        ("Two branches + router", TwoBranches),
        ("Data center: two routers", DataCenter),
    };

    public static TopologyData Empty() => new TopologyData { name = "Empty lab" };

    public static TopologyData SmallOffice()
    {
        TopologyData lab = new TopologyData { name = "Small office" };
        lab.Add("PC", "PC1", -5f, -2f, "192.168.1.11", Mask24, "192.168.1.1");
        lab.Add("PC", "PC2", -5f, 2f, "192.168.1.12", Mask24, "192.168.1.1");
        lab.Add("PC", "PC3", 4f, -2f, "192.168.1.13", Mask24, "192.168.1.1");
        lab.Add("Switch", "Switch1", 0f, 0f);
        lab.Add("Server", "Server1", 4f, 3f, "192.168.1.20", Mask24, "192.168.1.1");
        lab.Link("PC1", "Switch1").Link("PC2", "Switch1").Link("PC3", "Switch1").Link("Server1", "Switch1");
        return lab;
    }

    public static TopologyData TwoBranches()
    {
        TopologyData lab = new TopologyData { name = "Two branches" };
        lab.Add("PC", "PC1", -7f, -2f, "192.168.1.10", Mask24, "192.168.1.1");
        lab.Add("PC", "PC2", -7f, 2.5f, "192.168.1.11", Mask24, "192.168.1.1");
        lab.Add("Switch", "Switch1", -3.5f, 0.5f);
        lab.Add("Router", "Router1", 0f, 3f, "192.168.1.1, 192.168.2.1", Mask24);
        lab.Add("Switch", "Switch2", 3.5f, 0.5f);
        lab.Add("PC", "PC3", 7f, -2f, "192.168.2.10", Mask24, "192.168.2.1");
        lab.Add("Server", "Server1", 7f, 3f, "192.168.2.20", Mask24, "192.168.2.1");
        lab.Link("PC1", "Switch1").Link("PC2", "Switch1").Link("Switch1", "Router1")
           .Link("Router1", "Switch2").Link("Switch2", "PC3").Link("Switch2", "Server1");
        return lab;
    }

    public static TopologyData DataCenter()
    {
        TopologyData lab = new TopologyData { name = "Data center" };
        lab.Add("PC", "PC1", -8f, -1f, "10.0.1.10", Mask24, "10.0.1.1");
        lab.Add("PC", "PC2", -8f, 3f, "10.0.1.11", Mask24, "10.0.1.1");
        lab.Add("Switch", "Switch1", -5f, 1f);
        lab.Add("Router", "Router1", -2f, 1f, "10.0.1.1, 10.0.0.1", Mask24);
        lab.Add("Router", "Router2", 2f, 1f, "10.0.0.2, 10.0.2.1", Mask24);
        lab.Add("Switch", "Switch2", 5f, 1f);
        lab.Add("Server", "Web1", 8f, -1f, "10.0.2.20", Mask24, "10.0.2.1");
        lab.Add("Server", "DB1", 8f, 3f, "10.0.2.21", Mask24, "10.0.2.1");
        lab.Link("PC1", "Switch1").Link("PC2", "Switch1").Link("Switch1", "Router1")
           .Link("Router1", "Router2").Link("Router2", "Switch2").Link("Switch2", "Web1").Link("Switch2", "DB1");
        return lab;
    }

    // ---------- mission starting points ----------

    public static TopologyData LanUnconfigured()
    {
        TopologyData lab = new TopologyData { name = "Mission: addressing" };
        lab.Add("PC", "PC1", -4f, 0f);
        lab.Add("PC", "PC2", 4f, 0f);
        lab.Add("Switch", "Switch1", 0f, 2f);
        lab.Link("PC1", "Switch1").Link("PC2", "Switch1");
        return lab;
    }

    public static TopologyData TwoLansRouterUnconfigured()
    {
        TopologyData lab = TwoBranches();
        lab.name = "Mission: through the router";
        lab.devices.Find(d => d.name == "Router1").ip = "";
        lab.devices.Find(d => d.name == "Router1").mask = "";
        return lab;
    }

    // Ticket: "PC1 can't reach the server" - wrong gateway and an unplugged cable
    public static TopologyData BrokenGatewayAndCable()
    {
        TopologyData lab = TwoBranches();
        lab.name = "Ticket #1042";
        lab.devices.Find(d => d.name == "PC1").gateway = "192.168.1.254";
        lab.links.Find(l => l.a == "Switch2" && l.b == "Server1").connected = false;
        return lab;
    }

    // Ticket: "PC2 keeps dropping off the network" - duplicate IP and the server is powered off
    public static TopologyData BrokenIpConflict()
    {
        TopologyData lab = TwoBranches();
        lab.name = "Ticket #1057";
        lab.devices.Find(d => d.name == "PC2").ip = "192.168.1.10";
        lab.devices.Find(d => d.name == "Server1").online = false;
        return lab;
    }
}
