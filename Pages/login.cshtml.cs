using System.Data;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BCrypt.Net;
using MySqlConnector;
using System.Reflection;

namespace PowerDNS_Web.Pages
{
    public class loginModel : PageModel
    {
        private readonly ILogger<loginModel> _logger;
        private readonly IConfiguration _configuration;

        public loginModel(ILogger<loginModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        private string SqlConnection()
        {
            var server = _configuration["MySQLConnection:server"];
            var user = _configuration["MySQLConnection:user"];
            var password = _configuration["MySQLConnection:password"];
            var database = _configuration["MySQLConnection:database"];

            return $"Server={server};User ID={user};Password={password};Database={database}";
        }

        public class UserLogin
        {
            public string? username { get; set; }
            public string? password { get; set; }
        }

        public async Task OnGet()
        {
            CheckDTExist();
            ViewData["SettingsCheck"] = (await CheckSettingsExist()).ToString();
        }

        private void CheckDTExist()
        {
            try
            {
                using var connection = new MySqlConnection(SqlConnection());
                connection.Open();

                using var check_table = new MySqlCommand("SHOW TABLES LIKE 'users'", connection);

                using var create_users = new MySqlCommand("CREATE TABLE `users` (" +
                  "`id` INT NOT NULL AUTO_INCREMENT," +
                  "`username` TEXT NOT NULL," +
                  "`role` TEXT NOT NULL," +
                  "`password` LONGTEXT NOT NULL," +
                  "PRIMARY KEY(`id`)," +
                  "UNIQUE INDEX `id_UNIQUE` (`id` ASC) VISIBLE)", connection);

                using var reader_users = check_table.ExecuteReader();
                if (!reader_users.HasRows)
                {
                    reader_users.Close();
                    create_users.Prepare();
                    create_users.ExecuteNonQuery();
                }
                else reader_users.Close();
            }
            catch (MySqlException ex)
            {
                // Получаем код ошибки и её текст от MySQL
                _logger.LogError(ex.Message, "Error occurred while send mysql command");
                Console.WriteLine($"MySQL Error Code: {ex.Number}");
                Console.WriteLine($"Error Message: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private async Task<bool> CheckSettingsExist()
        {
            try
            {
                string connectionString = SqlConnection();
                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Database connection failed: {ex.Message}");
                return false;
            }
        }

        public async Task<IActionResult> OnPostLogin([FromBody] UserLogin loginRequest)
        {
            if (string.IsNullOrWhiteSpace(loginRequest.username) || string.IsNullOrWhiteSpace(loginRequest.password))
            {
                return new JsonResult(new { success = false, message = "Логин и пароль обязательны." });
            }

            string username = loginRequest.username;
            string password = loginRequest.password;

            try
            {
                // Подключаемся к базе данных
                using (var connection = new MySqlConnection(SqlConnection()))
                {
                    await connection.OpenAsync();

                    // Проверяем количество пользователей в базе данных
                    using (var countCommand = new MySqlCommand("SELECT COUNT(*) FROM users", connection))
                    {
                        var userCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

                        if (userCount == 0)
                        {
                            // Если пользователей нет, создаем первого пользователя
                            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);
                            string role = "Administrator"; // Роль для первого пользователя

                            // Добавляем первого пользователя
                            using (var insertCommand = new MySqlCommand("INSERT INTO users (username, password, role) VALUES (@username, @password, @role)", connection))
                            {
                                insertCommand.Parameters.AddWithValue("@username", username);
                                insertCommand.Parameters.AddWithValue("@password", hashedPassword);
                                insertCommand.Parameters.AddWithValue("@role", role);

                                await insertCommand.ExecuteNonQueryAsync();
                            }

                            // Создаем claims для нового пользователя
                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.Name, username),
                                new Claim(ClaimTypes.Role, role)
                            };

                            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                            var authProperties = new AuthenticationProperties { IsPersistent = true };

                            // Авторизуем нового пользователя
                            await HttpContext.SignInAsync(
                                CookieAuthenticationDefaults.AuthenticationScheme,
                                new ClaimsPrincipal(claimsIdentity),
                                authProperties
                            );

                            return new JsonResult(new { success = true, message = "Пользователь был создан и авторизован." });
                        }
                    }

                    // Если пользователей в базе больше одного, проверяем существование введенного пользователя
                    using (var command = new MySqlCommand("SELECT username, password, role FROM users WHERE username = @username", connection))
                    {
                        command.Parameters.AddWithValue("@username", username);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // Если пользователь найден, проверяем пароль
                                string storedPasswordHash = reader.GetString("password");
                                string role = reader.GetString("role");

                                if (BCrypt.Net.BCrypt.Verify(password, storedPasswordHash))
                                {
                                    // Создаем claims для аутентификации
                                    var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.Name, username),
                                new Claim(ClaimTypes.Role, role)
                            };

                                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                                    var authProperties = new AuthenticationProperties { IsPersistent = true };

                                    // Авторизуем пользователя
                                    await HttpContext.SignInAsync(
                                        CookieAuthenticationDefaults.AuthenticationScheme,
                                        new ClaimsPrincipal(claimsIdentity),
                                        authProperties
                                    );

                                    return new JsonResult(new { success = true });
                                }
                            }
                            else
                            {
                                return new JsonResult(new { success = false, message = "Неверный логин или пароль." });
                            }
                        }
                    }
                }
            }
            catch (MySqlException ex)
            {
                // Логирование ошибок от MySQL
                _logger.LogError(ex.Message, "Error occurred while sending MySQL command");
                return new JsonResult(new { success = false, message = "Ошибка базы данных: " + ex.Message });
            }
            catch (Exception ex)
            {
                // Логирование общих ошибок
                _logger.LogError(ex.Message, "General error");
                return new JsonResult(new { success = false, message = "Ошибка: " + ex.Message });
            }

            return new JsonResult(new { success = false, message = "Неверный логин или пароль." });
        }

    }
}
