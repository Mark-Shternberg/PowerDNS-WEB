using Microsoft.Extensions.Localization;
using MySqlConnector;

namespace PowerDNS_Web
{
    public class Functions
    {
        private readonly ILogger<Functions> _logger;
        private readonly IConfiguration Configuration;
        private readonly IStringLocalizer<SharedResource> _S;
        private readonly IWebHostEnvironment _env;

        public Functions(ILogger<Functions> logger, IConfiguration configuration, IStringLocalizer<SharedResource> S, IWebHostEnvironment env)
        {
            _logger = logger;
            Configuration = configuration;
            _S = S;
            _env = env;
        }

        public string sql_connection()
        {
            var server = Configuration["MySQLConnection:Server"];
            var user = Configuration["MySQLConnection:User"];
            var password = Configuration["MySQLConnection:Password"];
            var database = Configuration["MySQLConnection:Database"];

            return "Server=" + server + ";User ID=" + user + ";Password=" + password + ";Database=" + database;
        }

        public void CheckDTExist()
        {
            try
            {
                using var connection = new MySqlConnection(sql_connection());
                connection.Open();

                using var check_table = new MySqlCommand("SHOW TABLES LIKE 'users'", connection);
                using var create_users = new MySqlCommand(
                    "CREATE TABLE `users` (" +
                    "`id` INT NOT NULL AUTO_INCREMENT," +
                    "`username` VARCHAR(191) NOT NULL," +
                    "`role` VARCHAR(64) NOT NULL," +
                    "`password` LONGTEXT NOT NULL," +
                    "PRIMARY KEY(`id`)," +
                    "UNIQUE KEY `ux_users_username` (`username`))", connection);

                using var reader_users = check_table.ExecuteReader();
                if (!reader_users.HasRows)
                {
                    reader_users.Close();
                    create_users.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckDTExist failed");
            }
        }
    }
}
