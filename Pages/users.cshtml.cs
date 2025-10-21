using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using MySqlConnector;
using Newtonsoft.Json;
using System.Data;

namespace PowerDNS_Web.Pages
{
    [Authorize(Roles = "Administrator")]
    public class userModel : PageModel
    {
        private readonly ILogger<userModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly IStringLocalizer<userModel> _L;

        public userModel(ILogger<userModel> logger, IConfiguration configuration, IStringLocalizer<userModel> localizer)
        {
            _logger = logger;
            _configuration = configuration;
            _L = localizer;
        }

        private string SqlConnection()
        {
            var server = _configuration["MySQLConnection:server"];
            var user = _configuration["MySQLConnection:user"];
            var password = _configuration["MySQLConnection:password"];
            var database = _configuration["MySQLConnection:database"];
            return $"Server={server};User ID={user};Password={password};Database={database}";
        }

        public class NewUser
        {
            public string? username { get; set; }
            public string? role { get; set; }
            public string? password { get; set; }
        }

        public class Row
        {
            public string? username { get; set; }
            public string? role { get; set; }
            public string? password { get; set; }
        }

        public List<Row>? main_table { get; set; }

        public void OnGet()
        {
            try
            {
                using var connection = new MySqlConnection(SqlConnection());
                connection.Open();

                const string checkAdminCountQuery = "SELECT COUNT(*) FROM users WHERE role = 'Administrator'";
                using var checkCommand = new MySqlCommand(checkAdminCountQuery, connection);
                var adminCount = Convert.ToInt32(checkCommand.ExecuteScalar());
                ViewData["AdminCount"] = adminCount;
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "MySQL error in OnGet()");
            }

            LoadMainTable();
        }

        private void LoadMainTable()
        {
            using var connection = new MySqlConnection(SqlConnection());
            connection.Open();

            using var command = new MySqlCommand("SELECT username, role FROM users ORDER BY id DESC", connection);
            using var reader = command.ExecuteReader();

            if (reader.HasRows)
            {
                var dt = new DataTable();
                dt.Load(reader);

                var json = JsonConvert.SerializeObject(dt);
                main_table = JsonConvert.DeserializeObject<List<Row>>(json);
            }
            else
            {
                main_table = null;
            }
        }

        // ============= ADD NEW USER =============
        public async Task<IActionResult> OnPostAdd_new_user([FromBody] NewUser model)
        {
            try
            {
                if (model == null ||
                    string.IsNullOrWhiteSpace(model.username) ||
                    string.IsNullOrWhiteSpace(model.role) ||
                    string.IsNullOrWhiteSpace(model.password))
                {
                    return new JsonResult(new { success = false, message = _L["UM_Back_InvalidRequest"] });
                }

                await using var connection = new MySqlConnection(SqlConnection());
                await connection.OpenAsync();

                // duplicates
                const string checkQuery = "SELECT COUNT(*) FROM users WHERE username = @username";
                await using (var checkCmd = new MySqlCommand(checkQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@username", model.username.Trim());
                    var cnt = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (cnt > 0)
                    {
                        return new JsonResult(new { success = false, message = _L["UM_Back_UserExists"] });
                    }
                }

                // insert
                var hashed = BCrypt.Net.BCrypt.HashPassword(model.password);
                const string insertQuery = "INSERT INTO users (username, role, password) VALUES (@username, @role, @password)";
                await using (var insert = new MySqlCommand(insertQuery, connection))
                {
                    insert.Parameters.AddWithValue("@username", model.username.Trim());
                    insert.Parameters.AddWithValue("@role", model.role.Trim());
                    insert.Parameters.AddWithValue("@password", hashed);
                    await insert.ExecuteNonQueryAsync();
                }

                return new JsonResult(new { success = true });
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "MySQL error on Add user");
                return new JsonResult(new { success = false, message = string.Format(_L["UM_Back_SqlError"], ex.Message) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General error on Add user");
                return new JsonResult(new { success = false, message = string.Format(_L["UM_Back_Error"], ex.Message) });
            }
        }

        // ============= UPDATE USER =============
        public async Task<IActionResult> OnPostUpdate_user([FromBody] NewUser model)
        {
            try
            {
                if (model == null || string.IsNullOrWhiteSpace(model.username) || string.IsNullOrWhiteSpace(model.role))
                {
                    return new JsonResult(new { success = false, message = _L["UM_Back_InvalidRequest"] });
                }

                var updatePassword = !string.IsNullOrEmpty(model.password);

                string sql = updatePassword
                    ? "UPDATE users SET role=@role, password=@password WHERE username=@username"
                    : "UPDATE users SET role=@role WHERE username=@username";

                await using var connection = new MySqlConnection(SqlConnection());
                await connection.OpenAsync();

                await using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@username", model.username.Trim());
                cmd.Parameters.AddWithValue("@role", model.role.Trim());

                if (updatePassword)
                {
                    var hashed = BCrypt.Net.BCrypt.HashPassword(model.password);
                    cmd.Parameters.AddWithValue("@password", hashed);
                }

                await cmd.ExecuteNonQueryAsync();
                return new JsonResult(new { success = true });
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "MySQL error on Update user");
                return new JsonResult(new { success = false, message = string.Format(_L["UM_Back_SqlError"], ex.Message) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General error on Update user");
                return new JsonResult(new { success = false, message = string.Format(_L["UM_Back_Error"], ex.Message) });
            }
        }

        // ============= DELETE USER =============
        public async Task<IActionResult> OnPostDelete_user([FromBody] Row model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.username))
                return new JsonResult(new { success = false, message = _L["UM_Back_InvalidRequest"] });

            try
            {
                const string checkAdminCountQuery = "SELECT COUNT(*) FROM users WHERE role = 'Administrator'";
                await using var connection = new MySqlConnection(SqlConnection());
                await connection.OpenAsync();

                // check admins count
                int adminCount;
                await using (var checkCount = new MySqlCommand(checkAdminCountQuery, connection))
                {
                    adminCount = Convert.ToInt32(await checkCount.ExecuteScalarAsync());
                    ViewData["AdminCount"] = adminCount;
                }

                if (adminCount <= 1)
                {
                    // if only one admin exists, forbid deleting that admin
                    const string checkIfAdminSql = "SELECT role FROM users WHERE username = @username";
                    await using var checkRole = new MySqlCommand(checkIfAdminSql, connection);
                    checkRole.Parameters.AddWithValue("@username", model.username.Trim());

                    await using var reader = await checkRole.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var role = reader.GetString(0);
                        if (string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase))
                        {
                            LoadMainTable();
                            return new JsonResult(new { success = false, message = _L["UM_Back_OnlyAdminDeleteForbidden"] });
                        }
                    }
                }

                // delete
                const string deleteSql = "DELETE FROM users WHERE username = @username";
                await using (var deleteCmd = new MySqlCommand(deleteSql, connection))
                {
                    deleteCmd.Parameters.AddWithValue("@username", model.username.Trim());
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                // recount
                await using (var recount = new MySqlCommand(checkAdminCountQuery, connection))
                {
                    ViewData["AdminCount"] = Convert.ToInt32(await recount.ExecuteScalarAsync());
                }

                LoadMainTable();
                return new JsonResult(new { success = true });
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "MySQL error on Delete user");
                return new JsonResult(new { success = false, message = string.Format(_L["UM_Back_SqlError"], ex.Message) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General error on Delete user");
                return new JsonResult(new { success = false, message = string.Format(_L["UM_Back_Error"], ex.Message) });
            }
        }
    }
}
