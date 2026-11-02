using MySqlConnector;

namespace PowerDNS_Web.Services.DB
{
    public class ApplicationDatabase
    {
        // ========================= DEPENDENCIES =========================
        private readonly ILogger<ApplicationDatabase> _logger;
        private readonly IConfiguration _configuration;

        // ========================= CONSTRUCTION =========================
        public ApplicationDatabase(ILogger<ApplicationDatabase> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // ========================= CONNECTION SETTINGS =========================
        public string BuildConnectionString()
        {
            var server = _configuration["MySQLConnection:Server"];
            var user = _configuration["MySQLConnection:User"];
            var password = _configuration["MySQLConnection:Password"];
            var database = _configuration["MySQLConnection:Database"];

            return "Server=" + server + ";User ID=" + user + ";Password=" + password + ";Database=" + database;
        }

        // ========================= SCHEMA INITIALIZATION =========================
        public void InitializeSchema()
        {
            try
            {
                using var connection = new MySqlConnection(BuildConnectionString());
                connection.Open();

                using var checkTable = new MySqlCommand("SHOW TABLES LIKE 'users'", connection);
                using var createUsers = new MySqlCommand(
                    "CREATE TABLE `users` (" +
                    "`id` INT NOT NULL AUTO_INCREMENT," +
                    "`username` VARCHAR(191) NOT NULL," +
                    "`role` VARCHAR(64) NOT NULL," +
                    "`password` LONGTEXT NOT NULL," +
                    "PRIMARY KEY(`id`)," +
                    "UNIQUE KEY `ux_users_username` (`username`))", connection);

                using var usersReader = checkTable.ExecuteReader();
                if (!usersReader.HasRows)
                {
                    usersReader.Close();
                    createUsers.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InitializeSchema failed");
            }
        }
    }
}
