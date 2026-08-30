using Microsoft.AspNetCore.Identity;

namespace Models;

public class UserGroup
{

    public string UserId { get; set; } = string.Empty;
    public IdentityUser User { get; set; } = null!;

    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
}