using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace PowerDNS_Web.Pages.zone
{
    public class ZonePageModel : PageModel
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly string _apiKey;
        private readonly string _default_IP;

        public ZonePageModel(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _apiUrl = configuration["pdns:url"];
            _apiKey = configuration["pdns:api_key"];
            _default_IP = configuration["pdns:default_a"];
        }

        [BindProperty(SupportsGet = true)]
        public string ZoneName { get; set; }

        public Dictionary<string, List<DnsRecord>> GroupedRecords { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (string.IsNullOrEmpty(ZoneName))
                return NotFound();

            ZoneName = ZoneName.TrimEnd('.'); // Убираем точку в конце

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}.");
                request.Headers.Add("X-API-Key", _apiKey);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var zoneData = JsonSerializer.Deserialize<ZoneData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (zoneData != null && zoneData.Rrsets != null)
                    {
                        GroupedRecords = zoneData.Rrsets
                            .Where(r => r.Records.Count > 0)
                            .GroupBy(r => GetSubdomain(r.Name))
                            .ToDictionary(g => g.Key, g => g.Select(r => new DnsRecord
                            {
                                Name = r.Name,
                                Type = r.Type,
                                Content = r.Records.Select(rec => rec.Content).ToList(),
                                Ttl = r.Ttl
                            }).ToList());
                    }
                }
                else
                {
                    return StatusCode((int)response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                return StatusCode(500);
            }

            return Page();
        }

        private string GetSubdomain(string fullRecordName)
        {
            if (fullRecordName.EndsWith(ZoneName + "."))
            {
                string subdomain = fullRecordName.Substring(0, fullRecordName.Length - ZoneName.Length - 1);
                return string.IsNullOrEmpty(subdomain) ? "@" : subdomain;
            }
            return fullRecordName;
        }

        public class ZoneData
        {
            public List<Rrset> Rrsets { get; set; } = new();
        }

        public class Rrset
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public int Ttl { get; set; }
            public List<Record> Records { get; set; } = new();
        }

        public class Record
        {
            public string Content { get; set; }
            public bool Disabled { get; set; }
        }

        public class DnsRecord
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public List<string> Content { get; set; } = new();
            public int Ttl { get; set; }
        }

        public class AddRecordRequest
        {
            public string Subdomain { get; set; }
            public string RecordType { get; set; }
            public string Value { get; set; }
            public int? MxPriority { get; set; }
            public int? SrvPriority { get; set; }
            public int? SrvWeight { get; set; }
            public int? SrvPort { get; set; }
        }

        public class DeleteRecordRequest
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Value { get; set; }
        }

        public class EditRecordRequest
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string OldValue { get; set; }
            public string Value { get; set; }
            public int? Ttl { get; set; }
            public int? MxPriority { get; set; }
            public int? SrvPriority { get; set; }
            public int? SrvWeight { get; set; }
            public int? SrvPort { get; set; }
            public string SoaNs { get; set; }
            public string SoaEmail { get; set; }
            public int? SoaRefresh { get; set; }
            public int? SoaRetry { get; set; }
            public int? SoaExpire { get; set; }
            public int? SoaMinimumTtl { get; set; }
        }

        public class AddSubdomainRequest
        {
            public string Subdomain { get; set; }
        }


        public async Task<IActionResult> OnPostAddRecordAsync()
        {
            try
            {
                string requestBody = await new StreamReader(Request.Body).ReadToEndAsync();

                var request = JsonSerializer.Deserialize<AddRecordRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (request == null)
                {
                    return new JsonResult(new { success = false, message = "Invalid JSON format." }) { StatusCode = 400 };
                }

                string subdomain = request.Subdomain?.TrimEnd('.') ?? "@";
                string fullDomain = subdomain == "@" ? ZoneName : $"{subdomain}.{ZoneName}";

                // Получаем существующие записи
                var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}");
                getRequest.Headers.Add("X-API-Key", _apiKey);

                var getResponse = await _httpClient.SendAsync(getRequest);
                if (!getResponse.IsSuccessStatusCode)
                {
                    var errorContent = await getResponse.Content.ReadAsStringAsync();
                    return new JsonResult(new { success = false, message = $"Error fetching records: {errorContent}" }) { StatusCode = (int)getResponse.StatusCode };
                }

                var jsonResponse = await getResponse.Content.ReadAsStringAsync();
                var zoneData = JsonSerializer.Deserialize<ZoneData>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (zoneData == null || zoneData.Rrsets == null)
                {
                    return new JsonResult(new { success = false, message = "Zone data is empty." }) { StatusCode = 500 };
                }

                // Находим записи нужного типа
                var existingRecordSet = zoneData.Rrsets.FirstOrDefault(r => r.Name == fullDomain && r.Type == request.RecordType);
                List<Record> updatedRecords = existingRecordSet?.Records?.ToList() ?? new List<Record>();

                // Если запись уже существует – не добавляем
                if (updatedRecords.Any(r => r.Content == request.Value))
                {
                    return new JsonResult(new { success = false, message = "Record already exists." }) { StatusCode = 400 };
                }

                // Добавляем новую запись
                string recordContent = request.Value;

                if (request.RecordType == "MX")
                {
                    int mxPriority = request.MxPriority ?? 10;
                    string mailServer = request.Value.TrimEnd('.') + "."; 
                    recordContent = $"{mxPriority} {mailServer}";
                }
                else if (request.RecordType == "SRV")
                {
                    int srvPriority = request.SrvPriority ?? 0;
                    int srvWeight = request.SrvWeight ?? 0;
                    int srvPort = request.SrvPort ?? 0;
                    string targetHost = request.Value.TrimEnd('.') + "."; 
                    recordContent = $"{srvPriority} {srvWeight} {srvPort} {targetHost}";
                }


                updatedRecords.Add(new Record { Content = recordContent, Disabled = false });
               //Console.WriteLine($"Updated records: {JsonSerializer.Serialize(updatedRecords)}");

                // Обновляем записи
                var recordUpdate = new
                {
                    rrsets = new[]
                    {
                        new
                        {
                            name = fullDomain,
                            type = request.RecordType,
                            ttl = 3600,
                            changetype = "REPLACE",
                            records = updatedRecords
                        }
                    }
                };

                var json = JsonSerializer.Serialize(recordUpdate, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}")
                {
                    Headers = { { "X-API-Key", _apiKey } },
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                var patchResponse = await _httpClient.SendAsync(patchRequest);
                if (!patchResponse.IsSuccessStatusCode)
                {
                    var patchError = await patchResponse.Content.ReadAsStringAsync();
                    return new JsonResult(new { success = false, message = $"Error updating records: {patchError}" }) { StatusCode = (int)patchResponse.StatusCode };
                }

                return new JsonResult(new
                {
                    success = true,
                    message = "Record added successfully!",
                    subdomain = subdomain,
                    recordType = request.RecordType,
                    value = request.Value
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"Internal Server Error: {ex.Message}" }) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> OnPostDeleteRecordAsync([FromBody] DeleteRecordRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Type) || string.IsNullOrEmpty(request.Value))
                {
                    return new JsonResult(new { success = false, message = "Invalid request parameters." }) { StatusCode = 400 };
                }

                string name = request.Name.TrimEnd('.') + ".";

                // 1. Получаем текущие записи этой зоны
                var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}");
                getRequest.Headers.Add("X-API-Key", _apiKey);

                var getResponse = await _httpClient.SendAsync(getRequest);
                if (!getResponse.IsSuccessStatusCode)
                {
                    var errorContent = await getResponse.Content.ReadAsStringAsync();
                    return new JsonResult(new { success = false, message = $"Error fetching records: {errorContent}" }) { StatusCode = (int)getResponse.StatusCode };
                }

                var jsonResponse = await getResponse.Content.ReadAsStringAsync();
                var zoneData = JsonSerializer.Deserialize<ZoneData>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (zoneData == null || zoneData.Rrsets == null)
                {
                    return new JsonResult(new { success = false, message = "Zone data is empty." }) { StatusCode = 500 };
                }

                // 2. Находим текущие записи для данного имени и типа
                var recordSet = zoneData.Rrsets.FirstOrDefault(r => r.Name == name && r.Type == request.Type);
                if (recordSet == null || recordSet.Records == null || recordSet.Records.Count == 0)
                {
                    return new JsonResult(new { success = false, message = "Record not found." }) { StatusCode = 404 };
                }

                // 3. Оставляем только записи, которые НЕ равны удаляемой
                var updatedRecords = recordSet.Records.Where(r => r.Content != request.Value).ToList();

                // Если после удаления остались записи → используем "REPLACE", иначе "DELETE"
                string changeType = updatedRecords.Count > 0 ? "REPLACE" : "DELETE";

                // 4. Формируем запрос к PowerDNS
                var deleteRecord = new
                {
                    rrsets = new[]
                    {
                new
                {
                    name = name,
                    type = request.Type,
                    ttl = recordSet.Ttl,  // <-- Теперь TTL всегда присутствует
                    changetype = changeType,
                    records = updatedRecords.Select(r => new { content = r.Content, disabled = false }).ToArray()
                }
            }
                };

                var json = JsonSerializer.Serialize(deleteRecord, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                var requestMessage = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}")
                {
                    Headers = { { "X-API-Key", _apiKey } },
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                var response = await _httpClient.SendAsync(requestMessage);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return new JsonResult(new { success = false, message = $"Error deleting record: {errorContent}" }) { StatusCode = (int)response.StatusCode };
                }

                return new JsonResult(new { success = true, message = "Record deleted successfully!" });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"Internal Server Error: {ex.Message}" }) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> OnPostEditRecordAsync([FromBody] EditRecordRequest request)
        {
            try
            {
                // CHECK IF REQUEST PARAMETERS ARE VALID
                if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Type) || string.IsNullOrEmpty(request.Value))
                {
                    return new JsonResult(new { success = false, message = "INVALID REQUEST PARAMETERS." }) { StatusCode = 400 };
                }

                string name = request.Name.TrimEnd('.') + ".";

                // FETCH CURRENT RECORDS FROM POWERDNS
                var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}");
                getRequest.Headers.Add("X-API-Key", _apiKey);

                var getResponse = await _httpClient.SendAsync(getRequest);
                if (!getResponse.IsSuccessStatusCode)
                {
                    var errorContent = await getResponse.Content.ReadAsStringAsync();
                    return new JsonResult(new { success = false, message = $"ERROR FETCHING RECORDS: {errorContent}" }) { StatusCode = (int)getResponse.StatusCode };
                }

                var jsonResponse = await getResponse.Content.ReadAsStringAsync();
                var zoneData = JsonSerializer.Deserialize<ZoneData>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (zoneData == null || zoneData.Rrsets == null)
                {
                    return new JsonResult(new { success = false, message = "ZONE DATA IS EMPTY." }) { StatusCode = 500 };
                }

                // FIND EXISTING RECORD SET FOR THIS NAME AND TYPE
                var recordSet = zoneData.Rrsets.FirstOrDefault(r => r.Name == name && r.Type == request.Type);
                if (recordSet == null || recordSet.Records == null || recordSet.Records.Count == 0)
                {
                    return new JsonResult(new { success = false, message = "RECORD NOT FOUND." }) { StatusCode = 404 };
                }

                // REMOVE ONLY THE OLD RECORD, KEEPING OTHERS INTACT
                var remainingRecords = recordSet.Records
                    .Where(r => r.Content != request.OldValue)
                    .Select(r => new { content = r.Content, disabled = false })
                    .ToList();

                string newRecordContent = request.Value.TrimEnd('.');

                // HANDLE MX RECORD
                if (request.Type == "MX")
                {
                    int mxPriority = request.MxPriority ?? 10;
                    string[] parts = request.Value.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    string mailServer = parts.Length > 1 ? parts.Last() : parts.First();
                    newRecordContent = $"{mxPriority} {mailServer.TrimEnd('.')}.";
                }

                // HANDLE SRV RECORD
                else if (request.Type == "SRV")
                {
                    int srvPriority = request.SrvPriority ?? 0;
                    int srvWeight = request.SrvWeight ?? 0;
                    int srvPort = request.SrvPort ?? 0;

                    string[] parts = request.Value.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    string targetServer = parts.Length > 3 ? parts.Last() : parts.First();

                    newRecordContent = $"{srvPriority} {srvWeight} {srvPort} {targetServer.TrimEnd('.')}.";
                }

                // HANDLE SOA RECORD
                else if (request.Type == "SOA")
                {
                    string soaNs = request.SoaNs.TrimEnd('.') + ".";
                    string soaEmail = request.SoaEmail.TrimEnd('.') + ".";
                    long soaSerial = 0; 
                    int soaRefresh = request.SoaRefresh ?? 10800;
                    int soaRetry = request.SoaRetry ?? 3600;
                    int soaExpire = request.SoaExpire ?? 604800;
                    int soaMinimumTtl = request.SoaMinimumTtl ?? 3600;

                    newRecordContent = $"{soaNs} {soaEmail} {soaSerial} {soaRefresh} {soaRetry} {soaExpire} {soaMinimumTtl}";
                }

                // ADD NEW RECORD TO THE REMAINING RECORDS
                remainingRecords.Add(new { content = newRecordContent, disabled = false });

                // SEND UPDATED RECORD SET TO POWERDNS
                var updateRecord = new
                {
                    rrsets = new[]
                    {
                        new
                        {
                            name = name,
                            type = request.Type,
                            ttl = request.Ttl ?? recordSet.Ttl,
                            changetype = "REPLACE",
                            records = remainingRecords
                        }
                    }
                };

                var json = JsonSerializer.Serialize(updateRecord, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                var requestMessage = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}")
                {
                    Headers = { { "X-API-Key", _apiKey } },
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                var response = await _httpClient.SendAsync(requestMessage);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return new JsonResult(new { success = false, message = $"ERROR UPDATING RECORD: {errorContent}" }) { StatusCode = (int)response.StatusCode };
                }

                return new JsonResult(new { success = true, message = "RECORD UPDATED SUCCESSFULLY!" });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"INTERNAL SERVER ERROR: {ex.Message}" }) { StatusCode = 500 };
            }
        }


        public async Task<IActionResult> OnPostAddSubdomainAsync([FromBody] AddSubdomainRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Subdomain))
                {
                    return new JsonResult(new { success = false, message = "Invalid subdomain name." }) { StatusCode = 400 };
                }

                string fullSubdomain = $"{request.Subdomain}.{ZoneName}";

                // Получаем IP-адрес для A-записи из конфигурации
                if (string.IsNullOrEmpty(_default_IP))
                {
                    return new JsonResult(new { success = false, message = "Default A record IP is not set in configuration." }) { StatusCode = 500 };
                }

                // 1. Получаем текущие записи зоны
                var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}");
                getRequest.Headers.Add("X-API-Key", _apiKey);

                var getResponse = await _httpClient.SendAsync(getRequest);
                if (!getResponse.IsSuccessStatusCode)
                {
                    var errorContent = await getResponse.Content.ReadAsStringAsync();
                    return new JsonResult(new { success = false, message = $"Error fetching zone records: {errorContent}" }) { StatusCode = (int)getResponse.StatusCode };
                }

                var jsonResponse = await getResponse.Content.ReadAsStringAsync();
                var zoneData = JsonSerializer.Deserialize<ZoneData>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (zoneData == null || zoneData.Rrsets == null)
                {
                    return new JsonResult(new { success = false, message = "Zone data is empty." }) { StatusCode = 500 };
                }

                // 2. Создаём A-запись для подзоны
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
                            records = new[]
                            {
                                new { content = _default_IP, disabled = false }
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(newRecord, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                var requestMessage = new HttpRequestMessage(HttpMethod.Patch, $"{_apiUrl}/api/v1/servers/localhost/zones/{ZoneName}")
                {
                    Headers = { { "X-API-Key", _apiKey } },
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                var response = await _httpClient.SendAsync(requestMessage);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return new JsonResult(new { success = false, message = $"Error adding subdomain: {errorContent}" }) { StatusCode = (int)response.StatusCode };
                }

                return new JsonResult(new { success = true, message = "Subdomain added successfully!" });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"Internal Server Error: {ex.Message}" }) { StatusCode = 500 };
            }
        }

    }
}

