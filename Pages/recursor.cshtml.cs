using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace PowerDNS_Web.Pages
{
    public class RecursorModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _cfg;
        private readonly ILogger<RecursorModel> _logger;
        private readonly IStringLocalizer _L;

        public List<string> AvailableZones { get; private set; } = new();
        public List<ForwardZone> ForwardZones { get; private set; } = new();

        private string PdnsUrl => _cfg["pdns:url"] ?? "";
        private string PdnsKey => _cfg["pdns:api_key"] ?? "";
        private string RecursorUrl => _cfg["recursor:url"] ?? "";
        private string RecursorKey => _cfg["recursor:api_key"] ?? "";
        private string RootForwarder => _cfg["recursor:RootForwarder"] ?? "1.1.1.1:853";
        private string RecursorEnabled => _cfg["recursor:Enabled"] ?? _cfg["recursor:enabled"] ?? "Disabled";
        private bool IsRecursorOn => string.Equals(RecursorEnabled, "Enabled", StringComparison.OrdinalIgnoreCase);

        public RecursorModel(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<RecursorModel> logger, IStringLocalizerFactory factory)
        {
            _httpClientFactory = httpClientFactory;
            _cfg = configuration;
            _logger = logger;
            var asmName = Assembly.GetExecutingAssembly().GetName().Name!;
            _L = factory.Create("Pages.recursor", asmName);
        }

        // ===== View models / DTOs =====
        public class ForwardZone
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public List<string> ForwardTo { get; set; } = new();
        }

        private class AuthZoneDto
        {
            public string Name { get; set; } = "";
            public string Kind { get; set; } = "";
        }

        private class RecursorZoneDto
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Kind { get; set; } = "";
            public List<string> Servers { get; set; } = new();
            public bool? Recursion_Desired { get; set; }
        }

        public class ForwardZoneRequest
        {
            public string Id { get; set; } = "";
            public string Zone { get; set; } = "";
        }
        public class UpdateForwardZones
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string DnsServers { get; set; } = "";
            public string Transport { get; set; } = "";
        }

        // ===== Page GET =====
        public async Task OnGetAsync()
        {
            ViewData["RecursorEnabled"] = IsRecursorOn ? "Enabled" : "Disabled";
            if (!IsRecursorOn) return;

            // 1) авторитативные зоны (для списка "Available")
            var authZones = await SafeGetAuthZonesAsync();

            // 2) сконфигурированные у Recursor forward-зоны
            var recZones = await SafeGetRecursorZonesAsync();

            ForwardZones = recZones
                .Where(z => string.Equals(z.Kind, "Forwarded", StringComparison.OrdinalIgnoreCase))
                .Select(z => new ForwardZone
                {
                    Id = string.IsNullOrWhiteSpace(z.Id) ? ToPowerDnsZoneId(z.Name) : z.Id,
                    Name = z.Name,
                    ForwardTo = z.Servers ?? new List<string>()
                })
                .OrderBy(z => z.Name != "." ? 1 : 0)
                .ThenBy(z => z.Name)
                .ToList();

            // 3) доступные для добавления (в авторитативных, но ещё не во forward)
            var forwarded = new HashSet<string>(ForwardZones.Select(f => EnsureTrailingDot(f.Name)), StringComparer.OrdinalIgnoreCase);
            AvailableZones = authZones
                .Select(a => EnsureTrailingDot(a.Name))
                .Where(z => !forwarded.Contains(z))
                .OrderBy(z => z)
                .ToList();

            // Root forwarding is optional. Offer it explicitly instead of
            // changing Recursor configuration merely by opening this page.
            if (!forwarded.Contains(".") && !AvailableZones.Contains(".", StringComparer.OrdinalIgnoreCase))
                AvailableZones.Insert(0, ".");
        }

        // ===== Handlers =====

        // Add a local authoritative forward, or an explicit recursive root forward.
        public async Task<IActionResult> OnPostAddForwardZoneAsync([FromBody] ForwardZoneRequest req)
        {
            if (!IsRecursorOn)
                return BadRequest(new { success = false, message = _L["Err.RecursorDisabled"].Value });

            if (req == null || string.IsNullOrWhiteSpace(req.Zone))
                return BadRequest(new { success = false, message = _L["Err.ZoneRequired"].Value });

            try
            {
                using var c = NewRecursorClient();
                var name = EnsureTrailingDot(req.Zone);
                var isRoot = name == ".";
                var payload = new
                {
                    name,
                    kind = "Forwarded",
                    servers = isRoot ? new[] { RootForwarder } : new[] { "127.0.0.1:5300" },
                    recursion_desired = isRoot
                };
                var resp = await c.PostAsync($"{RecursorUrl}/api/v1/servers/localhost/zones",
                    new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, new { success = false, message = _L["Err.RecursorApi", body].Value });

                return new JsonResult(new { success = true, message = _L["Ans.Forward.Added"].Value });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnPostAddForwardZoneAsync failed");
                return StatusCode(500, new { success = false, message = _L["Err.Internal"].Value });
            }
        }

        // Удалить forward-зону
        public async Task<IActionResult> OnPostRemoveForwardZoneAsync([FromBody] ForwardZoneRequest req)
        {
            if (!IsRecursorOn)
                return BadRequest(new { success = false, message = _L["Err.RecursorDisabled"].Value });

            if (req == null || string.IsNullOrWhiteSpace(req.Zone))
                return BadRequest(new { success = false, message = _L["Err.ZoneRequired"].Value });

            var name = EnsureTrailingDot(req.Zone);
            if (name == ".")
                return BadRequest(new { success = false, message = _L["Err.CannotDeleteRoot"].Value });

            try
            {
                using var c = NewRecursorClient();
                var zoneId = GetSafeZoneId(req.Id, name);
                var resp = await c.DeleteAsync(BuildRecursorZoneUrl(zoneId));
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, new { success = false, message = _L["Err.RecursorApi", body].Value });

                return new JsonResult(new { success = true, message = _L["Ans.Forward.Removed"].Value });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnPostRemoveForwardZoneAsync failed");
                return StatusCode(500, new { success = false, message = _L["Err.Internal"].Value });
            }
        }

        // Сохранить список upstream DNS для зоны
        public async Task<IActionResult> OnPostEditZoneAsync([FromBody] UpdateForwardZones req)
        {
            if (!IsRecursorOn)
                return BadRequest(new { success = false, message = _L["Err.RecursorDisabled"].Value });

            if (req == null || string.IsNullOrWhiteSpace(req.Name))
                return BadRequest(new { success = false, message = _L["Err.ZoneRequired"].Value });

            var name = EnsureTrailingDot(req.Name);
            var servers = (req.DnsServers ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (servers.Count == 0)
                return BadRequest(new { success = false, message = _L["Err.NoServersProvided"].Value });

            foreach (var s in servers)
                if (!LooksLikeHostPort(s))
                    return BadRequest(new { success = false, message = _L["Err.InvalidServerFormat", s].Value });

            var transport = (req.Transport ?? "").Trim().ToLowerInvariant();
            if (name == "." && transport.Length > 0)
            {
                if (transport is not ("direct" or "dot"))
                    return BadRequest(new { success = false, message = _L["Err.InvalidTransport"].Value });

                if (transport == "dot" && servers.Any(s => !UsesPort(s, 853)))
                    return BadRequest(new { success = false, message = _L["Err.RootDotRequires853"].Value });

                if (transport == "direct" && servers.Any(s => UsesPort(s, 853)))
                    return BadRequest(new { success = false, message = _L["Err.RootDirectCannotUse853"].Value });
            }

            try
            {
                using var c = NewRecursorClient();

                // The root forwarder is a recursive resolver, so it must
                // receive queries with the RD bit set.
                // Per-zone forwards point to the local authoritative server.
                var payload = new
                {
                    name,
                    kind = "Forwarded",
                    servers,
                    recursion_desired = name == "."
                };

                var zoneId = GetSafeZoneId(req.Id, name);
                var resp = await c.PutAsync(BuildRecursorZoneUrl(zoneId),
                    new StringContent(JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }), Encoding.UTF8, "application/json"));
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, new { success = false, message = _L["Err.RecursorApi", body].Value });

                return new JsonResult(new { success = true, message = _L["Ans.Forward.Updated"].Value });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnPostEditZoneAsync failed");
                return StatusCode(500, new { success = false, message = _L["Err.Internal"].Value });
            }
        }

        // ===== helpers =====
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
