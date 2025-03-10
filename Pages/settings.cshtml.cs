using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;
using System.Diagnostics;
using System.Text.Json;

namespace PowerDNS_Web.Pages
{
    [Authorize(Roles = "Administrator")]
    public class SettingsModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SettingsModel> _logger;

        [BindProperty]
        public AppSettingsModel Settings { get; set; }

        public SettingsModel(ILogger<SettingsModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // LOAD SETTINGS FROM CONFIGURATION
            Settings = new AppSettingsModel
            {
                MySQL = _configuration.GetSection("MySQLConnection").Get<MySQLConnectionSettings>() ?? new MySQLConnectionSettings(),
                PowerDNS = _configuration.GetSection("pdns").Get<PowerDNSSettings>() ?? new PowerDNSSettings(),
                Recursor = _configuration.GetSection("recursor").Get<RecursorSettings>() ?? new RecursorSettings()
            };
        }

        public void OnGet()
        {
        }

        // SAVE SETTINGS
        public async Task<IActionResult> OnPostSaveSettings([FromBody] AppSettingsModel model)
        {
            try
            {
                // VALIDATE INPUT DATA
                if (string.IsNullOrWhiteSpace(model.MySQL.Server) ||
                    string.IsNullOrWhiteSpace(model.MySQL.User) ||
                    string.IsNullOrWhiteSpace(model.MySQL.Database) ||
                    string.IsNullOrWhiteSpace(model.PowerDNS.Url) ||
                    string.IsNullOrWhiteSpace(model.PowerDNS.Api_Key) ||
                    string.IsNullOrWhiteSpace(model.Recursor.Url) ||
                    string.IsNullOrWhiteSpace(model.Recursor.Api_Key) ||
                    string.IsNullOrWhiteSpace(model.Recursor.Enabled))
                {
                    return new JsonResult(new { success = false, message = "All fields must be filled!" });
                }

                // CHECK CONNECTION TO MYSQL
                string connectionString = $"Server={model.MySQL.Server};User ID={model.MySQL.User};Password={model.MySQL.Password};Database={model.MySQL.Database};";
                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                }

                // PATH TO APPSETTINGS.JSON
                string appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

                if (!System.IO.File.Exists(appSettingsPath))
                {
                    return new JsonResult(new { success = false, message = "Configuration file not found." });
                }

                // LOAD CURRENT SETTINGS
                string json = await System.IO.File.ReadAllTextAsync(appSettingsPath);
                var jsonDoc = JsonDocument.Parse(json);
                var jsonObject = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonDoc.RootElement.ToString()) ?? new Dictionary<string, object>();

                // UPDATE SETTINGS
                jsonObject["MySQLConnection"] = model.MySQL;
                jsonObject["pdns"] = model.PowerDNS;
                jsonObject["recursor"] = model.Recursor;

                string updatedJson = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions { WriteIndented = true });

                // WRITE CHANGES
                await System.IO.File.WriteAllTextAsync(appSettingsPath, updatedJson);

                // UPDATE SYSTEM SERVICES AND CONFIGURATION
                bool recursorEnabled = model.Recursor.Enabled.Equals("Enabled", StringComparison.OrdinalIgnoreCase);
                await UpdateRecursorStatus(recursorEnabled);

                return new JsonResult(new { success = true, message = "Settings saved successfully!" });
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "MySQL connection error");
                return new JsonResult(new { success = false, message = "MySQL connection error: " + ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Settings save error");
                return new JsonResult(new { success = false, message = "Save settings error: " + ex.Message });
            }
        }

        private async Task UpdateRecursorStatus(bool enable)
        {
            try
            {
                string pdnsConfigPath = "/etc/powerdns/pdns.conf";
                string recursorService = "pdns-recursor";
                string pdnsService = "pdns";

                // UPDATE pdns.conf
                if (System.IO.File.Exists(pdnsConfigPath))
                {
                    string[] configLines = await System.IO.File.ReadAllLinesAsync(pdnsConfigPath);
                    for (int i = 0; i < configLines.Length; i++)
                    {
                        if (configLines[i].StartsWith("local-port="))
                        {
                            configLines[i] = enable ? "local-port=5300" : "local-port=53";
                        }
                    }
                    await System.IO.File.WriteAllLinesAsync(pdnsConfigPath, configLines);
                }

                if (enable)
                {
                    // ENABLE AND START RECURSOR
                    ExecuteBashCommand($"systemctl enable {recursorService}");
                    ExecuteBashCommand($"systemctl start {recursorService}");
                }
                else
                {
                    // STOP AND DISABLE RECURSOR
                    ExecuteBashCommand($"systemctl stop {recursorService}");
                    ExecuteBashCommand($"systemctl disable {recursorService}");
                }

                // RESTART POWERDNS
                ExecuteBashCommand($"systemctl restart {pdnsService}");

                _logger.LogInformation($"Recursor status updated: {(enable ? "Enabled" : "Disabled")}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to update recursor status: {ex.Message}");
            }
        }

        private void ExecuteBashCommand(string command)
        {
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
                    process.WaitForExit();
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrEmpty(error))
                    {
                        _logger.LogError($"Command '{command}' error: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to execute command '{command}': {ex.Message}");
            }
        }
    }

    public class AppSettingsModel
    {
        public MySQLConnectionSettings MySQL { get; set; } = new();
        public PowerDNSSettings PowerDNS { get; set; } = new();
        public RecursorSettings Recursor { get; set; } = new();
    }

    public class MySQLConnectionSettings
    {
        public string Server { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
    }

    public class PowerDNSSettings
    {
        public string Url { get; set; } = string.Empty;
        public string Api_Key { get; set; } = string.Empty;
        public string Default_A { get; set; } = string.Empty;
        public SOASettings SOA { get; set; } = new();
    }

    public class SOASettings
    {
        public string Ns { get; set; } = string.Empty;
        public string Mail { get; set; } = string.Empty;
    }

    public class RecursorSettings
    {
        public string Url { get; set; } = string.Empty;
        public string Api_Key { get; set; } = string.Empty;
        public string Enabled { get; set; } = "Disabled"; 
    }
}
