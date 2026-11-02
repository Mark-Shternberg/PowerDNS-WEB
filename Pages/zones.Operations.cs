using System.Text;
using System.Text.Json;

namespace PowerDNS_Web.Pages
{
    public partial class ZonesModel
    {
        // ========================= INPUT NORMALIZATION =========================
        private static string EnsureTrailingDot(string? s)
            => string.IsNullOrWhiteSpace(s) ? "" : (s.EndsWith(".") ? s : s + ".");

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

        // ========================= API CLIENTS =========================
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

        // ========================= CREATE ZONE =========================
        private async Task<(bool ok, string message)> CreateZoneAsync(string? nameRaw, string kind, bool dnssec, string? masterSingle)
        {
            var name = EnsureTrailingDot(nameRaw?.Trim());
            if (string.IsNullOrWhiteSpace(name))
                return (false, _L["Err.ZoneNameEmpty"].Value);

            var apiBase = _configuration["pdns:url"];
            if (string.IsNullOrWhiteSpace(apiBase))
                return (false, _L["Err.PdnsUrlMissing"].Value);

            using var client = NewClientWithPdnsKey();

            if (!Services.DnsValidation.IsName(name) || kind is not ("Native" or "Master" or "Slave"))
                return (false, _L["Err.InvalidParams"].Value);
            var masters = Services.DnsValidation.Masters(masterSingle);
            if (kind == "Slave" && (masters.Length == 0 || masters.Any(m => !Services.DnsValidation.IsEndpoint(m))))
                return (false, _L["Err.InvalidParams"].Value);
            if (kind != "Slave") masters = Array.Empty<string>();
            if (kind == "Slave") dnssec = false;

            var zonePayload = new
            {
                name,
                kind,
                dnssec,
                soa_edit_api = "DEFAULT",
                nameservers = kind == "Slave" ? null : new[] { EnsureTrailingDot(_configuration["pdns:soa:ns"] ?? "ns1.default.com.") },
                masters
            };

            var content = new StringContent(JsonSerializer.Serialize(zonePayload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
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

            }

                // Forward secondary zones too.
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

            return (true, _L["Msg.ZoneAdded"].Value);
        }

        // ========================= UPDATE ZONE =========================
        private async Task<(bool ok, string message)> UpdateZoneAsync(string nameRaw, string kind, bool dnssec, long serial, string? masterInput)
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

            if (!Services.DnsValidation.IsName(name) || kind is not ("Native" or "Master" or "Slave"))
                return (false, _L["Err.InvalidParams"].Value);
            var masters = masterInput == null ? currentZone.masters.ToArray() : Services.DnsValidation.Masters(masterInput);
            if (kind == "Slave" && (masters.Length == 0 || masters.Any(m => !Services.DnsValidation.IsEndpoint(m))))
                return (false, _L["Err.InvalidParams"].Value);
            if (kind != "Slave") masters = Array.Empty<string>();
            bool isDnssec = kind == "Slave" ? currentZone.dnssec : dnssec;

            // 2) Update
            var updatePayload = new
            {
                name,
                kind,
                dnssec = isDnssec,
                masters,
                soa_edit_api = "DEFAULT"
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

            return (true, _L["Msg.ZoneUpdated"].Value);
        }

        // ========================= DELETE ZONE =========================
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
    }
}
