using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.IntegrationTests.Fixtures;
using Xunit;

namespace SmartGym.IntegrationTests.Reservations;

[Collection("PostgresIntegration")]
public class ReservationPartialUniqueIndexesIntegrationTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public ReservationPartialUniqueIndexesIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static void DetachReservationWithChanges(
        SmartGym.Infrastructure.Persistence.SmartGymDbContext db,
        Reservation reservation)
    {
        foreach (var sc in reservation.StatusChanges.ToList())
        {
            db.Entry(sc).State = EntityState.Detached;
        }
        db.Entry(reservation).State = EntityState.Detached;
    }

    private static async Task<(Person student1, Person student2, ClassSession session1, ClassSession session2)> SeedActorsAndSessionsAsync(
        SmartGym.Infrastructure.Persistence.SmartGymDbContext db)
    {
        var now = DateTime.UtcNow;
        var student1 = Person.Create("Alumno", "Uno", $"u1.{Guid.NewGuid():N}@test.com");
        var student2 = Person.Create("Alumno", "Dos", $"u2.{Guid.NewGuid():N}@test.com");
        var instructor1 = Person.Create("Inst", "Uno", $"inst1.{Guid.NewGuid():N}@test.com");
        var instructor2 = Person.Create("Inst", "Dos", $"inst2.{Guid.NewGuid():N}@test.com");

        var room1 = new Room
        {
            Name = $"Room-{Guid.NewGuid():N}"[..20],
            Capacity = 20
        };
        var room2 = new Room
        {
            Name = $"Room-{Guid.NewGuid():N}"[..20],
            Capacity = 20
        };

        var code = $"ACT_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var activity = new Activity(
            code: code,
            name: $"Act-{Guid.NewGuid():N}"[..20],
            shortDescription: "Short",
            description: "Desc",
            defaultCapacity: 15);

        db.People.AddRange(student1, student2, instructor1, instructor2);
        db.Rooms.AddRange(room1, room2);
        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        var session1 = ClassSession.CreateExtraordinary(
            activity.Id,
            room1.Id,
            now.AddDays(5),
            now.AddDays(5).AddHours(1),
            10,
            instructor1.Id,
            secretaryId: null);

        var session2 = ClassSession.CreateExtraordinary(
            activity.Id,
            room2.Id,
            now.AddDays(6),
            now.AddDays(6).AddHours(1),
            10,
            instructor2.Id,
            secretaryId: null);

        db.ClassSessions.AddRange(session1, session2);
        await db.SaveChangesAsync();

        return (student1, student2, session1, session2);
    }

    [Fact]
    public async Task UX_Reservations_Active_Session_Student_RejectsDuplicateActive_AllowsAfterCancel()
    {
        await using var db = _fixture.CreateDbContext();
        var (student, _, session, _) = await SeedActorsAndSessionsAsync(db);
        var now = DateTime.UtcNow;

        var r1 = Reservation.CreateConfirmed(
            session.Id,
            student.Id,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.UnlimitedMembership,
            now);

        db.Reservations.Add(r1);
        await db.SaveChangesAsync();

        var duplicateActive = Reservation.CreateWaitlisted(
            session.Id,
            student.Id,
            position: 1,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.UnlimitedMembership,
            now);

        db.Reservations.Add(duplicateActive);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)ex.InnerException!).SqlState);

        DetachReservationWithChanges(db, duplicateActive);

        // Cancel r1 -> now another active reservation on the same session and student is allowed
        r1.Cancel(
            reason: "Cancelada por el alumno",
            isLateCancellation: false,
            nowUtc: now.AddMinutes(5),
            source: ReservationCancellationSource.Student,
            trigger: ReservationChangeTrigger.StudentAction);
        await db.SaveChangesAsync();

        var r2 = Reservation.CreateConfirmed(
            session.Id,
            student.Id,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.UnlimitedMembership,
            now.AddMinutes(10));
        db.Reservations.Add(r2);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UX_Reservations_Student_IdempotencyKey_RejectsDuplicateKeyForSameStudent()
    {
        await using var db = _fixture.CreateDbContext();
        var (student1, student2, session1, session2) = await SeedActorsAndSessionsAsync(db);
        var now = DateTime.UtcNow;
        var key = $"idem-{Guid.NewGuid():N}";

        var r1 = Reservation.CreateConfirmed(
            session1.Id,
            student1.Id,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.UnlimitedMembership,
            now,
            idempotencyKey: key);
        db.Reservations.Add(r1);
        await db.SaveChangesAsync();

        // Same student, same key -> rejected
        var dupSameStudent = Reservation.CreateConfirmed(
            session2.Id,
            student1.Id,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.UnlimitedMembership,
            now,
            idempotencyKey: key);
        db.Reservations.Add(dupSameStudent);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)ex.InnerException!).SqlState);
        DetachReservationWithChanges(db, dupSameStudent);

        // Different student, same key -> allowed
        var diffStudent = Reservation.CreateConfirmed(
            session1.Id,
            student2.Id,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.UnlimitedMembership,
            now,
            idempotencyKey: key);
        db.Reservations.Add(diffStudent);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UX_Reservations_Held_AccessMovement_HoldsOnLateCancel_ReleasesOnOverrideOrEarlyCancel()
    {
        await using var db = _fixture.CreateDbContext();
        var (student, _, session1, session2) = await SeedActorsAndSessionsAsync(db);
        var now = DateTime.UtcNow;

        var ticketMovement = new MembershipCreditMovement
        {
            StudentId = student.Id,
            ClassSessionId = session1.Id,
            Type = CreditMovementType.SingleClassTicket,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Pase individual"
        };
        db.MembershipCreditMovements.Add(ticketMovement);
        await db.SaveChangesAsync();

        var r1 = Reservation.CreateConfirmed(
            session1.Id,
            student.Id,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.SingleClassTicket,
            now,
            singleClassTicketMovementId: ticketMovement.Id);
        db.Reservations.Add(r1);
        await db.SaveChangesAsync();

        // Late cancellation keeps the ticket held in the index
        r1.Cancel(
            reason: "Cancelación tardía",
            isLateCancellation: true,
            nowUtc: now.AddMinutes(5),
            source: ReservationCancellationSource.Student,
            trigger: ReservationChangeTrigger.StudentAction);
        await db.SaveChangesAsync();

        var reuseAfterLateCancel = Reservation.CreateConfirmed(
            session2.Id,
            student.Id,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.SingleClassTicket,
            now.AddMinutes(6),
            singleClassTicketMovementId: ticketMovement.Id);
        db.Reservations.Add(reuseAfterLateCancel);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)ex.InnerException!).SqlState);
        DetachReservationWithChanges(db, reuseAfterLateCancel);

        // Now test with a second ticket cancelled with policy override (IsLateCancellation = false) -> releases ticket
        var ticketMovement2 = new MembershipCreditMovement
        {
            StudentId = student.Id,
            ClassSessionId = session1.Id,
            Type = CreditMovementType.ClassSuspensionCompensation,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Compensación"
        };
        db.MembershipCreditMovements.Add(ticketMovement2);
        await db.SaveChangesAsync();

        var rWithOverride = Reservation.CreateConfirmed(
            session2.Id,
            student.Id,
            ReservationOrigin.StaffDesk,
            ReservationAccessType.CompensatoryCredit,
            now.AddMinutes(10),
            singleClassTicketMovementId: ticketMovement2.Id);
        db.Reservations.Add(rWithOverride);
        await db.SaveChangesAsync();

        rWithOverride.Cancel(
            reason: "Excepción administrativa",
            isLateCancellation: false,
            nowUtc: now.AddMinutes(12),
            source: ReservationCancellationSource.Staff,
            trigger: ReservationChangeTrigger.StaffAction,
            isPolicyOverride: true,
            policyOverrideReason: "Certificado médico presentado");
        await db.SaveChangesAsync();

        var reuseAfterOverride = Reservation.CreateConfirmed(
            session2.Id,
            student.Id,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.CompensatoryCredit,
            now.AddMinutes(15),
            singleClassTicketMovementId: ticketMovement2.Id);
        db.Reservations.Add(reuseAfterOverride);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UX_Reservations_PromotionRedemption_RejectsSecondReservationWithSameRedemption()
    {
        await using var db = _fixture.CreateDbContext();
        var (student, _, session1, session2) = await SeedActorsAndSessionsAsync(db);
        var now = DateTime.UtcNow;

        var promo = new Promotion
        {
            Code = $"PR-{Guid.NewGuid():N}"[..10],
            Name = "Promo Clase Gratis",
            Description = "Desc",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 100m,
            ValidFrom = DateOnly.FromDateTime(now.AddDays(-1)),
            ValidTo = DateOnly.FromDateTime(now.AddDays(10))
        };
        db.Promotions.Add(promo);
        await db.SaveChangesAsync();

        var redemption = new PromotionRedemption
        {
            PromotionId = promo.Id,
            PersonId = student.Id,
            OriginalAmount = 1000m,
            DiscountAmount = 1000m,
            FinalAmount = 0m,
            RedeemedAtUtc = now
        };
        db.PromotionRedemptions.Add(redemption);
        await db.SaveChangesAsync();

        var r1 = Reservation.CreateConfirmed(
            session1.Id,
            student.Id,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.PromotionalBenefit,
            now,
            promotionRedemptionId: redemption.Id);
        db.Reservations.Add(r1);
        await db.SaveChangesAsync();

        var r2 = Reservation.CreateConfirmed(
            session2.Id,
            student.Id,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.PromotionalBenefit,
            now.AddMinutes(5),
            promotionRedemptionId: redemption.Id);
        db.Reservations.Add(r2);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)ex.InnerException!).SqlState);
    }
}
