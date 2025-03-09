using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PowerDNS_Web.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<ZonesModel> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _apiUrl;
        private readonly string _apiKey;

        public IndexModel(ILogger<ZonesModel> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _apiUrl = configuration["pdns:url"];
            _apiKey = configuration["pdns:api-key"];
        }

        public async Task<IActionResult> OnGetStatsAsync()
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                var response = await client.GetAsync($"{_apiUrl}/api/v1/servers/localhost/statistics");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"PowerDNS API error: {response.StatusCode} - {errorContent}");
                    return new JsonResult(new { success = false, message = $"PowerDNS API error: {errorContent}" });
                }

                var json = await response.Content.ReadAsStringAsync();
                var statsList = JsonSerializer.Deserialize<List<StatEntry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (statsList == null)
                {
                    return new JsonResult(new { success = false, message = "Stats is null" });
                }

                return new JsonResult(new
                {
                    success = true,
                    cacheHits = statsList.FirstOrDefault(s => s.Name == "cache-hits")?.GetValueAsInt() ?? 0,
                    cacheMisses = statsList.FirstOrDefault(s => s.Name == "cache-misses")?.GetValueAsInt() ?? 0,
                    uptime = statsList.FirstOrDefault(s => s.Name == "uptime")?.GetValueAsInt() ?? 0,
                    totalQueries = statsList.FirstOrDefault(s => s.Name == "udp-queries")?.GetValueAsInt() ?? 0,
                    logs = statsList.FirstOrDefault(s => s.Name == "logmessages")?.GetRawJson() ?? "[]"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception in OnGetStatsAsync: {ex.Message}");
                return new JsonResult(new { success = false, message = $"Internal server error: {ex.Message}" });
            }
        }
    }

    public class StatEntry
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public JsonElement Value { get; set; }

        public int GetValueAsInt()
        {
            if (Value.ValueKind == JsonValueKind.Number)
                return Value.GetInt32();
            if (Value.ValueKind == JsonValueKind.String && int.TryParse(Value.GetString(), out int result))
                return result;

            return 0; 
        }

        public string GetRawJson() => Value.GetRawText();
    }

}
