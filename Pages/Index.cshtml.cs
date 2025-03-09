using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using static PowerDNS_Web.Pages.zone.ZonePageModel;

namespace PowerDNS_Web.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public List<DnsZone> Zones { get; private set; } = new();

        public IndexModel(ILogger<IndexModel> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        public async Task OnGetAsync()
        {
            var apiUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones";
            var apiKey = _configuration["pdns:api-key"];

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
                    _logger.LogError($"PowerDNS API ERROR: {response.StatusCode}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"CONNECTION ERROR TO PowerDNS API: {ex.Message}");
            }
        }

        public class DnsZone
        {
            public string name { get; set; }
            public string kind { get; set; }
            public List<string> masters { get; set; } = new List<string>();
            public bool dnssec { get; set; }
            public long serial { get; set; }

            // RETURN MASTER AS STRING (FOR UI DISPLAY)
            public string Master => masters != null && masters.Count > 0 ? string.Join(", ", masters) : "";
        }


        public async Task<IActionResult> OnPostAddZoneAsync()
        {
            try
            {
                var apiUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones";
                var apiKey = _configuration["pdns:api-key"];

                string requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
                var request = JsonSerializer.Deserialize<DnsZone>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (request == null || string.IsNullOrEmpty(request.name) || string.IsNullOrEmpty(request.kind))
                {
                    return new JsonResult(new { success = false, message = "INVALID REQUEST PARAMETERS." }) { StatusCode = 400 };
                }

                request.name = request.name.EndsWith(".") ? request.name : request.name + ".";

                // CONVERT MASTER SERVER TO ARRAY (POWERDNS REQUIRES LIST FOR SLAVE ZONES)
                var zonePayload = new
                {
                    name = request.name,
                    kind = request.kind,
                    masters = request.kind == "Slave" && !string.IsNullOrEmpty(request.Master)
                        ? new List<string> { request.Master }
                        : null 
                };

                var content = new StringContent(JsonSerializer.Serialize(zonePayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }), Encoding.UTF8, "application/json");

                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

                var response = await client.PostAsync(apiUrl, content);
                var jsonResponse = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"POWERDNS API ERROR: {jsonResponse}");
                    return new JsonResult(new { success = false, message = jsonResponse }) { StatusCode = (int)response.StatusCode };
                }

                // ADD SOA RECORD ONLY IF ZONE TYPE IS NOT SLAVE
                if (request.kind != "Slave")
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
                                name = request.name,
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

                    var soaUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones/{request.name}";
                    var soaResponse = await client.PatchAsync(soaUrl, soaContent);
                    var soaResponseContent = await soaResponse.Content.ReadAsStringAsync();

                    if (!soaResponse.IsSuccessStatusCode)
                    {
                        _logger.LogError($"FAILED TO ADD SOA RECORD: {soaResponseContent}");
                        return new JsonResult(new { success = false, message = $"FAILED TO ADD SOA RECORD: {soaResponseContent}" })
                        { StatusCode = (int)soaResponse.StatusCode };
                    }
                }

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError($"EXCEPTION IN OnPostAddZoneAsync: {ex.Message}");
                return new JsonResult(new { success = false, message = $"INTERNAL SERVER ERROR: {ex.Message}" }) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> OnPostEditZoneAsync()
        {
            try
            {
                string requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
                var request = JsonSerializer.Deserialize<DnsZone>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (request == null || string.IsNullOrEmpty(request.name) || string.IsNullOrEmpty(request.kind))
                {
                    return new JsonResult(new { success = false, message = "INVALID REQUEST PARAMETERS." }) { StatusCode = 400 };
                }

                var apiUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones/{request.name}";
                var apiKey = _configuration["pdns:api-key"];

                // HANDLE MASTERS AS LIST (IF SLAVE, OTHERWISE REMOVE)
                List<string> masterList = (request.kind == "Slave" && request.masters != null && request.masters.Count > 0)
                    ? request.masters
                    : null;

                var updatePayload = new
                {
                    name = request.name,
                    kind = request.kind,
                    masters = request.kind == "Slave" ? masterList : null, // DO NOT SEND EMPTY LIST FOR NON-SLAVE ZONES
                    dnssec = request.dnssec,
                    serial = request.serial > 0 ? request.serial : 0 // AVOID SENDING SERIAL 0
                };

                var content = new StringContent(JsonSerializer.Serialize(updatePayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }), Encoding.UTF8, "application/json");

                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

                var response = await client.PutAsync(apiUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return new JsonResult(new { success = true });
                }
                else
                {
                    _logger.LogError($"POWERDNS API ERROR: {response.StatusCode} - {responseContent}");
                    return new JsonResult(new { success = false, message = $"PowerDNS API error: {responseContent}" })
                    { StatusCode = (int)response.StatusCode };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"EXCEPTION IN OnPostEditZoneAsync: {ex.Message}");
                return new JsonResult(new { success = false, message = $"INTERNAL SERVER ERROR: {ex.Message}" }) { StatusCode = 500 };
            }
        }


        public async Task<IActionResult> OnPostDeleteZoneAsync([FromBody] DnsZone zone)
        {
            var apiUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones/{zone.name}";
            var apiKey = _configuration["pdns:api-key"];

            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

            var response = await client.DeleteAsync(apiUrl);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return new JsonResult(new { success = true });
            }
            else
            {
                _logger.LogError($"Error updating zone: {response.StatusCode} - {responseContent}");
                return new JsonResult(new { success = false, message = $"PowerDNS API error: {responseContent}" })
                { StatusCode = (int)response.StatusCode };
            }
        }
    }
}
