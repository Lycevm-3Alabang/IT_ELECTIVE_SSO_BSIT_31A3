namespace Gateway.Areas.Admin.Models.Groups;

public class GroupListItemViewModel
{
    public int Id { get; set; }

    public string AppName { get; set; } = string.Empty;

    /// <summary>The stored name, already prefixed: "[AppName]-[GroupName]".</summary>
    public string Name { get; set; } = string.Empty;

    public int PowerLevel { get; set; }

    public int MemberCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
