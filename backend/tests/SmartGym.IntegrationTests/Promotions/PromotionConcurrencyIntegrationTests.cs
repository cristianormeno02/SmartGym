using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartGym.Application;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Promotions;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.IntegrationTests.Fixtures;

namespace SmartGym.IntegrationTests.Promotions;

/// <summary>
/// Concurrencia real contra PostgreSQL: cada canje corre en su propio scope de DI (su propio DbContext y
/// transacción), como una solicitud HTTP, y compite por el bloqueo de la fila de la promoción.
/// </summary>
[Collection("PostgresIntegration")]
public class PromotionConcurrencyIntegrationTests : IDisposable
{
    private readonly PostgresDatabaseFixture _fixture;
    private readonly ServiceProvider _provider;

    public PromotionConcurrencyIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddApplication();
        services.AddScoped(sp => _fixture.CreateDbContext(sp));
        services.AddScoped<ISmartGymDbContext>(sp => sp.GetRequiredService<SmartGymDbContext>());
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    private sealed record Scenario(Guid PromotionId, string Code, List<Guid> StudentIds);

    private async Task<Scenario> SeedAsync(int students, int? maxUsesTotal, int currentUsesTotal = 0, int maxUsesPerPerson = 1)
    {
        await using var db = _fixture.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = $"P{suffix}"[..20].ToUpperInvariant(),
            Name = "Promo concurrencia",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 100m,
            ValidFrom = today.AddDays(-30),
            ValidTo = today.AddDays(30),
            MaxUsesTotal = maxUsesTotal,
            CurrentUsesTotal = currentUsesTotal,
            MaxUsesPerPerson = maxUsesPerPerson,
            IsActive = true
        };
        db.Promotions.Add(promo);

        var studentIds = new List<Guid>();
        for (var i = 0; i < students; i++)
        {
            var student = Person.Create($"Alumno{i}", "Promo", $"promo{i}.{suffix}@test.com");
            db.People.Add(student);
            studentIds.Add(student.Id);
        }

