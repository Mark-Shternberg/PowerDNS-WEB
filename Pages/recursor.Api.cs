using System.Text;
using System.Text.Json;

namespace PowerDNS_Web.Pages
{
    public partial class RecursorModel
    {
        // ========================= ZONE LOADERS =========================
        private async Task<List<AuthZoneDto>> SafeGetAuthZonesAsync()
        {
            try
            {
                using var c = _httpClientFactory.CreateClient();
                c.DefaultRequestHeaders.Remove("X-API-Key");
                c.DefaultRequestHeaders.Add("X-API-Key", PdnsKey);

                var resp = await c.GetAsync($"{PdnsUrl}/api/v1/servers/localhost/zones");
                if (!resp.IsSuccessStatusCode) return new List<AuthZoneDto>();

                var json = await resp.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<AuthZoneDto>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<AuthZoneDto>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch authoritative zones");
                return new List<AuthZoneDto>();
            }
        }

        private async Task<List<RecursorZoneDto>> SafeGetRecursorZonesAsync()
        {
            try
            {
                using var c = NewRecursorClient();
                var resp = await c.GetAsync($"{RecursorUrl}/api/v1/servers/localhost/zones");
                if (!resp.IsSuccessStatusCode) return new List<RecursorZoneDto>();

                var json = await resp.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<RecursorZoneDto>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<RecursorZoneDto>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch recursor zones");
                return new List<RecursorZoneDto>();
            }
        }

        private HttpClient NewRecursorClient()
        {
            var c = _httpClientFactory.CreateClient();
            c.DefaultRequestHeaders.Remove("X-API-Key");
            c.DefaultRequestHeaders.Add("X-API-Key", RecursorKey);
            return c;
        }

        // ========================= ZONE IDENTIFIERS =========================
        private string BuildRecursorZoneUrl(string zoneId)
            => $"{RecursorUrl.TrimEnd('/')}/api/v1/servers/localhost/zones/{zoneId}";

        private static string GetSafeZoneId(string? requestedId, string zoneName)
        {
            var zoneId = string.IsNullOrWhiteSpace(requestedId)
                ? ToPowerDnsZoneId(zoneName)
                : requestedId.Trim();

            // PowerDNS documents zone ids as opaque but URL-safe. Never allow a
            // client-supplied id to add or navigate path segments.
            if (zoneId.Length == 0 || zoneId is "." or ".." ||
                zoneId.Contains('/') || zoneId.Contains('\\') ||
                zoneId.Contains('?') || zoneId.Contains('#'))
            {
                return ToPowerDnsZoneId(zoneName);
            }

            return zoneId;
        }

        private static string ToPowerDnsZoneId(string zoneName)
        {
            var name = EnsureTrailingDot(zoneName.Trim());
            if (name == ".") return "=2E";

            var bytes = Encoding.UTF8.GetBytes(name);
            var result = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
            {
                var c = (char)b;
                if ((c >= 'A' && c <= 'Z') ||
                    (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') || c is '.' or '-')
                {
                    result.Append(c);
                }
                else
                {
                    result.Append('=').Append(b.ToString("X2"));
                }
            }

            return result.ToString();
        }

        private static string EnsureTrailingDot(string s)
            => string.IsNullOrWhiteSpace(s) ? s : (s.EndsWith('.') ? s : s + ".");

        // ========================= FORWARDING ENDPOINTS =========================
        private static bool LooksLikeHostPort(string s)
        {
            var idx = s.LastIndexOf(':');
            if (idx <= 0 || idx >= s.Length - 1) return false;
            if (!int.TryParse(s[(idx + 1)..], out var port) || port < 1 || port > 65535) return false;
            return !string.IsNullOrWhiteSpace(s[..idx]);
        }

        private static bool UsesPort(string server, int expectedPort)
        {
            var idx = server.LastIndexOf(':');
            return idx > 0 && int.TryParse(server[(idx + 1)..], out var port) && port == expectedPort;
        }
    }
}
