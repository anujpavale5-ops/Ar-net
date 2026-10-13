using System.Collections.Generic;

public static class IpUtil
{
    public static bool TryParse(string text, out uint address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string[] octets = text.Trim().Split('.');
        if (octets.Length != 4) return false;

        foreach (string octet in octets)
        {
            if (octet.Length == 0 || octet.Length > 3) return false;
            if (!int.TryParse(octet, out int value) || value < 0 || value > 255) return false;
            address = (address << 8) | (uint)value;
        }
        return true;
    }

    public static bool IsValidMask(string text)
    {
        if (!TryParse(text, out uint mask) || mask == 0) return false;
        uint inverted = ~mask;
        return (inverted & (inverted + 1)) == 0;
    }

    public static int PrefixLength(uint mask)
    {
        int bits = 0;
        while (mask != 0) { bits += (int)(mask >> 31); mask <<= 1; }
        return bits;
    }

    public static string ToString(uint address)
    {
        return $"{address >> 24}.{(address >> 16) & 255}.{(address >> 8) & 255}.{address & 255}";
    }

    // Routers may hold several interface addresses separated by commas, e.g. "192.168.1.1, 10.0.0.1"
    public static List<uint> ParseList(string text)
    {
        List<uint> result = new List<uint>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (string part in text.Split(','))
        {
            if (TryParse(part, out uint address)) result.Add(address);
        }
        return result;
    }

    public static bool IsValidList(string text, out string error)
    {
        error = null;
        string[] parts = text.Split(',');
        foreach (string part in parts)
        {
            if (!TryParse(part, out uint address))
            {
                error = $"'{part.Trim()}' is not a valid IPv4 address";
                return false;
            }
            uint first = address >> 24;
            if (first == 0 || first == 127 || first >= 224)
            {
                error = $"{part.Trim()} is reserved and cannot be assigned";
                return false;
            }
        }
        return true;
    }

    public static bool SameSubnet(uint a, uint b, uint mask)
    {
        return (a & mask) == (b & mask);
    }
}
