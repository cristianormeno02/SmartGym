using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SmartGym.Domain.Enums;
using SmartGym.IntegrationTests.Fixtures;
using Xunit;

namespace SmartGym.IntegrationTests.Promotions;

[Collection("PostgresIntegration")]
public class HardenPromotionsMigrationIntegrationTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public HardenPromotionsMigrationIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HardenPromotionsModule_MigratesExistingRows_Correctly()
    {
        var connString = await _fixture.CreateIsolatedDatabaseAsync(applyAllMigrations: false);

        // 1. Migrate up to 20261011190005_RefactorReservationsModule (just before HardenPromotionsModule)
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20261011190005_RefactorReservationsModule");
        }

        var student1Id = Guid.NewGuid();
        var student2Id = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        var promoActiveId = Guid.NewGuid();
        var promoDisabledId = Guid.NewGuid();
        var promoDriftedId = Guid.NewGuid();
        var promoOverCapId = Guid.NewGuid();

        var redemptionReferencedId = Guid.NewGuid();
        var redemptionDuplicateNewerId = Guid.NewGuid();
        var redemptionDriftedActiveId = Guid.NewGuid();
        var redemptionDriftedVoidedId = Guid.NewGuid();
        var redemptionOverCap1Id = Guid.NewGuid();
        var redemptionOverCap2Id = Guid.NewGuid();

        var now = new DateTime(2026, 10, 11, 15, 0, 0, DateTimeKind.Utc);

        // 2. Seed rows using the schema before HardenPromotionsModule
        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
