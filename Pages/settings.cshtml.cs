using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using MySqlConnector;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace PowerDNS_Web.Pages
{
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Administrator")]
    public class SettingsModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SettingsModel> _logger;
        private readonly IStringLocalizer _L;

        [BindProperty]
        public AppSettingsModel Settings { get; set; }

        public SettingsModel(ILogger<SettingsModel> logger, IConfiguration configuration, IStringLocalizerFactory factory)
        {
            _logger = logger;
            _configuration = configuration;
            var asmName = Assembly.GetExecutingAssembly().GetName().Name!;
            _L = factory.Create("Pages.settings", asmName);

            // LOAD SETTINGS FROM CONFIGURATION
            Settings = new AppSettingsModel
            {
                MySQL = _configuration.GetSection("MySQLConnection").Get<MySQLConnectionSettings>() ?? new MySQLConnectionSettings(),
                PowerDNS = _configuration.GetSection("pdns").Get<PowerDNSSettings>() ?? new PowerDNSSettings(),
                Recursor = _configuration.GetSection("recursor").Get<RecursorSettings>() ?? new RecursorSettings()
            };
        }

        public void OnGet() { }

        // ===== SAVE SETTINGS =====
        public async Task<IActionResult> OnPostSaveSettings([FromBody] AppSettingsModel model)
        {
            try
            {
                if (model is null)
                    return new JsonResult(new { success = false, message = _L["Err_Save"].Value });

                // --- BASIC VALIDATION ---
                if (string.IsNullOrWhiteSpace(model.MySQL.Server) ||
                    string.IsNullOrWhiteSpace(model.MySQL.User) ||
                    string.IsNullOrWhiteSpace(model.MySQL.Database))
                {
                    return new JsonResult(new { success = false, message = _L["Err_MySQL_Required"].Value });
                }

                if (string.IsNullOrWhiteSpace(model.PowerDNS.Url) ||
                    string.IsNullOrWhiteSpace(model.PowerDNS.Api_Key))
                {
                    return new JsonResult(new { success = false, message = _L["Err_PowerDNS_Required"].Value });
                }

                // Recursor URL/API-Key требуем ТОЛЬКО если включён
                var recEnabled = string.Equals(model.Recursor.Enabled, "Enabled", StringComparison.OrdinalIgnoreCase);
                if (recEnabled && (string.IsNullOrWhiteSpace(model.Recursor.Url) || string.IsNullOrWhiteSpace(model.Recursor.Api_Key)))
                {
                    return new JsonResult(new { success = false, message = _L["Err_Recursor_Required"].Value });
                }

                // --- CHECK MYSQL CONNECTION ---
                string connectionString =
                    $"Server={model.MySQL.Server};User ID={model.MySQL.User};Password={model.MySQL.Password};Database={model.MySQL.Database};";
                try
                {
                    using var connection = new MySqlConnection(connectionString);
                    await connection.OpenAsync();
                }
                catch (MySqlException ex)
                {
                    _logger.LogError(ex, "MySQL connection error");
                    return new JsonResult(new { success = false, message = string.Format(_L["Err_MySqlConnection"].Value, ex.Message) });
                }

                // --- WRITE appsettings.json ---
                string appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                if (!System.IO.File.Exists(appSettingsPath))
                {
                    return new JsonResult(new { success = false, message = _L["Err_ConfigNotFound"].Value });
                }

                // Read current JSON
                string json = await System.IO.File.ReadAllTextAsync(appSettingsPath);
                using var jsonDoc = JsonDocument.Parse(json);
                var jsonObject = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonDoc.RootElement.ToString())
                                 ?? new Dictionary<string, object>();

                // Update sections
                jsonObject["MySQLConnection"] = model.MySQL;
                jsonObject["pdns"] = model.PowerDNS;
                jsonObject["recursor"] = model.Recursor;

                string updatedJson = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions { WriteIndented = true });

                // Write back
                await System.IO.File.WriteAllTextAsync(appSettingsPath, updatedJson);

                // --- APPLY SYSTEM CHANGES (recursor/pdns) ---
                await UpdateRecursorStatus(recEnabled);

                return new JsonResult(new { success = true, message = _L["Msg_SettingsSaved"].Value });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Settings save error");
                return new JsonResult(new { success = false, message = string.Format(_L["Err_SaveDetailed"].Value, ex.Message) });
            }
        }

        // ===== RECUSROR/PDNS SERVICE CHANGES =====
        private async Task UpdateRecursorStatus(bool enable)
        {
            try
            {
                string pdnsConfigPath = "/etc/powerdns/pdns.conf";
                string recursorService = "pdns-recursor";
                string pdnsService = "pdns";

                // Update pdns.conf local-port
                if (System.IO.File.Exists(pdnsConfigPath))
                {
                    string[] configLines = await System.IO.File.ReadAllLinesAsync(pdnsConfigPath);
                    bool touched = false;
                    for (int i = 0; i < configLines.Length; i++)
                    {
                        if (configLines[i].StartsWith("local-port=", StringComparison.OrdinalIgnoreCase))
                        {
                            configLines[i] = enable ? "local-port=5300" : "local-port=53";
                            touched = true;
                            break;
                        }
                    }
                    // если строки не было — добавим
                    if (!touched)
                    {
                        var list = configLines.ToList();
                        list.Add(enable ? "local-port=5300" : "local-port=53");
                        configLines = list.ToArray();
                    }
                    await System.IO.File.WriteAllLinesAsync(pdnsConfigPath, configLines);
                }

                if (enable)
                {
                    ExecuteBashCommand($"systemctl enable {recursorService}");
                    ExecuteBashCommand($"systemctl start {recursorService}");
                }
                else
                {
                    ExecuteBashCommand($"systemctl stop {recursorService}");
                    ExecuteBashCommand($"systemctl disable {recursorService}");
                }

                ExecuteBashCommand($"systemctl restart {pdnsService}");
                _logger.LogInformation("Recursor status updated: {Status}", enable ? "Enabled" : "Disabled");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to update recursor status: {Message}", ex.Message);
            }
        }

        private void ExecuteBashCommand(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                process!.WaitForExit();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogError("Command '{Command}' error: {Error}", command, error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to execute command '{Command}': {Message}", command, ex.Message);
            }
        }
    }

    // ===== DTOs =====

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
