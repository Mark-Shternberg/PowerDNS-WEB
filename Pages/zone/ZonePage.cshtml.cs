using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

namespace PowerDNS_Web.Pages.zone
{
    public partial class ZonePageModel : PageModel
    {
        // ========================= DEPENDENCIES =========================
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly string _apiKey;
        private readonly string _default_IP;
        private readonly string _recursor_Enabled;
        private readonly ILogger<ZonePageModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly IStringLocalizer _L;

        // ========================= CONSTRUCTION =========================
        public ZonePageModel(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ZonePageModel> logger, IStringLocalizerFactory factory)
        {
            _logger = logger;
            var asmName = Assembly.GetExecutingAssembly().GetName().Name!;
            _L = factory.Create("Pages.zone.ZonePage", asmName);
            _configuration = configuration;

            _httpClient = httpClientFactory.CreateClient();
            _apiUrl = configuration["pdns:url"] ?? string.Empty;
            _apiKey = configuration["pdns:api_key"] ?? string.Empty;
            _default_IP = configuration["pdns:default_a"] ?? string.Empty;
            _recursor_Enabled = configuration["recursor:Enabled"] ?? "Disabled";
        }

        // ========================= PAGE STATE =========================
        [BindProperty(SupportsGet = true)]
        public string? ZoneName { get; set; }

        public bool IsReadOnly { get; private set; }

        public Dictionary<string, List<DnsRecord>> GroupedRecords { get; set; } = new();

        // ========================= PAGE LOAD =========================
        public async Task<IActionResult> OnGetAsync()
        {
            if (string.IsNullOrWhiteSpace(ZoneName))
                return NotFound();

            ZoneName = ZoneName!.TrimEnd('.');

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}.");
                request.Headers.Add("X-API-Key", _apiKey);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode);

