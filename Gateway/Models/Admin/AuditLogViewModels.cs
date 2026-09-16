namespace Gateway.Models.Admin;

public class AuditLogListItemViewModel
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
    public string? UserId { get; set; }
    public string? Email { get; set; }
}

public class AuditLogListViewModel
{
    public IReadOnlyList<AuditLogListItemViewModel> Logs { get; set; } = Array.Empty<AuditLogListItemViewModel>();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
