using System.Text.Json;

namespace PowerDNS_Web.Models.Dashboard
{
    public class QueryDetail
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
    }

    public class StatEntry
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public JsonElement Value { get; set; }

        public long GetValueAsLong()
        {
            try
            {
                if (Value.ValueKind == JsonValueKind.Number)
                    return Value.GetInt64();
                if (Value.ValueKind == JsonValueKind.String && long.TryParse(Value.GetString(), out long result))
                    return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing statistic value for {Name}: {ex.Message}");
            }
            return 0;
        }
    }
}
