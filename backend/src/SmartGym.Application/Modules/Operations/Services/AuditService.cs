using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Domain.Entities.Operations;

namespace SmartGym.Application.Modules.Operations.Services;

public class AuditService : IAuditService
{
    private readonly ISmartGymDbContext _dbContext;

    public AuditService(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogAsync(
        string action,
        string entityName,
        string? entityId,
        string? oldValues,
        string? newValues,
        Guid? userId,
        string? userEmail,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = action.Trim(),
            EntityName = entityName.Trim(),
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            UserId = userId,
            UserEmail = userEmail,
            IpAddress = ipAddress,
            TimestampUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.AuditLogs.Add(log);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<AuditLogDto>> GetRecentLogsAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.TimestampUtc)
            .Take(count)
            .ToListAsync(cancellationToken);

        return list.Select(a => new AuditLogDto(
            a.Id,
            a.UserId,
            a.UserEmail,
            a.Action,
            a.EntityName,
            a.EntityId,
            a.OldValues,
            a.NewValues,
            a.IpAddress,
            a.TimestampUtc
        )).ToList();
    }
}
