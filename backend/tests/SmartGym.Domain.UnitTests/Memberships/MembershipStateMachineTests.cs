using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Memberships;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipStateMachineTests
{
    private static Membership CreatePendingMembership(DateOnly? startDate = null, int durationDays = 30)
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

        return Membership.Create(
            studentId: Guid.NewGuid(),
            plan: plan,
            startDate: startDate ?? new DateOnly(2026, 4, 1),
            purchasedAtUtc: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            originalPrice: 20000m,
            discountAmount: 0m,
            purchasePrice: 20000m,
            currency: "ARS",
            createdByUserId: Guid.NewGuid());
    }

    [Fact]
    public void Activate_FromPending_ShouldTransitionToActive_RecordStatusChange_AndEmitEvent()
    {
        var membership = CreatePendingMembership(new DateOnly(2026, 4, 1));
        var actorUserId = Guid.NewGuid();
        var activatedAtUtc = new DateTime(2026, 4, 1, 10, 5, 0, DateTimeKind.Utc);
        var today = new DateOnly(2026, 4, 1);

        membership.Activate(
            today: today,
            activatedAtUtc: activatedAtUtc,
            changedByUserId: actorUserId,
            reason: "Pago confirmado en mostrador",
            notes: "Recibo #123");

        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(activatedAtUtc, membership.ActivatedAtUtc);
        Assert.Equal(2, membership.StatusChanges.Count);

        var lastChange = membership.StatusChanges.Last();
        Assert.Equal(MembershipStatus.Pending, lastChange.PreviousStatus);
        Assert.Equal(MembershipStatus.Active, lastChange.NewStatus);
        Assert.Equal("Pago confirmado en mostrador", lastChange.Reason);
        Assert.Equal("Recibo #123", lastChange.Notes);
        Assert.Equal(actorUserId, lastChange.ChangedByUserId);
        Assert.Equal(activatedAtUtc, lastChange.ChangedAtUtc);

        var evt = Assert.Single(membership.DomainEvents.OfType<MembershipActivatedEvent>());
        Assert.Equal(membership.Id, evt.MembershipId);
        Assert.Equal(membership.StudentId, evt.StudentId);
        Assert.Equal(actorUserId, evt.ActivatedByUserId);
        Assert.Equal(activatedAtUtc, evt.ActivatedAtUtc);
    }

    [Fact]
    public void Activate_WhenAlreadyPastEndDate_ShouldThrowInvalidOperationException()
    {
        var membership = CreatePendingMembership(new DateOnly(2026, 4, 1), durationDays: 10); // EndDate = 2026-04-10
        var today = new DateOnly(2026, 4, 11);

        Assert.Throws<InvalidOperationException>(() =>
            membership.Activate(today, DateTime.UtcNow, Guid.NewGuid(), "Activación tardía fuera de vigencia"));
    }

    [Theory]
    [InlineData(MembershipStatus.Active)]
    [InlineData(MembershipStatus.Suspended)]
    [InlineData(MembershipStatus.Expired)]
    [InlineData(MembershipStatus.Cancelled)]
    public void Activate_FromNonPendingState_ShouldThrowInvalidOperationException(MembershipStatus currentStatus)
    {
        var membership = CreatePendingMembership();
        membership.Status = currentStatus;

        Assert.Throws<InvalidOperationException>(() =>
            membership.Activate(new DateOnly(2026, 4, 5), DateTime.UtcNow, Guid.NewGuid(), "Intento inválido"));
    }

    [Fact]
    public void Cancel_FromPending_ShouldTransitionToCancelled_RecordReason_AndEmitEvent()
    {
        var membership = CreatePendingMembership();
        var actorUserId = Guid.NewGuid();
        var cancelledAtUtc = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc);

        membership.Cancel(
            reason: "Desistimiento del alumno antes de abonar",
            cancelledByUserId: actorUserId,
            cancelledAtUtc: cancelledAtUtc,
            notes: "Sin cargos");

        Assert.Equal(MembershipStatus.Cancelled, membership.Status);
        Assert.Equal(cancelledAtUtc, membership.CancelledAtUtc);
        Assert.Equal(actorUserId, membership.CancelledByUserId);
        Assert.Equal("Desistimiento del alumno antes de abonar", membership.CancellationReason);

        var lastChange = membership.StatusChanges.Last();
        Assert.Equal(MembershipStatus.Pending, lastChange.PreviousStatus);
        Assert.Equal(MembershipStatus.Cancelled, lastChange.NewStatus);
        Assert.Equal("Desistimiento del alumno antes de abonar", lastChange.Reason);
        Assert.Equal("Sin cargos", lastChange.Notes);
        Assert.Equal(actorUserId, lastChange.ChangedByUserId);

        var evt = Assert.Single(membership.DomainEvents.OfType<MembershipCancelledEvent>());
        Assert.Equal(membership.Id, evt.MembershipId);
        Assert.Equal(membership.StudentId, evt.StudentId);
        Assert.Equal(MembershipStatus.Pending, evt.PreviousStatus);
        Assert.Equal("Desistimiento del alumno antes de abonar", evt.Reason);
        Assert.Equal(actorUserId, evt.CancelledByUserId);
        Assert.Equal(cancelledAtUtc, evt.CancelledAtUtc);
    }

    [Fact]
    public void Cancel_FromActiveOrSuspended_ShouldTransitionToCancelled()
    {
        var membership = CreatePendingMembership();
        var today = new DateOnly(2026, 4, 1);
        membership.Activate(today, DateTime.UtcNow, Guid.NewGuid(), "Activación");
        membership.ClearDomainEvents();

        var cancelTime = DateTime.UtcNow;
        var adminId = Guid.NewGuid();
        membership.Cancel("Baja solicitada por mudanza", adminId, cancelTime);

        Assert.Equal(MembershipStatus.Cancelled, membership.Status);
        Assert.Equal("Baja solicitada por mudanza", membership.CancellationReason);
        Assert.Equal(adminId, membership.CancelledByUserId);
        var evt = Assert.Single(membership.DomainEvents.OfType<MembershipCancelledEvent>());
        Assert.Equal(MembershipStatus.Active, evt.PreviousStatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_WithoutReason_ShouldThrowArgumentException(string invalidReason)
    {
        var membership = CreatePendingMembership();

        Assert.Throws<ArgumentException>(() =>
            membership.Cancel(invalidReason, Guid.NewGuid(), DateTime.UtcNow));
    }

    [Theory]
    [InlineData(MembershipStatus.Expired)]
    [InlineData(MembershipStatus.Cancelled)]
    public void Cancel_FromTerminalState_ShouldThrowInvalidOperationException(MembershipStatus terminalStatus)
    {
        var membership = CreatePendingMembership();
        membership.Status = terminalStatus;

        Assert.Throws<InvalidOperationException>(() =>
            membership.Cancel("Intento desde estado terminal", Guid.NewGuid(), DateTime.UtcNow));
    }

    [Fact]
    public void Expire_FromActiveAfterEndDate_ShouldTransitionToExpired_RecordStatusChange_AndEmitEvent()
    {
        var membership = CreatePendingMembership(new DateOnly(2026, 4, 1), durationDays: 30); // EndDate = 2026-04-30
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid(), "Activación");
        membership.ClearDomainEvents();

        var today = new DateOnly(2026, 5, 1);
        var expiredAtUtc = new DateTime(2026, 5, 1, 3, 0, 0, DateTimeKind.Utc);

        membership.Expire(today, expiredAtUtc, "Vencimiento automático por finalización de vigencia");

        Assert.Equal(MembershipStatus.Expired, membership.Status);
        var lastChange = membership.StatusChanges.Last();
        Assert.Equal(MembershipStatus.Active, lastChange.PreviousStatus);
        Assert.Equal(MembershipStatus.Expired, lastChange.NewStatus);
        Assert.Null(lastChange.ChangedByUserId);
        Assert.Equal("Vencimiento automático por finalización de vigencia", lastChange.Reason);

        var evt = Assert.Single(membership.DomainEvents.OfType<MembershipExpiredEvent>());
        Assert.Equal(membership.Id, evt.MembershipId);
        Assert.Equal(membership.StudentId, evt.StudentId);
        Assert.Equal(expiredAtUtc, evt.ExpiredAtUtc);
    }

    [Fact]
    public void Expire_WhenNotYetPastEndDate_ShouldThrowInvalidOperationException()
    {
        var membership = CreatePendingMembership(new DateOnly(2026, 4, 1), durationDays: 30); // EndDate = 2026-04-30
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid(), "Activación");

        Assert.Throws<InvalidOperationException>(() =>
            membership.Expire(new DateOnly(2026, 4, 30), DateTime.UtcNow));
    }

    [Theory]
    [InlineData(MembershipStatus.Pending)]
    [InlineData(MembershipStatus.Expired)]
    [InlineData(MembershipStatus.Cancelled)]
    public void Expire_FromInvalidState_ShouldThrowInvalidOperationException(MembershipStatus invalidStatus)
    {
        var membership = CreatePendingMembership(new DateOnly(2026, 4, 1), durationDays: 30);
        membership.Status = invalidStatus;

        Assert.Throws<InvalidOperationException>(() =>
            membership.Expire(new DateOnly(2026, 5, 5), DateTime.UtcNow));
    }
}
