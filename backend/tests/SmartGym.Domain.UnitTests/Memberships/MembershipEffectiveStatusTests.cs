using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipEffectiveStatusTests
{
    private static Membership CreateMembership(
        DateOnly startDate,
        int durationDays = 30,
        int credits = 8,
        bool isUnlimited = false,
        bool appliesToAllActivities = true,
        params Guid[] allowedActivityIds)
    {
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan",
            DurationDays = durationDays,
            Price = 20000m,
            Credits = credits,
            IsUnlimited = isUnlimited,
            AppliesToAllActivities = appliesToAllActivities
        };

        foreach (var actId in allowedActivityIds)
        {
            plan.AddAllowedActivity(actId);
        }

        return Membership.Create(
            studentId: Guid.NewGuid(),
            plan: plan,
            startDate: startDate,
            purchasedAtUtc: new DateTime(startDate.Year, startDate.Month, startDate.Day, 10, 0, 0, DateTimeKind.Utc),
            originalPrice: 20000m,
            discountAmount: 0m,
            purchasePrice: 20000m,
            currency: "ARS");
    }

    [Fact]
    public void GetEffectiveStatus_WhenActiveAndPastEndDate_ShouldReturnExpired()
    {
        // Criterio 5: Una membresía vencida por fecha bloquea aunque el worker no haya corrido
        var membership = CreateMembership(new DateOnly(2026, 4, 1), durationDays: 30); // EndDate = 2026-04-30
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid());

        Assert.Equal(MembershipStatus.Active, membership.GetEffectiveStatus(new DateOnly(2026, 4, 30)));
        Assert.Equal(MembershipStatus.Expired, membership.GetEffectiveStatus(new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public void GetEffectiveStatus_WhenSuspendedWithoutExtensionAndPastEndDate_ShouldReturnExpired()
    {
        var membership = CreateMembership(new DateOnly(2026, 11, 1), durationDays: 30); // EndDate = 2026-11-30
        membership.Activate(new DateOnly(2026, 11, 1), DateTime.UtcNow, Guid.NewGuid());
        membership.Suspend(
            today: new DateOnly(2026, 11, 20),
            endDate: null,
            extendExpirationDate: false,
            reason: "Viaje",
            notes: null,
            suspendedByUserId: Guid.NewGuid(),
            suspendedAtUtc: DateTime.UtcNow);

        Assert.Equal(MembershipStatus.Suspended, membership.GetEffectiveStatus(new DateOnly(2026, 11, 30)));
        Assert.Equal(MembershipStatus.Expired, membership.GetEffectiveStatus(new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void GetEffectiveStatus_WhenSuspendedWithExtensionAndPastOriginalEndDate_ShouldRemainSuspended()
    {
        // Spec scenario: vence el 30/11, se suspende el 25/11 con ExtendExpirationDate = true y fecha de fin 09/12 -> el 05/12 sigue SUSPENDED
        var membership = CreateMembership(new DateOnly(2026, 11, 1), durationDays: 30); // EndDate = 2026-11-30
        membership.Activate(new DateOnly(2026, 11, 1), DateTime.UtcNow, Guid.NewGuid());
        membership.Suspend(
            today: new DateOnly(2026, 11, 25),
            endDate: new DateOnly(2026, 12, 9),
            extendExpirationDate: true,
            reason: "Reposo médico",
            notes: null,
            suspendedByUserId: Guid.NewGuid(),
            suspendedAtUtc: DateTime.UtcNow);

        Assert.Equal(MembershipStatus.Suspended, membership.GetEffectiveStatus(new DateOnly(2026, 12, 5)));
    }

    [Fact]
    public void IsDepleted_ShouldReflectLoadedZeroBalanceForCreditBasedPlansOnly()
    {
        var creditMembership = CreateMembership(new DateOnly(2026, 4, 1), credits: 8, isUnlimited: false);

        // Before initial credits are loaded (Pending): not depleted
        Assert.False(creditMembership.IsDepleted);

        // Credits loaded and > 0: not depleted
        creditMembership.InitialCreditsLoaded = true;
        creditMembership.TotalCredits = 8;
        creditMembership.AvailableCredits = 3;
        Assert.False(creditMembership.IsDepleted);

        // Credits loaded and == 0: depleted
        creditMembership.AvailableCredits = 0;
        Assert.True(creditMembership.IsDepleted);

        // Unlimited plan is never depleted even with AvailableCredits = 0
        var unlimitedMembership = CreateMembership(new DateOnly(2026, 4, 1), credits: 0, isUnlimited: true);
        unlimitedMembership.InitialCreditsLoaded = true;
        unlimitedMembership.AvailableCredits = 0;
        Assert.False(unlimitedMembership.IsDepleted);
    }

    [Fact]
    public void HasUsableOrPendingCredits_ShouldIncludePendingWithCreditsToLoad_AndExcludeDepleted()
    {
        // 1. Pending with InitialCredits = 8 (not yet loaded) -> true
        var pending = CreateMembership(new DateOnly(2026, 4, 1), credits: 8, isUnlimited: false);
        Assert.False(pending.InitialCreditsLoaded);
        Assert.True(pending.HasUsableOrPendingCredits());

        // 2. Active with AvailableCredits > 0 -> true
        pending.InitialCreditsLoaded = true;
        pending.AvailableCredits = 4;
        Assert.True(pending.HasUsableOrPendingCredits());

        // 3. Active with AvailableCredits == 0 (already loaded and consumed) -> false
        pending.AvailableCredits = 0;
        Assert.False(pending.HasUsableOrPendingCredits());

        // 4. Unlimited -> always true
        var unlimited = CreateMembership(new DateOnly(2026, 4, 1), credits: 0, isUnlimited: true);
        Assert.True(unlimited.HasUsableOrPendingCredits());
    }

    [Fact]
    public void AllowsActivity_ShouldCheckSnapshotOfAllowedActivities()
    {
        var pilatesId = Guid.NewGuid();
        var boxeoId = Guid.NewGuid();

        var restricted = CreateMembership(
            new DateOnly(2026, 4, 1),
            appliesToAllActivities: false,
            allowedActivityIds: pilatesId);

        Assert.True(restricted.AllowsActivity(pilatesId));
        Assert.False(restricted.AllowsActivity(boxeoId));

        var allActivities = CreateMembership(
            new DateOnly(2026, 4, 1),
            appliesToAllActivities: true);

        Assert.True(allActivities.AllowsActivity(pilatesId));
        Assert.True(allActivities.AllowsActivity(boxeoId));
    }
}
