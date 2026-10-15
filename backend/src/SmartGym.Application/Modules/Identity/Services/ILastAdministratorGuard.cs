namespace SmartGym.Application.Modules.Identity.Services;

public interface ILastAdministratorGuard
{
    Task<bool> IsOperationalAdministratorAsync(Guid personId, CancellationToken cancellationToken = default);
    Task EnsureAnotherOperationalAdministratorAsync(Guid targetPersonId, CancellationToken cancellationToken = default);
}

public interface IIdentityLock
{
    Task AcquireRolesLockAsync(CancellationToken cancellationToken = default);
}

public class NoOpIdentityLock : IIdentityLock
{
    public Task AcquireRolesLockAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
