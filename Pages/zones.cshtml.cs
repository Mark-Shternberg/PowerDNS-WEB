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
    public partial class ZonesModel : PageModel
    {
        // ========================= DEPENDENCIES =========================
        private readonly ILogger<ZonesModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IStringLocalizer _L;

        public List<DnsZone> Zones { get; private set; } = new();

        // ========================= CONSTRUCTION =========================
        public ZonesModel(ILogger<ZonesModel> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory, IStringLocalizerFactory factory)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;

            // Берём локалайзер для ресурсов представления "Pages/Zones"
            var asmName = Assembly.GetExecutingAssembly().GetName().Name!;
            _L = factory.Create("Pages.zones", asmName);
        }

        // ========================= PAGE LOAD =========================
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

        // ========================= HANDLERS =========================
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
                                  : (req.masters != null && req.masters.Count > 0 ? string.Join(",", req.masters) : null);

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

                    var (ok, msg) = await UpdateZoneAsync(name, kind, dnssec, serial, form["Master"].ToString());
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

                var (ok2, msg2) = await UpdateZoneAsync(req.name, req.kind, req.dnssec, req.serial, req.Master ?? (req.masters == null ? null : string.Join(",", req.masters)));
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
