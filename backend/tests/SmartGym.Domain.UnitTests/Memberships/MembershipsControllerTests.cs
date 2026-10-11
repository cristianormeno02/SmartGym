using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipsControllerTests
{
    private sealed class StubCurrentUserService : ICurrentUserService
    {
        private readonly HashSet<string> _roles;

        public StubCurrentUserService(Guid? userId, Guid? personId, params string[] roles)
        {
            UserId = userId;
            PersonId = personId;
            _roles = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        }

        public Guid? UserId { get; }
        public Guid? PersonId { get; }
        public string? Email => "user@test.com";
        public bool IsAuthenticated => UserId.HasValue;
        public IReadOnlyList<string> Roles => _roles.ToList();
        public bool IsInRole(string role) => _roles.Contains(role);
    }

    private sealed class StubGymCalendar : IGymCalendar
    {
        public StubGymCalendar(DateOnly today, DateTime? utcNow = null)
        {
            Today = today;
            UtcNow = utcNow ?? today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        }

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateOnly Today { get; set; }
        public DateTime UtcNow { get; set; }
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
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SmartGymDbContext(options);
    }

    private static MembershipsController CreateController(
        SmartGymDbContext db,
        IGymCalendar calendar,
        ICurrentUserService currentUser,
        HttpContext? httpContext = null)
    {
        var creditLedger = new CreditLedgerService(db, calendar);
        var auditService = new AuditService(db);
        var activationService = new MembershipActivationService(db, creditLedger, calendar, auditService);
        var paymentService = new PaymentService(db);
        var promotionService = new PromotionService(db, calendar);
        var eligibilityService = new MembershipEligibilityService(
            db,
            calendar,
            Options.Create(new GymOptions { DefaultCurrency = "ARS" }));

        var membershipService = new MembershipService(
            db,
            creditLedger,
            activationService,
            eligibilityService,
            paymentService,
            promotionService,
            calendar,
            Options.Create(new GymOptions { DefaultCurrency = "ARS" }),
            Options.Create(new MembershipsOptions()),
            auditService);

        var controller = new MembershipsController(
            membershipService,
            eligibilityService,
            creditLedger,
            currentUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext ?? new DefaultHttpContext()
        };

        return controller;
    }

    private static async Task<(Person Student1, Person Student2, MembershipPlan Plan, Activity Activity)> SeedAsync(SmartGymDbContext db)
    {
        var role = new Role
        {
            Id = (int)RoleType.Student,
            Name = RoleType.Student.ToString(),
            Description = "Alumno"
        };
        db.Roles.Add(role);

        var student1 = Person.Create("Alumno", "Uno", "31111111");
        student1.AddRole(role);

        var student2 = Person.Create("Alumno", "Dos", "32222222");
        student2.AddRole(role);

        var activity = new Activity("FUNC", "Funcional", defaultCapacity: 20);
        db.Activities.Add(activity);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8",
            Price = 20000m,
            DurationDays = 30,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };

        db.People.AddRange(student1, student2);
        db.MembershipPlans.Add(plan);
        await db.SaveChangesAsync();

        return (student1, student2, plan, activity);
    }

    [Fact]
    public async Task ContextualAuthorization_StudentCanReadOwnMemberships_ButReceives403ForAnotherStudentOrManagement()
    {
        // Arrange (Criterio 11)
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 6, 1);
        var calendar = new StubGymCalendar(today);
        var (student1, student2, plan, activity) = await SeedAsync(db);

        var adminUser = new StubCurrentUserService(Guid.NewGuid(), Guid.NewGuid(), Roles.Administrator);
        var adminController = CreateController(db, calendar, adminUser);

        var createRes = await adminController.Create(
            new CreateMembershipRequest(
                student1.Id,
                plan.Id,
                StartDate: today,
                InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            CancellationToken.None);
        var createdResult = Assert.IsType<CreatedAtActionResult>(createRes);
        var createdMembership = Assert.IsType<MembershipDetailDto>(createdResult.Value);

        // Alumno 1 consulta su propia membresía, su historial, elegibilidad y créditos -> 200 OK
        var student1User = new StubCurrentUserService(Guid.NewGuid(), student1.Id, Roles.Student);
        var student1Controller = CreateController(db, calendar, student1User);

        Assert.IsType<OkObjectResult>(await student1Controller.GetById(createdMembership.Id, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await student1Controller.GetByStudentId(student1.Id, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await student1Controller.GetCurrentByStudentId(student1.Id, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await student1Controller.GetActiveMembership(student1.Id, today, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await student1Controller.GetEligibility(createdMembership.Id, today, activity.Id, null, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await student1Controller.GetCreditsSummary(createdMembership.Id, CancellationToken.None));

        // Alumno 2 intenta consultar datos de Alumno 1 -> 403 ForbidResult
        var student2User = new StubCurrentUserService(Guid.NewGuid(), student2.Id, Roles.Student);
        var student2Controller = CreateController(db, calendar, student2User);

        Assert.IsType<ForbidResult>(await student2Controller.GetById(createdMembership.Id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await student2Controller.GetByStudentId(student1.Id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await student2Controller.GetCurrentByStudentId(student1.Id, CancellationToken.None));
        Assert.IsType<ForbidResult>(await student2Controller.GetActiveMembership(student1.Id, today, CancellationToken.None));
        Assert.IsType<ForbidResult>(await student2Controller.GetEligibility(createdMembership.Id, today, activity.Id, null, CancellationToken.None));
        Assert.IsType<ForbidResult>(await student2Controller.GetCreditsSummary(createdMembership.Id, CancellationToken.None));

        // Instructor sin rol de Staff intenta listar o gestionar membresías -> 403 ForbidResult
        var instructorUser = new StubCurrentUserService(Guid.NewGuid(), Guid.NewGuid(), Roles.Instructor);
        var instructorController = CreateController(db, calendar, instructorUser);

        Assert.IsType<ForbidResult>(await instructorController.GetPaged(new MembershipListFilter(), CancellationToken.None));
        Assert.IsType<ForbidResult>(await instructorController.Create(new CreateMembershipRequest(student2.Id, plan.Id), CancellationToken.None));
        Assert.IsType<ForbidResult>(await instructorController.Cancel(createdMembership.Id, new CancelMembershipRequest("Intento"), CancellationToken.None));
    }

    [Fact]
    public async Task Endpoints_MapHttpStatusCodes_ReadIdempotencyKeyHeader_AndEnforceAdminOnlyForUnpaidActivation()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 6, 1);
        var calendar = new StubGymCalendar(today);
        var (student1, _, plan, _) = await SeedAsync(db);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Idempotency-Key"] = "header-key-123";

        var secretaryUser = new StubCurrentUserService(Guid.NewGuid(), Guid.NewGuid(), Roles.Secretary);
        var secretaryController = CreateController(db, calendar, secretaryUser, httpContext);

        // 1. Contratación sin pago con header Idempotency-Key -> 201 Created (Pending)
        var createRes = await secretaryController.Create(
            new CreateMembershipRequest(student1.Id, plan.Id, StartDate: today),
            CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(createRes);
        var pendingDto = Assert.IsType<MembershipDetailDto>(created.Value);
        Assert.Equal(MembershipStatus.Pending, pendingDto.Status);

        // Reintento con el mismo header Idempotency-Key y otro plan -> 409 Conflict
        var conflictRes = await secretaryController.Create(
            new CreateMembershipRequest(student1.Id, Guid.NewGuid(), StartDate: today),
            CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(conflictRes);

        // 2. Secretario intenta activar con saldo pendiente -> 403 Forbid
        var secActivateRes = await secretaryController.Activate(
            pendingDto.Id,
            new ActivateMembershipRequest("Intento secretario"),
            CancellationToken.None);
        Assert.IsType<ForbidResult>(secActivateRes);

        // 3. Administrador activa con saldo pendiente y motivo -> 200 OK
        var adminUser = new StubCurrentUserService(Guid.NewGuid(), Guid.NewGuid(), Roles.Administrator);
        var adminController = CreateController(db, calendar, adminUser);

        var admActivateRes = await adminController.Activate(
            pendingDto.Id,
            new ActivateMembershipRequest("Autorizado por gerencia"),
            CancellationToken.None);
        var activatedDto = Assert.IsType<MembershipDetailDto>(Assert.IsType<OkObjectResult>(admActivateRes).Value);
        Assert.Equal(MembershipStatus.Active, activatedDto.Status);

        // 4. PATCH /notes -> 200 OK y 409 con versión vieja
        var patchRes = await secretaryController.UpdateNotes(
            pendingDto.Id,
            new UpdateMembershipNotesRequest("Nota desde PATCH", Version: activatedDto.Version),
            CancellationToken.None);
        var patchedDto = Assert.IsType<MembershipDetailDto>(Assert.IsType<OkObjectResult>(patchRes).Value);
        Assert.Equal("Nota desde PATCH", patchedDto.Notes);

        var stalePatchRes = await secretaryController.UpdateNotes(
            pendingDto.Id,
            new UpdateMembershipNotesRequest("Versión vieja", Version: activatedDto.Version + 50u),
            CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(stalePatchRes);

        // 5. Suspender, Reactivar, Renovar y Cancelar
        var suspendRes = await secretaryController.Suspend(
            pendingDto.Id,
            new SuspendMembershipRequest("Viaje", today.AddDays(3), false),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(suspendRes);

        var reactivateRes = await secretaryController.Reactivate(
            pendingDto.Id,
            new ReactivateMembershipRequest("Regreso"),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(reactivateRes);

        httpContext.Request.Headers["Idempotency-Key"] = "header-key-renew-123";
        var renewRes = await secretaryController.Renew(
            pendingDto.Id,
            new RenewMembershipRequest(),
            CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(renewRes);

        var cancelRes = await secretaryController.Cancel(
            pendingDto.Id,
            new CancelMembershipRequest("Baja definitiva"),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(cancelRes);

        // 6. Membresía inexistente -> 404 NotFound
        var notFoundRes = await secretaryController.GetById(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(notFoundRes);
    }
}
