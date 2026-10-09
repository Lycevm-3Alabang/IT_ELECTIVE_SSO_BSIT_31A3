namespace Gateway.Areas.Admin.Models.Users
{
    // In-memory representation of a user until Issue 4 (EF Core / real data
    // store) is merged. Mirrors the pattern used by AppsController.
    public class UserRecord
    {
        public string Id { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastLoginAt { get; set; }

        public List<string> Groups { get; set; } = new();

        // Placeholder only - a real implementation must hash this (Issue 4).
        public string TemporaryPassword { get; set; } = string.Empty;
    }
}
