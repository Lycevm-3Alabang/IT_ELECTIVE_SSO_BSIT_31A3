namespace Gateway.Areas.Admin.Models.Users
{

    public class UserDetailsViewModel
    {
        public string Id { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? LastLoginAt { get; set; }

        public List<string> Groups { get; set; } = new();

        public List<AuditLogEntry> RecentActivity { get; set; } = new();
    }
}
