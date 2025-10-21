using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;

namespace PowerDNS_Web.Pages
{
    public class ZonesModel : PageModel
    {
        private readonly ILogger<ZonesModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IStringLocalizer _L;

        public List<DnsZone> Zones { get; private set; } = new();

        public ZonesModel(ILogger<ZonesModel> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory, IStringLocalizerFactory factory)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;

            // Берём локалайзер для ресурсов представления "Pages/Zones"
            var asmName = Assembly.GetExecutingAssembly().GetName().Name!;
            _L = factory.Create("Pages.zones", asmName);
        }

        public async Task OnGetAsync()
        {
            var apiUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones";
            var apiKey = _configuration["pdns:api_key"];

            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Zones = JsonSerializer.Deserialize<List<DnsZone>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<DnsZone>();
                }
                else
                {
                    _logger.LogError("PowerDNS API ERROR: {StatusCode}", response.StatusCode);
                    TempData["NoteError"] = _L["Err.PowerDnsApi", response.StatusCode].ToString();
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "CONNECTION ERROR TO PowerDNS API");
                TempData["NoteError"] = _L["Err.ConnPdns"].ToString();
            }
        }

        // -------------------- Models --------------------

        public class DnsZone
        {
            public string? name { get; set; }
            public string? kind { get; set; }
            public List<string> masters { get; set; } = new List<string>();
            public bool dnssec { get; set; }
            public long serial { get; set; }

            public string Master => masters != null && masters.Count > 0 ? string.Join(", ", masters) : "";
        }

        public class DnssecKey
        {
            public int Id { get; set; }
            public bool Active { get; set; }
            public string? Algorithm { get; set; }
            public int Bits { get; set; }
            public string? KeyType { get; set; }
            public bool Published { get; set; }
        }

        private class AddZoneJson
        {
            public string? name { get; set; }
            public string? kind { get; set; }
            public bool dnssec { get; set; }
            public string? Master { get; set; }
            public List<string>? masters { get; set; }
            public string? Mode { get; set; }       // "Forward" | "Reverse"
            public string? revprefix { get; set; }  // e.g. 192.168.0.
        }

        private class EditZoneJson
        {
            public string? name { get; set; }
            public string? kind { get; set; }
            public bool dnssec { get; set; }
            public long serial { get; set; }
        }

        // -------------------- Helpers --------------------

        private static string EnsureTrailingDot(string s)
            => string.IsNullOrWhiteSpace(s) ? s : (s.EndsWith(".") ? s : s + ".");

        private static bool ParseBoolFlexible(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (bool.TryParse(value, out var b)) return b;
            if (string.Equals(value, "Enabled", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "Disabled", StringComparison.OrdinalIgnoreCase)) return false;
            return false;
        }

        private static bool TryBuildReverseFromPrefix(string? prefix, out string reverseZoneFqdn)
        {
            reverseZoneFqdn = "";
            if (string.IsNullOrWhiteSpace(prefix)) return false;

            var rx = System.Text.RegularExpressions.Regex.Match(prefix, @"^((25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)\.){3}$");
            if (!rx.Success) return false;

            var parts = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries); // [A,B,C]
            if (parts.Length != 3) return false;

            reverseZoneFqdn = $"{parts[2]}.{parts[1]}.{parts[0]}.in-addr.arpa.";
            return true;
        }

        private HttpClient NewClientWithPdnsKey()
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", _configuration["pdns:api_key"]);
            return client;
        }

        private HttpClient NewClientWithRecursorKey()
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", _configuration["recursor:api_key"]);
            return client;
        }

        private async Task<(bool ok, string message)> CreateZoneAsync(string nameRaw, string kind, bool dnssec, string? masterSingle)
        {
            var name = EnsureTrailingDot(nameRaw?.Trim());
            if (string.IsNullOrWhiteSpace(name))
                return (false, _L["Err.ZoneNameEmpty"].Value);

            var apiBase = _configuration["pdns:url"];
            if (string.IsNullOrWhiteSpace(apiBase))
                return (false, _L["Err.PdnsUrlMissing"].Value);

            using var client = NewClientWithPdnsKey();

            var masters = (kind == "Slave" && !string.IsNullOrWhiteSpace(masterSingle))
                ? new List<string> { masterSingle }
                : null;

            var zonePayload = new
            {
                name,
                kind,
                dnssec,
                masters
            };

            var content = new StringContent(JsonSerializer.Serialize(zonePayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }), Encoding.UTF8, "application/json");

            // 1) Create zone
            var createResp = await client.PostAsync($"{apiBase}/api/v1/servers/localhost/zones", content);
            var createBody = await createResp.Content.ReadAsStringAsync();
            if (!createResp.IsSuccessStatusCode)
            {
                _logger.LogError("Create zone failed: {Code} {Body}", createResp.StatusCode, createBody);
                return (false, _L["Err.PowerDnsApi", createBody].Value);
            }

            // 2) DNSSEC keys (if enabled)
            if (dnssec)
            {
                var dnssecPayload = new
                {
                    active = true,
                    keytype = "ksk",
                    algorithm = "ECDSAP256SHA256"
                };

                var dnssecContent = new StringContent(JsonSerializer.Serialize(dnssecPayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }), Encoding.UTF8, "application/json");

                var dnssecResp = await client.PostAsync($"{apiBase}/api/v1/servers/localhost/zones/{name}/cryptokeys", dnssecContent);
                var dnssecBody = await dnssecResp.Content.ReadAsStringAsync();
                if (!dnssecResp.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to create DNSSEC keys: {Body}", dnssecBody);
                    return (false, _L["Err.CreateDnssecKeys", dnssecBody].Value);
                }
            }

            // 3) Add SOA (not for Slave)
            if (!string.Equals(kind, "Slave", StringComparison.OrdinalIgnoreCase))
            {
                var nsServer = _configuration["pdns:soa:ns"] ?? "ns1.default.com.";
                var mailAdmin = _configuration["pdns:soa:mail"] ?? "hostmaster.default.com.";
                long soaSerial = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var soaRecord = new
                {
                    rrsets = new[]
                    {
                        new
                        {
                            name,
                            type = "SOA",
                            ttl = 3600,
                            changetype = "REPLACE",
                            records = new[]
                            {
                                new { content = $"{nsServer} {mailAdmin} {soaSerial} 10800 3600 604800 3600", disabled = false }
                            }
                        }
                    }
                };

                var soaContent = new StringContent(JsonSerializer.Serialize(soaRecord, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }), Encoding.UTF8, "application/json");

                var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"{apiBase}/api/v1/servers/localhost/zones/{name}")
                {
                    Content = soaContent
                };
                patchReq.Headers.Add("X-API-Key", _configuration["pdns:api_key"]);

                var soaResp = await client.SendAsync(patchReq);
                var soaBody = await soaResp.Content.ReadAsStringAsync();
                if (!soaResp.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to add SOA record: {Body}", soaBody);
                    return (false, _L["Err.AddSoa", soaBody].Value);
                }

                // 4) Recursor (if enabled)
                if (_configuration["recursor:Enabled"] == "Enabled")
                {
                    var recursorUrl = _configuration["recursor:url"];
                    if (!string.IsNullOrWhiteSpace(recursorUrl))
                    {
                        using var rclient = NewClientWithRecursorKey();

                        var forwardZoneData = new
                        {
                            name,
                            kind = "Forwarded",
                            servers = new[] { "127.0.0.1:5300" },
                            recursion_desired = false
                        };

                        var content2 = new StringContent(JsonSerializer.Serialize(forwardZoneData),
                                                         Encoding.UTF8, "application/json");

                        var rResp = await rclient.PostAsync($"{recursorUrl}/api/v1/servers/localhost/zones", content2);
                        if (!rResp.IsSuccessStatusCode)
                        {
                            var err = await rResp.Content.ReadAsStringAsync();
                            _logger.LogError("Zone added, but recursor forward add failed: {Body}", err);
                            return (true, _L["Msg.ZoneAddedRecursorWarn", err].Value);
                        }
                    }
                }
            }

            return (true, _L["Msg.ZoneAdded"].Value);
        }

        private async Task<(bool ok, string message)> UpdateZoneAsync(string nameRaw, string kind, bool dnssec, long serial)
        {
            var name = EnsureTrailingDot(nameRaw?.Trim());
            var apiBase = _configuration["pdns:url"];
            if (string.IsNullOrWhiteSpace(apiBase))
                return (false, _L["Err.PdnsUrlMissing"].Value);

            using var client = NewClientWithPdnsKey();
            var apiUrl = $"{apiBase}/api/v1/servers/localhost/zones/{name}";

            // 1) Get current zone
            var currentResp = await client.GetAsync(apiUrl);
            if (!currentResp.IsSuccessStatusCode)
            {
                var err = await currentResp.Content.ReadAsStringAsync();
                _logger.LogError("Failed to fetch zone '{Zone}': {Err}", name, err);
                return (false, _L["Err.FetchZone", name, err].Value);
            }

            var currentJson = await currentResp.Content.ReadAsStringAsync();
            var currentZone = JsonSerializer.Deserialize<DnsZone>(currentJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (currentZone == null) return (false, _L["Err.ParseZoneData"].Value);

            bool wasDnssec = currentZone.dnssec;
            bool isDnssec = dnssec;

            // 2) Update
            var updatePayload = new
            {
                name,
                kind,
                dnssec = isDnssec,
                serial = serial > 0 ? serial : 0
            };

            var content = new StringContent(JsonSerializer.Serialize(updatePayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }), Encoding.UTF8, "application/json");

            var putResp = await client.PutAsync(apiUrl, content);
            var putBody = await putResp.Content.ReadAsStringAsync();
            if (!putResp.IsSuccessStatusCode)
            {
                _logger.LogError("PowerDNS API error on update: {Code} {Body}", putResp.StatusCode, putBody);
                return (false, _L["Err.PowerDnsApi", putBody].Value);
            }

            // 3) DNSSEC transitions
            if (!wasDnssec && isDnssec)
            {
                var key = new { keytype = "ksk", active = true, algorithm = "ECDSAP256SHA256", bits = 256 };
                var keyContent = new StringContent(JsonSerializer.Serialize(key), Encoding.UTF8, "application/json");
                var kResp = await client.PostAsync($"{apiUrl}/cryptokeys", keyContent);
                if (!kResp.IsSuccessStatusCode)
                {
                    var err = await kResp.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to create DNSSEC key for '{Zone}': {Err}", name, err);
                    return (false, _L["Err.CreateDnssecKeyGeneric", err].Value);
                }
            }
            else if (wasDnssec && !isDnssec)
            {
                var ks = await client.GetAsync($"{apiUrl}/cryptokeys");
                if (ks.IsSuccessStatusCode)
                {
                    var j = await ks.Content.ReadAsStringAsync();
                    var keys = JsonSerializer.Deserialize<List<DnssecKey>>(j, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new();

                    foreach (var k in keys)
                    {
                        var del = await client.DeleteAsync($"{apiUrl}/cryptokeys/{k.Id}");
                        if (!del.IsSuccessStatusCode)
                        {
                            var err = await del.Content.ReadAsStringAsync();
                            _logger.LogError("Failed to delete DNSSEC key {KeyId} for '{Zone}': {Err}", k.Id, name, err);
                        }
                    }
                }
            }

            return (true, _L["Msg.ZoneUpdated"].Value);
        }

        private async Task<(bool ok, string message)> DeleteZoneAsync(string nameRaw)
        {
            var name = EnsureTrailingDot(nameRaw?.Trim());
            var apiBase = _configuration["pdns:url"];

            if (string.IsNullOrWhiteSpace(apiBase))
                return (false, _L["Err.PdnsUrlMissing"].Value);

            using var client = NewClientWithPdnsKey();

            var resp = await client.DeleteAsync($"{apiBase}/api/v1/servers/localhost/zones/{name}");
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Error deleting zone: {Code} {Body}", resp.StatusCode, body);
                return (false, _L["Err.PowerDnsApi", body].Value);
            }

            if (_configuration["recursor:Enabled"] == "Enabled")
            {
                try
                {
                    var recursorUrl = _configuration["recursor:url"];
                    if (!string.IsNullOrWhiteSpace(recursorUrl))
                    {
                        using var rclient = NewClientWithRecursorKey();
                        var rResp = await rclient.DeleteAsync($"{recursorUrl}/api/v1/servers/localhost/zones/{name}");
                        if (!rResp.IsSuccessStatusCode)
                        {
                            var err = await rResp.Content.ReadAsStringAsync();
                            _logger.LogError("Error removing forward zone from recursor: {Err}", err);
                            return (true, _L["Msg.ZoneDeletedRecursorWarn", err].Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception while removing forward zone in recursor");
                    return (true, _L["Msg.ZoneDeletedRecursorWarn2"].Value);
                }
            }

            return (true, _L["Msg.ZoneDeleted"].Value);
        }

        // -------------------- Handlers --------------------

        // ADD
        public async Task<IActionResult> OnPostAddZoneAsync()
        {
            try
            {
                // FORM path
                if (Request.HasFormContentType)
                {
                    var form = Request.Form;
                    var mode = form["Mode"].ToString(); // "Forward" | "Reverse"
                    string name = form["name"].ToString();
                    string revprefix = form["revprefix"].ToString();
                    var kind = form["kind"].ToString();
                    var dnssec = ParseBoolFlexible(form["dnssec"]);
                    var masterFromForm = form["Master"].ToString();

                    if (string.Equals(mode, "Reverse", StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(revprefix))
                    {
                        if (!TryBuildReverseFromPrefix(revprefix, out var reverseZone))
                        {
                            TempData["NoteError"] = _L["Err.InvalidReversePrefix"].ToString();
                            return RedirectToPage();
                        }
                        name = reverseZone;
                    }

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kind))
                    {
                        TempData["NoteError"] = _L["Err.InvalidParams"].ToString();
                        return RedirectToPage();
                    }

                    var (ok, msg) = await CreateZoneAsync(name, kind, dnssec, masterFromForm);
                    TempData[ok ? "NoteSuccess" : "NoteError"] = msg;
                    return RedirectToPage();
                }

                // JSON path (back-compat)
                string body = await new StreamReader(Request.Body).ReadToEndAsync();
                var req = JsonSerializer.Deserialize<AddZoneJson>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (req == null || (string.IsNullOrWhiteSpace(req.name) && string.IsNullOrWhiteSpace(req.revprefix)) || string.IsNullOrWhiteSpace(req.kind))
                    return new JsonResult(new { success = false, message = _L["Err.InvalidParams"].ToString() }) { StatusCode = 400 };

                var nameJson = req.name;
                if (string.Equals(req.Mode, "Reverse", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(nameJson))
                {
                    if (!TryBuildReverseFromPrefix(req.revprefix, out var reverseZone))
                        return new JsonResult(new { success = false, message = _L["Err.InvalidReversePrefix"].ToString() }) { StatusCode = 400 };
                    nameJson = reverseZone;
                }

                var masterFromJson = !string.IsNullOrWhiteSpace(req.Master) ? req.Master
                                  : (req.masters != null && req.masters.Count > 0 ? req.masters[0] : null);

                var (ok2, msg2) = await CreateZoneAsync(nameJson, req.kind, req.dnssec, masterFromJson);
                if (!ok2) return new JsonResult(new { success = false, message = msg2 }) { StatusCode = 400 };
                return new JsonResult(new { success = true, message = msg2 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXCEPTION IN OnPostAddZoneAsync");
                if (Request.HasFormContentType)
                {
                    TempData["NoteError"] = _L["Err.InternalAdd"].ToString();
                    return RedirectToPage();
                }
                return new JsonResult(new { success = false, message = _L["Err.InternalGeneric", ex.Message].ToString() }) { StatusCode = 500 };
            }
        }

        // EDIT
        public async Task<IActionResult> OnPostEditZoneAsync()
        {
            try
            {
                // FORM path
                if (Request.HasFormContentType)
                {
                    var form = Request.Form;
                    var name = form["name"].ToString();
                    var kind = form["kind"].ToString();
                    var dnssec = ParseBoolFlexible(form["dnssec"]);
                    long serial = 0;
                    long.TryParse(form["serial"], out serial);

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kind))
                    {
                        TempData["NoteError"] = _L["Err.InvalidParams"].ToString();
                        return RedirectToPage();
                    }

                    var (ok, msg) = await UpdateZoneAsync(name, kind, dnssec, serial);
                    TempData[ok ? "NoteSuccess" : "NoteError"] = msg;
                    return RedirectToPage();
                }

                // JSON path
                string body = await new StreamReader(Request.Body).ReadToEndAsync();
                var req = JsonSerializer.Deserialize<EditZoneJson>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (req == null || string.IsNullOrWhiteSpace(req.name) || string.IsNullOrWhiteSpace(req.kind))
                    return new JsonResult(new { success = false, message = _L["Err.InvalidParams"].ToString() }) { StatusCode = 400 };

                var (ok2, msg2) = await UpdateZoneAsync(req.name, req.kind, req.dnssec, req.serial);
                if (!ok2) return new JsonResult(new { success = false, message = msg2 }) { StatusCode = 400 };
                return new JsonResult(new { success = true, message = msg2 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXCEPTION IN OnPostEditZoneAsync");
                if (Request.HasFormContentType)
                {
                    TempData["NoteError"] = _L["Err.InternalEdit"].ToString();
                    return RedirectToPage();
                }
                return new JsonResult(new { success = false, message = _L["Err.InternalGeneric", ex.Message].ToString() }) { StatusCode = 500 };
            }
        }

        // DNSSEC keys (AJAX-read)
        public async Task<IActionResult> OnGetDnssecKeysAsync([FromQuery] string Name)
        {
            var apiUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones/{Name}/cryptokeys";
            var apiKey = _configuration["pdns:api_key"];

            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

            var response = await client.GetAsync(apiUrl);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Error seeking DNSSEC keys: {Code} - {Body}", response.StatusCode, responseContent);
                return new JsonResult(new { success = false, message = _L["Err.PowerDnsApi", responseContent].ToString() })
                { StatusCode = (int)response.StatusCode };
            }

            // Возвращаем «как есть» (ваш JS уже умеет парсить)
            return new JsonResult(new { success = true, keys = responseContent });
        }

        // DELETE
        public async Task<IActionResult> OnPostDeleteZoneAsync()
        {
            try
            {
                // FORM path
                if (Request.HasFormContentType)
                {
                    var name = Request.Form["name"].ToString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        TempData["NoteError"] = _L["Err.InvalidParams"].ToString();
                        return RedirectToPage();
                    }

                    var (ok, msg) = await DeleteZoneAsync(name);
                    TempData[ok ? "NoteSuccess" : "NoteError"] = msg;
                    return RedirectToPage();
                }

                // JSON path (old JS)
                string body = await new StreamReader(Request.Body).ReadToEndAsync();
                var obj = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
                var nameJson = obj != null && obj.TryGetValue("name", out var n) ? n?.ToString() : null;

                if (string.IsNullOrWhiteSpace(nameJson))
                    return new JsonResult(new { success = false, message = _L["Err.InvalidParams"].ToString() }) { StatusCode = 400 };

                var (ok2, msg2) = await DeleteZoneAsync(nameJson);
                if (!ok2) return new JsonResult(new { success = false, message = msg2 }) { StatusCode = 400 };
                return new JsonResult(new { success = true, message = msg2 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXCEPTION IN OnPostDeleteZoneAsync");
                if (Request.HasFormContentType)
                {
                    TempData["NoteError"] = _L["Err.InternalDelete"].ToString();
                    return RedirectToPage();
                }
                return new JsonResult(new { success = false, message = _L["Err.InternalGeneric", ex.Message].ToString() }) { StatusCode = 500 };
            }
        }
    }
}
