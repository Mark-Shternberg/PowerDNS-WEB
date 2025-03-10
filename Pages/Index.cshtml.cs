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
        private readonly string _recursorUrl;
        private readonly string _recursorApiKey;
        private readonly IConfiguration _configuration;

        public IndexModel(ILogger<ZonesModel> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _apiUrl = configuration["pdns:url"];
            _apiKey = configuration["pdns:api_key"];

            _recursorUrl = configuration["recursor:url"];
            _recursorApiKey = configuration["recursor:api_key"];
        }


        public void OnGet()
        {
            ViewData["RecursorEnabled"] = _configuration["recursor:Enabled"] ?? "Disabled";
        }

        public async Task<IActionResult> OnGetStatsAsync()
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();

                // GET AUTHORITATIVE SERVER STATS
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
                var authResponse = await client.GetAsync($"{_apiUrl}/api/v1/servers/localhost/statistics");
                var authStats = await DeserializeStatsAsync(authResponse);

                // GET RECURSOR STATS
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("X-API-Key", _recursorApiKey);
                var recursorResponse = await client.GetAsync($"{_recursorUrl}/api/v1/servers/localhost/statistics");
                var recursorStats = await DeserializeStatsAsync(recursorResponse);

                return new JsonResult(new
                {
                    success = true,
                    cacheHits = authStats.GetValueOrDefault("cache-hits", 0),
                    cacheMisses = authStats.GetValueOrDefault("cache-misses", 0),
                    uptime = authStats.GetValueOrDefault("uptime", 0),
                    totalQueries = authStats.GetValueOrDefault("udp-queries", 0),

                    recursorCacheHits = recursorStats.GetValueOrDefault("cache-hits", 0),
                    recursorCacheMisses = recursorStats.GetValueOrDefault("cache-misses", 0),
                    recursorUptime = recursorStats.GetValueOrDefault("uptime", 0),
                    recursorQueries = recursorStats.GetValueOrDefault("udp-queries", 0)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception in OnGetStatsAsync: {ex.Message}");
                return new JsonResult(new { success = false, message = $"Internal server error: {ex.Message}" });
            }
        }

        private async Task<Dictionary<string, int>> DeserializeStatsAsync(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"API Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                return new Dictionary<string, int>();
            }

            var json = await response.Content.ReadAsStringAsync();
            var statsList = JsonSerializer.Deserialize<List<StatEntry>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return statsList?.ToDictionary(stat => stat.Name, stat => stat.GetValueAsInt()) ?? new Dictionary<string, int>();
        }
    }

    public class StatEntry
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public JsonElement Value { get; set; }

        public int GetValueAsInt()
        {
            try
            {
                if (Value.ValueKind == JsonValueKind.Number)
                    return Value.GetInt32();
                if (Value.ValueKind == JsonValueKind.String && int.TryParse(Value.GetString(), out int result))
                    return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing value for {Name}: {ex.Message}");
            }
            return 0;
        }
    }

}