        await db.SaveChangesAsync();
        return new Scenario(promo.Id, promo.Code, studentIds);
    }

    /// <summary>Ejecuta la operación en un scope propio y devuelve su resultado o la excepción que lanzó.</summary>
    private async Task<(T? Result, Exception? Error)> RunInScopeAsync<T>(Func<IPromotionService, Task<T>> operation)
    {
        // Task.Run: asegura que las operaciones arranquen en paralelo y no encadenadas por la parte síncrona.
        return await Task.Run(async () =>
        {
            await using var scope = _provider.CreateAsyncScope();
            try
            {
                return (await operation(scope.ServiceProvider.GetRequiredService<IPromotionService>()), (Exception?)null);
            }
            catch (Exception ex)
            {
                return (default(T), ex);
            }
        });
    }

    private Task<(PromotionRedemptionDto? Result, Exception? Error)> RedeemAsync(Scenario scenario, Guid studentId) =>
        RunInScopeAsync(service => service.RedeemPromotionAsync(
            new ApplyPromotionRequest(scenario.Code, studentId, null, 10000m), membershipId: null));

    private static void AssertAllFailuresAreLimitReached(IEnumerable<(PromotionRedemptionDto? Result, Exception? Error)> outcomes)
    {
        foreach (var failure in outcomes.Where(o => o.Error != null))
        {
            var conflict = Assert.IsType<ConflictException>(failure.Error);
            Assert.Equal(PromotionErrorCodes.LimitReached, conflict.Code);
        }
    }

    [Fact]
    public async Task LastTotalUseRace_NeverExceedsTheTotalLimit()
    {
        var scenario = await SeedAsync(students: 8, maxUsesTotal: 100, currentUsesTotal: 99);

        var outcomes = await Task.WhenAll(scenario.StudentIds.Select(id => RedeemAsync(scenario, id)));

        Assert.Single(outcomes, o => o.Error == null);
        AssertAllFailuresAreLimitReached(outcomes);

        await using var db = _fixture.CreateDbContext();
        Assert.Equal(100, (await db.Promotions.SingleAsync(p => p.Id == scenario.PromotionId)).CurrentUsesTotal);
        Assert.Equal(1, await db.PromotionRedemptions.CountAsync(r => r.PromotionId == scenario.PromotionId && r.IsActive));
    }

    [Fact]
    public async Task ManyStudentsRace_GrantExactlyTheRemainingUses()
    {
        var scenario = await SeedAsync(students: 10, maxUsesTotal: 4);

        var outcomes = await Task.WhenAll(scenario.StudentIds.Select(id => RedeemAsync(scenario, id)));

        Assert.Equal(4, outcomes.Count(o => o.Error == null));
        AssertAllFailuresAreLimitReached(outcomes);

        await using var db = _fixture.CreateDbContext();
        Assert.Equal(4, (await db.Promotions.SingleAsync(p => p.Id == scenario.PromotionId)).CurrentUsesTotal);
    }

    [Fact]
    public async Task SameStudentRace_RespectsThePerPersonLimit()
    {
        var scenario = await SeedAsync(students: 1, maxUsesTotal: null, maxUsesPerPerson: 1);
        var studentId = scenario.StudentIds[0];

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RedeemAsync(scenario, studentId)));

        Assert.Single(outcomes, o => o.Error == null);
        AssertAllFailuresAreLimitReached(outcomes);

        await using var db = _fixture.CreateDbContext();
        Assert.Equal(1, await db.PromotionRedemptions.CountAsync(r => r.PromotionId == scenario.PromotionId && r.PersonId == studentId && r.IsActive));
        Assert.Equal(1, (await db.Promotions.SingleAsync(p => p.Id == scenario.PromotionId)).CurrentUsesTotal);
    }

    [Fact]
    public async Task RedemptionsRacingVoids_KeepTheCounterEqualToTheActiveRedemptions()
    {
        var scenario = await SeedAsync(students: 8, maxUsesTotal: null);
        var firstHalf = scenario.StudentIds.Take(4).ToList();
        var secondHalf = scenario.StudentIds.Skip(4).ToList();

        var initial = await Task.WhenAll(firstHalf.Select(id => RedeemAsync(scenario, id)));
        Assert.All(initial, o => Assert.Null(o.Error));

        // Cada canje se anula dos veces a la vez: la anulación debe ser idempotente
        var voids = initial.SelectMany(o => new[] { o.Result!.Id, o.Result!.Id })
            .Select(redemptionId => RunInScopeAsync(async service =>
            {
                await service.VoidRedemptionAsync(redemptionId);
                return true;
            }));
        var redeems = secondHalf.Select(async id => (await RedeemAsync(scenario, id)).Error);

        var voidOutcomes = Task.WhenAll(voids);
        var redeemErrors = Task.WhenAll(redeems);
        await Task.WhenAll(voidOutcomes, redeemErrors);

        Assert.All(voidOutcomes.Result, o => Assert.Null(o.Error));
        Assert.All(redeemErrors.Result, Assert.Null);

        await using var db = _fixture.CreateDbContext();
        var activeRedemptions = await db.PromotionRedemptions.CountAsync(r => r.PromotionId == scenario.PromotionId && r.IsActive);
        Assert.Equal(4, activeRedemptions);
        Assert.Equal(activeRedemptions, (await db.Promotions.SingleAsync(p => p.Id == scenario.PromotionId)).CurrentUsesTotal);
    }

    [Fact]
    public async Task Database_RejectsCountersAboveTheLimitAndASecondActiveRedemptionPerMembership()
    {
        var scenario = await SeedAsync(students: 1, maxUsesTotal: 2);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await using (var aboveLimit = conn.CreateCommand())
        {
            aboveLimit.CommandText = $@"UPDATE ""Promotions"" SET ""CurrentUsesTotal"" = 3 WHERE ""Id"" = '{scenario.PromotionId}';";
            var ex = await Assert.ThrowsAsync<PostgresException>(() => aboveLimit.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
            Assert.Equal("CK_Promotions_CurrentUsesTotal", ex.ConstraintName);
        }

        await using (var negative = conn.CreateCommand())
        {
            negative.CommandText = $@"UPDATE ""Promotions"" SET ""CurrentUsesTotal"" = -1 WHERE ""Id"" = '{scenario.PromotionId}';";
            var ex = await Assert.ThrowsAsync<PostgresException>(() => negative.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        }

        await using (var invalidStatus = conn.CreateCommand())
        {
            invalidStatus.CommandText = $@"UPDATE ""Promotions"" SET ""Status"" = 9 WHERE ""Id"" = '{scenario.PromotionId}';";
            var ex = await Assert.ThrowsAsync<PostgresException>(() => invalidStatus.ExecuteNonQueryAsync());
            Assert.Equal("CK_Promotions_Status", ex.ConstraintName);
        }
    }
}
