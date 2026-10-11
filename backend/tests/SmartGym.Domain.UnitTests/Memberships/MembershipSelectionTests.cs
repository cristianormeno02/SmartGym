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

public class MembershipSelectionTests
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

    private static async Task<Person> CreateStudentAsync(SmartGymDbContext db)
    {
        var role = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(role);
        var student = Person.Create("Julián", "Álvarez", "38999000", birthDate: new DateTime(2000, 1, 31));
        student.AddRole(role);
        db.People.Add(student);
        await db.SaveChangesAsync();
        return student;
    }

    [Fact]
    public async Task SelectMembershipForAsync_ShouldPreferUnlimitedOverCreditBased()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var student = await CreateStudentAsync(db);
        var activity = new Activity("SPIN", "Spinning", defaultCapacity: 15);
        db.Activities.Add(activity);

        var creditPlan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 4",
            DurationDays = 15,
            Price = 10000m,
            Credits = 4,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        var unlimitedPlan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pase Libre",
            DurationDays = 30,
            Price = 25000m,
            Credits = 0,
            IsUnlimited = true,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.AddRange(creditPlan, unlimitedPlan);

        var ledger = new CreditLedgerService(db, calendar);

        // Credit membership expires sooner (2026-06-20), unlimited expires later (2026-06-30)
        var creditMembership = Membership.Create(
            student.Id,
            creditPlan,
            new DateOnly(2026, 6, 6),
            calendar.UtcNow.AddDays(-5),
            10000m,
            0m,
            10000m,
            "ARS");
        creditMembership.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(creditMembership);

        var unlimitedMembership = Membership.Create(
            student.Id,
            unlimitedPlan,
            new DateOnly(2026, 6, 1),
            calendar.UtcNow.AddDays(-1),
            25000m,
            0m,
            25000m,
            "ARS");
        unlimitedMembership.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(unlimitedMembership);

        db.Memberships.AddRange(creditMembership, unlimitedMembership);
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var selection = await service.SelectMembershipForAsync(student.Id, activity.Id, calendar.Today);

        Assert.True(selection.IsEligible);
        Assert.NotNull(selection.SelectedMembership);
        Assert.Equal(unlimitedMembership.Id, selection.SelectedMembership!.Id);
    }

    [Fact]
    public async Task SelectMembershipForAsync_ShouldPreferClosestEndDate_ThenOldestPurchase()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var student = await CreateStudentAsync(db);
        var activity = new Activity("BOX", "Boxeo", defaultCapacity: 15);
        db.Activities.Add(activity);

        var plan15 = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Quincenal",
            DurationDays = 20,
            Price = 10000m,
            Credits = 4,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        var plan30 = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Mensual",
            DurationDays = 30,
            Price = 18000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.AddRange(plan15, plan30);

        var ledger = new CreditLedgerService(db, calendar);

        var mLaterEnd = Membership.Create(
            student.Id,
            plan30,
            new DateOnly(2026, 6, 1),
            new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
            18000m,
            0m,
            18000m,
            "ARS");
        mLaterEnd.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(mLaterEnd);

        var mSoonerEnd = Membership.Create(
            student.Id,
            plan15,
            new DateOnly(2026, 6, 1),
            new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc),
            10000m,
            0m,
            10000m,
            "ARS");
        mSoonerEnd.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(mSoonerEnd);

        db.Memberships.AddRange(mLaterEnd, mSoonerEnd);
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));
        var selection = await service.SelectMembershipForAsync(student.Id, activity.Id, calendar.Today);

        Assert.True(selection.IsEligible);
        Assert.Equal(mSoonerEnd.Id, selection.SelectedMembership!.Id);

        // Tie-breaker by oldest PurchasedAtUtc when EndDate is identical
        await using var dbTie = CreateDbContext();
        var student2 = await CreateStudentAsync(dbTie);
        var activity2 = new Activity("PIL", "Pilates", defaultCapacity: 10);
        dbTie.Activities.Add(activity2);
        dbTie.MembershipPlans.Add(plan30);
        var ledgerTie = new CreditLedgerService(dbTie, calendar);

        var mNewerPurchase = Membership.Create(
            student2.Id,
            plan30,
            new DateOnly(2026, 6, 1),
            new DateTime(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc),
            18000m,
            0m,
            18000m,
            "ARS");
        mNewerPurchase.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        await ledgerTie.RecordInitialCreditsAsync(mNewerPurchase);

        var mOlderPurchase = Membership.Create(
            student2.Id,
            plan30,
            new DateOnly(2026, 6, 1),
            new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
            18000m,
            0m,
            18000m,
            "ARS");
        mOlderPurchase.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        await ledgerTie.RecordInitialCreditsAsync(mOlderPurchase);

        dbTie.Memberships.AddRange(mNewerPurchase, mOlderPurchase);
        await dbTie.SaveChangesAsync();

        var serviceTie = new MembershipEligibilityService(dbTie, calendar, Options.Create(new GymOptions()));
        var tieSelection = await serviceTie.SelectMembershipForAsync(student2.Id, activity2.Id, calendar.Today);

        Assert.True(tieSelection.IsEligible);
        Assert.Equal(mOlderPurchase.Id, tieSelection.SelectedMembership!.Id);
    }

    [Fact]
    public async Task SelectMembershipForAsync_ShouldRespectExplicitMembershipId_AndReturnIssuesWhenNoneEligible()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var student = await CreateStudentAsync(db);
        var activity = new Activity("CROSS", "CrossFit", defaultCapacity: 15);
        db.Activities.Add(activity);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Mensual",
            DurationDays = 30,
            Price = 18000m,
            Credits = 4,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);
        var ledger = new CreditLedgerService(db, calendar);

        var m1 = Membership.Create(
            student.Id,
            plan,
            new DateOnly(2026, 6, 1),
            new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
            18000m,
            0m,
            18000m,
            "ARS");
        m1.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(m1);

        var m2 = Membership.Create(
            student.Id,
            plan,
            new DateOnly(2026, 6, 5),
            new DateTime(2026, 6, 5, 8, 0, 0, DateTimeKind.Utc),
            18000m,
            0m,
            18000m,
            "ARS");
        m2.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(m2);

        db.Memberships.AddRange(m1, m2);
        await db.SaveChangesAsync();

        var service = new MembershipEligibilityService(db, calendar, Options.Create(new GymOptions()));

        // Explicit m2 even though m1 has earlier EndDate
        var explicitSelection = await service.SelectMembershipForAsync(
            student.Id,
            activity.Id,
            calendar.Today,
            explicitMembershipId: m2.Id);
        Assert.True(explicitSelection.IsEligible);
        Assert.Equal(m2.Id, explicitSelection.SelectedMembership!.Id);

        // When only a Pending membership exists, returns its eligibility issues
        await using var dbNone = CreateDbContext();
        var studentNone = await CreateStudentAsync(dbNone);
        dbNone.Activities.Add(activity);
        dbNone.MembershipPlans.Add(plan);

        var pending = Membership.Create(
            studentNone.Id,
            plan,
            new DateOnly(2026, 6, 1),
            calendar.UtcNow,
            18000m,
            0m,
            18000m,
            "ARS");
        dbNone.Memberships.Add(pending);
        await dbNone.SaveChangesAsync();

        var serviceNone = new MembershipEligibilityService(dbNone, calendar, Options.Create(new GymOptions()));
        var noneSelection = await serviceNone.SelectMembershipForAsync(studentNone.Id, activity.Id, calendar.Today);

        Assert.False(noneSelection.IsEligible);
        Assert.Null(noneSelection.SelectedMembership);
        Assert.Contains(noneSelection.Issues, i => i.Code == EligibilityIssueCodes.MembershipPending);
    }
}
