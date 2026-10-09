namespace Gateway.Areas.Admin.Models.Users;

/// <summary>A group a user belongs to, with the app it grants access to.</summary>
public class UserGroupItemViewModel
{
    public int GroupId { get; set; }

    /// <summary>Stored name, already prefixed: "[AppName]-[GroupName]".</summary>
    public string GroupName { get; set; } = string.Empty;

    public int AppId { get; set; }

    public string AppName { get; set; } = string.Empty;

    /// <summary>0 = highest power; higher numbers = less power.</summary>
    public int PowerLevel { get; set; }
}

/// <summary>A group the user is not in yet, offered in the assign dropdown.</summary>
public class AvailableGroupViewModel
{
    public int GroupId { get; set; }

    public string GroupName { get; set; } = string.Empty;

    public string AppName { get; set; } = string.Empty;

    public int PowerLevel { get; set; }
}

/// <summary>Response body of GET /Admin/Users/{userId}/Groups.</summary>
public class UserGroupListResponse
{
    public string UserId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public List<UserGroupItemViewModel> Groups { get; set; } = new();
}