INSERT INTO ""People"" (""Id"", ""FirstName"", ""LastName"", ""SearchName"", ""Email"", ""Status"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    ('{student1Id}', 'Ana', 'Perez', 'ana perez', 'ana.promo@test.com', 'Active', '{now:O}', TRUE),
    ('{student2Id}', 'Luis', 'Gomez', 'luis gomez', 'luis.promo@test.com', 'Active', '{now:O}', TRUE);

INSERT INTO ""MembershipPlans"" (""Id"", ""Name"", ""Description"", ""Price"", ""DurationDays"", ""Credits"", ""IsUnlimited"", ""AppliesToAllActivities"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{planId}', 'Plan 8 Promo', 'Desc', 10000, 30, 8, FALSE, TRUE, '{now:O}', TRUE);

INSERT INTO ""Promotions"" (""Id"", ""Code"", ""Name"", ""ConditionType"", ""BenefitType"", ""BenefitValue"", ""ValidFrom"", ""MaxUsesTotal"", ""CurrentUsesTotal"", ""MaxUsesPerPerson"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    -- 1. Vigente, con dos canjes vigentes sobre la misma membresía
    ('{promoActiveId}', 'MIG_ACTIVE', 'Activa', 5, 1, 10, '2026-01-01', NULL, 2, 5, '{now:O}', TRUE),
    -- 2. Desactivada
    ('{promoDisabledId}', 'MIG_DISABLED', 'Desactivada', 5, 1, 10, '2026-01-01', 10, 0, 1, '{now:O}', FALSE),
    -- 3. Contador desviado: 7 usos registrados, un único canje vigente
    ('{promoDriftedId}', 'MIG_DRIFTED', 'Desviada', 5, 1, 10, '2026-01-01', 10, 7, 1, '{now:O}', TRUE),
    -- 4. Tope general superado: tope 1, dos canjes vigentes
    ('{promoOverCapId}', 'MIG_OVERCAP', 'Excedida', 5, 1, 10, '2026-01-01', 1, 1, 1, '{now:O}', TRUE);

INSERT INTO ""Memberships"" (""Id"", ""StudentId"", ""MembershipPlanId"", ""StartDate"", ""EndDate"", ""PurchasedAtUtc"", ""ActivatedAtUtc"", ""DurationDaysSnapshot"", ""InitialCredits"", ""ExtraCreditsGranted"", ""TotalCredits"", ""AvailableCredits"", ""IsUnlimited"", ""AppliesToAllActivities"", ""InitialCreditsLoaded"", ""OriginalPrice"", ""DiscountAmount"", ""PurchasePrice"", ""Currency"", ""Status"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    ('{membershipId}', '{student1Id}', '{planId}', '2026-10-01', '2026-10-31', '{now:O}', '{now:O}', 31, 8, 0, 8, 8, FALSE, TRUE, TRUE, 10000, 1000, 9000, 'ARS', 1, '{now:O}', TRUE);

INSERT INTO ""PromotionRedemptions"" (""Id"", ""PromotionId"", ""PersonId"", ""MembershipId"", ""OriginalAmount"", ""DiscountAmount"", ""FinalAmount"", ""ExtraCreditsGranted"", ""RedeemedAtUtc"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    ('{redemptionReferencedId}', '{promoActiveId}', '{student1Id}', '{membershipId}', 10000, 1000, 9000, 0, '{now.AddHours(-2):O}', '{now.AddHours(-2):O}', TRUE),
    ('{redemptionDuplicateNewerId}', '{promoActiveId}', '{student1Id}', '{membershipId}', 10000, 1000, 9000, 0, '{now.AddHours(-1):O}', '{now.AddHours(-1):O}', TRUE),
    ('{redemptionDriftedActiveId}', '{promoDriftedId}', '{student1Id}', NULL, 10000, 1000, 9000, 0, '{now:O}', '{now:O}', TRUE),
    ('{redemptionDriftedVoidedId}', '{promoDriftedId}', '{student2Id}', NULL, 10000, 1000, 9000, 0, '{now:O}', '{now:O}', FALSE),
    ('{redemptionOverCap1Id}', '{promoOverCapId}', '{student1Id}', NULL, 10000, 1000, 9000, 0, '{now:O}', '{now:O}', TRUE),
    ('{redemptionOverCap2Id}', '{promoOverCapId}', '{student2Id}', NULL, 10000, 1000, 9000, 0, '{now:O}', '{now:O}', TRUE);

UPDATE ""Memberships"" SET ""PromotionId"" = '{promoActiveId}', ""PromotionRedemptionId"" = '{redemptionReferencedId}' WHERE ""Id"" = '{membershipId}';
";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Apply HardenPromotionsModule migration
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20261012032939_HardenPromotionsModule");
        }

        // 4. Verify each data migration step
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            // Desactivada -> Paused, y sigue siendo consultable
            var disabled = await dbContext.Promotions.SingleAsync(p => p.Id == promoDisabledId);
            Assert.Equal(PromotionStatus.Paused, disabled.Status);
            Assert.True(disabled.IsActive);

            // La membresía conserva el canje que referencia aunque el duplicado sea más reciente
            var referenced = await dbContext.PromotionRedemptions.SingleAsync(r => r.Id == redemptionReferencedId);
            var duplicate = await dbContext.PromotionRedemptions.SingleAsync(r => r.Id == redemptionDuplicateNewerId);
            Assert.True(referenced.IsActive);
            Assert.False(duplicate.IsActive);

            var active = await dbContext.Promotions.SingleAsync(p => p.Id == promoActiveId);
            Assert.Equal(PromotionStatus.Active, active.Status);
            Assert.Equal(1, active.CurrentUsesTotal);

            // Contador recalculado desde los canjes vigentes
            var drifted = await dbContext.Promotions.SingleAsync(p => p.Id == promoDriftedId);
            Assert.Equal(1, drifted.CurrentUsesTotal);
            Assert.Equal(10, drifted.MaxUsesTotal);

            // Tope general elevado a lo ya consumido
            var overCap = await dbContext.Promotions.SingleAsync(p => p.Id == promoOverCapId);
            Assert.Equal(2, overCap.CurrentUsesTotal);
            Assert.Equal(2, overCap.MaxUsesTotal);
        }

        // 5. Las defensas quedan activas en la base
        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();

            await using var overCapCmd = conn.CreateCommand();
            overCapCmd.CommandText = $@"UPDATE ""Promotions"" SET ""CurrentUsesTotal"" = 3 WHERE ""Id"" = '{promoOverCapId}';";
            var checkEx = await Assert.ThrowsAsync<PostgresException>(() => overCapCmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, checkEx.SqlState);
            Assert.Equal("CK_Promotions_CurrentUsesTotal", checkEx.ConstraintName);

            await using var duplicateCmd = conn.CreateCommand();
            duplicateCmd.CommandText = $@"UPDATE ""PromotionRedemptions"" SET ""IsActive"" = TRUE WHERE ""Id"" = '{redemptionDuplicateNewerId}';";
            var uniqueEx = await Assert.ThrowsAsync<PostgresException>(() => duplicateCmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, uniqueEx.SqlState);
            Assert.Equal("UX_PromotionRedemptions_Membership", uniqueEx.ConstraintName);
        }
    }
}
