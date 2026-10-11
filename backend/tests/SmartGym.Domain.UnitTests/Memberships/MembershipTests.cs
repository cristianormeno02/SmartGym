using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipTests
{
    private sealed class StubGymCalendar : IGymCalendar
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateTime UtcNow { get; set; } = new(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc);
        public DateOnly Today { get; set; } = new(2026, 4, 10);
        public DateTime ToUtc(DateOnly date, TimeOnly time) =>
            new(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
        public DateTimeOffset ToLocal(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        public (DateTime FromUtc, DateTime ToUtc) LocalDayRange(DateOnly from, DateOnly to) =>
            (ToUtc(from, TimeOnly.MinValue), ToUtc(to.AddDays(1), TimeOnly.MinValue));
    }

    private static SmartGymDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SmartGymDbContext(options);
    }

    [Fact]
    public async Task Membership_ShouldDeductCredit_AndCreateLedgerMovement()
    {
        await using var db = CreateDbContext();
        var service = new CreditLedgerService(db, new StubGymCalendar());

        var student = Person.Create("Ana", "Gomez", "30111222");
        db.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 4 Clases",
            DurationDays = 30,
            Price = 15000m,
            Credits = 4,
            IsUnlimited = false
        };
        db.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            originalPrice: 15000m,
            discountAmount: 0m,
            purchasePrice: 15000m,
            currency: "ARS");
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid());
        await service.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var classSessionId = Guid.NewGuid();

        // Act
        var movement = await service.ConsumeCreditForReservationAsync(
            membership,
            classSessionId,
            new DateOnly(2026, 4, 10),
            "Reserva de clase Funcional",
            performedByUserId: null);

        // Assert
        Assert.Equal(3, membership.AvailableCredits);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(CreditMovementType.ClassReservationDebit, movement.Type);
        Assert.Equal(-1, movement.Amount);
        Assert.Equal(3, movement.BalanceAfter);
        Assert.Equal(classSessionId, movement.ClassSessionId);
    }

    [Fact]
    public async Task Membership_ShouldRemainActive_WhenLastCreditIsDeducted()
    {
        await using var db = CreateDbContext();
        var service = new CreditLedgerService(db, new StubGymCalendar());

        var student = Person.Create("Luis", "Perez", "30111333");
        db.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 1 Clase",
            DurationDays = 30,
            Price = 5000m,
            Credits = 1,
            IsUnlimited = false
        };
        db.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            originalPrice: 5000m,
            discountAmount: 0m,
            purchasePrice: 5000m,
            currency: "ARS");
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid());
        await service.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        // Act
        await service.ConsumeCreditForReservationAsync(
            membership,
            Guid.NewGuid(),
            new DateOnly(2026, 4, 15),
            "Última clase",
            performedByUserId: null);

        // Assert
        Assert.Equal(0, membership.AvailableCredits);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.True(membership.IsDepleted);
        Assert.False(membership.HasAvailableCredit());
    }

    [Fact]
    public async Task Membership_ShouldRefundCredit_AndKeepActiveStatus()
    {
        await using var db = CreateDbContext();
        var service = new CreditLedgerService(db, new StubGymCalendar());

        var student = Person.Create("Marta", "Diaz", "30111444");
        db.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 1 Clase",
            DurationDays = 30,
            Price = 5000m,
            Credits = 1,
            IsUnlimited = false
        };
        db.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            originalPrice: 5000m,
            discountAmount: 0m,
            purchasePrice: 5000m,
            currency: "ARS");
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid());
        await service.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        await service.ConsumeCreditForReservationAsync(
            membership,
            sessionId,
            new DateOnly(2026, 4, 10),
            "Reserva inicial",
            performedByUserId: null);
        await db.SaveChangesAsync();

        // Act
        var movement = await service.RefundCreditForReservationAsync(
            membership,
            sessionId,
            "Cancelación anticipada de clase",
            performedByUserId: null);

        // Assert
        Assert.Equal(1, membership.AvailableCredits);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(CreditMovementType.ClassCancellationRefund, movement.Type);
        Assert.Equal(1, movement.Amount);
        Assert.Equal(1, movement.BalanceAfter);
    }

    [Fact]
    public async Task Membership_ShouldAddCompensatoryCredit()
    {
        await using var db = CreateDbContext();
        var service = new CreditLedgerService(db, new StubGymCalendar());

        var student = Person.Create("Pedro", "Sosa", "30111555");
        db.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 4 Clases",
            DurationDays = 30,
            Price = 15000m,
            Credits = 4,
            IsUnlimited = false
        };
        db.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 4, 1),
            purchasedAtUtc: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            originalPrice: 15000m,
            discountAmount: 0m,
            purchasePrice: 15000m,
            currency: "ARS");
        membership.Activate(new DateOnly(2026, 4, 1), DateTime.UtcNow, Guid.NewGuid());
        await service.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var secretaryUserId = Guid.NewGuid();

        // Act
        var movement = await service.AddCompensatoryCreditAsync(
            new AddCompensatoryCreditRequest(
                student.Id,
                Guid.NewGuid(),
                "Compensación por clase suspendida por el gimnasio"),
            secretaryUserId);

        // Assert
        Assert.Equal(5, membership.AvailableCredits);
        Assert.Equal(5, membership.TotalCredits);
        Assert.Equal(CreditMovementType.ClassSuspensionCompensation, movement.Type);
        Assert.Equal(1, movement.Amount);
        Assert.Equal(secretaryUserId, movement.PerformedByUserId);
    }
}


