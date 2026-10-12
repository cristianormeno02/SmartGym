using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SmartGym.Domain.Enums;
using SmartGym.IntegrationTests.Fixtures;
using Xunit;

namespace SmartGym.IntegrationTests.Payments;

[Collection("PostgresIntegration")]
public class HardenPaymentsMigrationIntegrationTests
{
    private const string PreviousMigration = "20261012032939_HardenPromotionsModule";
    private const string TargetMigration = "20261012131844_HardenPaymentsModule";

    private readonly PostgresDatabaseFixture _fixture;

    public HardenPaymentsMigrationIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HardenPaymentsModule_MigratesExistingRows_Correctly()
    {
        var connString = await _fixture.CreateIsolatedDatabaseAsync(applyAllMigrations: false);

        // 1. Migrate up to the migration just before HardenPaymentsModule
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
        }

        var studentId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        var walletCompletedId = Guid.NewGuid();
        var cashRefundedId = Guid.NewGuid();
        var transferPendingId = Guid.NewGuid();
        var unlinkedFailedId = Guid.NewGuid();

        var paidAt = new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc);
        var refundedAt = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

        // 2. Seed rows using the schema before HardenPaymentsModule
        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
INSERT INTO ""People"" (""Id"", ""FirstName"", ""LastName"", ""SearchName"", ""Email"", ""Status"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{studentId}', 'Ana', 'Perez', 'ana perez', 'ana.pagos@test.com', 'Active', '{paidAt:O}', TRUE);

INSERT INTO ""MembershipPlans"" (""Id"", ""Name"", ""Description"", ""Price"", ""DurationDays"", ""Credits"", ""IsUnlimited"", ""AppliesToAllActivities"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{planId}', 'Plan Pagos', 'Desc', 100, 30, 8, FALSE, TRUE, '{paidAt:O}', TRUE);

-- Membresía en USD para verificar que el pago hereda su moneda
INSERT INTO ""Memberships"" (""Id"", ""StudentId"", ""MembershipPlanId"", ""StartDate"", ""EndDate"", ""PurchasedAtUtc"", ""ActivatedAtUtc"", ""DurationDaysSnapshot"", ""InitialCredits"", ""ExtraCreditsGranted"", ""TotalCredits"", ""AvailableCredits"", ""IsUnlimited"", ""AppliesToAllActivities"", ""InitialCreditsLoaded"", ""OriginalPrice"", ""DiscountAmount"", ""PurchasePrice"", ""Currency"", ""Status"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{membershipId}', '{studentId}', '{planId}', '2026-10-01', '2026-10-31', '{paidAt:O}', '{paidAt:O}', 31, 8, 0, 8, 8, FALSE, TRUE, TRUE, 100, 0, 100, 'USD', 1, '{paidAt:O}', TRUE);

INSERT INTO ""Payments"" (""Id"", ""PayerPersonId"", ""MembershipId"", ""Amount"", ""Method"", ""Status"", ""Notes"", ""PaidAtUtc"", ""CreatedAtUtc"", ""UpdatedAtUtc"", ""IsActive"")
VALUES
    -- 1. Confirmado con el antiguo medio MercadoPago (5), aplicado a la membresía
    ('{walletCompletedId}', '{studentId}', '{membershipId}', 60.00, 5, 2, NULL, '{paidAt:O}', '{paidAt:O}', NULL, TRUE),
    -- 2. Devuelto por el total con el modelo anterior
    ('{cashRefundedId}', '{studentId}', NULL, 25000.00, 1, 4, 'Reembolso: error de cobro', '{paidAt:O}', '{paidAt:O}', '{refundedAt:O}', TRUE),
    -- 3. Pendiente
    ('{transferPendingId}', '{studentId}', NULL, 18000.00, 2, 1, NULL, '{paidAt:O}', '{paidAt:O}', NULL, TRUE),
    -- 4. Rechazado
    ('{unlinkedFailedId}', '{studentId}', NULL, 9000.00, 2, 3, NULL, '{paidAt:O}', '{paidAt:O}', NULL, TRUE);
";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Apply HardenPaymentsModule migration
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync(TargetMigration);
        }

        // 4. Verify each data migration step
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            // El antiguo MercadoPago queda como billetera digital confirmada a mano
            var wallet = await dbContext.Payments.SingleAsync(p => p.Id == walletCompletedId);
            Assert.Equal(PaymentMethod.DigitalWallet, wallet.Method);
            Assert.Equal(PaymentProvider.Manual, wallet.Provider);
            Assert.Equal(PaymentStatus.Completed, wallet.Status);
            Assert.Equal("USD", wallet.Currency);
            Assert.Equal(paidAt, wallet.ConfirmedAtUtc);
            Assert.Equal(0m, wallet.RefundedAmount);
            Assert.Equal(60.00m, wallet.NetAmount);

            // La devolución total anterior conserva el importe y gana su fila de detalle
            var refunded = await dbContext.Payments.Include(p => p.Refunds).SingleAsync(p => p.Id == cashRefundedId);
            Assert.Equal(PaymentStatus.Refunded, refunded.Status);
            Assert.Equal("ARS", refunded.Currency);
            Assert.Equal(25000.00m, refunded.Amount);
            Assert.Equal(25000.00m, refunded.RefundedAmount);
            Assert.Equal(paidAt, refunded.ConfirmedAtUtc);

            var refund = Assert.Single(refunded.Refunds);
            Assert.Equal(25000.00m, refund.Amount);
            Assert.Equal("ARS", refund.Currency);
            Assert.Null(refund.RequestedByUserId);
            Assert.Equal(refundedAt, refund.CreatedAtUtc);
            Assert.False(string.IsNullOrWhiteSpace(refund.Reason));

            // Pendientes y rechazados no se confirman ni generan devoluciones
            var pending = await dbContext.Payments.SingleAsync(p => p.Id == transferPendingId);
            Assert.Null(pending.ConfirmedAtUtc);
            Assert.Equal(0m, pending.RefundedAmount);

            var failed = await dbContext.Payments.SingleAsync(p => p.Id == unlinkedFailedId);
            Assert.Null(failed.ConfirmedAtUtc);

            Assert.Equal(1, await dbContext.PaymentRefunds.CountAsync());
        }

        // 5. Las defensas quedan activas en la base
        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();

            await using var overRefundCmd = conn.CreateCommand();
            overRefundCmd.CommandText = $@"UPDATE ""Payments"" SET ""RefundedAmount"" = 60.01 WHERE ""Id"" = '{walletCompletedId}';";
            var amountsEx = await Assert.ThrowsAsync<PostgresException>(() => overRefundCmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, amountsEx.SqlState);
            Assert.Equal("CK_Payments_Amounts", amountsEx.ConstraintName);

            // Una clase cualquiera inexistente alcanza: el CHECK se evalúa antes que la FK
            await using var doubleTargetCmd = conn.CreateCommand();
            doubleTargetCmd.CommandText = $@"UPDATE ""Payments"" SET ""ClassSessionId"" = '{Guid.NewGuid()}' WHERE ""Id"" = '{walletCompletedId}';";
            var targetEx = await Assert.ThrowsAsync<PostgresException>(() => doubleTargetCmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, targetEx.SqlState);
            Assert.Equal("CK_Payments_SingleTarget", targetEx.ConstraintName);

            await using var deleteMembershipCmd = conn.CreateCommand();
            deleteMembershipCmd.CommandText = $@"DELETE FROM ""Memberships"" WHERE ""Id"" = '{membershipId}';";
            var fkEx = await Assert.ThrowsAsync<PostgresException>(() => deleteMembershipCmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, fkEx.SqlState);
        }
    }

    [Fact]
    public async Task HardenPaymentsModule_Down_RestoresPreviousStatuses()
    {
        var connString = await _fixture.CreateIsolatedDatabaseAsync(applyAllMigrations: false);

        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(TargetMigration);
        }

        var studentId = Guid.NewGuid();
        var partiallyRefundedId = Guid.NewGuid();
        var cancelledId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);

        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
