using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipModelConfigurationTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options;
        using var dbContext = new SmartGymDbContext(options);
        return dbContext.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void Membership_PropertiesCheckConstraintsIndexesAndFks_ShouldBeConfigured()
    {
        var model = BuildModel();
        var membershipType = model.FindEntityType(typeof(Membership))!;

        Assert.Equal("Memberships", membershipType.GetTableName());

        // RowVersion (xmin)
        var versionProp = membershipType.FindProperty(nameof(Membership.Version))!;
        Assert.True(versionProp.IsConcurrencyToken);

        // Numeric precision
        var originalPriceProp = membershipType.FindProperty(nameof(Membership.OriginalPrice))!;
        Assert.Equal(18, originalPriceProp.GetPrecision());
        Assert.Equal(2, originalPriceProp.GetScale());

        var discountAmountProp = membershipType.FindProperty(nameof(Membership.DiscountAmount))!;
        Assert.Equal(18, discountAmountProp.GetPrecision());
        Assert.Equal(2, discountAmountProp.GetScale());

        var purchasePriceProp = membershipType.FindProperty(nameof(Membership.PurchasePrice))!;
        Assert.Equal(18, purchasePriceProp.GetPrecision());
        Assert.Equal(2, purchasePriceProp.GetScale());

        // Max lengths
        Assert.Equal(3, membershipType.FindProperty(nameof(Membership.Currency))!.GetMaxLength());
        Assert.Equal(500, membershipType.FindProperty(nameof(Membership.CancellationReason))!.GetMaxLength());
        Assert.Equal(1000, membershipType.FindProperty(nameof(Membership.Notes))!.GetMaxLength());
        Assert.Equal(100, membershipType.FindProperty(nameof(Membership.IdempotencyKey))!.GetMaxLength());

        // Five CHECK constraints
        var checks = membershipType.GetCheckConstraints().ToList();
        Assert.Contains(checks, c => c.ModelName == "CK_Memberships_Dates");
        Assert.Contains(checks, c => c.ModelName == "CK_Memberships_Status");
        Assert.Contains(checks, c => c.ModelName == "CK_Memberships_Prices");
        Assert.Contains(checks, c => c.ModelName == "CK_Memberships_Credits");
        Assert.Contains(checks, c => c.ModelName == "CK_Memberships_Cancellation");

        // Partial unique index UX_Memberships_Student_IdempotencyKey
        var idempotencyIndex = Assert.Single(
            membershipType.GetIndexes(),
            i => i.GetDatabaseName() == "UX_Memberships_Student_IdempotencyKey");
        Assert.True(idempotencyIndex.IsUnique);
        Assert.Equal("\"IdempotencyKey\" IS NOT NULL", idempotencyIndex.GetFilter());

        // Operational indexes
        var indexes = membershipType.GetIndexes().Select(i => i.GetDatabaseName()).ToList();
        Assert.Contains("IX_Memberships_Student_Status_EndDate", indexes);
        Assert.Contains("IX_Memberships_Status_EndDate", indexes);
        Assert.Contains("IX_Memberships_MembershipPlanId", indexes);
        Assert.Contains("IX_Memberships_PurchasedAtUtc", indexes);

        // Foreign keys on Membership should all be Restrict
        foreach (var fk in membershipType.GetForeignKeys())
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }
    }

    [Fact]
    public void ChildEntities_AndCreditMovementFk_ShouldBeConfigured()
    {
        var model = BuildModel();

        // MembershipAllowedActivity
        var allowedActivityType = model.FindEntityType(typeof(MembershipAllowedActivity));
        Assert.NotNull(allowedActivityType);
        Assert.Equal("MembershipAllowedActivities", allowedActivityType!.GetTableName());
        var activityFk = Assert.Single(
            allowedActivityType.GetForeignKeys(),
            fk => fk.Properties.Any(p => p.Name == nameof(MembershipAllowedActivity.ActivityId)));
        Assert.Equal(DeleteBehavior.Restrict, activityFk.DeleteBehavior);

        // MembershipStatusChange
        var statusChangeType = model.FindEntityType(typeof(MembershipStatusChange));
        Assert.NotNull(statusChangeType);
        Assert.Equal("MembershipStatusChanges", statusChangeType!.GetTableName());
        Assert.Equal(500, statusChangeType.FindProperty(nameof(MembershipStatusChange.Reason))!.GetMaxLength());
        Assert.Equal(1000, statusChangeType.FindProperty(nameof(MembershipStatusChange.Notes))!.GetMaxLength());
        var changedByFk = Assert.Single(
            statusChangeType.GetForeignKeys(),
            fk => fk.Properties.Any(p => p.Name == nameof(MembershipStatusChange.ChangedByUserId)));
        Assert.Equal(DeleteBehavior.Restrict, changedByFk.DeleteBehavior);

        // MembershipSuspension
        var suspensionType = model.FindEntityType(typeof(MembershipSuspension));
        Assert.NotNull(suspensionType);
        Assert.Equal("MembershipSuspensions", suspensionType!.GetTableName());
        Assert.Equal(500, suspensionType.FindProperty(nameof(MembershipSuspension.Reason))!.GetMaxLength());
        Assert.Equal(1000, suspensionType.FindProperty(nameof(MembershipSuspension.Notes))!.GetMaxLength());
        var suspendedByFk = Assert.Single(
            suspensionType.GetForeignKeys(),
            fk => fk.Properties.Any(p => p.Name == nameof(MembershipSuspension.SuspendedByUserId)));
        Assert.Equal(DeleteBehavior.Restrict, suspendedByFk.DeleteBehavior);
        var reactivatedByFk = Assert.Single(
            suspensionType.GetForeignKeys(),
            fk => fk.Properties.Any(p => p.Name == nameof(MembershipSuspension.ReactivatedByUserId)));
        Assert.Equal(DeleteBehavior.Restrict, reactivatedByFk.DeleteBehavior);

        // MembershipCreditMovement.MembershipId -> Restrict
        var creditMovementType = model.FindEntityType(typeof(MembershipCreditMovement))!;
        var membershipFk = Assert.Single(
            creditMovementType.GetForeignKeys(),
            fk => fk.Properties.Any(p => p.Name == nameof(MembershipCreditMovement.MembershipId)));
        Assert.Equal(DeleteBehavior.Restrict, membershipFk.DeleteBehavior);
    }

    [Fact]
    public async Task LockMethods_OnInMemoryProvider_ShouldCompleteAsNoOp()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new SmartGymDbContext(options);

        await dbContext.LockPersonForUpdateAsync(Guid.NewGuid());
        await dbContext.LockMembershipForUpdateAsync(Guid.NewGuid());
    }
}
