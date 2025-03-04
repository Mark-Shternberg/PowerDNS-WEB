using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using System.Text.Json;

namespace PowerDNS_Web.Pages
{
    [Authorize(Roles = "Administrator")]
    public class settingsModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly IConfigurationSection _mysqlSection;
        private readonly ILogger<IndexModel> _logger;

        [BindProperty]
        public MySQLConnectionSettings MySQLConnection { get; set; }

        public settingsModel(ILogger<IndexModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _mysqlSection = _configuration.GetSection("MySQLConnection");
            MySQLConnection = _mysqlSection.Get<MySQLConnectionSettings>();
        }

        public void OnGet()
        {
            // Эта часть уже выполнена в конструкторе, MySQLConnection будет заполнен при запросе страницы.
        }

        public IActionResult OnPost()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Сохраняем изменения в конфигурации (например, записать обратно в appsettings.json, если необходимо)
            // Это нужно будет настроить через механизм хранения настроек, например, в базе данных или другом хранилище.

            // Если все хорошо, возвращаем страницу с сообщением об успешном обновлении.
            return RedirectToPage("Settings");
        }

        // SAVE SETTINGS
        public async Task<IActionResult> OnPostSave_settings([FromBody] MySQLConnectionSettings model)
        {
            try
            {
                // Проверяем подключение к MySQL
                var connectionString = $"Server={model.Server};User ID={model.User};Password={model.Password};Database={model.Database};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync(); // Проверяем подключение
                }

                // Путь к файлу конфигурации
                string appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

                if (!System.IO.File.Exists(appSettingsPath))
                {
                    return new JsonResult(new { success = false, message = "Файл конфигурации не найден." });
                }

                // Загружаем текущие настройки
                string json = await System.IO.File.ReadAllTextAsync(appSettingsPath);
                var jsonDoc = JsonDocument.Parse(json);
                var jsonObject = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonDoc.RootElement.ToString());

                // Обновляем секцию MySQL
                if (jsonObject.ContainsKey("MySQLConnection"))
                {
                    jsonObject["MySQLConnection"] = model;
                }
                else
                {
                    jsonObject.Add("MySQLConnection", model);
                }

                // Сериализуем обратно в JSON
                string updatedJson = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions { WriteIndented = true });

                // Записываем обратно в файл
                await System.IO.File.WriteAllTextAsync(appSettingsPath, updatedJson);

                return new JsonResult(new { success = true, message = "Настройки сохранены!" });
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "Ошибка подключения к MySQL");
                return new JsonResult(new { success = false, message = "Невозможно подключиться к базе" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сохранении настроек");
                return new JsonResult(new { success = false, message = "Ошибка при сохранении настроек" });
            }
        }

    }

    public class MySQLConnectionSettings
    {
        public string Server { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string Database { get; set; }
    }

}
