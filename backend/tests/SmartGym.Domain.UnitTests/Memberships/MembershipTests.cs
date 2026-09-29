using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipTests
{
    [Fact]
    public void Membership_ShouldDeductCredit_AndCreateLedgerMovement()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 4 Clases",
            DurationDays = 30,
            Price = 15000m,
            Credits = 4,
            IsUnlimited = false
        };

        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            MembershipPlanId = plan.Id,
            MembershipPlan = plan,
            StartDate = new DateOnly(2026, 4, 1),
            EndDate = new DateOnly(2026, 4, 30),
            TotalCredits = 4,
            AvailableCredits = 4,
            Status = MembershipStatus.Active
        };

        var classSessionId = Guid.NewGuid();

        // Act
        var movement = membership.DeductCredit(classSessionId, "Reserva de clase Funcional", new DateOnly(2026, 4, 10));

        // Assert
        Assert.Equal(3, membership.AvailableCredits);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(CreditMovementType.ClassReservationDebit, movement.Type);
        Assert.Equal(-1, movement.Amount);
        Assert.Equal(3, movement.BalanceAfter);
        Assert.Equal(classSessionId, movement.ClassSessionId);
    }

    [Fact]
    public void Membership_ShouldMarkAsDepleted_WhenLastCreditIsDeducted()
    {
        // Arrange
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 1 Clase",
            Credits = 1,
            IsUnlimited = false
        };

        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            MembershipPlanId = plan.Id,
            MembershipPlan = plan,
            StartDate = new DateOnly(2026, 4, 1),
            EndDate = new DateOnly(2026, 4, 30),
            TotalCredits = 1,
            AvailableCredits = 1,
            Status = MembershipStatus.Active
        };

        // Act
        membership.DeductCredit(Guid.NewGuid(), "Última clase", new DateOnly(2026, 4, 15));

        // Assert
        Assert.Equal(0, membership.AvailableCredits);
        Assert.Equal(MembershipStatus.Depleted, membership.Status);
        Assert.False(membership.HasAvailableCredit());
    }

    [Fact]
    public void Membership_ShouldRefundCredit_AndRestoreActiveStatus()
    {
        // Arrange
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 1 Clase",
            Credits = 1,
            IsUnlimited = false
        };

        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            MembershipPlanId = plan.Id,
            MembershipPlan = plan,
            StartDate = new DateOnly(2026, 4, 1),
            EndDate = new DateOnly(2026, 4, 30),
            TotalCredits = 1,
            AvailableCredits = 0,
            Status = MembershipStatus.Depleted
        };

        // Act
        var movement = membership.RefundCredit(Guid.NewGuid(), "Cancelación anticipada de clase");

        // Assert
        Assert.Equal(1, membership.AvailableCredits);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(CreditMovementType.ClassCancellationRefund, movement.Type);
        Assert.Equal(1, movement.Amount);
        Assert.Equal(1, movement.BalanceAfter);
    }

    [Fact]
    public void Membership_ShouldAddCompensatoryCredit()
    {
        // Arrange
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 4 Clases",
            Credits = 4,
            IsUnlimited = false
        };

        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            MembershipPlanId = plan.Id,
            MembershipPlan = plan,
            StartDate = new DateOnly(2026, 4, 1),
            EndDate = new DateOnly(2026, 4, 30),
            TotalCredits = 4,
            AvailableCredits = 2,
            Status = MembershipStatus.Active
        };

        var secretaryUserId = Guid.NewGuid();

        // Act
        var movement = membership.AddCompensatoryCredit(Guid.NewGuid(), "Compensación por clase suspendida por el gimnasio", secretaryUserId);

        // Assert
        Assert.Equal(3, membership.AvailableCredits);
        Assert.Equal(5, membership.TotalCredits);
        Assert.Equal(CreditMovementType.ClassSuspensionCompensation, movement.Type);
        Assert.Equal(1, movement.Amount);
        Assert.Equal(secretaryUserId, movement.PerformedByUserId);
    }
}
