using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SmartGym.Domain.Enums;
using SmartGym.IntegrationTests.Fixtures;
using Xunit;

namespace SmartGym.IntegrationTests.Payments;

[Collection("PostgresIntegration")]
public class AddPaymentGatewayMigrationIntegrationTests
{
    private const string PreviousMigration = "20261012131844_HardenPaymentsModule";
    private const string TargetMigration = "20261012183440_AddPaymentGateway";

    private readonly PostgresDatabaseFixture _fixture;

    public AddPaymentGatewayMigrationIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddPaymentGateway_MigratesExistingRefunds_AndEnforcesGatewayIndexes()
    {
        var connString = await _fixture.CreateIsolatedDatabaseAsync(applyAllMigrations: false);

        // 1. Migrate up to the migration just before AddPaymentGateway
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        var studentId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var refundedPaymentId = Guid.NewGuid();
        var manualA = Guid.NewGuid();
        var manualB = Guid.NewGuid();
        var refundId = Guid.NewGuid();

        var paidAt = new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc);
        var refundedAt = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

        // 2. Seed rows using the schema before AddPaymentGateway
        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
INSERT INTO ""People"" (""Id"", ""FirstName"", ""LastName"", ""SearchName"", ""Email"", ""Status"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{studentId}', 'Ana', 'Perez', 'ana perez', 'ana.gateway@test.com', 'Active', '{paidAt:O}', TRUE);

INSERT INTO ""MembershipPlans"" (""Id"", ""Name"", ""Description"", ""Price"", ""DurationDays"", ""Credits"", ""IsUnlimited"", ""AppliesToAllActivities"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{planId}', 'Plan Gateway', 'Desc', 60000, 30, 8, FALSE, TRUE, '{paidAt:O}', TRUE);

INSERT INTO ""Memberships"" (""Id"", ""StudentId"", ""MembershipPlanId"", ""StartDate"", ""EndDate"", ""PurchasedAtUtc"", ""DurationDaysSnapshot"", ""InitialCredits"", ""ExtraCreditsGranted"", ""TotalCredits"", ""AvailableCredits"", ""IsUnlimited"", ""AppliesToAllActivities"", ""InitialCreditsLoaded"", ""OriginalPrice"", ""DiscountAmount"", ""PurchasePrice"", ""Currency"", ""Status"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{membershipId}', '{studentId}', '{planId}', '2026-10-01', '2026-10-31', '{paidAt:O}', 31, 8, 0, 8, 0, FALSE, TRUE, FALSE, 60000, 0, 60000, 'ARS', 0, '{paidAt:O}', TRUE);

INSERT INTO ""Payments"" (""Id"", ""PayerPersonId"", ""Amount"", ""RefundedAmount"", ""Currency"", ""Method"", ""Provider"", ""Status"", ""ExternalTransactionId"", ""PaidAtUtc"", ""ConfirmedAtUtc"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    ('{refundedPaymentId}', '{studentId}', 30000.00, 10000.00, 'ARS', 1, 1, 6, NULL, '{paidAt:O}', '{paidAt:O}', '{paidAt:O}', TRUE),
    -- Dos pagos manuales con el mismo número de comprobante: texto libre del operador
    ('{manualA}', '{studentId}', 5000.00, 0, 'ARS', 2, 1, 2, 'TRF-0001', '{paidAt:O}', '{paidAt:O}', '{paidAt:O}', TRUE),
    ('{manualB}', '{studentId}', 5000.00, 0, 'ARS', 2, 1, 2, 'TRF-0001', '{paidAt:O}', '{paidAt:O}', '{paidAt:O}', TRUE);

INSERT INTO ""PaymentRefunds"" (""Id"", ""PaymentId"", ""Amount"", ""Currency"", ""Reason"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{refundId}', '{refundedPaymentId}', 10000.00, 'ARS', 'Ajuste', '{refundedAt:O}', TRUE);
";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Apply AddPaymentGateway: no debe fallar por los pagos manuales con la misma referencia
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(TargetMigration);
        }

        // 4. Las devoluciones existentes quedan completadas en su fecha de registro
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var refund = await dbContext.PaymentRefunds.SingleAsync(r => r.Id == refundId);
            Assert.Equal(PaymentRefundStatus.Completed, refund.Status);
            Assert.Equal(refundedAt, refund.CompletedAtUtc);
            Assert.Null(refund.ExternalRefundId);

            var payment = await dbContext.Payments.SingleAsync(p => p.Id == refundedPaymentId);
            Assert.Null(payment.CheckoutUrl);
            Assert.Null(payment.ExpiresAtUtc);
            Assert.False(payment.IsExternal);
        }

        // 5. Los índices de la pasarela quedan activos en la base
        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();

            async Task<PostgresException> AssertUniqueViolationAsync(string sql)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
                return ex;
            }

            async Task ExecuteAsync(string sql)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }

            string ExternalPayment(Guid id, int status, string? externalTx) => $@"
INSERT INTO ""Payments"" (""Id"", ""PayerPersonId"", ""MembershipId"", ""Amount"", ""RefundedAmount"", ""Currency"", ""Method"", ""Provider"", ""Status"", ""ExternalTransactionId"", ""PaidAtUtc"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{id}', '{studentId}', '{membershipId}', 60000.00, 0, 'ARS', 5, 2, {status}, {(externalTx == null ? "NULL" : $"'{externalTx}'")}, '{paidAt:O}', '{paidAt:O}', TRUE);";

            // Un checkout abierto por membresía; uno cancelado no cuenta
            await ExecuteAsync(ExternalPayment(Guid.NewGuid(), 5, null));
            await ExecuteAsync(ExternalPayment(Guid.NewGuid(), 1, null));
            var openEx = await AssertUniqueViolationAsync(ExternalPayment(Guid.NewGuid(), 1, null));
            Assert.Equal("UX_Payments_OpenCheckout", openEx.ConstraintName);

            // Una transacción del proveedor confirma un único pago
            await ExecuteAsync(ExternalPayment(Guid.NewGuid(), 2, "MP-123"));
            var txEx = await AssertUniqueViolationAsync(ExternalPayment(Guid.NewGuid(), 2, "MP-123"));
            Assert.Equal("UX_Payments_Provider_ExternalTx", txEx.ConstraintName);

            // Evento duplicado del proveedor
            string WebhookEvent(string eventId) => $@"
INSERT INTO ""PaymentWebhookEvents"" (""Id"", ""Provider"", ""ExternalEventId"", ""Status"", ""ReceivedAtUtc"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{Guid.NewGuid()}', 2, '{eventId}', 2, '{paidAt:O}', '{paidAt:O}', TRUE);";
            await ExecuteAsync(WebhookEvent("evt-1"));
            var eventEx = await AssertUniqueViolationAsync(WebhookEvent("evt-1"));
            Assert.Equal("UX_PaymentWebhookEvents_Provider_ExternalEventId", eventEx.ConstraintName);

            // Una sola discrepancia abierta por causa; resuelta la primera, se admite otra
            string Discrepancy(Guid id, Guid? paymentId, int status) => $@"
INSERT INTO ""PaymentDiscrepancies"" (""Id"", ""PaymentId"", ""Provider"", ""Type"", ""Status"", ""ExternalTransactionId"", ""DetectedAtUtc"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{id}', {(paymentId == null ? "NULL" : $"'{paymentId}'")}, 2, {(paymentId == null ? 7 : 1)}, {status}, 'MP-999', '{paidAt:O}', '{paidAt:O}', TRUE);";

            var firstDiscrepancy = Guid.NewGuid();
            await ExecuteAsync(Discrepancy(firstDiscrepancy, manualA, 1));
            var byPaymentEx = await AssertUniqueViolationAsync(Discrepancy(Guid.NewGuid(), manualA, 1));
            Assert.Equal("UX_PaymentDiscrepancies_Open_Payment", byPaymentEx.ConstraintName);
            await ExecuteAsync($@"UPDATE ""PaymentDiscrepancies"" SET ""Status"" = 2 WHERE ""Id"" = '{firstDiscrepancy}';");
            await ExecuteAsync(Discrepancy(Guid.NewGuid(), manualA, 1));

            await ExecuteAsync(Discrepancy(Guid.NewGuid(), null, 1));
            var unknownEx = await AssertUniqueViolationAsync(Discrepancy(Guid.NewGuid(), null, 1));
            Assert.Equal("UX_PaymentDiscrepancies_Open_Unknown", unknownEx.ConstraintName);
        }
    }
}
