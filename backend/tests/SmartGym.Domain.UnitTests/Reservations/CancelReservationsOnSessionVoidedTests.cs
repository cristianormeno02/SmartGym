using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Reservations.Handlers;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Activities;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class CancelReservationsOnSessionVoidedTests
{
    private readonly DbContextOptions<SmartGymDbContext> _options;
    private readonly Guid _activityId;
    private readonly Guid _roomId;
    private readonly Guid _instructorId;
    private readonly Guid _student1Id = Guid.NewGuid();
    private readonly Guid _student2Id = Guid.NewGuid();
    private readonly Guid _student3Id = Guid.NewGuid();
    private readonly Guid _planId = Guid.NewGuid();

    public CancelReservationsOnSessionVoidedTests()
    {
        _options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var seed = new SmartGymDbContext(_options);

        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        var instructorRole = new Role { Id = (int)RoleType.Instructor, Name = RoleType.Instructor.ToString(), Description = "Instructor" };
        seed.Roles.AddRange(studentRole, instructorRole);

        var s1 = Person.Create("Student", "One", "s1@gym.com");
        s1.Id = _student1Id;
        s1.AddRole(studentRole);

        var s2 = Person.Create("Student", "Two", "s2@gym.com");
        s2.Id = _student2Id;
        s2.AddRole(studentRole);

        var s3 = Person.Create("Student", "Three", "s3@gym.com");
        s3.Id = _student3Id;
        s3.AddRole(studentRole);

        var inst = Person.Create("Profe", "Test", "inst@gym.com");
        _instructorId = inst.Id;
        inst.AddRole(instructorRole);

        var room = new Room { Id = Guid.NewGuid(), Name = "Sala A", Capacity = 30, IsActive = true };
        _roomId = room.Id;

        var activity = new Activity("PILATES", "Pilates", defaultCapacity: 10);
        _activityId = activity.Id;

        var plan = new MembershipPlan { Id = _planId, Name = "Plan Mensual", Credits = 10, Price = 1000, IsActive = true };

        seed.AddRange(s1, s2, s3, inst, room, activity, plan);
        seed.SaveChanges();
    }

    private ServiceProvider BuildServiceProvider(SmartGymDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddScoped<ISmartGymDbContext>(_ => dbContext);
        services.AddScoped<CancelReservationsOnSessionVoidedHandler>();
        services.AddScoped<IDomainEventHandler<ClassSessionSuspended>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        services.AddScoped<IDomainEventHandler<ClassSessionCancelled>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        services.AddScoped<IDomainEventHandler<ClassSessionRescheduled>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SuspendSession_CancelsActiveAndWaitlistReservations_RefundsCredits_AndResetsReservedCount()
    {
        using var setupDb = new SmartGymDbContext(_options);

        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activityId, _roomId, starts, ends, 10, _instructorId, null);
        session.Open(DateTime.UtcNow);
        setupDb.ClassSessions.Add(session);

        // Alumno 1: con membresía (Reserved)
        var m1 = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = _student1Id,
            MembershipPlanId = _planId,
            StartDate = DateOnly.FromDateTime(starts).AddDays(-5),
            EndDate = DateOnly.FromDateTime(starts).AddDays(5),
            TotalCredits = 10,
            AvailableCredits = 5,
            Status = MembershipStatus.Active,
            IsActive = true
        };
        setupDb.Memberships.Add(m1);

        var r1 = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = session.Id,
            StudentId = _student1Id,
            MembershipId = m1.Id,
            Status = ReservationStatus.Reserved,
            ReservedAtUtc = DateTime.UtcNow,
            IsActive = true
        };
        setupDb.Reservations.Add(r1);
        session.IncrementReservedCount();

        // Alumno 2: sin membresía (con ticket individual, Confirmed)
        var r2 = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = session.Id,
            StudentId = _student2Id,
            MembershipId = null,
            Status = ReservationStatus.Confirmed,
            ReservedAtUtc = DateTime.UtcNow,
            IsActive = true
        };
        setupDb.Reservations.Add(r2);
        session.IncrementReservedCount();

        // Alumno 3: en WaitList
        var r3 = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = session.Id,
            StudentId = _student3Id,
            MembershipId = m1.Id,
            Status = ReservationStatus.WaitList,
            WaitListPosition = 1,
            ReservedAtUtc = DateTime.UtcNow,
            IsActive = true
        };
        setupDb.Reservations.Add(r3);

        await setupDb.SaveChangesAsync();

        // Ahora ejecutamos la suspensión usando el DbContext con despacho de domain events
        var services = new ServiceCollection();
        SmartGymDbContext? dbWithEvents = null;
        services.AddScoped<ISmartGymDbContext>(_ => dbWithEvents!);
        services.AddScoped<CancelReservationsOnSessionVoidedHandler>();
        services.AddScoped<IDomainEventHandler<ClassSessionSuspended>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        services.AddScoped<IDomainEventHandler<ClassSessionCancelled>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        services.AddScoped<IDomainEventHandler<ClassSessionRescheduled>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        var sp = services.BuildServiceProvider();

        dbWithEvents = new SmartGymDbContext(_options, sp);

        var sessionToSuspend = await dbWithEvents.ClassSessions.FirstAsync(cs => cs.Id == session.Id);
        sessionToSuspend.Suspend(ClassSessionSuspensionReason.Weather, "Lluvia intensa", Guid.NewGuid(), DateTime.UtcNow);

        await dbWithEvents.SaveChangesAsync();

        // Verificaciones
        using var verifyDb = new SmartGymDbContext(_options);
        var loadedSession = await verifyDb.ClassSessions.FirstAsync(cs => cs.Id == session.Id);
        Assert.Equal(ClassSessionStatus.Suspended, loadedSession.Status);
        Assert.Equal(0, loadedSession.ReservedCount);

        var reservations = await verifyDb.Reservations.Where(r => r.ClassSessionId == session.Id).ToListAsync();
        Assert.All(reservations, r =>
        {
            Assert.Equal(ReservationStatus.Cancelled, r.Status);
            Assert.False(r.IsLateCancellation);
            Assert.Null(r.WaitListPosition);
        });

        var r1Loaded = reservations.Single(r => r.Id == r1.Id);
        Assert.NotNull(r1Loaded.RefundCreditMovementId);

        // Ticket individual: queda cancelada sin movimiento, identificada para compensación administrativa
        var r2Loaded = reservations.Single(r => r.Id == r2.Id);
        Assert.Null(r2Loaded.RefundCreditMovementId);
        Assert.Null(r2Loaded.MembershipId);

        var r3Loaded = reservations.Single(r => r.Id == r3.Id);
        Assert.Null(r3Loaded.RefundCreditMovementId);

        // Verificamos que al alumno 1 con membresía se le devolvió el crédito
        var loadedM1 = await verifyDb.Memberships.FirstAsync(m => m.Id == m1.Id);
        Assert.Equal(6, loadedM1.AvailableCredits); // 5 + 1 refund

        // La compensación del ticket individual la otorga un administrativo (AddCompensatoryCreditAsync), no el handler
        var hasAutomaticCompensation = await verifyDb.MembershipCreditMovements
            .AnyAsync(cm => cm.StudentId == _student2Id && cm.ClassSessionId == session.Id);
        Assert.False(hasAutomaticCompensation);
    }
}
