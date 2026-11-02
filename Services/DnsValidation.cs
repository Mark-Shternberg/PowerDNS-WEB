using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace PowerDNS_Web.Services;

public static class DnsValidation
{
    // ========================= DNS NAMES =========================
    public static bool IsName(string? name, bool owner = false)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 254) return false;
        if (name == ".") return true;
        return name.TrimEnd('.').Split('.').All(label =>
            label.Length is > 0 and <= 63 &&
            (owner && label == "*" || Regex.IsMatch(label, owner
                ? @"^[a-zA-Z0-9_](?:[a-zA-Z0-9_-]*[a-zA-Z0-9_])?$"
                : @"^[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?$")));
    }

    // ========================= FORWARDING ENDPOINTS =========================
    public static string[] Masters(string? value) => (value ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsEndpoint(string value)
    {
        if (IPAddress.TryParse(value, out _)) return true;
        return IPEndPoint.TryParse(value, out var ep) && ep.Port is > 0 and <= 65535;
    }

    // ========================= ZONE OWNERSHIP =========================
    public static bool IsSecondary(string? kind) =>
        string.Equals(kind, "Slave", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, "Secondary", StringComparison.OrdinalIgnoreCase);

    public static bool IsWithin(string owner, string zone) =>
        owner.Equals(zone, StringComparison.OrdinalIgnoreCase) ||
        owner.EndsWith("." + zone, StringComparison.OrdinalIgnoreCase);

    // ========================= RECORD CONTENT =========================
    public static string? RecordError(string name, string zone, string type, string content, int ttl)
    {
        if (!IsName(name, true) || !IsWithin(name, zone)) return "Record name must belong to this zone.";
        if (ttl < 0) return "TTL must be a non-negative integer.";
        if (string.IsNullOrWhiteSpace(content) || content.Contains('\n') || content.Contains('\r'))
            return "Record content is required and must be a single line.";
        if (type is "A" or "AAAA")
        {
            if (!IPAddress.TryParse(content, out var ip) ||
                ip.AddressFamily != (type == "A" ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6))
                return "Invalid " + type + " address.";
        }
        else if (type is "NS" or "CNAME" or "PTR")
        {
            if (!IsName(content) || !content.EndsWith('.')) return "Target must be a fully qualified DNS name.";
            if (type == "CNAME" && name.Equals(zone, StringComparison.OrdinalIgnoreCase)) return "A zone apex cannot be a CNAME.";
        }
        else if (type is "MX" or "SRV")
        {
            var fields = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int numbers = type == "MX" ? 1 : 3;
            if (fields.Length != numbers + 1 || fields.Take(numbers).Any(x => !ushort.TryParse(x, out _)) ||
                !IsName(fields.Last()) || !fields.Last().EndsWith('.')) return "Invalid priority, weight, port or target.";
        }
        else if (type == "SOA")
        {
            var fields = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!name.Equals(zone, StringComparison.OrdinalIgnoreCase) || fields.Length != 7 ||
                !IsName(fields[0]) || !IsName(fields[1]) || fields.Skip(2).Any(x => !uint.TryParse(x, out _)))
                return "Invalid SOA record.";
        }
        else if (type == "HTTPS")
        {
            var fields = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || !ushort.TryParse(fields[0], out _) || !IsName(fields[1]))
                return "HTTPS requires a priority and target, followed by optional service parameters.";
        }
        else if (type != "TXT") return "Unsupported record type.";
        return null;
    }
}
