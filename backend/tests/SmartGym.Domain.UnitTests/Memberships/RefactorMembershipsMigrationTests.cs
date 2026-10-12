using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SmartGym.Infrastructure.Persistence.Migrations;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class RefactorMembershipsMigrationTests
{
    private static IReadOnlyList<MigrationOperation> UpOperations() =>
        new RefactorMembershipsModule { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.UpOperations;

    private static IReadOnlyList<MigrationOperation> DownOperations() =>
        new RefactorMembershipsModule { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.DownOperations;

    [Fact]
    public void Up_Operations_CreateTablesColumnsAndMigrateDataInOrder()
    {
        var ops = UpOperations().ToList();

        // 1. Creates new tables MembershipAllowedActivities, MembershipStatusChanges, MembershipSuspensions
        Assert.Contains(ops.OfType<CreateTableOperation>(), t => t.Name == "MembershipAllowedActivities");
        Assert.Contains(ops.OfType<CreateTableOperation>(), t => t.Name == "MembershipStatusChanges");
        Assert.Contains(ops.OfType<CreateTableOperation>(), t => t.Name == "MembershipSuspensions");

        // 2. Adds new snapshot and audit columns to Memberships
        var addedColumns = ops.OfType<AddColumnOperation>()
            .Where(c => c.Table == "Memberships")
            .Select(c => c.Name)
            .ToList();

        Assert.Contains("PurchasedAtUtc", addedColumns);
        Assert.Contains("ActivatedAtUtc", addedColumns);
        Assert.Contains("DurationDaysSnapshot", addedColumns);
        Assert.Contains("InitialCredits", addedColumns);
        Assert.Contains("ExtraCreditsGranted", addedColumns);
        Assert.Contains("IsUnlimited", addedColumns);
        Assert.Contains("AppliesToAllActivities", addedColumns);
        Assert.Contains("InitialCreditsLoaded", addedColumns);
        Assert.Contains("OriginalPrice", addedColumns);
        Assert.Contains("DiscountAmount", addedColumns);
        Assert.Contains("PurchasePrice", addedColumns);
        Assert.Contains("Currency", addedColumns);
        Assert.Contains("PromotionId", addedColumns);
        Assert.Contains("PromotionRedemptionId", addedColumns);
        Assert.Contains("RenewedFromMembershipId", addedColumns);
        Assert.Contains("CreatedByUserId", addedColumns);
        Assert.Contains("CancelledAtUtc", addedColumns);
        Assert.Contains("CancelledByUserId", addedColumns);
        Assert.Contains("CancellationReason", addedColumns);
        Assert.Contains("Notes", addedColumns);
        Assert.Contains("IdempotencyKey", addedColumns);
        Assert.Contains("ExpiringNotificationSentAtUtc", addedColumns);
        Assert.Contains("ExpiredNotificationSentAtUtc", addedColumns);
        Assert.DoesNotContain("xmin", addedColumns);

        // 3. SQL data migration steps
        var sqlTexts = ops.OfType<SqlOperation>().Select(s => s.Sql).ToList();
        var combinedSql = string.Join("\n", sqlTexts);

        // Populates commercial snapshot from MembershipPlans and leaves estimated price note
        Assert.Contains("MembershipPlans", combinedSql);
        Assert.Contains("Precio estimado desde el plan vigente al migrar", combinedSql);
        Assert.Contains("MembershipAllowedActivities", combinedSql);
        Assert.Contains("MembershipPlanActivities", combinedSql);

        // Uses local gym timezone date
        Assert.Contains("AT TIME ZONE 'America/Argentina/San_Juan'", combinedSql);

        // Remaps Status = 5 (Depleted) -> 1 (Active)
        Assert.Contains("Status\" = 5", combinedSql);

        // Closes already-expired memberships (Status = 2, Expiration movement = 9, notification timestamps)
        Assert.Contains("Status\" = 2", combinedSql);
        Assert.Contains("MembershipCreditMovements", combinedSql);
        Assert.Contains("ExpiredNotificationSentAtUtc", combinedSql);
        Assert.Contains("ExpiringNotificationSentAtUtc", combinedSql);

        // Populates CancelledAtUtc and CancellationReason for Status = 4
        Assert.Contains("Cancelación previa a migración", combinedSql);

        // Inserts initial MembershipStatusChanges row
        Assert.Contains("MembershipStatusChanges", combinedSql);
        Assert.Contains("Estado migrado", combinedSql);

        // 4. Changes MembershipCreditMovements.MembershipId FK to Restrict
        var addLedgerFk = Assert.Single(
            ops.OfType<AddForeignKeyOperation>(),
            fk => fk.Table == "MembershipCreditMovements" && fk.PrincipalTable == "Memberships");
        Assert.Equal(ReferentialAction.Restrict, addLedgerFk.OnDelete);
    }

    [Fact]
    public void Down_Operations_AreProvidedAndRevertSchemaChanges()
    {
        var downOps = DownOperations().ToList();
        Assert.NotEmpty(downOps);
        Assert.Contains(downOps.OfType<DropTableOperation>(), t => t.Name == "MembershipAllowedActivities");
        Assert.Contains(downOps.OfType<DropTableOperation>(), t => t.Name == "MembershipStatusChanges");
        Assert.Contains(downOps.OfType<DropTableOperation>(), t => t.Name == "MembershipSuspensions");
    }
}
