using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class NotificationAuthorizationAndControllerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 10, 12, 16, 0, 0, DateTimeKind.Utc);

    private static SmartGymDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(databaseName: $"NotificationsAuthTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SmartGymDbContext(options);
    }

    private static IGymCalendar CreateFixedCalendar(DateTime utcNow)
    {
        var mock = new Mock<IGymCalendar>();
        var inner = new GymCalendar(Options.Create(new GymOptions()));
        mock.Setup(c => c.UtcNow).Returns(utcNow);
        mock.Setup(c => c.Today).Returns(DateOnly.FromDateTime(utcNow));
        mock.Setup(c => c.TimeZone).Returns(inner.TimeZone);
        return mock.Object;
    }

    private static ICurrentUserService CreateCurrentUser(Guid userId, Guid personId, string email = "user@test.com")
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(u => u.UserId).Returns(userId);
        mock.Setup(u => u.PersonId).Returns(personId);
        mock.Setup(u => u.Email).Returns(email);
        mock.Setup(u => u.IsAuthenticated).Returns(true);
        return mock.Object;
    }

    [Fact]
    public async Task NotificationsController_GetById_And_ReadUnreadMutations_RejectAccessToOtherPersonsNotificationWith403()
    {
        await using var db = CreateInMemoryContext();
        var calendar = CreateFixedCalendar(FixedUtcNow);

        var personA = Person.Create("Ana", "Gomez", "ana@test.com", new DateTime(1992, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var personB = Person.Create("Bruno", "Diaz", "bruno@test.com", new DateTime(1991, 8, 12, 0, 0, 0, DateTimeKind.Utc));
        db.People.AddRange(personA, personB);

        var notifB = NotificationMessage.Create(
            recipientPersonId: personB.Id,
            type: NotificationType.ReservationConfirmed,
            title: "Reserva de Bruno",
            body: "Confirmada",
            nowUtc: FixedUtcNow);
        db.NotificationMessages.Add(notifB);
        await db.SaveChangesAsync();

        var currentUserA = CreateCurrentUser(Guid.NewGuid(), personA.Id, "ana@test.com");
        var service = new NotificationService(db, calendar, currentUserA);
        var controller = new NotificationsController(service, currentUserA);

        // 1. GET /api/notifications/{id} -> 403 Forbidden
        var getResult = await controller.GetById(notifB.Id, CancellationToken.None);
        var getForbidden = Assert.IsType<ObjectResult>(getResult);
        Assert.Equal(StatusCodes.Status403Forbidden, getForbidden.StatusCode);

        // 2. PATCH /api/notifications/{id}/read -> 403 Forbidden
        var patchReadResult = await controller.MarkAsReadPatch(notifB.Id, CancellationToken.None);
        var patchReadForbidden = Assert.IsType<ObjectResult>(patchReadResult);
        Assert.Equal(StatusCodes.Status403Forbidden, patchReadForbidden.StatusCode);

        // 3. POST /api/notifications/{id}/read -> 403 Forbidden
        var postReadResult = await controller.MarkAsRead(notifB.Id, CancellationToken.None);
        var postReadForbidden = Assert.IsType<ObjectResult>(postReadResult);
        Assert.Equal(StatusCodes.Status403Forbidden, postReadForbidden.StatusCode);

        // 4. PATCH /api/notifications/{id}/unread -> 403 Forbidden
        var patchUnreadResult = await controller.MarkAsUnread(notifB.Id, CancellationToken.None);
        var patchUnreadForbidden = Assert.IsType<ObjectResult>(patchUnreadResult);
        Assert.Equal(StatusCodes.Status403Forbidden, patchUnreadForbidden.StatusCode);

        // Verify Bruno's notification remained untouched
        var reloaded = await db.NotificationMessages.SingleAsync(n => n.Id == notifB.Id);
        Assert.False(reloaded.IsRead);
        Assert.Null(reloaded.ReadAtUtc);
    }

    [Fact]
    public async Task NotificationsController_GetPaged_UnreadCount_And_MarkAllRead_ScopeStrictlyToAuthenticatedPersonId()
    {
        await using var db = CreateInMemoryContext();
        var calendar = CreateFixedCalendar(FixedUtcNow);

        var personA = Person.Create("Ana", "Gomez", "ana@test.com", new DateTime(1992, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var personB = Person.Create("Bruno", "Diaz", "bruno@test.com", new DateTime(1991, 8, 12, 0, 0, 0, DateTimeKind.Utc));
        db.People.AddRange(personA, personB);

        var a1 = NotificationMessage.Create(personA.Id, NotificationType.ReservationConfirmed, "Reserva 1", "Body 1", FixedUtcNow.AddMinutes(-30));
        var a2 = NotificationMessage.Create(personA.Id, NotificationType.PaymentConfirmed, "Pago 1", "Body 2", FixedUtcNow.AddMinutes(-20));
        var a3 = NotificationMessage.Create(personA.Id, NotificationType.ReservationCancelled, "Reserva 2", "Body 3", FixedUtcNow.AddMinutes(-10));
        a3.MarkAsRead(FixedUtcNow.AddMinutes(-5));

        var b1 = NotificationMessage.Create(personB.Id, NotificationType.ReservationConfirmed, "Reserva B", "Body B", FixedUtcNow.AddMinutes(-15));

        db.NotificationMessages.AddRange(a1, a2, a3, b1);
        await db.SaveChangesAsync();

        var currentUserA = CreateCurrentUser(Guid.NewGuid(), personA.Id, "ana@test.com");
        var service = new NotificationService(db, calendar, currentUserA);
        var controller = new NotificationsController(service, currentUserA);

        // Unread count for A is 2 (a1 and a2)
        var unreadAction = await controller.GetUnreadCount(CancellationToken.None);
        var unreadOk = Assert.IsType<OkObjectResult>(unreadAction);
        var unreadDto = Assert.IsType<UnreadNotificationsCountDto>(unreadOk.Value);
        Assert.Equal(2, unreadDto.UnreadCount);

        // Paged query filtered by Category = Reservations for A returns a3 and a1 (2 items), never b1
        var pagedAction = await controller.GetNotifications(
            isRead: null,
            category: NotificationCategory.Reservations,
            type: null,
            fromUtc: null,
            toUtc: null,
            page: 1,
            pageSize: 10,
            cancellationToken: CancellationToken.None);

        var pagedOk = Assert.IsType<OkObjectResult>(pagedAction);
        var pagedResult = Assert.IsType<PagedResult<NotificationDto>>(pagedOk.Value);
        Assert.Equal(2, pagedResult.TotalCount);
        Assert.All(pagedResult.Items, item => Assert.Equal(personA.Id, item.RecipientPersonId));

        // Mark all read for A marks only A's 2 unread notifications; B's notification remains unread
        var markAllAction = await controller.MarkAllAsRead(CancellationToken.None);
        var markAllOk = Assert.IsType<OkObjectResult>(markAllAction);
        var markAllDto = Assert.IsType<MarkAllNotificationsReadResultDto>(markAllOk.Value);
        Assert.Equal(2, markAllDto.MarkedCount);

        var reloadedB1 = await db.NotificationMessages.SingleAsync(n => n.Id == b1.Id);
        Assert.False(reloadedB1.IsRead);
    }

    [Fact]
    public async Task StudentRole_LacksAdminNotificationPermissions_And_AdminControllersEnforcePolicies()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=smartgym;Username=postgres;Password=postgres",
                ["Jwt:SecretKey"] = "12345678901234567890123456789012"
            })
            .Build();
        SmartGym.Infrastructure.DependencyInjection.AddInfrastructure(services, config);
        await using var provider = services.BuildServiceProvider();
        var authService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<IAuthorizationService>(provider);

        var studentPrincipal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, Roles.Student)],
                "TestAuth"));

        var adminPrincipal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, Roles.Administrator)],
                "TestAuth"));

        Assert.True((await authService.AuthorizeAsync(studentPrincipal, Policies.NotificationsReadOwn)).Succeeded);
        Assert.True((await authService.AuthorizeAsync(studentPrincipal, Policies.NotificationsMarkReadOwn)).Succeeded);
        Assert.True((await authService.AuthorizeAsync(studentPrincipal, Policies.NotificationsPreferencesManage)).Succeeded);

        Assert.False((await authService.AuthorizeAsync(studentPrincipal, Policies.NotificationsReadAll)).Succeeded);
        Assert.False((await authService.AuthorizeAsync(studentPrincipal, Policies.NotificationsManage)).Succeeded);
        Assert.False((await authService.AuthorizeAsync(studentPrincipal, Policies.NotificationsDeliveryRead)).Succeeded);
        Assert.False((await authService.AuthorizeAsync(studentPrincipal, Policies.NotificationsRetryFailed)).Succeeded);
        Assert.False((await authService.AuthorizeAsync(studentPrincipal, Policies.NotificationsTemplatesRead)).Succeeded);
        Assert.False((await authService.AuthorizeAsync(studentPrincipal, Policies.NotificationsTemplatesManage)).Succeeded);

        Assert.True((await authService.AuthorizeAsync(adminPrincipal, Policies.NotificationsReadAll)).Succeeded);
        Assert.True((await authService.AuthorizeAsync(adminPrincipal, Policies.NotificationsRetryFailed)).Succeeded);
        Assert.True((await authService.AuthorizeAsync(adminPrincipal, Policies.NotificationsTemplatesManage)).Succeeded);

        // Verify AdminNotificationsController & AdminNotificationTemplatesController actions have explicit Authorize policies
        var adminNotificationsMethods = typeof(AdminNotificationsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(adminNotificationsMethods);
        foreach (var method in adminNotificationsMethods)
        {
            var authorizeAttr = method.GetCustomAttribute<AuthorizeAttribute>()
                                ?? typeof(AdminNotificationsController).GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorizeAttr);
            Assert.False(string.IsNullOrWhiteSpace(authorizeAttr!.Policy));
        }

        var adminTemplatesMethods = typeof(AdminNotificationTemplatesController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(adminTemplatesMethods);
        foreach (var method in adminTemplatesMethods)
        {
            var authorizeAttr = method.GetCustomAttribute<AuthorizeAttribute>()
                                ?? typeof(AdminNotificationTemplatesController).GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorizeAttr);
            Assert.False(string.IsNullOrWhiteSpace(authorizeAttr!.Policy));
        }
    }

    [Fact]
    public async Task NotificationService_RetryDeliveryAsync_RequeuesFailedDelivery_AndWritesAuditLog()
    {
        await using var db = CreateInMemoryContext();
        var calendar = CreateFixedCalendar(FixedUtcNow);
        var adminUserId = Guid.NewGuid();
        var adminPersonId = Guid.NewGuid();
        var currentUser = CreateCurrentUser(adminUserId, adminPersonId, "admin@smartgym.com");

        var student = Person.Create("Lucia", "Vega", "lucia@test.com", new DateTime(1995, 4, 10, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(student);

        var message = NotificationMessage.Create(
            student.Id,
            NotificationType.PaymentConfirmed,
            "Pago confirmado",
            "Se registró tu pago",
            FixedUtcNow.AddMinutes(-10));
        db.NotificationMessages.Add(message);

        var delivery = NotificationDelivery.CreatePending(
            message.Id,
            NotificationChannel.Email,
            "ConfigurableSmtp",
            student.Email,
            FixedUtcNow.AddMinutes(-10),
            maxAttempts: 4);
        delivery.MarkProcessing(FixedUtcNow.AddMinutes(-9));
        delivery.RecordPermanentFailure("SMTP_AUTH_ERROR", "Credenciales SMTP vencidas", FixedUtcNow.AddMinutes(-9));
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        var service = new NotificationService(db, calendar, currentUser);

        var retriedDto = await service.RetryDeliveryAsync(
            delivery.Id,
            new RetryNotificationDeliveryRequest("Credenciales SMTP rotadas y verificadas"),
            CancellationToken.None);

        Assert.Equal(NotificationDeliveryStatus.Pending, retriedDto.Status);
        Assert.Equal(adminUserId, retriedDto.RetriedByUserId);

        var auditEntry = await db.AuditLogs.SingleAsync();
        Assert.Equal("NOTIFICATION_DELIVERY_MANUAL_RETRY", auditEntry.Action);
        Assert.Equal("NotificationDelivery", auditEntry.EntityName);
        Assert.Equal(delivery.Id.ToString(), auditEntry.EntityId);
        Assert.Equal(adminUserId, auditEntry.UserId);
    }

    [Fact]
    public async Task NotificationService_TemplateLifecycle_CreatesUpdatesActivatesDeactivates_AndAuditsEachMutation()
    {
        await using var db = CreateInMemoryContext();
        var calendar = CreateFixedCalendar(FixedUtcNow);
        var adminUserId = Guid.NewGuid();
        var adminPersonId = Guid.NewGuid();
        var currentUser = CreateCurrentUser(adminUserId, adminPersonId, "admin@smartgym.com");

        var service = new NotificationService(db, calendar, currentUser);

        // 1. Create active template v1
        var v1 = await service.CreateTemplateAsync(
            new CreateNotificationTemplateRequest(
                Code: "RES_CONFIRMED_CUSTOM",
                Type: NotificationType.ReservationConfirmed,
                Channel: NotificationChannel.Email,
                SubjectTemplate: "Reserva confirmada: {{activityName}}",
                BodyTemplate: "<p>Hola {{recipientName}}, tu clase de {{activityName}} está confirmada.</p>",
                PlainTextTemplate: "Hola {{recipientName}}, tu clase de {{activityName}} está confirmada.",
                AllowedVariables: ["recipientName", "activityName"],
                Locale: "es-AR",
                Version: 1,
                ActivateImmediately: true),
            CancellationToken.None);

        Assert.Equal(NotificationTemplateStatus.Active, v1.Status);
        Assert.Equal(1, v1.Version);

        // 2. Create draft template v2 for same Type + Channel + Locale
        var v2 = await service.CreateTemplateAsync(
            new CreateNotificationTemplateRequest(
                Code: "RES_CONFIRMED_CUSTOM",
                Type: NotificationType.ReservationConfirmed,
                Channel: NotificationChannel.Email,
                SubjectTemplate: "Confirmación v2: {{activityName}}",
                BodyTemplate: "<p>Hola {{recipientName}}!</p>",
                PlainTextTemplate: "Hola {{recipientName}}!",
                AllowedVariables: ["recipientName", "activityName"],
                Locale: "es-AR",
                Version: 2,
                ActivateImmediately: false),
            CancellationToken.None);

        Assert.Equal(NotificationTemplateStatus.Draft, v2.Status);
        Assert.Equal(2, v2.Version);

        // 3. Update v2
        var updatedV2 = await service.UpdateTemplateAsync(
            v2.Id,
            new UpdateNotificationTemplateRequest(
                SubjectTemplate: "Confirmación v2 actualizada: {{activityName}}",
                BodyTemplate: "<p>Hola {{recipientName}}, te esperamos en {{activityName}}.</p>",
                PlainTextTemplate: "Hola {{recipientName}}, te esperamos en {{activityName}}.",
                AllowedVariables: ["recipientName", "activityName"]),
            CancellationToken.None);

        Assert.Equal("Confirmación v2 actualizada: {{activityName}}", updatedV2.SubjectTemplate);

        // 4. Activate v2 -> automatically deactivates v1
        var activatedV2 = await service.ActivateTemplateAsync(v2.Id, CancellationToken.None);
        Assert.Equal(NotificationTemplateStatus.Active, activatedV2.Status);

        var reloadedV1 = await db.NotificationTemplates.SingleAsync(t => t.Id == v1.Id);
        Assert.Equal(NotificationTemplateStatus.Inactive, reloadedV1.Status);

        // 5. Deactivate v2
        var deactivatedV2 = await service.DeactivateTemplateAsync(v2.Id, CancellationToken.None);
        Assert.Equal(NotificationTemplateStatus.Inactive, deactivatedV2.Status);

        // Verify AuditLogs recorded for all 5 mutations
        var auditLogs = await db.AuditLogs
            .OrderBy(a => a.TimestampUtc)
            .ToListAsync();
        Assert.Equal(5, auditLogs.Count);
        Assert.All(auditLogs, log =>
        {
            Assert.Equal("NotificationTemplate", log.EntityName);
            Assert.Equal(adminUserId, log.UserId);
        });
    }
}
