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

        public ZonePageModel(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _apiUrl = configuration["pdns:url"];
            _apiKey = configuration["pdns:api-key"];
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
        }


        public async Task<IActionResult> OnPostAddRecordAsync()
        {
            try
            {
                string requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
                Console.WriteLine($"Received JSON: {requestBody}");

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

                // ----------------------------------------

                // 2. Находим текущие записи для данного имени и типа
                var recordSet = zoneData.Rrsets.FirstOrDefault(r => r.Name == name && r.Type == request.Type);
                if (recordSet == null || recordSet.Records == null || recordSet.Records.Count == 0)
                {
                    return new JsonResult(new { success = false, message = "Record not found." }) { StatusCode = 404 };
                }

                // ----------------------------------------

                // 3. Удаляем только старую запись, оставляя остальные
                var remainingRecords = recordSet.Records
                    .Where(r => r.Content != request.OldValue)
                    .Select(r => new { content = r.Content, disabled = false })
                    .ToList();

                // ----------------------------------------

                // 4. Формируем новое значение
                string newRecordContent = request.Value.TrimEnd('.');

                if (request.Type == "MX")
                {
                    int mxPriority = request.MxPriority ?? 10;
                    string[] parts = request.Value.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    string mailServer = parts.Length > 1 ? parts.Last() : parts.First();
                    newRecordContent = $"{mxPriority} {mailServer.TrimEnd('.')}.";
                }
                else if (request.Type == "SRV")
                {
                    int srvPriority = request.SrvPriority ?? 0;
                    int srvWeight = request.SrvWeight ?? 0;
                    int srvPort = request.SrvPort ?? 0;

                    string[] parts = request.Value.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    string targetServer = parts.Length > 3 ? parts.Last() : parts.First();

                    newRecordContent = $"{srvPriority} {srvWeight} {srvPort} {targetServer.TrimEnd('.')}.";
                }

                // ----------------------------------------

                // Добавляем новую запись в список
                remainingRecords.Add(new { content = newRecordContent, disabled = false });
                // ----------------------------------------

                // 5. Отправляем обновленный список записей в PowerDNS
                var updateRecord = new
                {
                    rrsets = new[]
                    {
                        new
                        {
                            name = name,
                            type = request.Type,
                            ttl = request.Ttl ?? recordSet.Ttl, // Используем новый TTL или старый
                            changetype = "REPLACE",
                            records = remainingRecords // Передаём оставшиеся + новую запись
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
                    return new JsonResult(new { success = false, message = $"Error updating record: {errorContent}" }) { StatusCode = (int)response.StatusCode };
                }

                return new JsonResult(new { success = true, message = "Record updated successfully!" });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"Internal Server Error: {ex.Message}" }) { StatusCode = 500 };
            }
        }

    }
}

