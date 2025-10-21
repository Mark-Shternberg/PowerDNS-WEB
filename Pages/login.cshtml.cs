using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySqlConnector;

namespace PowerDNS_Web.Pages
{
    public class loginModel : PageModel
    {
        private readonly ILogger<loginModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly Functions _functions;

        public loginModel(ILogger<loginModel> logger, Functions functions, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _functions = functions;
        }

        public class UserLogin
        {
            public string? username { get; set; }
            public string? password { get; set; }
            public string? returnUrl { get; set; }
        }

        public async Task OnGet()
        {
            ViewData["SettingsCheck"] = (await CheckSettingsExist()).ToString();
        }

        private async Task<bool> CheckSettingsExist()
        {
            try
            {
                using var connection = new MySqlConnection(_functions.sql_connection());
                await connection.OpenAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection failed");
                return false;
            }
        }

        public async Task<IActionResult> OnPostLogin([FromBody] UserLogin loginRequest)
        {
            if (loginRequest == null ||
                string.IsNullOrWhiteSpace(loginRequest.username) ||
                string.IsNullOrWhiteSpace(loginRequest.password))
            {
                return new JsonResult(new { success = false, message = "Логин и пароль обязательны." });
            }

            var username = loginRequest.username.Trim();
            var password = loginRequest.password;
            var desired = loginRequest.returnUrl;
            var redirect = Url.IsLocalUrl(desired) ? desired : Url.Content("~/");

            try
            {
                using var connection = new MySqlConnection(_functions.sql_connection());
                await connection.OpenAsync();

                // 1) Если пользователей нет — создаём первого админа
                using (var countCommand = new MySqlCommand("SELECT COUNT(*) FROM users", connection))
                {
                    var userCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
                    if (userCount == 0)
                    {
                        var hashed = BCrypt.Net.BCrypt.HashPassword(password);
                        using var insert = new MySqlCommand(
                            "INSERT INTO users (username, password, role) VALUES (@u, @p, @r)", connection);
                        insert.Parameters.AddWithValue("@u", username);
                        insert.Parameters.AddWithValue("@p", hashed);
                        insert.Parameters.AddWithValue("@r", "Administrator");
                        await insert.ExecuteNonQueryAsync();

                        await SignInAsync(username, "Administrator");
                        return new JsonResult(new { success = true, message = "Пользователь создан и авторизован.", redirect });
                    }
                }

                // 2) Обычная авторизация
                using var cmd = new MySqlCommand(
                    "SELECT username, password, role FROM users WHERE username = @u LIMIT 1", connection);
                cmd.Parameters.AddWithValue("@u", username);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var hash = reader.GetString("password");
                    var role = reader.GetString("role");
                    if (BCrypt.Net.BCrypt.Verify(password, hash))
                    {
                        await SignInAsync(username, role);
                        return new JsonResult(new { success = true, redirect });
                    }
                }

                return new JsonResult(new { success = false, message = "Неверный логин или пароль." });
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "MySQL error during login");
                return new JsonResult(new { success = false, message = "Ошибка базы данных." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General error during login");
                return new JsonResult(new { success = false, message = "Внутренняя ошибка." });
            }
        }

        private async Task SignInAsync(string username, string role)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            // долговременная кука (можете добавить ExpiresUtc и т.п.)
            var props = new AuthenticationProperties { IsPersistent = true };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                props
            );
        }
    }
}