                var json = await response.Content.ReadAsStringAsync();
                var zoneData = JsonSerializer.Deserialize<ZoneData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                IsReadOnly = Services.DnsValidation.IsSecondary(zoneData?.Kind);
                if (zoneData?.Rrsets != null)
                {
                    GroupedRecords = zoneData.Rrsets
                        .Where(r => !string.IsNullOrEmpty(r.Name) && !string.IsNullOrEmpty(r.Type) && r.Records.Count > 0)
                        .GroupBy(r => GetSubdomain(r.Name!))
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(r => new DnsRecord
                            {
                                Name = r.Name!,
                                Type = r.Type!,
                                Content = r.Records.Select(rec => rec.Content ?? string.Empty).ToList(),
                                Ttl = r.Ttl
                            }).ToList()
                        );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Zone page GET failed for {Zone}", ZoneName);
                return StatusCode(500);
            }

            return Page();
        }

        private string GetSubdomain(string fullRecordName)
        {
            var zn = ZoneName ?? string.Empty;
            if (fullRecordName.EndsWith(zn + ".", StringComparison.OrdinalIgnoreCase))
            {
                string subdomain = fullRecordName.Substring(0, fullRecordName.Length - zn.Length - 1);
                return string.IsNullOrEmpty(subdomain) ? "@" : subdomain.TrimEnd('.');
            }
            return fullRecordName;
        }

        private static string EnsureDot(string s) =>
            string.IsNullOrWhiteSpace(s) ? s : (s.EndsWith(".", StringComparison.Ordinal) ? s : s + ".");

        // CANONICAL ZONE ID WITH TRAILING DOT
        private string ZoneId => Uri.EscapeDataString(EnsureDot((ZoneName ?? string.Empty).Trim()));

        // ========================= ADD RECORD (TEMPDATA + REDIRECT) =========================
        public async Task<IActionResult> OnPostAddRecordAsync()
        {
            if (string.IsNullOrWhiteSpace(ZoneName))
            {
                TempData["NoteError"] = _L["Err.ZoneNotSpecified"].Value;
                return RedirectToPage("/zones");
            }

            try
            {
                AddRecordRequest? request;
                if (Request.HasFormContentType)
                {
                    request = ParseAddRecordFromForm();
                }
                else
                {
                    var body = await new StreamReader(Request.Body).ReadToEndAsync();
                    request = string.IsNullOrWhiteSpace(body)
                        ? null
                        : JsonSerializer.Deserialize<AddRecordRequest>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }

                if (request == null || string.IsNullOrWhiteSpace(request.RecordType))
                {
                    TempData["NoteError"] = _L["Err.InvalidRequest"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                var subdomain = (request.Subdomain ?? "@").TrimEnd('.');
                var fullDomain = EnsureDot(subdomain == "@" || subdomain == "" ? ZoneName!.TrimEnd('.') : $"{subdomain}.{ZoneName!.TrimEnd('.')}");

                // fetch zone
                var getReq = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneId}");
                getReq.Headers.Add("X-API-Key", _apiKey);
                var getResp = await _httpClient.SendAsync(getReq);
                if (!getResp.IsSuccessStatusCode)
                {
                    var errBody = await getResp.Content.ReadAsStringAsync();
                    TempData["NoteError"] = _L["Err.FetchRecords", errBody].Value;
                    return RedirectToPage(new { ZoneName });
                }

                var zoneJson = await getResp.Content.ReadAsStringAsync();
                var zoneData = JsonSerializer.Deserialize<ZoneData>(zoneJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (zoneData?.Rrsets == null || Services.DnsValidation.IsSecondary(zoneData.Kind))
                {
                    TempData["NoteError"] = Services.DnsValidation.IsSecondary(zoneData?.Kind)
                        ? _L["Err.SecondaryReadOnly"].Value : _L["Err.ZoneEmpty"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                var recordType = request.RecordType!;
                var existing = zoneData.Rrsets.FirstOrDefault(r => string.Equals(r.Name, fullDomain, StringComparison.OrdinalIgnoreCase) && r.Type == recordType);
                var updated = existing?.Records?.ToList() ?? new List<Record>();

                // normalize & build content
                var rawVal = request.Value ?? string.Empty;

                if (recordType is "NS" or "CNAME" or "PTR")
                    rawVal = rawVal.TrimEnd('.') + ".";

                string content = rawVal;
                if (recordType == "MX")
                {
                    var mx = request.MxPriority ?? 10;
                    var host = rawVal.TrimEnd('.') + ".";
                    content = $"{mx} {host}";
                }
                else if (recordType == "SRV")
                {
                    var pr = request.SrvPriority ?? 0;
                    var wt = request.SrvWeight ?? 0;
                    var pt = request.SrvPort ?? 0;
                    var target = rawVal.TrimEnd('.') + ".";
                    content = $"{pr} {wt} {pt} {target}";
                }
                else if (recordType == "TXT")
                {
                    content = FormatTxtForPowerDNS(rawVal);
                }

                if (updated.Any(r => (r.Content ?? string.Empty) == content))
                {
                    TempData["NoteWarn"] = _L["Err.RecordExists"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                updated.Add(new Record { Content = content, Disabled = false });

                int ttl = request.Ttl ?? existing?.Ttl ?? 3600;
                var validationError = Services.DnsValidation.RecordError(fullDomain, EnsureDot(ZoneName!), recordType, content, ttl);
                if (validationError != null || (recordType == "CNAME" && zoneData.Rrsets.Any(r => string.Equals(r.Name, fullDomain, StringComparison.OrdinalIgnoreCase))) ||
                    (recordType != "CNAME" && zoneData.Rrsets.Any(r => string.Equals(r.Name, fullDomain, StringComparison.OrdinalIgnoreCase) && r.Type == "CNAME")))
                {
                    TempData["NoteError"] = validationError ?? "CNAME cannot coexist with other records.";
                    return RedirectToPage(new { ZoneName });
                }

                var patch = new
                {
                    rrsets = new[] {
                        new { name = fullDomain, type = recordType, ttl = ttl, changetype = "REPLACE", records = updated }
                    }
                };

                var json = JsonSerializer.Serialize(patch, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneId}")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                patchReq.Headers.Add("X-API-Key", _apiKey);

                var patchResp = await _httpClient.SendAsync(patchReq);
                if (!patchResp.IsSuccessStatusCode)
                {
                    var err = await patchResp.Content.ReadAsStringAsync();
                    TempData["NoteError"] = _L["Err.AddRecordApi", err].Value;
                    return RedirectToPage(new { ZoneName });
                }

                TempData["NoteSuccess"] = _L["Ans.Record.Added"].Value;
                return RedirectToPage(new { ZoneName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Add record failed for {Zone}", ZoneName);
                TempData["NoteError"] = _L["Err.Internal"].Value;
                return RedirectToPage(new { ZoneName });
            }
        }

        // ========================= DELETE RECORD (TEMPDATA + REDIRECT) =========================
        public async Task<IActionResult> OnPostDeleteRecordAsync()
        {
            if (string.IsNullOrWhiteSpace(ZoneName))
            {
                TempData["NoteError"] = _L["Err.ZoneNotSpecified"].Value;
                return RedirectToPage("/zones");
            }

            try
            {
                // из формы
                var name = Request.Form["Name"].ToString();
                var type = Request.Form["Type"].ToString();
                var value = Request.Form["Value"].ToString();

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(value))
                {
                    TempData["NoteError"] = _L["Err.InvalidRequest"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                string fqdn = name.TrimEnd('.') + ".";

                // fetch current
                var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneId}");
                getRequest.Headers.Add("X-API-Key", _apiKey);
                var getResponse = await _httpClient.SendAsync(getRequest);
                if (!getResponse.IsSuccessStatusCode)
                {
                    var err = await getResponse.Content.ReadAsStringAsync();
                    TempData["NoteError"] = _L["Err.FetchRecords", err].Value;
                    return RedirectToPage(new { ZoneName });
                }

                var jsonResponse = await getResponse.Content.ReadAsStringAsync();
                var zoneData = JsonSerializer.Deserialize<ZoneData>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (zoneData?.Rrsets == null || Services.DnsValidation.IsSecondary(zoneData.Kind))
                {
                    TempData["NoteError"] = Services.DnsValidation.IsSecondary(zoneData?.Kind)
                        ? _L["Err.SecondaryReadOnly"].Value : _L["Err.ZoneEmpty"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                var recordSet = zoneData.Rrsets.FirstOrDefault(r => r.Name == fqdn && r.Type == type);
                if (recordSet == null || recordSet.Records == null || recordSet.Records.Count == 0)
                {
                    TempData["NoteError"] = _L["Err.RecordNotFound"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                if (type == "SOA")
                {
                    TempData["NoteError"] = "The zone SOA cannot be deleted.";
                    return RedirectToPage(new { ZoneName });
                }

                var updatedRecords = recordSet.Records.Where(r => (r.Content ?? string.Empty) != value).ToList();
                string changeType = updatedRecords.Count > 0 ? "REPLACE" : "DELETE";

                var deleteRecord = new
                {
                    rrsets = new[]
                    {
                        new
                        {
                            name = fqdn,
                            type = type,
                            ttl = recordSet.Ttl,
                            changetype = changeType,
                            records = updatedRecords.Select(r => new { content = r.Content ?? string.Empty, disabled = r.Disabled }).ToArray()
                        }
                    }
                };

                var json = JsonSerializer.Serialize(deleteRecord, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                var requestMessage = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneId}")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                requestMessage.Headers.Add("X-API-Key", _apiKey);

                var response = await _httpClient.SendAsync(requestMessage);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    TempData["NoteError"] = _L["Err.DeleteRecordApi", err].Value;
                    return RedirectToPage(new { ZoneName });
                }



                TempData["NoteSuccess"] = _L["Ans.Record.Deleted"].Value;
                return RedirectToPage(new { ZoneName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete record failed for {Zone}", ZoneName);
                TempData["NoteError"] = _L["Err.Internal"].Value;
                return RedirectToPage(new { ZoneName });
            }
        }

        // ========================= EDIT RECORD (TEMPDATA + REDIRECT) =========================
        public async Task<IActionResult> OnPostEditRecordAsync()
        {
            if (string.IsNullOrWhiteSpace(ZoneName))
            {
                TempData["NoteError"] = _L["Err.ZoneNotSpecified"].Value;
                return RedirectToPage("/zones");
            }

            try
            {
                var req = ParseEditRecordFromForm();

                if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Type) || (req.Type != "SOA" && string.IsNullOrWhiteSpace(req.Value)))
                {
                    TempData["NoteError"] = _L["Err.InvalidRequest"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                string fqdn = req.Name.TrimEnd('.') + ".";

                // fetch
                var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneId}");
                getRequest.Headers.Add("X-API-Key", _apiKey);
                var getResponse = await _httpClient.SendAsync(getRequest);
                if (!getResponse.IsSuccessStatusCode)
                {
                    var err = await getResponse.Content.ReadAsStringAsync();
                    TempData["NoteError"] = _L["Err.FetchRecords", err].Value;
                    return RedirectToPage(new { ZoneName });
                }

                var jsonResponse = await getResponse.Content.ReadAsStringAsync();
                var zoneData = JsonSerializer.Deserialize<ZoneData>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (zoneData?.Rrsets == null || Services.DnsValidation.IsSecondary(zoneData.Kind))
                {
                    TempData["NoteError"] = Services.DnsValidation.IsSecondary(zoneData?.Kind)
                        ? _L["Err.SecondaryReadOnly"].Value : _L["Err.ZoneEmpty"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                var recordSet = zoneData.Rrsets.FirstOrDefault(r => r.Name == fqdn && r.Type == req.Type);
                if (recordSet == null || recordSet.Records == null || recordSet.Records.Count == 0)
                {
                    TempData["NoteError"] = _L["Err.RecordNotFound"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                var remainingRecords = recordSet.Records
                    .Where(r => (r.Content ?? string.Empty) != (req.OldValue ?? string.Empty))
                    .Select(r => new { content = r.Content ?? string.Empty, disabled = r.Disabled })
                    .ToList();

                string newRecordContent = (req.Value ?? string.Empty).Trim();
                if (req.Type is "NS" or "CNAME" or "PTR") newRecordContent = EnsureDot(newRecordContent);

                if (req.Type == "MX")
                {
                    int mxPriority = req.MxPriority ?? 10;
                    string[] parts = (req.Value ?? string.Empty).Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    string mailServer = parts.Length > 1 ? parts.Last() : parts.FirstOrDefault() ?? string.Empty;
                    newRecordContent = $"{mxPriority} {mailServer.TrimEnd('.')}.".Trim();
                }
                else if (req.Type == "SRV")
                {
                    int srvPriority = req.SrvPriority ?? 0;
                    int srvWeight = req.SrvWeight ?? 0;
                    int srvPort = req.SrvPort ?? 0;
                    string[] parts = (req.Value ?? string.Empty).Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    string targetServer = parts.Length > 3 ? parts.Last() : parts.FirstOrDefault() ?? string.Empty;
                    newRecordContent = $"{srvPriority} {srvWeight} {srvPort} {targetServer.TrimEnd('.')}.".Trim();
                }
                else if (req.Type == "SOA")
                {
                    string soaNs = ((req.SoaNs ?? string.Empty).TrimEnd('.')) + ".";
                    string soaEmail = ((req.SoaEmail ?? string.Empty).TrimEnd('.')) + ".";
                    var soaSerial = recordSet.Records[0].Content?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(2) ?? "0";
                    int soaRefresh = req.SoaRefresh ?? 10800;
                    int soaRetry = req.SoaRetry ?? 3600;
                    int soaExpire = req.SoaExpire ?? 604800;
                    int soaMinimumTtl = req.SoaMinimumTtl ?? 3600;
                    newRecordContent = $"{soaNs} {soaEmail} {soaSerial} {soaRefresh} {soaRetry} {soaExpire} {soaMinimumTtl}";
                }
                else if (req.Type == "TXT")
                {
                    newRecordContent = FormatTxtForPowerDNS(req.Value ?? string.Empty);
                }

                if (!recordSet.Records.Any(r => r.Content == req.OldValue))
                {
                    TempData["NoteError"] = "Record changed or was removed. Refresh the page.";
                    return RedirectToPage(new { ZoneName });
                }
                var validationError = Services.DnsValidation.RecordError(fqdn, EnsureDot(ZoneName!), req.Type!, newRecordContent, req.Ttl ?? recordSet.Ttl);
                if (validationError != null || remainingRecords.Any(r => r.content == newRecordContent))
                {
                    TempData["NoteError"] = validationError ?? _L["Err.RecordExists"].Value;
                    return RedirectToPage(new { ZoneName });
                }
                remainingRecords.Add(new { content = newRecordContent, disabled = recordSet.Records.First(r => r.Content == req.OldValue).Disabled });

                var updateRecord = new
                {
                    rrsets = new[] {
                        new {
                            name = fqdn,
                            type = req.Type,
                            ttl = req.Ttl ?? recordSet.Ttl,
                            changetype = "REPLACE",
                            records = remainingRecords
                        }
                    }
                };

                var json = JsonSerializer.Serialize(updateRecord, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var requestMessage = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneId}")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                requestMessage.Headers.Add("X-API-Key", _apiKey);

                var response = await _httpClient.SendAsync(requestMessage);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    TempData["NoteError"] = _L["Err.UpdateRecordApi", err].Value;
                    return RedirectToPage(new { ZoneName });
                }


                TempData["NoteSuccess"] = _L["Ans.Record.Updated"].Value;
                return RedirectToPage(new { ZoneName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Edit record failed for {Zone}", ZoneName);
                TempData["NoteError"] = _L["Err.Internal"].Value;
                return RedirectToPage(new { ZoneName });
            }
        }

        // ========================= ADD SUBDOMAIN (TEMPDATA + REDIRECT) =========================
        public async Task<IActionResult> OnPostAddSubdomainAsync()
        {
            if (string.IsNullOrWhiteSpace(ZoneName))
            {
                TempData["NoteError"] = _L["Err.ZoneNotSpecified"].Value;
                return RedirectToPage("/zones");
            }

            try
            {
                // READ FORM
                var raw = (Request.Form["Subdomain"].ToString() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    TempData["NoteError"] = _L["Err.InvalidRequest"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                // DEFAULT A IP MUST BE SET
                if (string.IsNullOrWhiteSpace(_default_IP))
                {
                    TempData["NoteError"] = _L["Err.DefaultARequired"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                // NORMALIZE: ONLY LABELS, NO ZONE APPENDED BY USER
                var label = raw.Trim('.');
                if (!Services.DnsValidation.IsName(label, true) || !System.Net.IPAddress.TryParse(_default_IP, out var defaultAddress) || defaultAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    TempData["NoteError"] = _L["Err.InvalidRequest"].Value;
                    return RedirectToPage(new { ZoneName });
                }
                using var zoneRequest = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneId}");
                zoneRequest.Headers.Add("X-API-Key", _apiKey);
                using var zoneResponse = await _httpClient.SendAsync(zoneRequest);
                if (!zoneResponse.IsSuccessStatusCode) return StatusCode((int)zoneResponse.StatusCode);
                var currentZone = JsonSerializer.Deserialize<ZoneData>(await zoneResponse.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (currentZone == null || Services.DnsValidation.IsSecondary(currentZone.Kind) ||
                    currentZone.Rrsets.Any(r => string.Equals(r.Name, EnsureDot($"{label}.{ZoneName!.TrimEnd('.')}"), StringComparison.OrdinalIgnoreCase)))
                {
                    TempData["NoteError"] = "This name already exists or the zone is read-only.";
                    return RedirectToPage(new { ZoneName });
                }

                // FORBID ACCIDENTAL ROOT/WHOLE-ZONE INPUTS
                if (label == "@" ||
                    label.Equals(ZoneName, StringComparison.OrdinalIgnoreCase) ||
                    label.EndsWith("." + (ZoneName ?? string.Empty).TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
                {
                    TempData["NoteError"] = _L["Err.InvalidRequest"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                // BUILD FQDN (MUST END WITH DOT)
                var fqdn = EnsureDot($"{label}.{(ZoneName ?? string.Empty).TrimEnd('.')}");

                // PATCH TO CANONICAL ZONE-ID (WITH DOT)
                var payload = new
                {
                    rrsets = new[]
                    {
                new
                {
                    name = fqdn,
                    type = "A",
                    ttl = 3600,
                    changetype = "REPLACE",
                    records = new[] { new { content = _default_IP, disabled = false } }
                }
            }
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var req = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneId}")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                req.Headers.Add("X-API-Key", _apiKey);

                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    TempData["NoteError"] = _L["Err.AddSubdomainApi", err].Value;
                    return RedirectToPage(new { ZoneName });
                }



                TempData["NoteSuccess"] = _L["Ans.Subdomain.Added"].Value;
                return RedirectToPage(new { ZoneName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Add subdomain failed for {Zone}", ZoneName);
                TempData["NoteError"] = _L["Err.Internal"].Value;
                return RedirectToPage(new { ZoneName });
            }
        }


    }
}
