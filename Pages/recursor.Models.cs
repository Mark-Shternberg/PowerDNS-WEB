namespace PowerDNS_Web.Pages
{
    public partial class RecursorModel
    {
        // ========================= VIEW MODELS / DTOS =========================
        public class ForwardZone
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public List<string> ForwardTo { get; set; } = new();
        }

        private class AuthZoneDto
        {
            public string Name { get; set; } = "";
            public string Kind { get; set; } = "";
        }

        private class RecursorZoneDto
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Kind { get; set; } = "";
            public List<string> Servers { get; set; } = new();
            public bool? Recursion_Desired { get; set; }
        }

        // ========================= FORWARDING REQUESTS =========================
        public class ForwardZoneRequest
        {
            public string Id { get; set; } = "";
            public string Zone { get; set; } = "";
        }
        public class UpdateForwardZones
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string DnsServers { get; set; } = "";
            public string Transport { get; set; } = "";
        }
    }
}
