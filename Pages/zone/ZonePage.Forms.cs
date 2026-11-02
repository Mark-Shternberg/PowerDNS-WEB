using System.Text;

namespace PowerDNS_Web.Pages.zone
{
    public partial class ZonePageModel
    {
        // ========================= TXT ENCODING =========================
        private static string FormatTxtForPowerDNS(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "\"\"";
            var chunks = new List<string>();
            var chunk = new StringBuilder();
            int bytes = 0;
            foreach (var rune in raw.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 255)
                {
                    chunks.Add(chunk.ToString());
                    chunk.Clear();
                    bytes = 0;
                }
                chunk.Append(rune.ToString());
                bytes += rune.Utf8SequenceLength;
            }
            chunks.Add(chunk.ToString());
            return string.Join(" ", chunks.Select(c => "\"" + c.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));
        }

        // ========================= FORM VALIDATION =========================
        private void ValidateNumericForm()
        {
            foreach (var field in new[] { "Ttl", "MxPriority", "SrvPriority", "SrvWeight", "SrvPort", "SoaRefresh", "SoaRetry", "SoaExpire", "SoaMinimumTtl" })
            {
                var value = Request.Form[field].ToString();
                if (value.Length > 0 && (!int.TryParse(value, out var number) || number < 0 ||
                    (field is "MxPriority" or "SrvPriority" or "SrvWeight" or "SrvPort" && number > 65535)))
                    throw new FormatException("Invalid numeric field: " + field);
            }
        }

        // ========================= FORM PARSING =========================
        private AddRecordRequest ParseAddRecordFromForm()
        {
            ValidateNumericForm();
            var type = Request.Form["RecordType"].ToString();
            string value = Request.Form["Value"].ToString();
            if (type == "TXT") value = Request.Form["TxtValue"].ToString();
            else if (type == "NS") value = Request.Form["NsTarget"].ToString();
            else if (type == "HTTPS") value = Request.Form["HttpsValue"].ToString();

            return new AddRecordRequest
            {
                Subdomain = Request.Form["Subdomain"].ToString(),
                RecordType = type,
                Value = value,
                Ttl = int.TryParse(Request.Form["Ttl"], out var ttl) ? ttl : null,
                MxPriority = int.TryParse(Request.Form["MxPriority"], out var mx) ? mx : null,
                SrvPriority = int.TryParse(Request.Form["SrvPriority"], out var sp) ? sp : null,
                SrvWeight = int.TryParse(Request.Form["SrvWeight"], out var sw) ? sw : null,
                SrvPort = int.TryParse(Request.Form["SrvPort"], out var sport) ? sport : null
            };
        }

        private EditRecordRequest ParseEditRecordFromForm()
        {
            ValidateNumericForm();
            return new EditRecordRequest
            {
                Name = Request.Form["Name"].ToString(),
                Type = Request.Form["Type"].ToString(),
                OldValue = Request.Form["OldValue"].ToString(),
                Value = Request.Form["Value"].ToString(),
                Ttl = int.TryParse(Request.Form["Ttl"], out var ttl) ? ttl : null,
                MxPriority = int.TryParse(Request.Form["MxPriority"], out var mx) ? mx : null,
                SrvPriority = int.TryParse(Request.Form["SrvPriority"], out var sp) ? sp : null,
                SrvWeight = int.TryParse(Request.Form["SrvWeight"], out var sw) ? sw : null,
                SrvPort = int.TryParse(Request.Form["SrvPort"], out var sport) ? sport : null,
                SoaNs = Request.Form["SoaNs"].ToString(),
                SoaEmail = Request.Form["SoaEmail"].ToString(),
                SoaRefresh = int.TryParse(Request.Form["SoaRefresh"], out var sr) ? sr : null,
                SoaRetry = int.TryParse(Request.Form["SoaRetry"], out var sry) ? sry : null,
                SoaExpire = int.TryParse(Request.Form["SoaExpire"], out var se) ? se : null,
                SoaMinimumTtl = int.TryParse(Request.Form["SoaMinimumTtl"], out var sm) ? sm : null
            };
        }
    }
}