INSERT INTO ""People"" (""Id"", ""FirstName"", ""LastName"", ""SearchName"", ""Email"", ""Status"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{studentId}', 'Luis', 'Gomez', 'luis gomez', 'luis.pagos@test.com', 'Active', '{now:O}', TRUE);

INSERT INTO ""Payments"" (""Id"", ""PayerPersonId"", ""Amount"", ""RefundedAmount"", ""Method"", ""Status"", ""PaidAtUtc"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    ('{partiallyRefundedId}', '{studentId}', 30000.00, 10000.00, 1, 6, '{now:O}', '{now:O}', TRUE),
    ('{cancelledId}', '{studentId}', 5000.00, 0, 1, 5, '{now:O}', '{now:O}', TRUE);

INSERT INTO ""PaymentRefunds"" (""Id"", ""PaymentId"", ""Amount"", ""Currency"", ""Reason"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{Guid.NewGuid()}', '{partiallyRefundedId}', 10000.00, 'ARS', 'Parcial', '{now:O}', TRUE);
";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"SELECT ""Id"", ""Status"" FROM ""Payments"";";
            var statuses = new Dictionary<Guid, int>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                statuses[reader.GetGuid(0)] = reader.GetInt32(1);
            }

            Assert.Equal((int)PaymentStatus.Completed, statuses[partiallyRefundedId]);
            Assert.Equal((int)PaymentStatus.Failed, statuses[cancelledId]);
        }
    }
}
