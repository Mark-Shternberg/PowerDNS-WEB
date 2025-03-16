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
    public class ZonesModel : PageModel
    {
        private readonly ILogger<ZonesModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public List<DnsZone> Zones { get; private set; } = new();

        public ZonesModel(ILogger<ZonesModel> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
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

        public class DnssecKey
        {
            public int Id { get; set; }
            public bool Active { get; set; }
            public string Algorithm { get; set; }
            public int Bits { get; set; }
            public string KeyType { get; set; }
            public bool Published { get; set; }
        }

        public async Task<IActionResult> OnPostAddZoneAsync()
        {
            try
            {
                var apiUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones";
                var apiKey = _configuration["pdns:api_key"];

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
                    dnssec = request.dnssec,
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

                // IF DNSSEC IS ENABLED, GENERATE CRYPTOKEYS
                if (request.dnssec)
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

                    var dnssecResponse = await client.PostAsync($"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones/{request.name}/cryptokeys", dnssecContent);
                    var dnssecResponseContent = await dnssecResponse.Content.ReadAsStringAsync();

                    if (!dnssecResponse.IsSuccessStatusCode)
                    {
                        _logger.LogError($"FAILED TO CREATE DNSSEC KEYS: {dnssecResponseContent}");
                        return new JsonResult(new { success = false, message = $"FAILED TO CREATE DNSSEC KEYS: {dnssecResponseContent}" }) { StatusCode = (int)dnssecResponse.StatusCode };
                    }
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

                    // ADD ZONE TO RECURSOR
                    if (_configuration["recursor:Enabled"] == "Enabled")
                    {
                        var recursorUrl = _configuration["recursor:url"];
                        var recursorApiKey = _configuration["recursor:api_key"];

                        var forwardZoneData = new
                        {
                            name = request.name.EndsWith(".") ? request.name : request.name + ".",
                            kind = "Forwarded",
                            servers = new[] { "127.0.0.1:5300" },
                            recursion_desired = false
                        };

                        var content2 = new StringContent(JsonSerializer.Serialize(forwardZoneData), Encoding.UTF8, "application/json");
                        client.DefaultRequestHeaders.Clear();
                        client.DefaultRequestHeaders.Add("X-API-Key", recursorApiKey);

                        var response2 = await client.PostAsync($"{recursorUrl}/api/v1/servers/localhost/zones", content2);

                        if (!response2.IsSuccessStatusCode)
                        {
                            var errorMessage = await response2.Content.ReadAsStringAsync();
                            return new JsonResult(new { success = false, message = $"ZONE ADDED. ERROR ADDING FORWARD ZONE: {errorMessage}" }) { StatusCode = (int)response.StatusCode };
                        }
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
                var apiKey = _configuration["pdns:api_key"];

                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

                // GET CURRENT ZONE INFO
                var currentZoneResponse = await client.GetAsync(apiUrl);
                if (!currentZoneResponse.IsSuccessStatusCode)
                {
                    var errorMessage = await currentZoneResponse.Content.ReadAsStringAsync();
                    _logger.LogError($"FAILED TO FETCH ZONE '{request.name}': {errorMessage}");
                    return new JsonResult(new { success = false, message = $"FAILED TO FETCH ZONE '{request.name}': {errorMessage}" }) { StatusCode = (int)currentZoneResponse.StatusCode };
                }

                var currentZoneJson = await currentZoneResponse.Content.ReadAsStringAsync();
                var currentZone = JsonSerializer.Deserialize<DnsZone>(currentZoneJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (currentZone == null)
                {
                    return new JsonResult(new { success = false, message = "FAILED TO PARSE CURRENT ZONE DATA." }) { StatusCode = 500 };
                }

                bool wasDnssecEnabled = currentZone.dnssec;
                bool isDnssecEnabled = request.dnssec;

                // UPDATE ZONE
                var updatePayload = new
                {
                    name = request.name,
                    kind = request.kind,
                    dnssec = isDnssecEnabled,
                    serial = request.serial > 0 ? request.serial : 0
                };

                var content = new StringContent(JsonSerializer.Serialize(updatePayload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }), Encoding.UTF8, "application/json");

                var response = await client.PutAsync(apiUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"POWERDNS API ERROR: {response.StatusCode} - {responseContent}");
                    return new JsonResult(new { success = false, message = $"PowerDNS API error: {responseContent}" }) { StatusCode = (int)response.StatusCode };
                }

                // HANDLE DNSSEC KEYS
                if (!wasDnssecEnabled && isDnssecEnabled)
                {
                    // ENABLE DNSSEC: CREATE NEW KEYS (KSK & ZSK)
                    var keyTypes = new[]
                    {
                        new { Type = "ksk", Algorithm = "ECDSAP256SHA256", Bits = 256 }//,
                       // new { Type = "zsk", Algorithm = "ECDSAP256SHA256", Bits = 256 }
                    };

                    foreach (var key in keyTypes)
                    {
                        var createKeyPayload = new
                        {
                            keytype = key.Type,
                            active = true,
                            algorithm = key.Algorithm,
                            bits = key.Bits
                        };

                        var createKeyContent = new StringContent(JsonSerializer.Serialize(createKeyPayload), Encoding.UTF8, "application/json");
                        var createKeyResponse = await client.PostAsync($"{apiUrl}/cryptokeys", createKeyContent);

                        if (!createKeyResponse.IsSuccessStatusCode)
                        {
                            var errorMessage = await createKeyResponse.Content.ReadAsStringAsync();
                            _logger.LogError($"FAILED TO CREATE {key.Type} DNSSEC KEY FOR '{request.name}': {errorMessage}");
                            return new JsonResult(new { success = false, message = $"FAILED TO CREATE {key.Type} DNSSEC KEY: {errorMessage}" }) { StatusCode = (int)createKeyResponse.StatusCode };
                        }
                    }

                    _logger.LogInformation($"DNSSEC ENABLED FOR '{request.name}', KSK & ZSK KEYS CREATED.");
                }
                else if (wasDnssecEnabled && !isDnssecEnabled)
                {
                    // DISABLE DNSSEC: DELETE ALL KEYS
                    var keysResponse = await client.GetAsync($"{apiUrl}/cryptokeys");
                    if (keysResponse.IsSuccessStatusCode)
                    {
                        var keysJson = await keysResponse.Content.ReadAsStringAsync();
                        var dnsKeys = JsonSerializer.Deserialize<List<DnssecKey>>(keysJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (dnsKeys != null)
                        {
                            foreach (var key in dnsKeys)
                            {
                                var deleteKeyResponse = await client.DeleteAsync($"{apiUrl}/cryptokeys/{key.Id}");
                                if (!deleteKeyResponse.IsSuccessStatusCode)
                                {
                                    var errorMsg = await deleteKeyResponse.Content.ReadAsStringAsync();
                                    _logger.LogError($"FAILED TO DELETE DNSSEC KEY {key.Id} FOR '{request.name}': {errorMsg}");
                                }
                            }
                        }
                    }

                    _logger.LogInformation($"DNSSEC DISABLED FOR '{request.name}', KEYS AND DS RECORDS REMOVED.");
                }

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError($"EXCEPTION IN OnPostEditZoneAsync: {ex.Message}");
                return new JsonResult(new { success = false, message = $"INTERNAL SERVER ERROR: {ex.Message}" }) { StatusCode = 500 };
            }
        }

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
                _logger.LogError($"Error seeking DNSSEC keys: {response.StatusCode} - {responseContent}");
                return new JsonResult(new { success = false, message = $"PowerDNS API error: {responseContent}" })
                { StatusCode = (int)response.StatusCode };
            }

            return new JsonResult(new { success = true, keys = responseContent });
        }

        public async Task<IActionResult> OnPostDeleteZoneAsync([FromBody] DnsZone zone)
        {
            var apiUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones/{zone.name}";
            var apiKey = _configuration["pdns:api_key"];

            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

            var response = await client.DeleteAsync(apiUrl);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Error updating zone: {response.StatusCode} - {responseContent}");
                return new JsonResult(new { success = false, message = $"PowerDNS API error: {responseContent}" })
                { StatusCode = (int)response.StatusCode };
            }

            // DELETE ZONE FROM RECURSOR
            if (_configuration["recursor:Enabled"] == "Enabled")
            {
                try
                {
                    var recursorUrl = _configuration["recursor:url"];
                    var recursorApiKey = _configuration["recursor:api_key"];

                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("X-API-Key", recursorApiKey);
                    var response2 = await client.DeleteAsync($"{recursorUrl}/api/v1/servers/localhost/zones/{zone.name}");

                    if (!response2.IsSuccessStatusCode)
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

            return new JsonResult(new { success = true });
        }
    }
}
