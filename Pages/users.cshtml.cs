﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Newtonsoft.Json;
using System.Data;
using Microsoft.AspNetCore.Authorization;

namespace PowerDNS_Web.Pages
{
    [Authorize(Roles = "Administrator")]
    public class userModel : PageModel
    {
        private readonly ILogger<userModel> _logger;
        private readonly IConfiguration Configuration;

        public userModel(ILogger<userModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            Configuration = configuration;
        }

        public string sql_connection()
        {

            var server = Configuration["MySQLConnection:server"];
            var user = Configuration["MySQLConnection:user"];
            var password = Configuration["MySQLConnection:password"];
            var database = Configuration["MySQLConnection:database"];

            return "Server=" + server + ";User ID=" + user + ";Password=" + password + ";Database=" + database;
        }

        public class new_user
        {
            public string? username { get; set; }
            public string? role { get; set; }
            public string? password { get; set; }
        }

        public class main_table_model
        {
            public string? username { get; set; }
            public string? role { get; set; }
            public string? password { get; set; }
        }

        public List<main_table_model>? main_table { get; set; }

        public void OnGet()
        {
            try
            {
                using (var connection = new MySqlConnection(sql_connection()))
                {
                    connection.Open();

                    // Проверяем количество администраторов
                    string checkAdminCountQuery = "SELECT COUNT(*) FROM users WHERE role = 'Administrator'";
                    using (var checkCommand = new MySqlCommand(checkAdminCountQuery, connection))
                    {
                        int adminCount = Convert.ToInt32(checkCommand.ExecuteScalar());

                        // Передаём в ViewData, чтобы использовать в Razor
                        ViewData["AdminCount"] = adminCount;
                    }
                }
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "Error");
            }

            LoadMainTable();
        }

        private void LoadMainTable() // LOAD USERS
        {
            using var connection = new MySqlConnection(sql_connection());
            connection.Open();

            using var command = new MySqlCommand("SELECT username,role FROM users ORDER BY id DESC", connection);

            using var reader_main = command.ExecuteReader();
            if (reader_main.HasRows)
            {
                DataTable dt = new DataTable();
                dt.Load(reader_main);

                string serializeObject = JsonConvert.SerializeObject(dt);
                var dataTableObjectInPOCO = JsonConvert.DeserializeObject<List<main_table_model>>(serializeObject);
                main_table = dataTableObjectInPOCO;
            }
            else main_table = null;
        }

        public async Task<IActionResult> OnPostAdd_new_user([FromBody] new_user model) //ADDING NEW USER
        {
            try
            {
                using var connection = new MySqlConnection(sql_connection());
                await connection.OpenAsync(); 

                // CHECK FOR DUPLICATES
                string checkQuery = "SELECT COUNT(*) FROM users WHERE username = @username";
                using (var cmd = new MySqlCommand(checkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@username", model.username);
                    var result = await cmd.ExecuteScalarAsync(); 
                    if (Convert.ToInt32(result) > 0)
                    {
                        return new JsonResult(new { success = false, message = "A user with such a login already exists." });
                    }
                }

                // ADD NEW USER
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(model.password);
                string insertQuery = "INSERT INTO users (username, role, password) VALUES (@username, @role, @password)";
                using (var cmd = new MySqlCommand(insertQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@username", model.username);
                    cmd.Parameters.AddWithValue("@role", model.role);
                    cmd.Parameters.AddWithValue("@password", hashedPassword);
                    await cmd.ExecuteNonQueryAsync(); 
                }

                return new JsonResult(new { success = true });
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex.Message, "Error occurred while sending MySQL command");
                return new JsonResult(new { success = false, message = "SQL error: " + ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "General error");
                return new JsonResult(new { success = false, message = "Error: " + ex.Message });
            }
        }

        public async Task<IActionResult> OnPostUpdate_user([FromBody] new_user model) // UPDATE USER
        {
            try
            {
                string sql;
                if (model.password == "") sql = "UPDATE users SET role=@role WHERE (username=@username)";
                else sql = "UPDATE users SET role=@role, password=@password WHERE (username=@username)";

                using var connection = new MySqlConnection(sql_connection());
                await connection.OpenAsync();

                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(model.password);
                using (var cmd = new MySqlCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@username", model.username);
                    cmd.Parameters.AddWithValue("@role", model.role);
                    cmd.Parameters.AddWithValue("@password", hashedPassword);
                    await cmd.ExecuteNonQueryAsync();
                }

                return new JsonResult(new { success = true });
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex.Message, "Error occurred while sending MySQL command");
                return new JsonResult(new { success = false, message = "SQL error: " + ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "General error");
                return new JsonResult(new { success = false, message = "Error: " + ex.Message });
            }
        }

        public async Task<IActionResult> OnPostDelete_user([FromBody] main_table_model model) // DELETE USER
        {
            int adminCount;
            try
            {
                string sqlExpression = "DELETE FROM users WHERE (username = ?username)";

                using (var connection = new MySqlConnection(sql_connection()))
                {
                    connection.Open();

                    // CHECK IF THERE IS ONLY ONE ADMIN
                    string checkAdminCountQuery = "SELECT COUNT(*) FROM users WHERE role = 'Administrator'";
                    using (var checkCommand = new MySqlCommand(checkAdminCountQuery, connection))
                    {
                        adminCount = Convert.ToInt32(checkCommand.ExecuteScalar());
                        ViewData["AdminCount"] = adminCount;
                    }

                    if (adminCount <= 1)
                    {
                        string checkIfAdmin = "SELECT role FROM users WHERE username = @username";
                        using (var checkCommand = new MySqlCommand(checkIfAdmin, connection))
                        {
                            checkCommand.Parameters.AddWithValue("@username", model.username);

                            using (var reader = checkCommand.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    string role = reader.GetString(0); 
                                    if (role == "Administrator")
                                    {
                                        LoadMainTable();
                                        return new JsonResult(new { success = false, message = "You cannot delete the only administrator!" });
                                    }
                                }
                            }
                        }

                    }
                    //--------------------------------

                    using var command = new MySqlCommand(sqlExpression, connection);

                    command.Prepare();

                    int error = 0;
                    if (model.username != null && model.username != "")
                    {
                        command.Parameters.AddWithValue("?username", model.username);
                    }
                    else error++;

                    //---------EXECUTE------------
                    if (error == 0)
                    {
                        command.ExecuteNonQuery();
                    }

                    // RECOUNT ADMINS
                    using (var checkCommand = new MySqlCommand(checkAdminCountQuery, connection))
                    {
                        ViewData["AdminCount"] = Convert.ToInt32(checkCommand.ExecuteScalar());
                    }
                }
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex.Message, "Error occurred while send mysql command");
                return new JsonResult(new { success = false, message = "Error: " + ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "General error");
                return new JsonResult(new { success = false, message = "Error: " + ex.ToString() });
            }
            LoadMainTable();
            return new JsonResult(new { success = true });
        }

    }
}
