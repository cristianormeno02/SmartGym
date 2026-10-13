using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Infrastructure.Services.Auth;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class AdminUserSeedHostedServiceTests
{
    private sealed class FakeSeeder : IAdminUserSeeder
    {
        private readonly Func<AdminSeedOutcome> _behaviour;

        public FakeSeeder(Func<AdminSeedOutcome> behaviour) => _behaviour = behaviour;

        public int Calls { get; private set; }

        public Task<AdminSeedOutcome> SeedAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_behaviour());
        }
    }

    private static AdminUserSeedHostedService CreateService(FakeSeeder seeder)
    {
        var provider = new ServiceCollection()
            .AddScoped<IAdminUserSeeder>(_ => seeder)
            .BuildServiceProvider();

        return new AdminUserSeedHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AdminUserSeedHostedService>.Instance);
    }

    [Fact]
    public async Task StartAsync_ShouldRunTheSeederOnce()
    {
        var seeder = new FakeSeeder(() => AdminSeedOutcome.Created);

        await CreateService(seeder).StartAsync(CancellationToken.None);

        Assert.Equal(1, seeder.Calls);
    }

    [Fact]
    public async Task StartAsync_ShouldNotPropagateSeederFailures()
    {
        var seeder = new FakeSeeder(() => throw new InvalidOperationException("base inaccesible"));

        await CreateService(seeder).StartAsync(CancellationToken.None);

        Assert.Equal(1, seeder.Calls);
    }
}
