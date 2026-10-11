using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

/// <summary>
/// La modalidad de consumo y las actividades de una membresía son las del snapshot tomado al contratar:
/// editar el plan después no debe cambiar lo ya vendido (Criterio 2).
/// </summary>
public class MembershipUnlimitedSnapshotIsolationTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    private static MembershipPlan CreateLimitedPlan(int credits = 1) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Plan por créditos",
        Price = 10000m,
        DurationDays = 30,
        Credits = credits,
        IsUnlimited = false,
        AppliesToAllActivities = true,
        IsActive = true
    };

    [Fact]
    public async Task PlanChangedToUnlimitedAfterPurchase_DoesNotMakeExistingMembershipUnlimited()
    {
        var calendar = new TestGymCalendar(Today);
        await using var db = new SmartGymDbContext(MembershipTestSupport.InMemoryOptions());
        var ledger = new CreditLedgerService(db, calendar);

        var plan = CreateLimitedPlan(credits: 1);
        var membership = Membership.Create(Guid.NewGuid(), plan, Today, calendar.UtcNow, 10000m, 0m, 10000m);
        membership.Activate(Today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);

        plan.IsUnlimited = true;

        var debit = await ledger.ConsumeCreditForReservationAsync(membership, Guid.NewGuid(), Today, "Reserva");

        Assert.Equal(-1, debit.Amount);
        Assert.Equal(0, membership.AvailableCredits);
        Assert.False(membership.HasAvailableCredit());
        Assert.False(membership.HasUsableOrPendingCredits());
        Assert.True(membership.IsDepleted);

        var refund = await ledger.RefundCreditForReservationAsync(membership, debit.ClassSessionId!.Value, "Devolución");
        Assert.Equal(1, refund.Amount);
        Assert.Equal(1, membership.AvailableCredits);

        var forfeiture = await ledger.ForfeitRemainingCreditsOnCancelAsync(membership, "Baja");
        Assert.NotNull(forfeiture);
        Assert.Equal(CreditMovementType.CancellationForfeiture, forfeiture!.Type);
        Assert.Equal(-1, forfeiture.Amount);
    }

    [Fact]
    public void RestrictedMembershipWithEmptySnapshot_DoesNotFallBackToCurrentPlanActivities()
    {
        var calendar = new TestGymCalendar(Today);
        var plan = CreateLimitedPlan();
        plan.AppliesToAllActivities = false;

        var membership = Membership.Create(Guid.NewGuid(), plan, Today, calendar.UtcNow, 10000m, 0m, 10000m);

        var activityAddedLater = Guid.NewGuid();
        plan.AddAllowedActivity(activityAddedLater);

        Assert.False(membership.AllowsActivity(activityAddedLater));
    }
}
