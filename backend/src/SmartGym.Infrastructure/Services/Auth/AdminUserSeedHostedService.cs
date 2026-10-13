using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartGym.Application.Common.Interfaces;

namespace SmartGym.Infrastructure.Services.Auth;

/// <summary>Crea el administrador inicial al arrancar la API. Un fallo se registra pero no impide el arranque.</summary>
public class AdminUserSeedHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminUserSeedHostedService> _logger;

    public AdminUserSeedHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AdminUserSeedHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<IAdminUserSeeder>();
            await seeder.SeedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminSeed: no se pudo crear el administrador inicial.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
