using SmartGym.Application.Modules.Operations.Dtos;

namespace SmartGym.Application.Modules.Operations.Services;

public interface IAuditService
{
    Task LogAsync(string action, string entityName, string? entityId, string? oldValues, string? newValues, Guid? userId, string? userEmail, string? ipAddress, CancellationToken cancellationToken = default);
    Task<List<AuditLogDto>> GetRecentLogsAsync(int count = 50, CancellationToken cancellationToken = default);
}
