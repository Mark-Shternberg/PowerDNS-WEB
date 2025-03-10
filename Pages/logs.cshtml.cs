using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PowerDNS_Web.Pages
{
    public class logsModel : PageModel
    {
        private readonly ILogger<logsModel> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _pdnsUrl;
        private readonly string _pdnsApiKey;

        public logsModel(ILogger<logsModel> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _pdnsUrl = configuration["pdns:url"];
            _pdnsApiKey = configuration["pdns:api_key"];
        }

        public async Task<IActionResult> OnGetLogsAsync()
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();

                // GET POWERDNS AUTHORITATIVE LOGS
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("X-API-Key", _pdnsApiKey);
                var authResponse = await client.GetAsync($"{_pdnsUrl}/api/v1/servers/localhost/statistics");
                var authLogs = await DeserializeLogsAsync(authResponse);

                return new JsonResult(new
                {
                    success = true,
                    authoritativeLogs = authLogs
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception in OnGetLogsAsync: {ex.Message}");
                return new JsonResult(new { success = false, message = $"Internal server error: {ex.Message}" });
            }
        }

        private async Task<string> DeserializeLogsAsync(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to fetch logs: {errorMessage}");
                return "Failed to fetch logs.";
            }

            var json = await response.Content.ReadAsStringAsync();
            var statsList = JsonSerializer.Deserialize<List<LogEntry>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var logs = statsList?.FirstOrDefault(s => s.Name == "logmessages")?.GetRawJson() ?? "No logs available.";
            return logs.Replace("[", "").Replace("]", "").Replace("{", "").Replace("}", "").Replace("\"", "").Replace(",", "\n");
        }
    }

    public class LogEntry
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public JsonElement Value { get; set; }

        public string GetRawJson() => Value.GetRawText();
    }
}
