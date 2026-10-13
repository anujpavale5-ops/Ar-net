using System;

// One router access-control entry, e.g. "deny 192.168.1.0/24 192.168.2.20".
// Rules are checked top to bottom; the first match wins and unmatched traffic is permitted.
[Serializable]
public class AclRule
{
    public bool permit;
    public uint sourceNetwork, sourceMask;
    public uint destinationNetwork, destinationMask;

    public bool Matches(uint source, uint destination)
    {
        return (source & sourceMask) == sourceNetwork && (destination & destinationMask) == destinationNetwork;
    }

    public override string ToString()
    {
        return (permit ? "permit " : "deny ") + Describe(sourceNetwork, sourceMask) + " " + Describe(destinationNetwork, destinationMask);
    }

    static string Describe(uint network, uint mask)
    {
        if (mask == 0) return "any";
        int prefix = IpUtil.PrefixLength(mask);
        return prefix == 32 ? IpUtil.ToString(network) : IpUtil.ToString(network) + "/" + prefix;
    }

    // Parses "deny <source> <destination>" where each side is "any", an address, or address/prefix
    public static bool TryParse(string text, out AclRule rule, out string error)
    {
        rule = null;
        error = null;
        string[] parts = text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 3 || (parts[0] != "deny" && parts[0] != "permit"))
        {
            error = "usage: acl deny|permit <source|any> <destination|any>";
            return false;
        }

        if (!TryParseTarget(parts[1], out uint sourceNetwork, out uint sourceMask))
        {
            error = $"'{parts[1]}' is not an address, network (a.b.c.d/nn) or 'any'";
            return false;
        }

        if (!TryParseTarget(parts[2], out uint destinationNetwork, out uint destinationMask))
        {
            error = $"'{parts[2]}' is not an address, network (a.b.c.d/nn) or 'any'";
            return false;
        }

        rule = new AclRule
        {
            permit = parts[0] == "permit",
            sourceNetwork = sourceNetwork,
            sourceMask = sourceMask,
            destinationNetwork = destinationNetwork,
            destinationMask = destinationMask
        };
        return true;
    }

    static bool TryParseTarget(string text, out uint network, out uint mask)
    {
        network = 0;
        mask = 0;
        if (text == "any") return true;

        string[] pieces = text.Split('/');
        int prefix = 32;
        if (pieces.Length == 2 && (!int.TryParse(pieces[1], out prefix) || prefix < 0 || prefix > 32)) return false;
        if (pieces.Length > 2 || !IpUtil.TryParse(pieces[0], out uint address)) return false;

        mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        network = address & mask;
        return true;
    }
}
