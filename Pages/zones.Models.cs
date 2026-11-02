namespace PowerDNS_Web.Pages
{
    public partial class ZonesModel
    {
        // ========================= MODELS =========================
        public class DnsZone
        {
            public string? name { get; set; }
            public string? kind { get; set; }
            public List<string> masters { get; set; } = new List<string>();
            public bool dnssec { get; set; }
            public long serial { get; set; }

            public string Master => masters != null && masters.Count > 0 ? string.Join(", ", masters) : "";
        }

        public class DnssecKey
        {
            public int Id { get; set; }
            public bool Active { get; set; }
            public string? Algorithm { get; set; }
            public int Bits { get; set; }
            public string? KeyType { get; set; }
            public bool Published { get; set; }
        }

        // ========================= ZONE REQUESTS =========================
        private class AddZoneJson
        {
            public string? name { get; set; }
            public string? kind { get; set; }
            public bool dnssec { get; set; }
            public string? Master { get; set; }
            public List<string>? masters { get; set; }
            public string? Mode { get; set; }       // "Forward" | "Reverse"
            public string? revprefix { get; set; }  // e.g. 192.168.0.
        }

        private class EditZoneJson
        {
            public string? name { get; set; }
            public string? kind { get; set; }
            public bool dnssec { get; set; }
            public long serial { get; set; }
            public string? Master { get; set; }
            public List<string>? masters { get; set; }
        }
    }
}
