using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Identity.Services;

public class LastAdministratorGuard : ILastAdministratorGuard
{
    private readonly ISmartGymDbContext _dbContext;

    public LastAdministratorGuard(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> IsOperationalAdministratorAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.PersonRoles
            .AsNoTracking()
            .AnyAsync(pr => pr.PersonId == personId
                            && pr.RoleId == (int)RoleType.Administrator
                            && pr.IsActive
                            && pr.Person.Status == PersonStatus.Active
                            && pr.Person.User != null
                            && pr.Person.User.IsActive, cancellationToken);
    }

    public async Task EnsureAnotherOperationalAdministratorAsync(Guid targetPersonId, CancellationToken cancellationToken = default)
    {
        var others = await _dbContext.PersonRoles
            .AsNoTracking()
            .Where(pr => pr.RoleId == (int)RoleType.Administrator
                      && pr.IsActive
                      && pr.PersonId != targetPersonId
                      && pr.Person.Status == PersonStatus.Active
                      && pr.Person.User != null
                      && pr.Person.User.IsActive)
            .CountAsync(cancellationToken);

        if (others == 0)
        {
            throw new ConflictException("No se puede revocar o inhabilitar al último administrador activo del gimnasio.");
        }
    }
}
