using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationsControllerTests : IDisposable
{
    private sealed class StubCurrentUserService : ICurrentUserService
    {
        private readonly HashSet<string> _roles;

        public StubCurrentUserService(Guid? personId, params string[] roles)
        {
            UserId = Guid.NewGuid();
            PersonId = personId;
            _roles = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        }

        public Guid? UserId { get; }
        public Guid? PersonId { get; }
        public string? Email => "user@test.com";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles => _roles.ToList();
        public bool IsInRole(string role) => _roles.Contains(role);
    }

    private sealed class StubGymCalendar : IGymCalendar
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
        public DateTime UtcNow { get; set; } = new(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc);
        public DateTime ToUtc(DateOnly date, TimeOnly time) =>
            new(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
        public DateTimeOffset ToLocal(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        public (DateTime FromUtc, DateTime ToUtc) LocalDayRange(DateOnly from, DateOnly to) =>
            (ToUtc(from, TimeOnly.MinValue), ToUtc(to.AddDays(1), TimeOnly.MinValue));
    }

    private readonly SmartGymDbContext _db;
    private readonly StubGymCalendar _calendar = new();
    private readonly Role _studentRole = new() { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
    private readonly Person _student;
    private readonly Person _otherStudent;
    private readonly Person _instructor;
    private readonly ClassSession _session;

    public ReservationsControllerTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new SmartGymDbContext(options);

        _db.Roles.Add(_studentRole);
        _student = CreateStudent("Valentina", "38111222");
        _otherStudent = CreateStudent("Lucas", "39222333");
        _instructor = Person.Create("Martín", "Paz", "instructor@gym.com");

        var activity = new Activity("CROSS", "Cross Training", defaultCapacity: 10);
        var room = new Room { Id = Guid.NewGuid(), Name = "Box 1", Capacity = 10, IsActive = true };

        // La clase empieza 24 h después de "ahora"
        var starts = _calendar.UtcNow.AddDays(1);
        _session = ClassSession.CreateExtraordinary(activity.Id, room.Id, starts, starts.AddHours(1), 10, _instructor.Id, null);
        _session.Open(_calendar.UtcNow);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8 Clases",
            DurationDays = 60,
            Price = 20000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };

        // Sólo _student tiene membresía; _otherStudent no posee ningún derecho de acceso
        var membership = Membership.Create(_student.Id, plan, _calendar.Today, _calendar.UtcNow, 20000m, 0m, 20000m);
        membership.Activate(_calendar.Today, _calendar.UtcNow, Guid.NewGuid());
        new CreditLedgerService(_db, _calendar).RecordInitialCreditsAsync(membership).GetAwaiter().GetResult();

        _db.AddRange(_student, _otherStudent, _instructor, activity, room, _session, plan, membership);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Person CreateStudent(string firstName, string documentNumber)
    {
        var person = Person.Create(
            firstName,
            "Gómez",
            documentNumber,
            birthDate: new DateTime(1996, 5, 20),
            emergencyContact: EmergencyContact.Create("Padre", "+5492644111111", "Padre"));
        person.AddRole(_studentRole);
        return person;
    }

    private StubCurrentUserService AsStudent(Person person) => new(person.Id, Roles.Student);
    private static StubCurrentUserService AsSecretary() => new(Guid.NewGuid(), Roles.Secretary);
    private static StubCurrentUserService AsInstructor(Guid personId) => new(personId, Roles.Instructor);

    private ReservationsController CreateController(ICurrentUserService currentUser, string? idempotencyKey = null)
    {
        var service = new ReservationService(
            _db,
            _calendar,
            currentUserService: currentUser,
            auditService: new AuditService(_db));

        var httpContext = new DefaultHttpContext();
        if (idempotencyKey != null)
        {
            httpContext.Request.Headers[ReservationsController.IdempotencyKeyHeader] = idempotencyKey;
        }

        return new ReservationsController(service, currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private async Task<ReservationDto> ReserveAsStudentAsync(string? idempotencyKey = null)
    {
        var result = await CreateController(AsStudent(_student), idempotencyKey)
            .CreateReservation(new CreateReservationRequest(_session.Id), CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        return Assert.IsType<ReservationDto>(created.Value);
    }

    private static ReservationApiError AssertError(IActionResult result, int expectedStatus, string expectedCode)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        var error = Assert.IsType<ReservationApiError>(objectResult.Value);
        Assert.Equal(expectedCode, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        return error;
    }

    // ---- Creación, idempotencia y contrato de error ----

    [Fact]
    public async Task Create_Returns201_AndReplayWithSameIdempotencyKeyHeaderReturns200WithTheSameReservation()
    {
        var first = await ReserveAsStudentAsync("key-1");
        Assert.Equal(ReservationStatus.Confirmed, first.Status);
        Assert.Equal(_student.Id, first.StudentId);
        Assert.Equal("key-1", first.IdempotencyKey);

        var replay = await CreateController(AsStudent(_student), "key-1")
            .CreateReservation(new CreateReservationRequest(_session.Id), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(replay);
        Assert.Equal(first.Id, Assert.IsType<ReservationDto>(ok.Value).Id);
        Assert.Equal(1, await _db.Reservations.CountAsync());
    }

    [Fact]
    public async Task Create_DuplicateActiveReservation_Returns409WithStableCode()
    {
        await ReserveAsStudentAsync();

        var result = await CreateController(AsStudent(_student))
            .CreateReservation(new CreateReservationRequest(_session.Id), CancellationToken.None);

        AssertError(result, StatusCodes.Status409Conflict, ReservationErrorCodes.AlreadyReserved);
    }

    [Fact]
    public async Task Create_WithoutAccessRight_Returns409WithReasons()
    {
        var result = await CreateController(AsStudent(_otherStudent))
            .CreateReservation(new CreateReservationRequest(_session.Id), CancellationToken.None);

        var error = AssertError(result, StatusCodes.Status409Conflict, ReservationErrorCodes.NoAccessRight);
        Assert.NotNull(error.Reasons);
    }

    [Fact]
    public async Task Create_ForAnotherStudentAsStudent_Returns403()
    {
        var result = await CreateController(AsStudent(_otherStudent))
            .CreateReservation(new CreateReservationRequest(_session.Id, _student.Id), CancellationToken.None);

        AssertError(result, StatusCodes.Status403Forbidden, "FORBIDDEN");
        Assert.Empty(await _db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task Create_OverrideBookingWindow_RequiresStaffAndReason()
    {
        var asStudent = await CreateController(AsStudent(_student)).CreateReservation(
            new CreateReservationRequest(_session.Id, OverrideBookingWindow: true, PolicyOverrideReason: "Quiero"),
            CancellationToken.None);
        AssertError(asStudent, StatusCodes.Status403Forbidden, "FORBIDDEN");

        var staffWithoutReason = await CreateController(AsSecretary()).CreateReservation(
            new CreateReservationRequest(_session.Id, _student.Id, OverrideBookingWindow: true),
            CancellationToken.None);
        AssertError(staffWithoutReason, StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var staffWithReason = await CreateController(AsSecretary()).CreateReservation(
            new CreateReservationRequest(_session.Id, _student.Id, OverrideBookingWindow: true, PolicyOverrideReason: "Evento especial"),
            CancellationToken.None);
        var dto = Assert.IsType<ReservationDto>(Assert.IsType<CreatedAtActionResult>(staffWithReason).Value);
        Assert.True(dto.IsPolicyOverride);
        Assert.Equal(ReservationOrigin.StaffDesk, dto.Origin);
    }

    // ---- Privacidad del alumno (Criterio 17) ----

    [Fact]
    public async Task Student_CannotReadOrCancelAnotherStudentsReservation()
    {
        var reservation = await ReserveAsStudentAsync();
        var intruder = CreateController(AsStudent(_otherStudent));

        AssertError(await intruder.GetById(reservation.Id, CancellationToken.None), StatusCodes.Status403Forbidden, "FORBIDDEN");
        AssertError(
            await intruder.GetByStudentId(_student.Id, new StudentReservationHistoryFilterRequest(), CancellationToken.None),
            StatusCodes.Status403Forbidden,
            "FORBIDDEN");
        AssertError(
            await intruder.CancelReservation(reservation.Id, new CancelReservationRequest("No es mía"), CancellationToken.None),
            StatusCodes.Status403Forbidden,
            "FORBIDDEN");

        var persisted = await _db.Reservations.AsNoTracking().SingleAsync(r => r.Id == reservation.Id);
        Assert.Equal(ReservationStatus.Confirmed, persisted.Status);
    }

    [Fact]
    public async Task Student_CanReadAndCancelOwnReservation()
    {
        var reservation = await ReserveAsStudentAsync();
        var owner = CreateController(AsStudent(_student));

        var detail = Assert.IsType<OkObjectResult>(await owner.GetById(reservation.Id, CancellationToken.None));
        Assert.Equal(reservation.Id, Assert.IsType<ReservationDetailDto>(detail.Value).Id);

        // Sin cuerpo: el motivo es opcional para el alumno
        var cancelled = Assert.IsType<OkObjectResult>(await owner.CancelReservation(reservation.Id, null, CancellationToken.None));
        var dto = Assert.IsType<ReservationDto>(cancelled.Value);
        Assert.Equal(ReservationStatus.Cancelled, dto.Status);
        Assert.False(string.IsNullOrWhiteSpace(dto.CancellationReason));
    }

    // ---- Ámbito del instructor ----

    [Fact]
    public async Task Instructor_SeesOwnClass_Gets404ForForeignClass_And403ForStudentHistory()
    {
        await ReserveAsStudentAsync();

        var assigned = CreateController(AsInstructor(_instructor.Id));
        Assert.IsType<OkObjectResult>(await assigned.GetByClassSession(_session.Id, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await assigned.GetWaitlist(_session.Id, CancellationToken.None));

        var foreign = CreateController(AsInstructor(Guid.NewGuid()));
        AssertError(await foreign.GetByClassSession(_session.Id, CancellationToken.None), StatusCodes.Status404NotFound, "NOT_FOUND");
        AssertError(await foreign.GetWaitlist(_session.Id, CancellationToken.None), StatusCodes.Status404NotFound, "NOT_FOUND");
        AssertError(await foreign.GetSessionAttendanceSummary(_session.Id, CancellationToken.None), StatusCodes.Status404NotFound, "NOT_FOUND");

        AssertError(
            await assigned.GetByStudentId(_student.Id, new StudentReservationHistoryFilterRequest(), CancellationToken.None),
            StatusCodes.Status403Forbidden,
            "FORBIDDEN");
    }

    // ---- Excepción administrativa en cancelación ----

    [Fact]
    public async Task Cancel_OverridePolicy_RequiresStaffAndReason()
    {
        var reservation = await ReserveAsStudentAsync();
        // Faltan 30 minutos: cancelación tardía
        _calendar.UtcNow = _session.StartsAtUtc.AddMinutes(-30);

        var asStudent = await CreateController(AsStudent(_student)).CancelReservation(
            reservation.Id,
            new CancelReservationRequest("Urgencia", OverridePolicy: true, PolicyOverrideReason: "Urgencia"),
            CancellationToken.None);
        AssertError(asStudent, StatusCodes.Status403Forbidden, "FORBIDDEN");

        var staffWithoutReason = await CreateController(AsSecretary()).CancelReservation(
            reservation.Id,
            new CancelReservationRequest("Urgencia", OverridePolicy: true),
            CancellationToken.None);
        AssertError(staffWithoutReason, StatusCodes.Status400BadRequest, "VALIDATION_ERROR");

        var staffWithReason = await CreateController(AsSecretary()).CancelReservation(
            reservation.Id,
            new CancelReservationRequest("Urgencia", OverridePolicy: true, PolicyOverrideReason: "Emergencia médica acreditada"),
            CancellationToken.None);
        var dto = Assert.IsType<ReservationDto>(Assert.IsType<OkObjectResult>(staffWithReason).Value);
        Assert.Equal(ReservationStatus.Cancelled, dto.Status);
        Assert.True(dto.IsPolicyOverride);
        Assert.False(dto.IsLateCancellation);
        Assert.NotNull(dto.RefundCreditMovementId);
    }

    [Fact]
    public async Task Cancel_AfterClassStarted_Returns409ForStaffToo()
    {
        var reservation = await ReserveAsStudentAsync();
        _calendar.UtcNow = _session.StartsAtUtc.AddMinutes(5);

        var result = await CreateController(AsSecretary()).CancelReservation(
            reservation.Id,
            new CancelReservationRequest("Tarde", OverridePolicy: true, PolicyOverrideReason: "Excepción"),
            CancellationToken.None);

        AssertError(result, StatusCodes.Status409Conflict, ReservationErrorCodes.ClassAlreadyStarted);
    }

    // ---- Códigos varios ----

    [Fact]
    public async Task UnknownReservation_Returns404()
    {
        var controller = CreateController(AsSecretary());

        AssertError(await controller.GetById(Guid.NewGuid(), CancellationToken.None), StatusCodes.Status404NotFound, "NOT_FOUND");
        AssertError(
            await controller.CancelReservation(Guid.NewGuid(), null, CancellationToken.None),
            StatusCodes.Status404NotFound,
            "NOT_FOUND");
    }

    [Fact]
    public async Task AvailableClasses_RangeAboveMaximum_Returns400()
    {
        var result = await CreateController(AsStudent(_student)).GetAvailableClasses(
            new AvailableClassesFilterRequest(_calendar.Today, _calendar.Today.AddDays(90)),
            CancellationToken.None);

        AssertError(result, StatusCodes.Status400BadRequest, "VALIDATION_ERROR");
    }

    // ---- Políticas y rutas declaradas ----

    [Theory]
    [InlineData(nameof(ReservationsController.GetPaged), Policies.ReservationsRead)]
    [InlineData(nameof(ReservationsController.CreateReservation), Policies.ReservationsCreate)]
    [InlineData(nameof(ReservationsController.JoinWaitlist), Policies.ReservationsCreate)]
    [InlineData(nameof(ReservationsController.CancelReservation), Policies.ReservationsCancel)]
    [InlineData(nameof(ReservationsController.GetWaitlist), Policies.ReservationsWaitlistRead)]
    [InlineData(nameof(ReservationsController.GetMine), Policies.RequireStudent)]
    [InlineData(nameof(ReservationsController.LeaveWaitlist), Policies.RequireStudent)]
    [InlineData(nameof(ReservationsController.GetByClassSession), Policies.RequireTeachingAndStaff)]
    [InlineData(nameof(ReservationsController.RecordAttendance), Policies.RequireTeachingAndStaff)]
    [InlineData(nameof(ReservationsController.RecordNoShow), Policies.RequireTeachingAndStaff)]
    public void Endpoint_DeclaresExpectedPolicy(string actionName, string expectedPolicy)
    {
        var authorize = typeof(ReservationsController).GetMethod(actionName)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(expectedPolicy, authorize!.Policy);
    }

    [Fact]
    public void EveryAction_RequiresAuthorization()
    {
        var actions = typeof(ReservationsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

        Assert.NotEmpty(actions);
        Assert.All(actions, m => Assert.NotNull(m.GetCustomAttribute<AuthorizeAttribute>()));
    }

    [Fact]
    public void GetByClassSession_KeepsLegacySessionRouteAlias()
    {
        var templates = typeof(ReservationsController)
            .GetMethod(nameof(ReservationsController.GetByClassSession))!
            .GetCustomAttributes<HttpGetAttribute>()
            .Select(a => a.Template)
            .ToList();

        Assert.Contains("class-session/{classSessionId:guid}", templates);
        Assert.Contains("session/{classSessionId:guid}", templates);
    }
}
