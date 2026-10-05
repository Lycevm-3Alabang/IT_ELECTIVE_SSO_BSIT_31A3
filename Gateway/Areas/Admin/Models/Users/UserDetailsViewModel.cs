namespace Gateway.Areas.Admin.Models.Users
{

    public class UserDetailsViewModel
    {
        public string Id { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? LastLoginAt { get; set; }

        /// <summary>Every group the user is in, across all apps.</summary>
        public List<UserGroupItemViewModel> Groups { get; set; } = new();

        /// <summary>Groups the user is not in yet (the assign dropdown).</summary>
        public List<AvailableGroupViewModel> AvailableGroups { get; set; } = new();

        public List<AuditLogEntry> RecentActivity { get; set; } = new();
    }
}
