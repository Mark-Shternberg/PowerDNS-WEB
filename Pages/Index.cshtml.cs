using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PowerDNS_Web.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _apiUrl;
        private readonly string _apiKey;
        private readonly string _recursorUrl;
        private readonly string _recursorApiKey;
        private readonly IConfiguration _configuration;

        public IndexModel(ILogger<IndexModel> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _apiUrl = configuration["pdns:url"] ?? "";
            _apiKey = configuration["pdns:api_key"] ?? "";
            _recursorUrl = configuration["recursor:url"] ?? "";
            _recursorApiKey = configuration["recursor:api_key"] ?? "";
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

                // === FETCH AUTHORITATIVE SERVER STATS ===
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
                var authResponse = await client.GetAsync($"{_apiUrl}/api/v1/servers/localhost/statistics");
                var authStats = await DeserializeAuthStatsAsync(authResponse);

                // === FETCH RECURSOR STATS IF ENABLED ===
                Dictionary<string, int> recursorStats = new();
                List<string> topQueries = new();
                List<string> topRemotes = new();

                if (_configuration["recursor:Enabled"] == "Enabled")
                {
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("X-API-Key", _recursorApiKey);
                    var recursorResponse = await client.GetAsync($"{_recursorUrl}/api/v1/servers/localhost/statistics");
                    recursorStats = await DeserializeRecursorStatsAsync(recursorResponse);

                    topQueries = GetRecursorTopStats("rec_control top-queries");
                    topRemotes = GetRecursorTopStats("rec_control top-remotes");
                }

                return new JsonResult(new
                {
                    success = true,
                    uptime = authStats.GetValueOrDefault("uptime", 0),
                    totalQueries = authStats.GetValueOrDefault("udp-queries", 0),

                    // FETCH NOERROR, NXDOMAIN, AND QUERIES FROM AUTHORITATIVE SERVER
                    noerrorQueries = await DeserializeQueryStatsAsync(authResponse, "noerror-queries"),
                    nxdomainQueries = await DeserializeQueryStatsAsync(authResponse, "nxdomain-queries"),
                    queries = await DeserializeQueryStatsAsync(authResponse, "queries"),

                    recursorCacheHits = recursorStats.GetValueOrDefault("cache-hits", 0),
                    recursorCacheMisses = recursorStats.GetValueOrDefault("cache-misses", 0),
                    recursorUptime = recursorStats.GetValueOrDefault("uptime", 0),

                    topQueries = ParseTopStats(topQueries),
                    topRemotes = ParseTopRemotes(topRemotes)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"EXCEPTION IN OnGetStatsAsync: {ex.Message}");
                return new JsonResult(new { success = false, message = $"INTERNAL SERVER ERROR: {ex.Message}" });
            }
        }

        // === FUNCTION TO DESERIALIZE QUERY STATS FROM AUTHORITATIVE SERVER ===
        private async Task<List<QueryDetail>> DeserializeQueryStatsAsync(HttpResponseMessage response, string statName)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"API ERROR: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                return new List<QueryDetail>();
            }

            var json = await response.Content.ReadAsStringAsync();
            try
            {
                var statsList = JsonSerializer.Deserialize<List<StatEntry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var statEntry = statsList?.FirstOrDefault(s => s.Name == statName);

                if (statEntry != null && statEntry.Value.ValueKind == JsonValueKind.Array)
                {
                    var queryDetails = JsonSerializer.Deserialize<List<QueryDetail>>(statEntry.Value.GetRawText(), new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (queryDetails != null)
                    {
                        return queryDetails;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"JSON DESERIALIZATION ERROR FOR {statName}: {ex.Message}");
            }

            return new List<QueryDetail>();
        }

        // === FUNCTION TO DESERIALIZE AUTHORITATIVE SERVER STATS ===
        private async Task<Dictionary<string, int>> DeserializeAuthStatsAsync(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"AUTH SERVER API ERROR: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                return new Dictionary<string, int>();
            }

            var json = await response.Content.ReadAsStringAsync();
            try
            {
                var statsList = JsonSerializer.Deserialize<List<StatEntry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return statsList?.ToDictionary(stat => stat.Name, stat => stat.GetValueAsInt()) ?? new Dictionary<string, int>();
            }
            catch (Exception ex)
            {
                _logger.LogError($"AUTH SERVER JSON DESERIALIZATION ERROR: {ex.Message}");
                return new Dictionary<string, int>();
            }
        }

        // === FUNCTION TO DESERIALIZE RECURSOR STATS ===
        private async Task<Dictionary<string, int>> DeserializeRecursorStatsAsync(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"RECURSOR API ERROR: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                return new Dictionary<string, int>();
            }

            var json = await response.Content.ReadAsStringAsync();
            try
            {
                var statsList = JsonSerializer.Deserialize<List<StatEntry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return statsList?.ToDictionary(stat => stat.Name, stat => stat.GetValueAsInt()) ?? new Dictionary<string, int>();
            }
            catch (Exception ex)
            {
                _logger.LogError($"RECURSOR JSON DESERIALIZATION ERROR: {ex.Message}");
                return new Dictionary<string, int>();
            }
        }

        // === FUNCTION TO FETCH TOP QUERIES AND REMOTES FROM RECURSOR ===
        private List<string> GetRecursorTopStats(string command)
        {
            List<string> result = new();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        while (!process.StandardOutput.EndOfStream)
                        {
                            string line = process.StandardOutput.ReadLine();
                            if (!string.IsNullOrEmpty(line))
                            {
                                result.Add(line.Trim());
                            }
                        }

                        process.WaitForExit();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"FAILED TO EXECUTE COMMAND '{command}': {ex.Message}");
            }

            return result;
        }

        // === PARSE TOP QUERIES AND REMOTES FROM RECURSOR ===
        private List<QueryDetail> ParseTopStats(List<string> rawData)
        {
            var result = new List<QueryDetail>();

            if (rawData == null || rawData.Count == 0)
                return result;

            foreach (var line in rawData.Skip(1))
            {
                if (line.Contains("rest")) continue;

                var parts = line.Split('\t');
                if (parts.Length < 2) continue;

                var percentagePart = parts[0].TrimEnd('%');
                var queryData = parts[1].Split('|');

                if (queryData.Length < 2) continue;

                if (double.TryParse(percentagePart, out double percentage))
                {
                    result.Add(new QueryDetail
                    {
                        Name = queryData[0].Trim(),
                        Value = $"{percentage}% ({queryData[1]})"
                    });
                }
            }

            return result;
        }

        private List<QueryDetail> ParseTopRemotes(List<string> rawData)
        {
            var result = new List<QueryDetail>();

            if (rawData == null || rawData.Count == 0)
                return result;

            foreach (var line in rawData.Skip(1))
            {
                if (line.Contains("rest")) continue;

                var parts = line.Split('\t');
                if (parts.Length < 2) continue;

                var percentagePart = parts[0].TrimEnd('%');
                var ipAddress = parts[1].Trim();

                if (double.TryParse(percentagePart, out double percentage))
                {
                    result.Add(new QueryDetail
                    {
                        Name = ipAddress,
                        Value = $"{percentage}%"
                    });
                }
            }

            return result;
        }
    }

    public class QueryDetail
    {
        public string Name { get; set; }
        public string Value { get; set; }
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
                Console.WriteLine($"ERROR PARSING VALUE FOR {Name}: {ex.Message}");
            }
            return 0;
        }
    }
}
