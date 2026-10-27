using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Activities;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure;
using SmartGym.Infrastructure.Persistence;
using SmartGym.IntegrationTests.Fixtures;
using SmartGym.IntegrationTests.Identity;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.IntegrationTests.Activities;

[Collection("PostgresIntegration")]
public class RecurringScheduleBatchIntegrationTests : IDisposable
{
    private readonly PostgresDatabaseFixture _fixture;
    private readonly List<ServiceProvider> _providers = new();

    private readonly TestCurrentUserService _currentUser = new()
    {
        UserId = Guid.NewGuid(),
        Email = "staff@smartgym.com",
        IsAuthenticated = true,
        Roles = new List<string> { Roles.Administrator, Roles.Secretary }
    };

    public RecurringScheduleBatchIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }

    private async Task<ServiceProvider> BuildProviderAsync(Action<IServiceCollection>? configure = null)
    {
        var connectionString = await _fixture.CreateIsolatedDatabaseAsync(applyAllMigrations: true);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Jwt:Issuer"] = "SmartGymTest",
                ["Jwt:Audience"] = "SmartGymClientTest",
                ["Jwt:SecretKey"] = "super_secret_key_for_testing_purposes_only_at_least_32_bytes_long!",
                ["Jwt:ExpirationMinutes"] = "60",
                ["S3Storage:UseLocalStorage"] = "true",
                ["PublicS3Storage:UseLocalStorage"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddScoped<ICurrentUserService>(_ => _currentUser);
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private static async Task<(Activity activity, Room room, Person instructor)> SeedBaseEntitiesAsync(SmartGymDbContext db)
    {
        var activity = new Activity("FUNCIONAL", "Funcional", defaultCapacity: 20);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala A", Capacity = 30 };

        var instructorRole = await db.Roles.FirstAsync(r => r.Id == (int)RoleType.Instructor);
        var instructor = Person.Create("Carlos", "Entrenador", "carlos@example.com");
        instructor.AddRole(instructorRole);

        db.Activities.Add(activity);
        db.Rooms.Add(room);
        db.People.Add(instructor);
        await db.SaveChangesAsync();

        return (activity, room, instructor);
    }

    [Fact]
    public async Task CreateBatch_WhenValid_PersistsAllSchedulesAndGeneratesClassSessions()
    {
        var provider = await BuildProviderAsync();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartGymDbContext>();
        var scheduleService = scope.ServiceProvider.GetRequiredService<IRecurringScheduleService>();

        var (activity, room, instructor) = await SeedBaseEntitiesAsync(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var nextMonday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7);
        if (nextMonday == today) nextMonday = today.AddDays(7);

        var slots = new List<BatchScheduleSlotRequest>
        {
            new(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }, new TimeOnly(9, 30), new TimeOnly(10, 30))
        };

        var request = new CreateBatchRecurringScheduleRequest(
            activity.Id, room.Id, instructor.Id, null,
            nextMonday, nextMonday.AddDays(14), 20, slots);

        var result = await scheduleService.CreateBatchAsync(request);

        Assert.Equal(2, result.Count);

        // Verify in DB
        var count = await db.RecurringSchedules.CountAsync(s => s.ActivityId == activity.Id && s.IsActive);
        Assert.Equal(2, count);

        // Verify class sessions were synchronized/generated
        var sessionsCount = await db.ClassSessions.CountAsync(cs => cs.ActivityId == activity.Id);
        Assert.True(sessionsCount > 0);
    }

    [Fact]
    public async Task CreateBatch_WhenConflictOccurs_RollsBackAllSchedulesAndClassSessions()
    {
        var provider = await BuildProviderAsync();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartGymDbContext>();
        var scheduleService = scope.ServiceProvider.GetRequiredService<IRecurringScheduleService>();

        var (activity, room, instructor) = await SeedBaseEntitiesAsync(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var nextMonday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7);
        if (nextMonday == today) nextMonday = today.AddDays(7);

        // Creación previa de un horario existente
        var preExistingReq = new CreateRecurringScheduleRequest(
            activity.Id, room.Id, instructor.Id, null,
            DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0),
            nextMonday, nextMonday.AddDays(30), 20);
        await scheduleService.CreateAsync(preExistingReq);

        var initialSchedulesCount = await db.RecurringSchedules.CountAsync();
        var initialSessionsCount = await db.ClassSessions.CountAsync();

        // Intento de batch que choca en uno de sus slots
        var slots = new List<BatchScheduleSlotRequest>
        {
            new(new[] { DayOfWeek.Wednesday }, new TimeOnly(14, 0), new TimeOnly(15, 0)),
            new(new[] { DayOfWeek.Monday }, new TimeOnly(9, 30), new TimeOnly(10, 30)) // Choca con preExisting
        };

        var batchReq = new CreateBatchRecurringScheduleRequest(
            activity.Id, room.Id, instructor.Id, null,
            nextMonday, nextMonday.AddDays(30), 20, slots);

        await Assert.ThrowsAsync<ScheduleConflictException>(() => scheduleService.CreateBatchAsync(batchReq));

        // Verificamos que NADA nuevo fue insertado (rollback completo)
        var finalSchedulesCount = await db.RecurringSchedules.CountAsync();
        var finalSessionsCount = await db.ClassSessions.CountAsync();

        Assert.Equal(initialSchedulesCount, finalSchedulesCount);
        Assert.Equal(initialSessionsCount, finalSessionsCount);
    }

    [Fact]
    public async Task CreateBatch_WhenSyncFailsAfterSchedulesAreSaved_RollsBackSchedulesAndClassSessions()
    {
        var provider = await BuildProviderAsync(services =>
        {
            services.AddScoped<ClassSessionScheduleSync>();
            services.AddScoped<IClassSessionScheduleSync>(sp =>
                new FailOnSecondCallScheduleSync(sp.GetRequiredService<ClassSessionScheduleSync>()));
        });

        Guid activityId;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmartGymDbContext>();
            var scheduleService = scope.ServiceProvider.GetRequiredService<IRecurringScheduleService>();

            var (activity, room, instructor) = await SeedBaseEntitiesAsync(db);
            activityId = activity.Id;

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var nextMonday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7);
            if (nextMonday == today) nextMonday = today.AddDays(7);

            var slots = new List<BatchScheduleSlotRequest>
            {
                new(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }, new TimeOnly(9, 30), new TimeOnly(10, 30))
            };

            var request = new CreateBatchRecurringScheduleRequest(
                activity.Id, room.Id, instructor.Id, null,
                nextMonday, nextMonday.AddDays(14), 20, slots);

            await Assert.ThrowsAsync<InvalidOperationException>(() => scheduleService.CreateBatchAsync(request));
        }

        using (var verifyScope = provider.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<SmartGymDbContext>();
            Assert.Equal(0, await db.RecurringSchedules.CountAsync(s => s.ActivityId == activityId));
            Assert.Equal(0, await db.ClassSessions.CountAsync(cs => cs.ActivityId == activityId));
        }
    }

    // Delega en la sincronización real y falla en la segunda llamada, cuando el primer
    // horario ya tiene sus clases guardadas dentro de la transacción del lote.
    private sealed class FailOnSecondCallScheduleSync : IClassSessionScheduleSync
    {
        private readonly IClassSessionScheduleSync _inner;
        private int _calls;

        public FailOnSecondCallScheduleSync(IClassSessionScheduleSync inner) => _inner = inner;

        public Task<List<Guid>> SyncAsync(Guid scheduleId, DateTime? nowUtc = null, CancellationToken cancellationToken = default)
        {
            if (++_calls == 2)
            {
                throw new InvalidOperationException("Fallo simulado durante la sincronización de clases.");
            }

            return _inner.SyncAsync(scheduleId, nowUtc, cancellationToken);
        }

        public Task<int> SyncAllActiveAsync(DateTime? nowUtc = null, CancellationToken cancellationToken = default) =>
            _inner.SyncAllActiveAsync(nowUtc, cancellationToken);
    }
}
