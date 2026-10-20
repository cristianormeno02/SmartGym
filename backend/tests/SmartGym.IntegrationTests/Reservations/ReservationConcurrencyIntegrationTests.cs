using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.IntegrationTests.Fixtures;

namespace SmartGym.IntegrationTests.Reservations;

/// <summary>
/// Concurrencia real contra PostgreSQL: cada operación corre en su propio scope de DI (su propio DbContext y
/// transacción), con los servicios y handlers registrados por <c>AddApplication</c>, como una solicitud HTTP.
/// </summary>
[Collection("PostgresIntegration")]
public class ReservationConcurrencyIntegrationTests : IDisposable
{
    private const int InitialCredits = 8;

    private readonly PostgresDatabaseFixture _fixture;
    private readonly ServiceProvider _provider;

    public ReservationConcurrencyIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
        _provider = BuildProvider();
    }

    private ServiceProvider BuildProvider(
        Action<ReservationsOptions>? configureReservations = null,
        TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddApplication();
        if (timeProvider != null)
        {
            services.AddSingleton(timeProvider);
        }
        services.AddScoped(sp => _fixture.CreateDbContext(sp));
        services.AddScoped<ISmartGymDbContext>(sp => sp.GetRequiredService<SmartGymDbContext>());
        if (configureReservations != null)
        {
            services.Configure(configureReservations);
        }

        return services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    /// <summary>Reloj del sistema desplazado un intervalo fijo: el tiempo sigue avanzando, pero en otra hora del día.</summary>
    private sealed class ShiftedTimeProvider(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + offset;
    }

    /// <summary>
    /// Reloj que ubica "ahora" a las 23:00 hora del gimnasio, cuando la fecha UTC ya es la del día siguiente
    /// (con UTC-3, entre las 21:00 y las 24:00 locales).
    /// </summary>
    private ServiceProvider BuildLateEveningProvider()
    {
        var calendar = _provider.GetRequiredService<IGymCalendar>();
        var lateEveningUtc = calendar.ToUtc(calendar.Today, new TimeOnly(23, 0));
        var provider = BuildProvider(timeProvider: new ShiftedTimeProvider(lateEveningUtc - calendar.UtcNow));
        var shifted = provider.GetRequiredService<IGymCalendar>();
        Assert.NotEqual(DateOnly.FromDateTime(shifted.UtcNow), shifted.Today);
        return provider;
    }

    private sealed record Scenario(Guid SessionId, List<Guid> StudentIds, Dictionary<Guid, Guid> MembershipByStudent, Guid StaffUserId);

    private async Task<Scenario> SeedAsync(int students, int capacity, ServiceProvider? provider = null)
    {
        await using var db = _fixture.CreateDbContext();
        // Las fechas de vigencia salen del calendario del gimnasio, igual que en los servicios: con la fecha UTC,
        // entre las 21:00 y las 24:00 locales la membresía "empezaría mañana" y no podría suspenderse.
        var calendar = (provider ?? _provider).GetRequiredService<IGymCalendar>();
        var now = calendar.UtcNow;
        var today = calendar.Today;
        var suffix = Guid.NewGuid().ToString("N");

        var studentRole = await db.Roles.FirstOrDefaultAsync(r => r.Id == (int)RoleType.Student);
        if (studentRole == null)
        {
            studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
            db.Roles.Add(studentRole);
        }

        var instructor = Person.Create("Inst", "Concurrencia", $"inst.{suffix}@test.com");
        var staffPerson = Person.Create("Staff", "Concurrencia", $"staff.{suffix}@test.com");
        var staffUser = new User { Id = Guid.NewGuid(), PersonId = staffPerson.Id, Username = $"staff.{suffix}", IsActive = true };
        var room = new Room { Name = $"Room-{suffix}"[..20], Capacity = 30 };
        var activity = new Activity(
            code: $"C{suffix}"[..12].ToUpperInvariant(),
            name: $"Act-{suffix}"[..20],
            shortDescription: "Short",
            description: "Desc",
            defaultCapacity: 15);
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = $"Plan-{suffix}"[..20],
            DurationDays = 60,
            Price = 20000m,
            Credits = InitialCredits,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };

        db.People.AddRange(instructor, staffPerson);
        db.Users.Add(staffUser);
        db.Rooms.Add(room);
        db.Activities.Add(activity);
        db.MembershipPlans.Add(plan);
        await db.SaveChangesAsync();

        var session = ClassSession.CreateExtraordinary(
            activity.Id, room.Id, now.AddDays(3), now.AddDays(3).AddHours(1), capacity, instructor.Id, secretaryId: null);
        session.Open(now);
        db.ClassSessions.Add(session);

        var ledger = new CreditLedgerService(db);
        var studentIds = new List<Guid>();
        var membershipByStudent = new Dictionary<Guid, Guid>();

        for (var i = 0; i < students; i++)
        {
            var student = Person.Create($"Alumno{i}", "Concurrencia", $"alumno{i}.{suffix}@test.com", birthDate: new DateTime(1995, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            student.AddRole(studentRole);
            db.People.Add(student);

            var membership = Membership.Create(student.Id, plan, today, now, 20000m, 0m, 20000m);
            membership.Activate(today, now, changedByUserId: null);
            await ledger.RecordInitialCreditsAsync(membership);
            db.Memberships.Add(membership);

            studentIds.Add(student.Id);
            membershipByStudent[student.Id] = membership.Id;
        }

        await db.SaveChangesAsync();
        return new Scenario(session.Id, studentIds, membershipByStudent, staffUser.Id);
    }

    /// <summary>Ejecuta la operación en un scope propio y devuelve su resultado o la excepción que lanzó.</summary>
    private async Task<(T? Result, Exception? Error)> RunInScopeAsync<T>(
        Func<IServiceProvider, Task<T>> operation,
        ServiceProvider? provider = null)
    {
        // Task.Run: asegura que las operaciones arranquen en paralelo y no encadenadas por la parte síncrona.
        return await Task.Run(async () =>
        {
            await using var scope = (provider ?? _provider).CreateAsyncScope();
            try
            {
                return (await operation(scope.ServiceProvider), (Exception?)null);
            }
            catch (Exception ex)
            {
                return (default(T), ex);
            }
        });
    }

    private Task<(ReservationDto? Result, Exception? Error)> ReserveAsync(
        Guid sessionId, Guid studentId, bool allowWaitlist = true, string? idempotencyKey = null,
        ServiceProvider? provider = null) =>
        RunInScopeAsync(sp => sp.GetRequiredService<IReservationService>().CreateReservationAsync(
            new CreateReservationRequest(sessionId, studentId, AllowWaitlist: allowWaitlist, IdempotencyKey: idempotencyKey),
            Guid.Empty), provider);

    private Task<(ReservationDto? Result, Exception? Error)> CancelAsync(Guid reservationId, ServiceProvider? provider = null) =>
        RunInScopeAsync(sp => sp.GetRequiredService<IReservationService>().CancelReservationAsync(
            reservationId, new CancelReservationRequest("Cancelación concurrente"), Guid.Empty), provider);

    private static void AssertOnlyConflicts(IEnumerable<Exception?> errors)
    {
        foreach (var error in errors.Where(e => e != null))
        {
            Assert.True(error is ConflictException, $"Se esperaba ConflictException y se obtuvo {error!.GetType().Name}: {error.Message}");
        }
    }

    private async Task<int> CountMovementsAsync(SmartGymDbContext db, Guid sessionId, CreditMovementType type) =>
        await db.MembershipCreditMovements.CountAsync(m => m.ClassSessionId == sessionId && m.Type == type);

    [Fact]
    public async Task LastSeatRace_NeverOversells()
    {
        const int capacity = 3;
        var scenario = await SeedAsync(students: 8, capacity: capacity);

        var outcomes = await Task.WhenAll(
            scenario.StudentIds.Select(studentId => ReserveAsync(scenario.SessionId, studentId, allowWaitlist: false)));

        var succeeded = outcomes.Where(o => o.Error == null).ToList();
        var failed = outcomes.Where(o => o.Error != null).Select(o => o.Error).ToList();

        Assert.Equal(capacity, succeeded.Count);
        Assert.All(succeeded, o => Assert.Equal(ReservationStatus.Confirmed, o.Result!.Status));
        Assert.All(failed, e => Assert.Equal(ReservationErrorCodes.ClassFull, Assert.IsType<ConflictException>(e).Code));

        await using var db = _fixture.CreateDbContext();
        var session = await db.ClassSessions.AsNoTracking().SingleAsync(cs => cs.Id == scenario.SessionId);
        Assert.Equal(capacity, session.ReservedCount);
        Assert.Equal(capacity, await db.Reservations.CountAsync(r => r.ClassSessionId == scenario.SessionId));
        Assert.Equal(capacity, await CountMovementsAsync(db, scenario.SessionId, CreditMovementType.ClassReservationDebit));
    }

    [Fact]
    public async Task ConcurrentDuplicateRequests_CreateOneReservationAndOneDebit()
    {
        var scenario = await SeedAsync(students: 1, capacity: 10);
        var studentId = scenario.StudentIds[0];

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ReserveAsync(scenario.SessionId, studentId)));

        Assert.Single(outcomes, o => o.Error == null);
        AssertOnlyConflicts(outcomes.Select(o => o.Error));

        await using var db = _fixture.CreateDbContext();
        Assert.Equal(1, await db.Reservations.CountAsync(r => r.ClassSessionId == scenario.SessionId));
        Assert.Equal(1, await CountMovementsAsync(db, scenario.SessionId, CreditMovementType.ClassReservationDebit));
        var membership = await db.Memberships.AsNoTracking().SingleAsync(m => m.Id == scenario.MembershipByStudent[studentId]);
        Assert.Equal(InitialCredits - 1, membership.AvailableCredits);
        Assert.Equal(1, (await db.ClassSessions.AsNoTracking().SingleAsync(cs => cs.Id == scenario.SessionId)).ReservedCount);
    }

    [Fact]
    public async Task ConcurrentIdempotentRetries_ReturnTheSameReservationWithASingleDebit()
    {
        var scenario = await SeedAsync(students: 1, capacity: 10);
        var studentId = scenario.StudentIds[0];
        var key = $"retry-{Guid.NewGuid():N}";

        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => ReserveAsync(scenario.SessionId, studentId, idempotencyKey: key)));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Single(outcomes.Select(o => o.Result!.Id).Distinct());
        Assert.Single(outcomes, o => !o.Result!.WasIdempotentReplay);

        await using var db = _fixture.CreateDbContext();
        Assert.Equal(1, await db.Reservations.CountAsync(r => r.ClassSessionId == scenario.SessionId));
        Assert.Equal(1, await CountMovementsAsync(db, scenario.SessionId, CreditMovementType.ClassReservationDebit));
        var membership = await db.Memberships.AsNoTracking().SingleAsync(m => m.Id == scenario.MembershipByStudent[studentId]);
        Assert.Equal(InitialCredits - 1, membership.AvailableCredits);
    }

    [Fact]
    public async Task ConcurrentCancellationsOfTheSameReservation_RefundOnce()
    {
        var scenario = await SeedAsync(students: 1, capacity: 10);
        var studentId = scenario.StudentIds[0];
        var reservation = (await ReserveAsync(scenario.SessionId, studentId)).Result!;

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CancelAsync(reservation.Id)));

        Assert.Single(outcomes, o => o.Error == null);
        AssertOnlyConflicts(outcomes.Select(o => o.Error));

        await using var db = _fixture.CreateDbContext();
        Assert.Equal(1, await CountMovementsAsync(db, scenario.SessionId, CreditMovementType.ClassCancellationRefund));
        var membership = await db.Memberships.AsNoTracking().SingleAsync(m => m.Id == scenario.MembershipByStudent[studentId]);
        Assert.Equal(InitialCredits, membership.AvailableCredits);
        Assert.Equal(0, (await db.ClassSessions.AsNoTracking().SingleAsync(cs => cs.Id == scenario.SessionId)).ReservedCount);
    }

    [Fact]
    public async Task ConcurrentCancellationsWithWaitlist_PromoteEachCandidateOnceWithoutExceedingCapacity()
    {
        const int capacity = 2;
        var scenario = await SeedAsync(students: 4, capacity: capacity);

        // Dos confirmados y dos en lista de espera, en orden
        var confirmedA = (await ReserveAsync(scenario.SessionId, scenario.StudentIds[0])).Result!;
        var confirmedB = (await ReserveAsync(scenario.SessionId, scenario.StudentIds[1])).Result!;
        var waiting1 = (await ReserveAsync(scenario.SessionId, scenario.StudentIds[2])).Result!;
        var waiting2 = (await ReserveAsync(scenario.SessionId, scenario.StudentIds[3])).Result!;
        Assert.Equal(ReservationStatus.Waitlisted, waiting1.Status);
        Assert.Equal(ReservationStatus.Waitlisted, waiting2.Status);

        var outcomes = await Task.WhenAll(CancelAsync(confirmedA.Id), CancelAsync(confirmedB.Id));
        Assert.All(outcomes, o => Assert.Null(o.Error));

        await using var db = _fixture.CreateDbContext();
        var reservations = await db.Reservations.AsNoTracking()
            .Where(r => r.ClassSessionId == scenario.SessionId)
            .ToListAsync();

        Assert.Equal(capacity, reservations.Count(r => r.Status == ReservationStatus.Confirmed));
        Assert.Equal(capacity, reservations.Count(r => r.Status == ReservationStatus.Cancelled));
        Assert.DoesNotContain(reservations, r => r.Status == ReservationStatus.Waitlisted);
        Assert.Equal(ReservationStatus.Confirmed, reservations.Single(r => r.Id == waiting1.Id).Status);
        Assert.Equal(ReservationStatus.Confirmed, reservations.Single(r => r.Id == waiting2.Id).Status);
        Assert.Equal(capacity, (await db.ClassSessions.AsNoTracking().SingleAsync(cs => cs.Id == scenario.SessionId)).ReservedCount);

        // Un único débito por cada candidato promovido
        foreach (var promoted in new[] { waiting1.Id, waiting2.Id })
        {
            Assert.Equal(1, await db.MembershipCreditMovements.CountAsync(
                m => m.ReservationId == promoted && m.Type == CreditMovementType.ClassReservationDebit));
        }
    }

    [Fact]
    public Task ReservationCancellationRacingMembershipSuspension_DoesNotDeadlockAndEndsConsistent() =>
        AssertCancellationRacingSuspensionEndsConsistentAsync(_provider);

    [Fact]
    public async Task ReservationCancellationRacingMembershipSuspension_LateEveningGymTime_EndsConsistent()
    {
        using var lateEvening = BuildLateEveningProvider();
        await AssertCancellationRacingSuspensionEndsConsistentAsync(lateEvening);
    }

    private async Task AssertCancellationRacingSuspensionEndsConsistentAsync(ServiceProvider provider)
    {
        const int pairs = 4;
        var scenario = await SeedAsync(students: pairs, capacity: 10, provider);

        var reservations = new List<ReservationDto>();
        foreach (var studentId in scenario.StudentIds)
        {
            reservations.Add((await ReserveAsync(scenario.SessionId, studentId, provider: provider)).Result!);
        }

        // Por cada alumno compiten la cancelación de su reserva y la suspensión de su membresía
        var tasks = new List<Task<Exception?>>();
        foreach (var reservation in reservations)
        {
            var membershipId = scenario.MembershipByStudent[reservation.StudentId];
            tasks.Add(CancelAsync(reservation.Id, provider).ContinueWith(t => t.Result.Error));
            tasks.Add(RunInScopeAsync(sp => sp.GetRequiredService<IMembershipService>().SuspendAsync(
                membershipId, new SuspendMembershipRequest("Suspensión concurrente"), scenario.StaffUserId), provider)
                .ContinueWith(t => t.Result.Error));
        }

        var all = Task.WhenAll(tasks);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(90)));
        Assert.True(finished == all, "Las operaciones concurrentes no terminaron: posible interbloqueo.");

        AssertOnlyConflicts(await all);

        await using var db = _fixture.CreateDbContext();
        var persisted = await db.Reservations.AsNoTracking()
            .Where(r => r.ClassSessionId == scenario.SessionId)
            .ToListAsync();

        Assert.Equal(pairs, persisted.Count);
        Assert.All(persisted, r => Assert.Equal(ReservationStatus.Cancelled, r.Status));
        Assert.Equal(0, (await db.ClassSessions.AsNoTracking().SingleAsync(cs => cs.Id == scenario.SessionId)).ReservedCount);

        // Gane quien gane, cada reserva se devuelve exactamente una vez
        foreach (var reservation in reservations)
        {
            Assert.Equal(1, await db.MembershipCreditMovements.CountAsync(
                m => m.ReservationId == reservation.Id && m.Type == CreditMovementType.ClassCancellationRefund));
            var membership = await db.Memberships.AsNoTracking()
                .SingleAsync(m => m.Id == scenario.MembershipByStudent[reservation.StudentId]);
            Assert.Equal(InitialCredits, membership.AvailableCredits);
            Assert.Equal(MembershipStatus.Suspended, membership.Status);
        }
    }

    [Fact]
    public async Task OfferMode_HoldsTheSeatUntilTheOfferIsDeclinedOrAccepted()
    {
        using var offerProvider = BuildProvider(o => o.WaitlistPromotionMode = WaitlistPromotionMode.OfferWithConfirmation);
        var scenario = await SeedAsync(students: 3, capacity: 1);

        Task<(ReservationDto? Result, Exception? Error)> Run(Func<IReservationService, Task<ReservationDto>> op) =>
            RunInScopeAsync(sp => op(sp.GetRequiredService<IReservationService>()), offerProvider);

        var confirmed = (await ReserveAsync(scenario.SessionId, scenario.StudentIds[0])).Result!;
        var waiting1 = (await ReserveAsync(scenario.SessionId, scenario.StudentIds[1])).Result!;
        var waiting2 = (await ReserveAsync(scenario.SessionId, scenario.StudentIds[2])).Result!;

        // Se libera el cupo: el primero en espera recibe una oferta que retiene el lugar sin consumir crédito
        var cancel = await Run(s => s.CancelReservationAsync(confirmed.Id, new CancelReservationRequest("Libero el cupo"), Guid.Empty));
        Assert.Null(cancel.Error);

        await using (var db = _fixture.CreateDbContext())
        {
            var offered = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == waiting1.Id);
            Assert.Equal(ReservationStatus.Pending, offered.Status);
            Assert.NotNull(offered.WaitlistOfferExpiresAtUtc);
            Assert.Null(offered.CreditMovementId);
            Assert.Equal(1, (await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == waiting2.Id)).WaitListPosition);
            Assert.Equal(1, (await db.ClassSessions.AsNoTracking().SingleAsync(cs => cs.Id == scenario.SessionId)).ReservedCount);
        }

        // El primero rechaza: la oferta pasa al siguiente, que la acepta y recién ahí consume su crédito
        var decline = await Run(s => s.DeclineWaitlistOfferAsync(waiting1.Id, Guid.Empty));
        Assert.Null(decline.Error);
        var accept = await Run(s => s.AcceptWaitlistOfferAsync(waiting2.Id, Guid.Empty));
        Assert.Null(accept.Error);
        Assert.Equal(ReservationStatus.Confirmed, accept.Result!.Status);

        await using var verifyDb = _fixture.CreateDbContext();
        Assert.Equal(ReservationStatus.Cancelled, (await verifyDb.Reservations.AsNoTracking().SingleAsync(r => r.Id == waiting1.Id)).Status);
        Assert.Equal(1, (await verifyDb.ClassSessions.AsNoTracking().SingleAsync(cs => cs.Id == scenario.SessionId)).ReservedCount);
        Assert.Equal(1, await verifyDb.MembershipCreditMovements.CountAsync(
            m => m.ReservationId == waiting2.Id && m.Type == CreditMovementType.ClassReservationDebit));
        Assert.Equal(0, await verifyDb.MembershipCreditMovements.CountAsync(
            m => m.ReservationId == waiting1.Id && m.Type == CreditMovementType.ClassReservationDebit));
    }
}
