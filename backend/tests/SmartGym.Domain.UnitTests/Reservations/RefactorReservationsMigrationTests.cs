using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SmartGym.Infrastructure.Persistence.Migrations;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class RefactorReservationsMigrationTests
{
    private static IReadOnlyList<MigrationOperation> UpOperations() =>
        new RefactorReservationsModule { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.UpOperations;

    private static IReadOnlyList<MigrationOperation> DownOperations() =>
        new RefactorReservationsModule { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.DownOperations;

    [Fact]
    public void Up_Operations_CreateTablesColumnsAndMigrateDataInOrder()
    {
        var ops = UpOperations().ToList();

        // 1. Creates ReservationStatusChanges table
        Assert.Contains(ops.OfType<CreateTableOperation>(), t => t.Name == "ReservationStatusChanges");

        // 2. Adds new columns to Reservations, MembershipCreditMovements, PromotionRedemptions (excluding xmin)
        var addedReservationColumns = ops.OfType<AddColumnOperation>()
            .Where(c => c.Table == "Reservations")
            .Select(c => c.Name)
            .ToList();

        Assert.Contains("Origin", addedReservationColumns);
        Assert.Contains("AccessType", addedReservationColumns);
        Assert.Contains("SingleClassTicketMovementId", addedReservationColumns);
        Assert.Contains("PaymentId", addedReservationColumns);
        Assert.Contains("PromotionRedemptionId", addedReservationColumns);
        Assert.Contains("RequestedPromotionCode", addedReservationColumns);
        Assert.Contains("CreatedByUserId", addedReservationColumns);
        Assert.Contains("WaitlistEnteredAtUtc", addedReservationColumns);
        Assert.Contains("WaitlistPromotedAtUtc", addedReservationColumns);
        Assert.Contains("WaitlistOfferExpiresAtUtc", addedReservationColumns);
        Assert.Contains("CancelledByUserId", addedReservationColumns);
        Assert.Contains("CancellationSource", addedReservationColumns);
        Assert.Contains("IsPolicyOverride", addedReservationColumns);
        Assert.Contains("PolicyOverrideReason", addedReservationColumns);
        Assert.Contains("AffectedBySessionChange", addedReservationColumns);
        Assert.Contains("RescheduledToSessionId", addedReservationColumns);
        Assert.Contains("Notes", addedReservationColumns);
        Assert.Contains("IdempotencyKey", addedReservationColumns);
        Assert.DoesNotContain("xmin", addedReservationColumns);

        var addedMovementColumns = ops.OfType<AddColumnOperation>()
            .Where(c => c.Table == "MembershipCreditMovements")
            .Select(c => c.Name)
            .ToList();
        Assert.Contains("ReservationId", addedMovementColumns);
        Assert.Contains("ExpiresOn", addedMovementColumns);
        Assert.Contains("ActivityId", addedMovementColumns);

        var addedPromoColumns = ops.OfType<AddColumnOperation>()
            .Where(c => c.Table == "PromotionRedemptions")
            .Select(c => c.Name)
            .ToList();
        Assert.Contains("ReservationId", addedPromoColumns);

        // 3. SQL data migration steps
        var sqlTexts = ops.OfType<SqlOperation>().Select(s => s.Sql).ToList();
        var combinedSql = string.Join("\n", sqlTexts);

        // Merges Status 2 -> 1 and populates ConfirmedAtUtc / WaitlistEnteredAtUtc
        Assert.Contains("Status\" IN (1, 2)", combinedSql);
        Assert.Contains("WaitlistEnteredAtUtc", combinedSql);

        // Sets Origin = 3 (Legacy)
        Assert.Contains("\"Origin\" = 3", combinedSql);

        // AccessType and moving SingleClassTicketMovementId
        Assert.Contains("SingleClassTicketMovementId", combinedSql);
        Assert.Contains("IsUnlimited", combinedSql);

        // Deduplicates shared SingleClassTicketMovementId with note
        Assert.Contains("Pase compartido con otra reserva antes de la migración", combinedSql);

        // Populates CancellationReason and CancelledAtUtc for cancelled reservations
        Assert.Contains("Cancelación previa a migración", combinedSql);

        // Populates MembershipCreditMovements.ReservationId for Type IN (2, 3)
        Assert.Contains("MembershipCreditMovements", combinedSql);

        // Inserts initial ReservationStatusChanges row with Trigger = 8 and Reason = 'Estado migrado'
        Assert.Contains("ReservationStatusChanges", combinedSql);
        Assert.Contains("Estado migrado", combinedSql);

        // 4. Changes FKs to Restrict
        var addReservationFks = ops.OfType<AddForeignKeyOperation>()
            .Where(fk => fk.Table == "Reservations")
            .ToList();
        Assert.NotEmpty(addReservationFks);
        Assert.All(addReservationFks, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
    }

    [Fact]
    public void Down_Operations_AreProvidedAndRevertSchemaChanges()
    {
        var downOps = DownOperations().ToList();
        Assert.NotEmpty(downOps);
        Assert.Contains(downOps.OfType<DropTableOperation>(), t => t.Name == "ReservationStatusChanges");
        Assert.DoesNotContain(downOps.OfType<DropColumnOperation>(), c => c.Name == "xmin");
    }
}
