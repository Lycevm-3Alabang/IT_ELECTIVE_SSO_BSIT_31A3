namespace Gateway.Areas.Admin.Models.Users
{
    public class AuditLogEntry
    {
        public string UserId { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string PerformedBy { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
