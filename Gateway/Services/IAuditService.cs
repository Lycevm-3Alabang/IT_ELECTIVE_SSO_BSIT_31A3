using System.Threading.Tasks;

namespace Gateway.Services;

public interface IAuditService
{
    Task LogAction(string action, string details, string? userId, string? ipAddress);
}
