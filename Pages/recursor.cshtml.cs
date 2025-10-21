using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
        private readonly IStringLocalizer<RecursorModel> _L;

        public List<string> AvailableZones { get; private set; } = new();
        public List<ForwardZone> ForwardZones { get; private set; } = new();

        private string PdnsUrl => _cfg["pdns:url"] ?? "";
        private string PdnsKey => _cfg["pdns:api_key"] ?? "";
        private string RecursorUrl => _cfg["recursor:url"] ?? "";
        private string RecursorKey => _cfg["recursor:api_key"] ?? "";
        private string RecursorEnabled => _cfg["recursor:Enabled"] ?? _cfg["recursor:enabled"] ?? "Disabled";
        private bool IsRecursorOn => string.Equals(RecursorEnabled, "Enabled", StringComparison.OrdinalIgnoreCase);

        public RecursorModel(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<RecursorModel> logger,
            IStringLocalizer<RecursorModel> localizer)
        {
            _httpClientFactory = httpClientFactory;
            _cfg = configuration;
            _logger = logger;
            _L = localizer;
        }

        // ===== View models / DTOs =====
        public class ForwardZone
        {
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
            public string Name { get; set; } = "";
            public string Kind { get; set; } = "";
            public List<string> Servers { get; set; } = new();
            public bool? Recursion_Desired { get; set; }
        }

        public class ForwardZoneRequest { public string Zone { get; set; } = ""; }
        public class UpdateForwardZones
        {
            public string Name { get; set; } = "";
            public string DnsServers { get; set; } = "";
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
                .Select(z => new ForwardZone { Name = z.Name, ForwardTo = z.Servers ?? new List<string>() })
                .OrderBy(z => z.Name != "." ? 1 : 0)
                .ThenBy(z => z.Name)
                .ToList();

            // если нет корневой зоны – создадим дефолтную (как у вас было: 1.1.1.1:53)
            if (!ForwardZones.Any(z => z.Name == "."))
            {
                try
                {
                    using var c = NewRecursorClient();
                    var payload = new
                    {
                        name = ".",
                        kind = "Forwarded",
                        servers = new[] { "1.1.1.1:53" },
                        recursion_desired = false
                    };
                    var resp = await c.PostAsync($"{RecursorUrl}/api/v1/servers/localhost/zones",
                        new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

                    if (resp.IsSuccessStatusCode)
                    {
                        ForwardZones.Insert(0, new ForwardZone { Name = ".", ForwardTo = new List<string> { "1.1.1.1:53" } });
                    }
                    else
                    {
                        var body = await resp.Content.ReadAsStringAsync();
                        _logger.LogError("Failed to create root forward zone '.': {Body}", body);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception while creating root forward zone '.'");
                }
            }

            // 3) доступные для добавления (в авторитативных, но ещё не во forward)
            var forwarded = new HashSet<string>(ForwardZones.Select(f => EnsureTrailingDot(f.Name)), StringComparer.OrdinalIgnoreCase);
            AvailableZones = authZones
                .Select(a => EnsureTrailingDot(a.Name))
                .Where(z => !forwarded.Contains(z))
                .OrderBy(z => z)
                .ToList();
        }

        // ===== Handlers =====

        // Добавить forward-зону (по умолчанию на 127.0.0.1:5300)
        public async Task<IActionResult> OnPostAddForwardZoneAsync([FromBody] ForwardZoneRequest req)
        {
            if (!IsRecursorOn)
                return BadRequest(new { success = false, message = _L["Err.RecursorDisabled"] });

            if (req == null || string.IsNullOrWhiteSpace(req.Zone))
                return BadRequest(new { success = false, message = _L["Err.ZoneRequired"] });

            try
            {
                using var c = NewRecursorClient();
                var name = EnsureTrailingDot(req.Zone);
                var payload = new
                {
                    name,
                    kind = "Forwarded",
                    servers = new[] { "127.0.0.1:5300" },
                    recursion_desired = false
                };
                var resp = await c.PostAsync($"{RecursorUrl}/api/v1/servers/localhost/zones",
                    new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, new { success = false, message = _L["Err.RecursorApi", body] });

                return new JsonResult(new { success = true, message = _L["Ans.Forward.Added"] });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnPostAddForwardZoneAsync failed");
                return StatusCode(500, new { success = false, message = _L["Err.Internal"] });
            }
        }

        // Удалить forward-зону
        public async Task<IActionResult> OnPostRemoveForwardZoneAsync([FromBody] ForwardZoneRequest req)
        {
            if (!IsRecursorOn)
                return BadRequest(new { success = false, message = _L["Err.RecursorDisabled"] });

            if (req == null || string.IsNullOrWhiteSpace(req.Zone))
                return BadRequest(new { success = false, message = _L["Err.ZoneRequired"] });

            var name = EnsureTrailingDot(req.Zone);
            if (name == ".")
                return BadRequest(new { success = false, message = _L["Err.CannotDeleteRoot"] });

            try
            {
                using var c = NewRecursorClient();
                var pathName = Uri.EscapeDataString(name); // корректно закодирует точку и др. символы
                var resp = await c.DeleteAsync($"{RecursorUrl}/api/v1/servers/localhost/zones/{pathName}");
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, new { success = false, message = _L["Err.RecursorApi", body] });

                return new JsonResult(new { success = true, message = _L["Ans.Forward.Removed"] });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnPostRemoveForwardZoneAsync failed");
                return StatusCode(500, new { success = false, message = _L["Err.Internal"] });
            }
        }

        // Сохранить список upstream DNS для зоны
        public async Task<IActionResult> OnPostEditZoneAsync([FromBody] UpdateForwardZones req)
        {
            if (!IsRecursorOn)
                return BadRequest(new { success = false, message = _L["Err.RecursorDisabled"] });

            if (req == null || string.IsNullOrWhiteSpace(req.Name))
                return BadRequest(new { success = false, message = _L["Err.ZoneRequired"] });

            var servers = (req.DnsServers ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (servers.Count == 0)
                return BadRequest(new { success = false, message = _L["Err.NoServersProvided"] });

            foreach (var s in servers)
                if (!LooksLikeHostPort(s))
                    return BadRequest(new { success = false, message = _L["Err.InvalidServerFormat", s] });

            try
            {
                using var c = NewRecursorClient();
                var name = EnsureTrailingDot(req.Name);

                // Для корня оставляем false: вы уже используете явные forwarders
                var payload = new
                {
                    name,
                    kind = "Forwarded",
                    servers,
                    recursion_desired = false
                };

                var pathName = Uri.EscapeDataString(name);
                var resp = await c.PutAsync($"{RecursorUrl}/api/v1/servers/localhost/zones/{pathName}",
                    new StringContent(JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }), Encoding.UTF8, "application/json"));
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return StatusCode((int)resp.StatusCode, new { success = false, message = _L["Err.RecursorApi", body] });

                return new JsonResult(new { success = true, message = _L["Ans.Forward.Updated"] });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnPostEditZoneAsync failed");
                return StatusCode(500, new { success = false, message = _L["Err.Internal"] });
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

        private static string EnsureTrailingDot(string s)
            => string.IsNullOrWhiteSpace(s) ? s : (s.EndsWith('.') ? s : s + ".");

        private static bool LooksLikeHostPort(string s)
        {
            var idx = s.LastIndexOf(':');
            if (idx <= 0 || idx >= s.Length - 1) return false;
            if (!int.TryParse(s[(idx + 1)..], out var port) || port < 1 || port > 65535) return false;
            return !string.IsNullOrWhiteSpace(s[..idx]);
        }
    }
}
