namespace PowerDNS_Web.Pages.zone
{
    public partial class ZonePageModel
    {
        // ========================= RECORD MODELS =========================
        public class ZoneData
        {
            public string Kind { get; set; } = "";
            public List<Rrset> Rrsets { get; set; } = new();
        }

        public class Rrset
        {
            public string? Name { get; set; }
            public string? Type { get; set; }
            public int Ttl { get; set; }
            public List<Record> Records { get; set; } = new();
        }

        public class Record
        {
            public string? Content { get; set; }
            public bool Disabled { get; set; }
        }

        public class DnsRecord
        {
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public List<string> Content { get; set; } = new();
            public int Ttl { get; set; }
        }

        // ========================= RECORD REQUESTS =========================
        public class AddRecordRequest
        {
            public string? Subdomain { get; set; }
            public string? RecordType { get; set; }
            public string? Value { get; set; }
            public int? Ttl { get; set; }
            public int? MxPriority { get; set; }
            public int? SrvPriority { get; set; }
            public int? SrvWeight { get; set; }
            public int? SrvPort { get; set; }
        }

        public class DeleteRecordRequest
        {
            public string? Name { get; set; }
            public string? Type { get; set; }
            public string? Value { get; set; }
        }

        public class EditRecordRequest
        {
            public string? Name { get; set; }
            public string? Type { get; set; }
            public string? OldValue { get; set; }
            public string? Value { get; set; }
            public int? Ttl { get; set; }
            public int? MxPriority { get; set; }
            public int? SrvPriority { get; set; }
            public int? SrvWeight { get; set; }
            public int? SrvPort { get; set; }
            public string? SoaNs { get; set; }
            public string? SoaEmail { get; set; }
            public int? SoaRefresh { get; set; }
            public int? SoaRetry { get; set; }
            public int? SoaExpire { get; set; }
            public int? SoaMinimumTtl { get; set; }
        }

        public class AddSubdomainRequest
        {
            public string? Subdomain { get; set; }
        }
    }
}
