using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Text;

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
                Console.WriteLine("try");
                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

                var response = await client.GetAsync(apiUrl);
                Console.WriteLine(response);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Console.WriteLine(json);
                    Zones = JsonSerializer.Deserialize<List<DnsZone>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<DnsZone>();
                }
                else
                {
                    _logger.LogError($"PowerDNS API вернул ошибку: {response.StatusCode}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"Ошибка подключения к PowerDNS API: {ex.Message}");
            }
        }

        public class DnsZone
        {
            public string name { get; set; } = string.Empty;
            public string kind { get; set; } = string.Empty;
            public bool dnssec { get; set; }
            public long serial { get; set; }
        }

        public async Task<IActionResult> OnPostAddZone([FromBody] DnsZone zone)
        {
            var apiUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones";
            var apiKey = _configuration["pdns:api-key"];

            zone.name = zone.name.EndsWith(".") ? zone.name : zone.name + ".";

            var content = new StringContent(JsonSerializer.Serialize(zone), Encoding.UTF8, "application/json");

            Console.WriteLine(await content.ReadAsStringAsync());

            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

            var response = await client.PostAsync(apiUrl, content);

            var jsonResponse = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                return new JsonResult(new { success = true });
            }
            else
            {
                _logger.LogError($"Ошибка API: {jsonResponse}");
                return new JsonResult(new { success = false, message = jsonResponse });
            }
        }


        [HttpPost]
        public async Task<IActionResult> OnPostEditZoneAsync([FromBody] DnsZone zone)
        {
            var apiUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones/{zone.name}";
            var apiKey = _configuration["pdns:api-key"];

            var content = new StringContent(JsonSerializer.Serialize(zone), Encoding.UTF8, "application/json");

            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

            var response = await client.PutAsync(apiUrl, content);

            return response.IsSuccessStatusCode ? new JsonResult(new { success = true }) : new JsonResult(new { success = false });
        }

        [HttpPost]
        public async Task<IActionResult> OnPostDeleteZoneAsync([FromBody] DnsZone zone)
        {
            var apiUrl = $"{_configuration["pdns:url"]}/api/v1/servers/localhost/zones/{zone.name}";
            var apiKey = _configuration["pdns:api-key"];

            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

            var response = await client.DeleteAsync(apiUrl);

            return response.IsSuccessStatusCode ? new JsonResult(new { success = true }) : new JsonResult(new { success = false });
        }
    }
}
