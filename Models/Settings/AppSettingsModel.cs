namespace PowerDNS_Web.Models.Settings
{
    // ========================= APPLICATION SETTINGS =========================
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
