using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipDomainCreationTests
{
    [Fact]
    public void Create_ShouldCaptureCommercialSnapshot_AndCalculateInclusiveEndDate()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var activityCrossFitId = Guid.NewGuid();
        var activityPilatesId = Guid.NewGuid();
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 8 Clases",
            DurationDays = 30,
            Price = 25000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = false
        };
        plan.AddAllowedActivity(activityCrossFitId);
        plan.AddAllowedActivity(activityPilatesId);

        var purchasedAtUtc = new DateTime(2026, 4, 1, 14, 30, 0, DateTimeKind.Utc);
        var startDate = new DateOnly(2026, 4, 1);
        var createdByUserId = Guid.NewGuid();
        var promotionId = Guid.NewGuid();
        var redemptionId = Guid.NewGuid();

        // Act
        var membership = Membership.Create(
            studentId: studentId,
            plan: plan,
            startDate: startDate,
            purchasedAtUtc: purchasedAtUtc,
            originalPrice: 25000m,
            discountAmount: 5000m,
            purchasePrice: 20000m,
            currency: "ARS",
            extraCreditsGranted: 2,
            promotionId: promotionId,
            promotionRedemptionId: redemptionId,
            renewedFromMembershipId: null,
            createdByUserId: createdByUserId,
            notes: "Contratación con descuento promocional",
            idempotencyKey: "idem-001");

        // Assert
        Assert.Equal(studentId, membership.StudentId);
        Assert.Equal(plan.Id, membership.MembershipPlanId);
        Assert.Equal(MembershipStatus.Pending, membership.Status);
        Assert.Equal(new DateOnly(2026, 4, 1), membership.StartDate);
        // Inclusive 30 days: April 1 -> April 30 (StartDate.AddDays(30 - 1))
        Assert.Equal(new DateOnly(2026, 4, 30), membership.EndDate);
        Assert.Equal(30, membership.DurationDaysSnapshot);
        Assert.Equal(purchasedAtUtc, membership.PurchasedAtUtc);
        Assert.Null(membership.ActivatedAtUtc);
        Assert.Equal(8, membership.InitialCredits);
        Assert.Equal(2, membership.ExtraCreditsGranted);
        Assert.Equal(0, membership.TotalCredits);
        Assert.Equal(0, membership.AvailableCredits);
        Assert.False(membership.InitialCreditsLoaded);
        Assert.False(membership.IsUnlimited);
        Assert.False(membership.AppliesToAllActivities);
        Assert.Equal(25000m, membership.OriginalPrice);
        Assert.Equal(5000m, membership.DiscountAmount);
        Assert.Equal(20000m, membership.PurchasePrice);
        Assert.Equal("ARS", membership.Currency);
        Assert.Equal(promotionId, membership.PromotionId);
        Assert.Equal(redemptionId, membership.PromotionRedemptionId);
        Assert.Equal(createdByUserId, membership.CreatedByUserId);
        Assert.Equal("Contratación con descuento promocional", membership.Notes);
        Assert.Equal("idem-001", membership.IdempotencyKey);
        Assert.Equal(2, membership.AllowedActivities.Count);
        Assert.Contains(membership.AllowedActivities, a => a.ActivityId == activityCrossFitId);
        Assert.Contains(membership.AllowedActivities, a => a.ActivityId == activityPilatesId);

        // Initial status change row
        var statusChange = Assert.Single(membership.StatusChanges);
        Assert.Null(statusChange.PreviousStatus);
        Assert.Equal(MembershipStatus.Pending, statusChange.NewStatus);
        Assert.Equal(createdByUserId, statusChange.ChangedByUserId);
        Assert.Equal(purchasedAtUtc, statusChange.ChangedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(statusChange.Reason));
    }

    [Fact]
    public void Create_WithSingleDayPass_ShouldSetEndDateEqualToStartDate()
    {
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pase Diario",
            DurationDays = 1,
            Price = 5000m,
            Credits = 1,
            IsUnlimited = false,
            AppliesToAllActivities = true
        };

        var startDate = new DateOnly(2026, 5, 10);
        var membership = Membership.Create(
            studentId: Guid.NewGuid(),
            plan: plan,
            startDate: startDate,
            purchasedAtUtc: DateTime.UtcNow,
            originalPrice: 5000m,
            discountAmount: 0m,
            purchasePrice: 5000m,
            currency: "ARS");

        Assert.Equal(startDate, membership.StartDate);
        Assert.Equal(startDate, membership.EndDate);
        Assert.Equal(1, membership.DurationDaysSnapshot);
        Assert.Empty(membership.AllowedActivities);
    }

    [Fact]
    public void Create_WhenPlanChangesLater_ShouldPreserveCommercialSnapshotAndAllowedActivities()
    {
        // Criterio 2: El precio y las condiciones contratadas no cambian si cambia el plan
        var crossFitId = Guid.NewGuid();
        var boxeoId = Guid.NewGuid();
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan CrossFit",
            DurationDays = 30,
            Price = 25000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = false
        };
        plan.AddAllowedActivity(crossFitId);

        var membership = Membership.Create(
            studentId: Guid.NewGuid(),
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: DateTime.UtcNow,
            originalPrice: plan.Price,
            discountAmount: 0m,
            purchasePrice: plan.Price,
            currency: "ARS");

        // Mutate the plan afterwards
        plan.Price = 32000m;
        plan.Credits = 4;
        plan.DurationDays = 15;
        plan.RemoveAllowedActivity(crossFitId);
        plan.AddAllowedActivity(boxeoId);

        // Assert membership snapshot is untouched
        Assert.Equal(25000m, membership.OriginalPrice);
        Assert.Equal(25000m, membership.PurchasePrice);
        Assert.Equal(8, membership.InitialCredits);
        Assert.Equal(30, membership.DurationDaysSnapshot);
        Assert.Equal(new DateOnly(2026, 4, 30), membership.EndDate);
        Assert.True(membership.AllowsActivity(crossFitId));
        Assert.False(membership.AllowsActivity(boxeoId));
    }

    [Fact]
    public void MembershipStatus_ShouldHaveExpectedNumericValues_WithoutDepleted()
    {
        Assert.Equal(0, (int)MembershipStatus.Pending);
        Assert.Equal(1, (int)MembershipStatus.Active);
        Assert.Equal(2, (int)MembershipStatus.Expired);
        Assert.Equal(3, (int)MembershipStatus.Suspended);
        Assert.Equal(4, (int)MembershipStatus.Cancelled);
        Assert.Equal(5, Enum.GetValues<MembershipStatus>().Length);
        Assert.DoesNotContain("Depleted", Enum.GetNames<MembershipStatus>());
    }

    [Fact]
    public void Create_UnlimitedPlan_ShouldSetZeroInitialCreditsAndIsUnlimitedTrue()
    {
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pase Libre",
            DurationDays = 30,
            Price = 40000m,
            Credits = 999,
            IsUnlimited = true,
            AppliesToAllActivities = true
        };

        var membership = Membership.Create(
            studentId: Guid.NewGuid(),
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: DateTime.UtcNow,
            originalPrice: 40000m,
            discountAmount: 0m,
            purchasePrice: 40000m,
            currency: "ARS",
            extraCreditsGranted: 5);

        Assert.True(membership.IsUnlimited);
        Assert.Equal(0, membership.InitialCredits);
        Assert.Equal(0, membership.ExtraCreditsGranted);
        Assert.Equal(0, membership.AvailableCredits);
    }
}
