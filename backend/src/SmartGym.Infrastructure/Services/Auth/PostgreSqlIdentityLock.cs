using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Identity.Services;

namespace SmartGym.Infrastructure.Services.Auth;

public class PostgreSqlIdentityLock : IIdentityLock
{
    private const long RolesAdvisoryLockKey = 771239823487123491L;
    private readonly ISmartGymDbContext _dbContext;

    public PostgreSqlIdentityLock(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AcquireRolesLockAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock({RolesAdvisoryLockKey});",
            cancellationToken);
    }
}
