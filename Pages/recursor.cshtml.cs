using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PowerDNS_Web.Pages
{
    public class RecursorModel : PageModel
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RecursorModel> _logger;

        public List<string> AvailableZones { get; set; } = new();
        public List<ForwardZone> ForwardZones { get; set; } = new();

        public RecursorModel(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<RecursorModel> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _configuration = configuration;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            await LoadZonesAsync();
        }

        private async Task LoadZonesAsync()
        {
            try
            {
                var pdnsUrl = _configuration["pdns:url"];
                var pdnsApiKey = _configuration["pdns:api-key"];
                var recursorUrl = _configuration["recursor:url"];
                var recursorApiKey = _configuration["recursor:api-key"];

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", pdnsApiKey);

                // FETCH AUTHORITATIVE ZONES
                var authResponse = await _httpClient.GetAsync($"{pdnsUrl}/api/v1/servers/localhost/zones");
                var authZones = JsonSerializer.Deserialize<List<DnsZone>>(await authResponse.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<DnsZone>();

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", recursorApiKey);

                // FETCH FORWARD ZONES FROM RECURSOR
                var recursorResponse = await _httpClient.GetAsync($"{recursorUrl}/api/v1/servers/localhost/zones");
                var recursorZones = JsonSerializer.Deserialize<List<RecursorZone>>(
                    await recursorResponse.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new List<RecursorZone>();

                var forwardZones = recursorZones
                    .Where(z => z.Kind != null && z.Kind.Equals("Forwarded", StringComparison.OrdinalIgnoreCase))
                    .Select(z => new ForwardZone { Name = z.Name, ForwardTo = z.Servers })
                    .ToList();

                AvailableZones = authZones
                    .Select(z => z.Name)
                    .Except(forwardZones.Select(fz => fz.Name))
                    .ToList();

                ForwardZones = forwardZones;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                _logger.LogError($"EXCEPTION IN LoadZonesAsync: {ex.Message}");
            }
        }

        public async Task<IActionResult> OnPostAddForwardZoneAsync([FromBody] ForwardZoneRequest request)
        {
            if (string.IsNullOrEmpty(request.Zone))
            {
                return new JsonResult(new { success = false, message = "INVALID ZONE" }) { StatusCode = 400 };
            }

            try
            {
                var recursorUrl = _configuration["recursor:url"];
                var recursorApiKey = _configuration["recursor:api-key"];

                var forwardZoneData = new
                {
                    name = request.Zone.EndsWith(".") ? request.Zone : request.Zone + ".",
                    kind = "Forwarded",
                    servers = new[] { "127.0.0.1:5300" },
                    recursion_desired = false
                };

                var content = new StringContent(JsonSerializer.Serialize(forwardZoneData), Encoding.UTF8, "application/json");
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", recursorApiKey);

                var response = await _httpClient.PostAsync($"{recursorUrl}/api/v1/servers/localhost/zones", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await response.Content.ReadAsStringAsync();
                    return new JsonResult(new { success = false, message = $"ERROR ADDING FORWARD ZONE: {errorMessage}" }) { StatusCode = (int)response.StatusCode };
                }

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError($"EXCEPTION IN OnPostAddForwardZoneAsync: {ex.Message}");
                return new JsonResult(new { success = false, message = $"INTERNAL SERVER ERROR: {ex.Message}" }) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> OnPostRemoveForwardZoneAsync([FromBody] ForwardZoneRequest request)
        {
            if (string.IsNullOrEmpty(request.Zone))
            {
                return new JsonResult(new { success = false, message = "INVALID ZONE" }) { StatusCode = 400 };
            }

            try
            {
                var recursorUrl = _configuration["recursor:url"];
                var recursorApiKey = _configuration["recursor:api-key"];

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", recursorApiKey);
                var response = await _httpClient.DeleteAsync($"{recursorUrl}/api/v1/servers/localhost/zones/{request.Zone}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await response.Content.ReadAsStringAsync();
                    return new JsonResult(new { success = false, message = $"ERROR REMOVING FORWARD ZONE: {errorMessage}" }) { StatusCode = (int)response.StatusCode };
                }

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError($"EXCEPTION IN OnPostRemoveForwardZoneAsync: {ex.Message}");
                return new JsonResult(new { success = false, message = $"INTERNAL SERVER ERROR: {ex.Message}" }) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> OnPostEditWildcardZoneAsync([FromBody] UpstreamDNS request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.DnsServers))
                {
                    return new JsonResult(new { success = false, message = "INVALID DNS SERVERS LIST" }) { StatusCode = 400 };
                }

                var recursorConfPath = "recursor.conf";
                var backupConfPath = "recursor.conf.bak";

                // READ CONFIG
                if (!System.IO.File.Exists(recursorConfPath))
                {
                    return new JsonResult(new { success = false, message = "RECURSOR CONFIG NOT FOUND" }) { StatusCode = 500 };
                }

                var configLines = await System.IO.File.ReadAllLinesAsync(recursorConfPath);

                // CREATE BACKUP
                await System.IO.File.WriteAllLinesAsync(backupConfPath, configLines);

                // UPDATE forward-zones
                bool updated = false;
                for (int i = 0; i < configLines.Length; i++)
                {
                    if (configLines[i].StartsWith("forward-zones="))
                    {
                        configLines[i] = $"forward-zones=.={request.DnsServers}";
                        updated = true;
                        break;
                    }
                }

                if (!updated)
                {
                    var newConfig = new List<string>(configLines) { $"forward-zones=.={request.DnsServers}" };
                    configLines = newConfig.ToArray();
                }

                // SAVE NEW CONFIG
                await System.IO.File.WriteAllLinesAsync(recursorConfPath, configLines);

                /* RESTART RECURSOR
                if (!RestartRecursor())
                {
                    _logger.LogError("FAILED TO RESTART POWERDNS RECURSOR, ROLLING BACK CONFIGURATION");

                    await System.IO.File.WriteAllLinesAsync(recursorConfPath, await System.IO.File.ReadAllLinesAsync(backupConfPath));

                    if (!RestartRecursor())
                    {
                        return new JsonResult(new { success = false, message = "FAILED TO RESTART RECURSOR AFTER ROLLBACK" }) { StatusCode = 500 };
                    }
                }*/

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError($"EXCEPTION IN OnPostEditWildcardZoneAsync: {ex.Message}");
                return new JsonResult(new { success = false, message = $"INTERNAL SERVER ERROR: {ex.Message}" }) { StatusCode = 500 };
            }
        }

        // RESTART RECURSOR
        private bool RestartRecursor()
        {
            try
            {
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/systemctl",
                        Arguments = "restart pdns-recursor",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError($"FAILED TO RESTART RECURSOR: {ex.Message}");
                return false;
            }
        }

    }

    public class UpstreamDNS
    {
        public string DnsServers { get; set; }
    }

    public class RecursorZone
    {
        public string Id { get; set; }
        public string Kind { get; set; } 
        public string Name { get; set; }
        public List<string> Servers { get; set; } = new();
        public bool Recursion_Desired { get; set; }
    }

    public class ForwardZoneRequest
    {
        public string Zone { get; set; }
    }

    public class ForwardZone
    {
        public string Name { get; set; }
        public List<string> ForwardTo { get; set; } = new();
    }

    public class DnsZone
    {
        public string Name { get; set; }
    }
}
