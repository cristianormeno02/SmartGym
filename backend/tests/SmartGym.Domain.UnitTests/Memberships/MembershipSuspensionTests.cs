using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Memberships;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipSuspensionTests
{
    private static Membership CreateActiveMembership(DateOnly startDate, int durationDays = 30)
    {
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan Mensual",
            DurationDays = durationDays,
            Price = 20000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true
        };

        var membership = Membership.Create(
            studentId: Guid.NewGuid(),
            plan: plan,
            startDate: startDate,
            purchasedAtUtc: new DateTime(startDate.Year, startDate.Month, startDate.Day, 10, 0, 0, DateTimeKind.Utc),
            originalPrice: 20000m,
            discountAmount: 0m,
            purchasePrice: 20000m,
            currency: "ARS",
            createdByUserId: Guid.NewGuid());

        membership.Activate(
            today: startDate,
            activatedAtUtc: new DateTime(startDate.Year, startDate.Month, startDate.Day, 10, 5, 0, DateTimeKind.Utc),
            changedByUserId: Guid.NewGuid(),
            reason: "Activación inicial");

        membership.ClearDomainEvents();
        return membership;
    }

    [Fact]
    public void Suspend_FromActiveWithinValidity_ShouldTransitionToSuspended_RecordSuspensionAndEmitEvent()
    {
        var membership = CreateActiveMembership(new DateOnly(2026, 11, 1), durationDays: 30); // EndDate = 2026-11-30
        var staffUserId = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 10);
        var suspendedAtUtc = new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Utc);

        var suspension = membership.Suspend(
            today: today,
            endDate: null,
            extendExpirationDate: false,
            reason: "Viaje laboral",
            notes: "Regresa en una semana",
            suspendedByUserId: staffUserId,
            suspendedAtUtc: suspendedAtUtc);

        Assert.Equal(MembershipStatus.Suspended, membership.Status);
        Assert.Equal(new DateOnly(2026, 11, 30), membership.EndDate);
        Assert.Single(membership.Suspensions);
        Assert.True(suspension.IsActiveSuspension);
        Assert.Equal(today, suspension.StartDate);
        Assert.Null(suspension.EndDate);
        Assert.False(suspension.ExtendExpirationDate);
        Assert.Equal(0, suspension.ExtensionDaysApplied);
        Assert.Equal("Viaje laboral", suspension.Reason);
        Assert.Equal("Regresa en una semana", suspension.Notes);
        Assert.Equal(staffUserId, suspension.SuspendedByUserId);
        Assert.Equal(suspendedAtUtc, suspension.SuspendedAtUtc);

        var lastStatusChange = membership.StatusChanges.Last();
        Assert.Equal(MembershipStatus.Active, lastStatusChange.PreviousStatus);
        Assert.Equal(MembershipStatus.Suspended, lastStatusChange.NewStatus);
        Assert.Equal("Viaje laboral", lastStatusChange.Reason);
        Assert.Equal(staffUserId, lastStatusChange.ChangedByUserId);

        var evt = Assert.Single(membership.DomainEvents.OfType<MembershipSuspendedEvent>());
        Assert.Equal(membership.Id, evt.MembershipId);
        Assert.Equal(suspension.Id, evt.SuspensionId);
        Assert.Equal(today, evt.StartDate);
        Assert.Null(evt.EndDate);
        Assert.False(evt.ExtendExpirationDate);
    }

    [Theory]
    [InlineData(MembershipStatus.Pending)]
    [InlineData(MembershipStatus.Suspended)]
    [InlineData(MembershipStatus.Expired)]
    [InlineData(MembershipStatus.Cancelled)]
    public void Suspend_FromNonActiveState_ShouldThrowInvalidOperationException(MembershipStatus status)
    {
        var membership = CreateActiveMembership(new DateOnly(2026, 11, 1));
        membership.Status = status;

        Assert.Throws<InvalidOperationException>(() =>
            membership.Suspend(
                today: new DateOnly(2026, 11, 10),
                endDate: null,
                extendExpirationDate: false,
                reason: "Motivo",
                notes: null,
                suspendedByUserId: Guid.NewGuid(),
                suspendedAtUtc: DateTime.UtcNow));
    }

    [Fact]
    public void Suspend_WhenOutsideValidityPeriod_ShouldThrowInvalidOperationException()
    {
        var membership = CreateActiveMembership(new DateOnly(2026, 11, 1), durationDays: 30); // 2026-11-01 .. 2026-11-30

        // Before StartDate
        Assert.Throws<InvalidOperationException>(() =>
            membership.Suspend(
                today: new DateOnly(2026, 10, 31),
                endDate: null,
                extendExpirationDate: false,
                reason: "Antes de iniciar",
                notes: null,
                suspendedByUserId: Guid.NewGuid(),
                suspendedAtUtc: DateTime.UtcNow));

        // After EndDate
        Assert.Throws<InvalidOperationException>(() =>
            membership.Suspend(
                today: new DateOnly(2026, 12, 1),
                endDate: null,
                extendExpirationDate: false,
                reason: "Ya vencida",
                notes: null,
                suspendedByUserId: Guid.NewGuid(),
                suspendedAtUtc: DateTime.UtcNow));
    }

    [Fact]
    public void Suspend_WithExtendExpirationDateTrue_AndNullEndDate_ShouldThrowArgumentException()
    {
        var membership = CreateActiveMembership(new DateOnly(2026, 11, 1));

        Assert.Throws<ArgumentException>(() =>
            membership.Suspend(
                today: new DateOnly(2026, 11, 10),
                endDate: null,
                extendExpirationDate: true,
                reason: "Extensión sin fecha fin",
                notes: null,
                suspendedByUserId: Guid.NewGuid(),
                suspendedAtUtc: DateTime.UtcNow));
    }

    [Fact]
    public void Suspend_WhenEndDateExceedsMaxSuspensionDays_ShouldThrowArgumentException()
    {
        var membership = CreateActiveMembership(new DateOnly(2026, 11, 1));
        var today = new DateOnly(2026, 11, 10);
        // Max 90 days inclusive: today.AddDays(89) is valid, today.AddDays(90) is 91 days
        var exceededEndDate = today.AddDays(90);

        Assert.Throws<ArgumentException>(() =>
            membership.Suspend(
                today: today,
                endDate: exceededEndDate,
                extendExpirationDate: false,
                reason: "Supera máximo de días",
                notes: null,
                suspendedByUserId: Guid.NewGuid(),
                suspendedAtUtc: DateTime.UtcNow,
                maxSuspensionDays: 90));
    }

    [Fact]
    public void Reactivate_WithoutExtension_ShouldKeepOriginalEndDate_AndCloseActiveSuspension()
    {
        var membership = CreateActiveMembership(new DateOnly(2026, 11, 1), durationDays: 30); // EndDate = 2026-11-30
        var staffUserId = Guid.NewGuid();
        var suspension = membership.Suspend(
            today: new DateOnly(2026, 11, 10),
            endDate: null,
            extendExpirationDate: false,
            reason: "Viaje laboral",
            notes: null,
            suspendedByUserId: staffUserId,
            suspendedAtUtc: DateTime.UtcNow);

        var reactivatedAtDate = new DateOnly(2026, 11, 15);
        var reactivatedAtUtc = new DateTime(2026, 11, 15, 11, 0, 0, DateTimeKind.Utc);
        var reactivatedByUserId = Guid.NewGuid();

        var extensionDays = membership.Reactivate(
            reactivationDate: reactivatedAtDate,
            reactivatedAtUtc: reactivatedAtUtc,
            reactivatedByUserId: reactivatedByUserId,
            reason: "Regreso de viaje");

        Assert.Equal(0, extensionDays);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(new DateOnly(2026, 11, 30), membership.EndDate);
        Assert.False(suspension.IsActiveSuspension);
        Assert.Equal(reactivatedAtDate, suspension.ReactivatedAtDate);
        Assert.Equal(reactivatedByUserId, suspension.ReactivatedByUserId);
        Assert.Equal(reactivatedAtUtc, suspension.ReactivatedAtUtc);
        Assert.Equal(0, suspension.ExtensionDaysApplied);

        var lastChange = membership.StatusChanges.Last();
        Assert.Equal(MembershipStatus.Suspended, lastChange.PreviousStatus);
        Assert.Equal(MembershipStatus.Active, lastChange.NewStatus);
        Assert.Equal("Regreso de viaje", lastChange.Reason);
    }

    [Fact]
    public void Reactivate_WithExtension_10NovTo15Nov_ShouldApply5DaysAndExtendEndDateTo05Dec()
    {
        // Spec scenario: vence el 30/11, se suspende el 10/11 con ExtendExpirationDate = true y fecha fin 14/11, se reactiva el 15/11 -> EndDate = 05/12 (5 días devueltos)
        var membership = CreateActiveMembership(new DateOnly(2026, 11, 1), durationDays: 30); // EndDate = 2026-11-30
        var suspension = membership.Suspend(
            today: new DateOnly(2026, 11, 10),
            endDate: new DateOnly(2026, 11, 14),
            extendExpirationDate: true,
            reason: "Lesión leve",
            notes: null,
            suspendedByUserId: Guid.NewGuid(),
            suspendedAtUtc: DateTime.UtcNow);

        var extensionDays = membership.Reactivate(
            reactivationDate: new DateOnly(2026, 11, 15),
            reactivatedAtUtc: new DateTime(2026, 11, 15, 3, 0, 0, DateTimeKind.Utc),
            reactivatedByUserId: null,
            reason: "Reactivación automática por finalización de suspensión");

        Assert.Equal(5, extensionDays);
        Assert.Equal(5, suspension.ExtensionDaysApplied);
        Assert.Equal(new DateOnly(2026, 12, 5), membership.EndDate);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.False(suspension.IsActiveSuspension);
    }

    [Fact]
    public void Reactivate_WithExtensionExceedingOriginalEndDate_ShouldExtendEndDateProperly()
    {
        // Spec scenario: vence el 30/11, se suspende el 25/11 con ExtendExpirationDate = true y fecha de fin 09/12, se reactiva el 10/12 -> EndDate = 15/12 (15 días devueltos)
        var membership = CreateActiveMembership(new DateOnly(2026, 11, 1), durationDays: 30); // EndDate = 2026-11-30
        var suspension = membership.Suspend(
            today: new DateOnly(2026, 11, 25),
            endDate: new DateOnly(2026, 12, 9),
            extendExpirationDate: true,
            reason: "Reposo médico",
            notes: null,
            suspendedByUserId: Guid.NewGuid(),
            suspendedAtUtc: DateTime.UtcNow);

        var extensionDays = membership.Reactivate(
            reactivationDate: new DateOnly(2026, 12, 10),
            reactivatedAtUtc: new DateTime(2026, 12, 10, 3, 0, 0, DateTimeKind.Utc),
            reactivatedByUserId: null,
            reason: "Reactivación automática al finalizar ventana de suspensión");

        Assert.Equal(15, extensionDays);
        Assert.Equal(15, suspension.ExtensionDaysApplied);
        Assert.Equal(new DateOnly(2026, 12, 15), membership.EndDate);
        Assert.Equal(MembershipStatus.Active, membership.Status);
    }

    [Fact]
    public void Reactivate_WithoutExtensionWhenAlreadyPastEndDate_ShouldThrowInvalidOperationException()
    {
        var membership = CreateActiveMembership(new DateOnly(2026, 11, 1), durationDays: 30); // EndDate = 2026-11-30
        membership.Suspend(
            today: new DateOnly(2026, 11, 10),
            endDate: null,
            extendExpirationDate: false,
            reason: "Viaje",
            notes: null,
            suspendedByUserId: Guid.NewGuid(),
            suspendedAtUtc: DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            membership.Reactivate(
                reactivationDate: new DateOnly(2026, 12, 1),
                reactivatedAtUtc: DateTime.UtcNow,
                reactivatedByUserId: Guid.NewGuid(),
                reason: "Intento fuera de vigencia"));
    }
}
