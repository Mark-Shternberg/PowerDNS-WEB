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
    public class ZonePageModel : PageModel
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly string _apiKey;
        private readonly string _default_IP;
        private readonly string _recursor_Enabled;
        private readonly ILogger<ZonePageModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly IStringLocalizer _L;

        public ZonePageModel(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ZonePageModel> logger, IStringLocalizerFactory factory)
        {
            _logger = logger;
            var asmName = Assembly.GetExecutingAssembly().GetName().Name!;
            _L = factory.Create("Pages.zone.zone", asmName);
            _configuration = configuration;

            _httpClient = httpClientFactory.CreateClient();
            _apiUrl = configuration["pdns:url"] ?? string.Empty;
            _apiKey = configuration["pdns:api_key"] ?? string.Empty;
            _default_IP = configuration["pdns:default_a"] ?? string.Empty;
            _recursor_Enabled = configuration["recursor:Enabled"] ?? "Disabled";
        }

        [BindProperty(SupportsGet = true)]
        public string? ZoneName { get; set; }

        public Dictionary<string, List<DnsRecord>> GroupedRecords { get; set; } = new();

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
            if (fullRecordName.EndsWith(zn + ".", StringComparison.Ordinal))
            {
                string subdomain = fullRecordName.Substring(0, fullRecordName.Length - zn.Length - 1);
                return string.IsNullOrEmpty(subdomain) ? "@" : subdomain;
            }
            return fullRecordName;
        }

        // ---------- DTOs ----------
        public class ZoneData { public List<Rrset> Rrsets { get; set; } = new(); }
        public class Rrset { public string? Name { get; set; } public string? Type { get; set; } public int Ttl { get; set; } public List<Record> Records { get; set; } = new(); }
        public class Record { public string? Content { get; set; } public bool Disabled { get; set; } }
        public class DnsRecord { public string Name { get; set; } = string.Empty; public string Type { get; set; } = string.Empty; public List<string> Content { get; set; } = new(); public int Ttl { get; set; } }

        public class AddRecordRequest
        {
            public string? Subdomain { get; set; }
            public string? RecordType { get; set; }
            public string? Value { get; set; }
            public int? Ttl { get; set; }
            public int? MxPriority { get; set; }
            public int? SrvPriority { get; set; }
            public int? SrvWeight { get; set; }
            public int? SrvPort { get; set; }
        }

        public class DeleteRecordRequest { public string? Name { get; set; } public string? Type { get; set; } public string? Value { get; set; } }

        public class EditRecordRequest
        {
            public string? Name { get; set; }
            public string? Type { get; set; }
            public string? OldValue { get; set; }
            public string? Value { get; set; }
            public int? Ttl { get; set; }
            public int? MxPriority { get; set; }
            public int? SrvPriority { get; set; }
            public int? SrvWeight { get; set; }
            public int? SrvPort { get; set; }
            public string? SoaNs { get; set; }
            public string? SoaEmail { get; set; }
            public int? SoaRefresh { get; set; }
            public int? SoaRetry { get; set; }
            public int? SoaExpire { get; set; }
            public int? SoaMinimumTtl { get; set; }
        }

        public class AddSubdomainRequest { public string? Subdomain { get; set; } }

        // === ADD RECORD (TempData + Redirect) ===
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
                var fullDomain = subdomain == "@" ? ZoneName! : $"{subdomain}.{ZoneName}";

                // fetch zone
                var getReq = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}");
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
                if (zoneData?.Rrsets == null)
                {
                    TempData["NoteError"] = _L["Err.ZoneEmpty"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                var recordType = request.RecordType!;
                var existing = zoneData.Rrsets.FirstOrDefault(r => r.Name == fullDomain && r.Type == recordType);
                var updated = existing?.Records?.ToList() ?? new List<Record>();

                // normalize & build content
                var rawVal = request.Value ?? string.Empty;

                if (recordType == "NS")
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

                int ttl = request.Ttl is >= 300 and <= 604800 ? request.Ttl.Value : 3600;

                var patch = new
                {
                    rrsets = new[] {
                        new { name = fullDomain, type = recordType, ttl = ttl, changetype = "REPLACE", records = updated }
                    }
                };

                var json = JsonSerializer.Serialize(patch, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}")
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

        // === DELETE RECORD (TempData + Redirect) ===
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
                var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}");
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
                if (zoneData?.Rrsets == null)
                {
                    TempData["NoteError"] = _L["Err.ZoneEmpty"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                var recordSet = zoneData.Rrsets.FirstOrDefault(r => r.Name == fqdn && r.Type == type);
                if (recordSet == null || recordSet.Records == null || recordSet.Records.Count == 0)
                {
                    TempData["NoteError"] = _L["Err.RecordNotFound"].Value;
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
                            records = updatedRecords.Select(r => new { content = r.Content ?? string.Empty, disabled = false }).ToArray()
                        }
                    }
                };

                var json = JsonSerializer.Serialize(deleteRecord, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                var requestMessage = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}")
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

                if (_recursor_Enabled == "Enabled") ExecuteBashCommand("rec_control reload-zones");

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

        // === EDIT RECORD (TempData + Redirect) ===
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

                if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Type) || string.IsNullOrWhiteSpace(req.Value))
                {
                    TempData["NoteError"] = _L["Err.InvalidRequest"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                string fqdn = req.Name.TrimEnd('.') + ".";

                // fetch
                var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}");
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
                if (zoneData?.Rrsets == null)
                {
                    TempData["NoteError"] = _L["Err.ZoneEmpty"].Value;
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
                    .Select(r => new { content = r.Content ?? string.Empty, disabled = false })
                    .ToList();

                string newRecordContent = (req.Value ?? string.Empty).TrimEnd('.');

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
                    long soaSerial = 0; // серийник обновит PowerDNS
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

                remainingRecords.Add(new { content = newRecordContent, disabled = false });

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
                var requestMessage = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}")
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

                if (_recursor_Enabled == "Enabled") ExecuteBashCommand("rec_control reload-zones");

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

        // === ADD SUBDOMAIN (TempData + Redirect) ===
        public async Task<IActionResult> OnPostAddSubdomainAsync()
        {
            if (string.IsNullOrWhiteSpace(ZoneName))
            {
                TempData["NoteError"] = _L["Err.ZoneNotSpecified"].Value;
                return RedirectToPage("/zones");
            }

            try
            {
                var sub = Request.Form["Subdomain"].ToString();
                if (string.IsNullOrWhiteSpace(sub))
                {
                    TempData["NoteError"] = _L["Err.InvalidRequest"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                if (string.IsNullOrWhiteSpace(_default_IP))
                {
                    TempData["NoteError"] = _L["Err.DefaultARequired"].Value;
                    return RedirectToPage(new { ZoneName });
                }

                string fullSubdomain = $"{sub.TrimEnd('.')}.{ZoneName}";

                var newRecord = new
                {
                    rrsets = new[]
                    {
                        new
                        {
                            name = fullSubdomain,
                            type = "A",
                            ttl = 3600,
                            changetype = "REPLACE",
                            records = new[] { new { content = _default_IP, disabled = false } }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(newRecord, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var requestMessage = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                requestMessage.Headers.Add("X-API-Key", _apiKey);

                var response = await _httpClient.SendAsync(requestMessage);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    TempData["NoteError"] = _L["Err.AddSubdomainApi", err].Value;
                    return RedirectToPage(new { ZoneName });
                }

                if (_recursor_Enabled == "Enabled") ExecuteBashCommand("rec_control reload-zones");

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

        // ---------- helpers ----------
        private void ExecuteBashCommand(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);
                if (process == null)
                {
                    _logger.LogError("Failed to start process for '{Cmd}'", command);
                    return;
                }

                using (process)
                {
                    process.WaitForExit();
                    var err = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrEmpty(err))
                        _logger.LogError("Command '{Cmd}' error: {Err}", command, err);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute command '{Cmd}'", command);
            }
        }

        private static string FormatTxtForPowerDNS(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "\"\"";
            raw = raw.Trim();
            if (raw.StartsWith("\"")) raw = raw[1..];
            if (raw.EndsWith("\"")) raw = raw[..^1];
            return $"\"{raw}\"";
        }

        private AddRecordRequest ParseAddRecordFromForm()
        {
            var type = Request.Form["RecordType"].ToString();
            string value = Request.Form["Value"].ToString();
            if (type == "TXT") value = Request.Form["TxtValue"].ToString();
            else if (type == "NS") value = Request.Form["NsTarget"].ToString();
            else if (type == "HTTPS") value = Request.Form["HttpsValue"].ToString();

            return new AddRecordRequest
            {
                Subdomain = Request.Form["Subdomain"].ToString(),
                RecordType = type,
                Value = value,
                Ttl = int.TryParse(Request.Form["Ttl"], out var ttl) ? ttl : null,
                MxPriority = int.TryParse(Request.Form["MxPriority"], out var mx) ? mx : null,
                SrvPriority = int.TryParse(Request.Form["SrvPriority"], out var sp) ? sp : null,
                SrvWeight = int.TryParse(Request.Form["SrvWeight"], out var sw) ? sw : null,
                SrvPort = int.TryParse(Request.Form["SrvPort"], out var sport) ? sport : null
            };
        }

        private EditRecordRequest ParseEditRecordFromForm()
        {
            return new EditRecordRequest
            {
                Name = Request.Form["Name"].ToString(),
                Type = Request.Form["Type"].ToString(),
                OldValue = Request.Form["OldValue"].ToString(),
                Value = Request.Form["Value"].ToString(),
                Ttl = int.TryParse(Request.Form["Ttl"], out var ttl) ? ttl : null,
                MxPriority = int.TryParse(Request.Form["MxPriority"], out var mx) ? mx : null,
                SrvPriority = int.TryParse(Request.Form["SrvPriority"], out var sp) ? sp : null,
                SrvWeight = int.TryParse(Request.Form["SrvWeight"], out var sw) ? sw : null,
                SrvPort = int.TryParse(Request.Form["SrvPort"], out var sport) ? sport : null,
                SoaNs = Request.Form["SoaNs"].ToString(),
                SoaEmail = Request.Form["SoaEmail"].ToString(),
                SoaRefresh = int.TryParse(Request.Form["SoaRefresh"], out var sr) ? sr : null,
                SoaRetry = int.TryParse(Request.Form["SoaRetry"], out var sry) ? sry : null,
                SoaExpire = int.TryParse(Request.Form["SoaExpire"], out var se) ? se : null,
                SoaMinimumTtl = int.TryParse(Request.Form["SoaMinimumTtl"], out var sm) ? sm : null
            };
        }
    }
}
