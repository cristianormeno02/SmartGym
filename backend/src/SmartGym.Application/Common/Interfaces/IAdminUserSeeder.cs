namespace SmartGym.Application.Common.Interfaces;

public enum AdminSeedOutcome
{
    Created,
    NotConfigured,
    InvalidConfiguration,
    AdministratorAlreadyExists,
    EmailAlreadyInUse
}

public interface IAdminUserSeeder
{
    /// <summary>Crea el administrador inicial si está configurado y todavía no existe ninguno. Es idempotente.</summary>
    Task<AdminSeedOutcome> SeedAsync(CancellationToken cancellationToken = default);
}
