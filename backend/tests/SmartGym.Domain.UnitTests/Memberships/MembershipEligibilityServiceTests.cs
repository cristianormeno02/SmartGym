using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipEligibilityServiceTests
{
    private sealed class StubGymCalendar : IGymCalendar
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateTime UtcNow { get; set; } = new(2026, 6, 15, 14, 0, 0, DateTimeKind.Utc);
        public DateOnly Today { get; set; } = new(2026, 6, 15);
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

    private static async Task<(Person Student, MembershipPlan Plan, Activity Activity, Membership Membership)> SeedActiveMembershipAsync(
        SmartGymDbContext db,
        StubGymCalendar calendar,
        bool isUnlimited = false,
        int credits = 8,
        DateTime? birthDate = null,
        int? minAge = null,
        int? maxAge = null)
    {
        var studentRole = await db.Roles.FirstOrDefaultAsync(r => r.Id == (int)RoleType.Student);
        if (studentRole == null)
        {
            studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
            db.Roles.Add(studentRole);
        }

        var student = Person.Create(
            "Ana",
            "Gómez",
            "30111222",
            birthDate: birthDate ?? new DateTime(1995, 5, 10),
            emergencyContact: EmergencyContact.Create("Tutor", "+549111222333", "Familiar"));
        student.AddRole(studentRole);
        db.People.Add(student);

        var activity = new Activity("FUNC", "Funcional", defaultCapacity: 20, minAge: minAge, maxAge: maxAge);
        db.Activities.Add(activity);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = isUnlimited ? "Pase Libre" : "Plan 8 Clases",
            DurationDays = 30,
            Price = 20000m,
            Credits = isUnlimited ? 0 : credits,
            IsUnlimited = isUnlimited,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 6, 1),
            purchasedAtUtc: calendar.UtcNow,
            originalPrice: 20000m,
            discountAmount: 0m,
            purchasePrice: 20000m,
            currency: "ARS");
        membership.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        var ledger = new CreditLedgerService(db, calendar);
        await ledger.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        return (student, plan, activity, membership);
    }

    [Fact]
    public async Task EvaluateEligibility_EligibleWithCreditsAndUnlimited_ShouldReturnEligibleAndNotMutateCredits()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var (_, _, activity, membership) = await SeedActiveMembershipAsync(db, calendar, isUnlimited: false, credits: 4);
        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));

        var result = await service.EvaluateEligibilityAsync(
            membership.Id,
            targetDate: new DateOnly(2026, 6, 15),
            activityId: activity.Id);

        Assert.True(result.IsEligible);
        Assert.Empty(result.Issues);
        Assert.Equal(4, result.AvailableCredits);
        Assert.Equal(4, (await db.Memberships.SingleAsync(m => m.Id == membership.Id)).AvailableCredits);
        Assert.Single(await db.MembershipCreditMovements.Where(cm => cm.MembershipId == membership.Id).ToListAsync());

        // Unlimited case
        await using var dbUnlimited = CreateDbContext();
        var (_, _, activity2, unlimitedMembership) = await SeedActiveMembershipAsync(dbUnlimited, calendar, isUnlimited: true, credits: 0);
        var serviceUnlimited = new MembershipEligibilityService(dbUnlimited, calendar, Options.Create(new GymOptions()));

        var unlimitedResult = await serviceUnlimited.EvaluateEligibilityAsync(
            unlimitedMembership.Id,
            targetDate: new DateOnly(2026, 6, 15),
            activityId: activity2.Id);

        Assert.True(unlimitedResult.IsEligible);
        Assert.True(unlimitedResult.IsUnlimited);
        Assert.Empty(unlimitedResult.Issues);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenStudentNotActive_ShouldReportStudentNotActive()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var (student, _, activity, membership) = await SeedActiveMembershipAsync(db, calendar);
        student.ChangeStatus(PersonStatus.Blocked, "Falta de pago histórica", Guid.NewGuid(), isAdmin: true);
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var result = await service.EvaluateEligibilityAsync(membership.Id, calendar.Today, activity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.StudentNotActive);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenMembershipPending_ShouldReportMembershipPending()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var (student, plan, activity, _) = await SeedActiveMembershipAsync(db, calendar);

        var pending = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 6, 1),
            purchasedAtUtc: calendar.UtcNow,
            originalPrice: 20000m,
            discountAmount: 0m,
            purchasePrice: 20000m,
            currency: "ARS");
        db.Memberships.Add(pending);
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var result = await service.EvaluateEligibilityAsync(pending.Id, calendar.Today, activity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.MembershipPending);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenMembershipCancelled_ShouldReportMembershipCancelled()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var (_, _, activity, membership) = await SeedActiveMembershipAsync(db, calendar);
        membership.Cancel("Solicitud del alumno", Guid.NewGuid(), calendar.UtcNow);
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var result = await service.EvaluateEligibilityAsync(membership.Id, calendar.Today, activity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.MembershipCancelled);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenMembershipExpiredByDateEvenBeforeWorkerRuns_ShouldReportMembershipExpired()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        var (_, _, activity, membership) = await SeedActiveMembershipAsync(db, calendar);
        calendar.Today = new DateOnly(2026, 7, 15);
        // Persisted status is still Active, but EndDate is 2026-06-30 < Today (2026-07-15)
        Assert.Equal(MembershipStatus.Active, membership.Status);

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var result = await service.EvaluateEligibilityAsync(membership.Id, calendar.Today, activity.Id);

        Assert.False(result.IsEligible);
        Assert.Equal(MembershipStatus.Expired, result.EffectiveStatus);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.MembershipExpired);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenTargetDateOutOfValidityRange_ShouldReportOutOfValidityRange()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        var (_, _, activity, membership) = await SeedActiveMembershipAsync(db, calendar);

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var result = await service.EvaluateEligibilityAsync(membership.Id, new DateOnly(2026, 7, 10), activity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.OutOfValidityRange);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenMembershipSuspended_ShouldReportMembershipSuspended()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        var (_, _, activity, membership) = await SeedActiveMembershipAsync(db, calendar);
        membership.Suspend(
            today: calendar.Today,
            endDate: new DateOnly(2026, 6, 20),
            extendExpirationDate: false,
            reason: "Viaje",
            notes: null,
            suspendedByUserId: Guid.NewGuid(),
            suspendedAtUtc: calendar.UtcNow);
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        // Blocks even for a date after suspension.EndDate while Status == Suspended
        var result = await service.EvaluateEligibilityAsync(membership.Id, new DateOnly(2026, 6, 25), activity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.MembershipSuspended);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenActivityNotActive_ShouldReportActivityNotActive()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var (_, _, activity, membership) = await SeedActiveMembershipAsync(db, calendar);
        activity.ChangeStatus(ActivityStatus.Inactive);
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var result = await service.EvaluateEligibilityAsync(membership.Id, calendar.Today, activity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.ActivityNotActive);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenActivityNotAllowedBySnapshot_ShouldReportActivityNotAllowed()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var (student, _, allowedActivity, _) = await SeedActiveMembershipAsync(db, calendar);

        var otherActivity = new Activity("YOGA", "Yoga", defaultCapacity: 15);
        db.Activities.Add(otherActivity);

        var restrictedPlan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Sólo Funcional",
            DurationDays = 30,
            Price = 15000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = false,
            IsActive = true,
            AllowedActivities = new List<MembershipPlanActivity>
            {
                new() { ActivityId = allowedActivity.Id }
            }
        };
        db.MembershipPlans.Add(restrictedPlan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: restrictedPlan,
            startDate: new DateOnly(2026, 6, 1),
            purchasedAtUtc: calendar.UtcNow,
            originalPrice: 15000m,
            discountAmount: 0m,
            purchasePrice: 15000m,
            currency: "ARS");
        membership.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        var ledger = new CreditLedgerService(db, calendar);
        await ledger.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var result = await service.EvaluateEligibilityAsync(membership.Id, calendar.Today, otherActivity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.ActivityNotAllowed);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenNoAvailableCredits_ShouldReportNoAvailableCredits()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var (_, _, activity, membership) = await SeedActiveMembershipAsync(db, calendar, isUnlimited: false, credits: 1);

        var ledger = new CreditLedgerService(db, calendar);
        await ledger.ConsumeCreditForReservationAsync(membership, Guid.NewGuid(), calendar.Today, "Uso de único crédito");
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var result = await service.EvaluateEligibilityAsync(membership.Id, calendar.Today, activity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.NoAvailableCredits);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenAgeNotAllowed_ShouldReportAgeNotAllowed()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        // Student born 1995 (31 years old), activity only for 6..12
        var (_, _, activity, membership) = await SeedActiveMembershipAsync(
            db,
            calendar,
            birthDate: new DateTime(1995, 1, 1),
            minAge: 6,
            maxAge: 12);

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var result = await service.EvaluateEligibilityAsync(membership.Id, calendar.Today, activity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.AgeNotAllowed);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenMedicalCertificateRequiredAndMissing_ShouldReportMedicalCertificateRequired()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var (_, _, activity, membership) = await SeedActiveMembershipAsync(db, calendar);

        var gymOptions = Options.Create(new GymOptions { RequireValidMedicalCertificateForAccess = true });
        var service = new MembershipEligibilityService(db, calendar, gymOptions);
        var result = await service.EvaluateEligibilityAsync(membership.Id, calendar.Today, activity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.MedicalCertificateRequired);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenUnderageWithoutEmergencyContact_ShouldReportUnderageEmergencyContactRequired()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        var (student, _, activity, membership) = await SeedActiveMembershipAsync(
            db,
            calendar,
            birthDate: new DateTime(2014, 1, 1));
        typeof(Person).GetProperty(nameof(Person.EmergencyContact))!.SetValue(student, null);
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var result = await service.EvaluateEligibilityAsync(membership.Id, calendar.Today, activity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.UnderageEmergencyContactRequired);
    }

    [Fact]
    public async Task EvaluateEligibility_WhenMultipleIssuesExist_ShouldAccumulateAllReasons()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        var (_, _, activity, membership) = await SeedActiveMembershipAsync(
            db,
            calendar,
            isUnlimited: false,
            credits: 1,
            birthDate: new DateTime(1990, 1, 1),
            minAge: 6,
            maxAge: 12);

        var ledger = new CreditLedgerService(db, calendar);
        await ledger.ConsumeCreditForReservationAsync(membership, Guid.NewGuid(), calendar.Today, "Agotar crédito");
        await db.SaveChangesAsync();

        var gymOptions = Options.Create(new GymOptions { RequireValidMedicalCertificateForAccess = true });
        var service = new MembershipEligibilityService(db, calendar, gymOptions);
        var result = await service.EvaluateEligibilityAsync(
            membership.Id,
            targetDate: new DateOnly(2026, 7, 20),
            activityId: activity.Id);

        Assert.False(result.IsEligible);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.OutOfValidityRange);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.NoAvailableCredits);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.AgeNotAllowed);
        Assert.Contains(result.Issues, i => i.Code == EligibilityIssueCodes.MedicalCertificateRequired);
    }

    [Fact]
    public async Task EvaluateForSessionsAsync_EvaluatesMultipleSessionsInMemory_WithoutMutatingCredits()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        var (student, plan, allowedActivity, membership) = await SeedActiveMembershipAsync(
            db,
            calendar,
            isUnlimited: false,
            credits: 2);

        // Restrict plan & membership to allowedActivity only
        plan.AppliesToAllActivities = false;
        membership.AppliesToAllActivities = false;
        db.MembershipPlanActivities.Add(new MembershipPlanActivity
        {
            MembershipPlanId = plan.Id,
            ActivityId = allowedActivity.Id
        });
        db.MembershipAllowedActivities.Add(new MembershipAllowedActivity
        {
            MembershipId = membership.Id,
            ActivityId = allowedActivity.Id
        });

        var otherActivity = new Activity("YOGA", "Yoga", defaultCapacity: 15);
        var thirdActivity = new Activity("BOX", "Boxeo", defaultCapacity: 15);
        db.Activities.AddRange(otherActivity, thirdActivity);

        var session1Id = Guid.NewGuid(); // covered by membership
        var session2Id = Guid.NewGuid(); // otherActivity, covered by SingleClassTicket
        var session3Id = Guid.NewGuid(); // thirdActivity, covered by autonomous CompensatoryCredit
        var session4Id = Guid.NewGuid(); // otherActivity, no ticket and out of membership activity -> ineligible

        var ticketMovement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            ClassSessionId = session2Id,
            Type = CreditMovementType.SingleClassTicket,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Pase individual Yoga",
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };

        var compMovement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            ActivityId = thirdActivity.Id,
            ExpiresOn = new DateOnly(2026, 6, 25),
            Type = CreditMovementType.ClassSuspensionCompensation,
            Amount = 1,
            BalanceAfter = 1,
            Reason = "Compensación Boxeo",
            CreatedAtUtc = calendar.UtcNow,
            IsActive = true
        };

        db.MembershipCreditMovements.AddRange(ticketMovement, compMovement);
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));

        var sessions = new[]
        {
            new SessionEligibilityContextDto(session1Id, allowedActivity.Id, new DateOnly(2026, 6, 16), allowedActivity),
            new SessionEligibilityContextDto(session2Id, otherActivity.Id, new DateOnly(2026, 6, 16), otherActivity),
            new SessionEligibilityContextDto(session3Id, thirdActivity.Id, new DateOnly(2026, 6, 18), thirdActivity),
            new SessionEligibilityContextDto(session4Id, otherActivity.Id, new DateOnly(2026, 6, 19), otherActivity)
        };

        var evaluations = await service.EvaluateForSessionsAsync(student.Id, sessions);

        Assert.Equal(4, evaluations.Count);

        var ev1 = evaluations[session1Id];
        Assert.True(ev1.IsEligible);
        Assert.Equal(ReservationAccessType.MembershipCredit, ev1.SuggestedAccessType);
        Assert.Equal(membership.Id, ev1.SelectedMembershipId);
        Assert.Equal(2, ev1.UsableCredits);

        var ev2 = evaluations[session2Id];
        Assert.True(ev2.IsEligible);
        Assert.Equal(ReservationAccessType.SingleClassTicket, ev2.SuggestedAccessType);
        Assert.Equal(ticketMovement.Id, ev2.SingleClassTicketMovementId);
        Assert.Equal(1, ev2.UsableCredits);

        var ev3 = evaluations[session3Id];
        Assert.True(ev3.IsEligible);
        Assert.Equal(ReservationAccessType.CompensatoryCredit, ev3.SuggestedAccessType);
        Assert.Equal(compMovement.Id, ev3.SingleClassTicketMovementId);
        Assert.Equal(1, ev3.UsableCredits);

        var ev4 = evaluations[session4Id];
        Assert.False(ev4.IsEligible);
        Assert.Null(ev4.SuggestedAccessType);
        Assert.Contains(ev4.Issues, i => i.Code == EligibilityIssueCodes.ActivityNotAllowed);

        // Ensure no credits or movements were mutated
        Assert.Equal(2, (await db.Memberships.SingleAsync(m => m.Id == membership.Id)).AvailableCredits);
    }
}

