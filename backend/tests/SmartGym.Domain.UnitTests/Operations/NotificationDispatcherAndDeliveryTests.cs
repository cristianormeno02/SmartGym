using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.Infrastructure.Services.Notifications;

namespace SmartGym.Domain.UnitTests.Operations;

public class NotificationDispatcherAndDeliveryTests
{
    private static SmartGymDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SmartGymDbContext(options);
    }

    private static IGymCalendar CreateCalendar() =>
        new GymCalendar(Options.Create(new GymOptions()));

    private static NotificationDispatcher CreateDispatcher(SmartGymDbContext db)
    {
        var calendar = CreateCalendar();
        var resolver = new NotificationRecipientResolver(db, calendar);
        var renderer = new NotificationTemplateRenderer(db);
        var service = new NotificationService(db, calendar);
        return new NotificationDispatcher(db, resolver, renderer, service, calendar);
    }

    [Fact]
    public async Task DispatchAsync_CreatesInAppMessageAndPendingEmailDelivery_AndDeduplicatesByIdempotencyKey()
    {
        await using var db = CreateDbContext();
        var student = Person.Create("Valentina", "Rios", "valen@test.com", new DateTime(1998, 4, 15, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(student);
        await db.SaveChangesAsync();

        var dispatcher = CreateDispatcher(db);
        var command = new DispatchNotificationCommand(
            TargetPersonId: student.Id,
            Type: NotificationType.ReservationConfirmed,
            Variables: new Dictionary<string, string?>
            {
                ["recipientName"] = student.FullName,
                ["activityName"] = "Spinning",
                ["sessionDate"] = "20/10 19:00",
                ["roomName"] = "Sala Cycling"
            },
            IdempotencyKey: "res-confirmed:res-100",
            RelatedEntityType: "Reservation",
            RelatedEntityId: Guid.NewGuid(),
            ActionUrl: "/portal/alumno");

        var first = await dispatcher.DispatchAsync(command);
        Assert.Single(first.CreatedMessages);
        Assert.Equal(0, first.DeduplicatedCount);

        var deliveries = await db.NotificationDeliveries
            .Where(d => d.NotificationId == first.CreatedMessages[0].Id)
            .ToListAsync();
        Assert.Equal(2, deliveries.Count);
        Assert.Contains(deliveries, d => d.Channel == NotificationChannel.InApp && d.Status == NotificationDeliveryStatus.Delivered);
        Assert.Contains(deliveries, d => d.Channel == NotificationChannel.Email && d.Status == NotificationDeliveryStatus.Pending && d.RecipientAddress == "valen@test.com");

        // Second dispatch with same IdempotencyKey must be a no-op
        var second = await dispatcher.DispatchAsync(command);
        Assert.Empty(second.CreatedMessages);
        Assert.Equal(1, second.DeduplicatedCount);
        Assert.Equal(1, await db.NotificationMessages.CountAsync());
        Assert.Equal(2, await db.NotificationDeliveries.CountAsync());
    }

    [Fact]
    public async Task DispatchAsync_PromotionalNotification_WhenBothInAppAndEmailDisabled_DoesNotCreateMessage()
    {
        await using var db = CreateDbContext();
        var student = Person.Create("Tomas", "Vega", "tomas@test.com", new DateTime(1993, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(student);
        await db.SaveChangesAsync();

        var service = new NotificationService(db, CreateCalendar());
        await service.UpdatePreferencesAsync(
            student.Id,
            new UpdateNotificationPreferencesRequest(
            [
                new UpdateNotificationPreferenceEntry(NotificationCategory.Promotions, NotificationChannel.InApp, false),
                new UpdateNotificationPreferenceEntry(NotificationCategory.Promotions, NotificationChannel.Email, false)
            ]));

        var dispatcher = CreateDispatcher(db);
        var result = await dispatcher.DispatchAsync(new DispatchNotificationCommand(
            TargetPersonId: student.Id,
            Type: NotificationType.PromotionAvailable,
            Variables: new Dictionary<string, string?>
            {
                ["recipientName"] = student.FullName,
                ["promotionName"] = "2x1 Verano",
                ["promotionDescription"] = "Descuento especial"
            },
            IdempotencyKey: "promo:verano:1"));

        Assert.Empty(result.CreatedMessages);
        Assert.Equal(1, result.SuppressedByPreferenceCount);
        Assert.Equal(0, await db.NotificationMessages.CountAsync());
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_MarksSentWithProviderMessageId_OnSuccess()
    {
        await using var db = CreateDbContext();
        var student = Person.Create("Camila", "Diaz", "camila@test.com", new DateTime(1996, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(student);
        await db.SaveChangesAsync();

        var dispatcher = CreateDispatcher(db);
        await dispatcher.DispatchAsync(new DispatchNotificationCommand(
            TargetPersonId: student.Id,
            Type: NotificationType.PaymentConfirmed,
            Variables: new Dictionary<string, string?>
            {
                ["recipientName"] = student.FullName,
                ["amount"] = "25000",
                ["currency"] = "ARS"
            },
            IdempotencyKey: "pay-confirmed:1"));

        var fakeEmailProvider = new FakeEmailChannelProvider(
            isConfigured: true,
            handler: req => ChannelDeliveryResult.Success("FakeSmtp", "msg-999"));

        var processor = new NotificationDeliveryProcessor(
            db,
            [fakeEmailProvider],
            new NotificationTemplateRenderer(db),
            new NotificationService(db, CreateCalendar()),
            CreateCalendar());

        var processedCount = await processor.ProcessPendingBatchAsync();
        Assert.Equal(1, processedCount);

        var emailDelivery = await db.NotificationDeliveries.SingleAsync(d => d.Channel == NotificationChannel.Email);
        Assert.Equal(NotificationDeliveryStatus.Sent, emailDelivery.Status);
        Assert.Equal("FakeSmtp", emailDelivery.Provider);
        Assert.Equal("msg-999", emailDelivery.ProviderMessageId);
        Assert.Equal(1, emailDelivery.AttemptCount);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_ReschedulesWithExponentialBackoffOnTransientFailure_AndMarksFailedOnPermanentOrUnconfigured()
    {
        await using var db = CreateDbContext();
        var student = Person.Create("Martin", "Sosa", "martin@test.com", new DateTime(1991, 7, 22, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(student);
        await db.SaveChangesAsync();

        var dispatcher = CreateDispatcher(db);
        await dispatcher.DispatchAsync(new DispatchNotificationCommand(
            TargetPersonId: student.Id,
            Type: NotificationType.ReservationConfirmed,
            Variables: new Dictionary<string, string?>
            {
                ["recipientName"] = student.FullName,
                ["activityName"] = "CrossFit",
                ["sessionDate"] = "21/10 10:00",
                ["roomName"] = "Box Principal"
            },
            IdempotencyKey: "res-confirmed:transient-1"));

        var transientProvider = new FakeEmailChannelProvider(
            isConfigured: true,
            handler: _ => ChannelDeliveryResult.TransientFailure("Smtp", "SMTP_TIMEOUT", "Connection timed out"));

        var processor = new NotificationDeliveryProcessor(
            db,
            [transientProvider],
            new NotificationTemplateRenderer(db),
            new NotificationService(db, CreateCalendar()),
            CreateCalendar());

        await processor.ProcessPendingBatchAsync();

        var emailDelivery = await db.NotificationDeliveries.SingleAsync(d => d.Channel == NotificationChannel.Email);
        Assert.Equal(NotificationDeliveryStatus.Pending, emailDelivery.Status);
        Assert.Equal(1, emailDelivery.AttemptCount);
        Assert.Equal("SMTP_TIMEOUT", emailDelivery.LastErrorCode);
        Assert.NotNull(emailDelivery.NextAttemptAtUtc);
        Assert.True(emailDelivery.NextAttemptAtUtc > DateTime.UtcNow.AddSeconds(15));

        // Now test PROVIDER_NOT_CONFIGURED (default ConfigurableEmailNotificationProvider with Enabled = false)
        await dispatcher.DispatchAsync(new DispatchNotificationCommand(
            TargetPersonId: student.Id,
            Type: NotificationType.MembershipActivated,
            Variables: new Dictionary<string, string?>
            {
                ["recipientName"] = student.FullName,
                ["planName"] = "Plan Pase Libre",
                ["startDate"] = "01/10/2026",
                ["endDate"] = "31/10/2026"
            },
            IdempotencyKey: "mem-act:unconf-1"));

        var secondDelivery = await db.NotificationDeliveries
            .OrderByDescending(d => d.CreatedAtUtc)
            .FirstAsync(d => d.Channel == NotificationChannel.Email && d.Id != emailDelivery.Id);

        var unconfiguredProvider = new ConfigurableEmailNotificationProvider(
            Options.Create(new EmailNotificationOptions { Enabled = false }));

        var unconfProcessor = new NotificationDeliveryProcessor(
            db,
            [unconfiguredProvider],
            new NotificationTemplateRenderer(db),
            new NotificationService(db, CreateCalendar()),
            CreateCalendar());

        await unconfProcessor.ProcessPendingBatchAsync();

        await db.Entry(secondDelivery).ReloadAsync();
        Assert.Equal(NotificationDeliveryStatus.Failed, secondDelivery.Status);
        Assert.Equal("PROVIDER_NOT_CONFIGURED", secondDelivery.LastErrorCode);
        Assert.Null(secondDelivery.NextAttemptAtUtc);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_MarksSkipped_WhenPreferenceDisabledOrEmailRemovedBeforeSend()
    {
        await using var db = CreateDbContext();
        var student = Person.Create("Paula", "Castro", "paula@test.com", new DateTime(1994, 11, 5, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(student);
        await db.SaveChangesAsync();

        var dispatcher = CreateDispatcher(db);
        await dispatcher.DispatchAsync(new DispatchNotificationCommand(
            TargetPersonId: student.Id,
            Type: NotificationType.ReservationConfirmed,
            Variables: new Dictionary<string, string?>
            {
                ["recipientName"] = student.FullName,
                ["activityName"] = "Pilates",
                ["sessionDate"] = "22/10 09:00",
                ["roomName"] = "Sala Reformer"
            },
            IdempotencyKey: "res-confirmed:skip-1"));

        // User opts out of Reservations Email before worker processes the queue
        var service = new NotificationService(db, CreateCalendar());
        await service.UpdatePreferencesAsync(
            student.Id,
            new UpdateNotificationPreferencesRequest(
            [
                new UpdateNotificationPreferenceEntry(NotificationCategory.Reservations, NotificationChannel.Email, false)
            ]));

        var providerCalled = false;
        var fakeProvider = new FakeEmailChannelProvider(
            isConfigured: true,
            handler: _ =>
            {
                providerCalled = true;
                return ChannelDeliveryResult.Success("FakeSmtp", "msg-1");
            });

        var processor = new NotificationDeliveryProcessor(
            db,
            [fakeProvider],
            new NotificationTemplateRenderer(db),
            service,
            CreateCalendar());

        await processor.ProcessPendingBatchAsync();

        var emailDelivery = await db.NotificationDeliveries.SingleAsync(d => d.Channel == NotificationChannel.Email);
        Assert.Equal(NotificationDeliveryStatus.Skipped, emailDelivery.Status);
        Assert.False(providerCalled);
    }

    [Fact]
    public async Task ProcessPendingBatchAsync_UsesMessageContent_WhenTemplateVariablesWereNotProvided()
    {
        await using var db = CreateDbContext();
        var student = Person.Create("Lucia", "Paz", "lucia@test.com", new DateTime(1994, 3, 2, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(student);
        await db.SaveChangesAsync();

        const string directBody = "Tu membresía fue suspendida desde el 2026-10-12 hasta el 2026-10-20. Motivo: viaje.";
        var dispatcher = CreateDispatcher(db);
        await dispatcher.DispatchAsync(new DispatchNotificationCommand(
            TargetPersonId: student.Id,
            Type: NotificationType.MembershipSuspended,
            Variables: new Dictionary<string, string?>
            {
                ["planName"] = "Membresía",
                ["reason"] = "viaje"
            },
            IdempotencyKey: "membership-suspended:1",
            DirectTitleOverride: "Membresía suspendida",
            DirectBodyOverride: directBody));

        ChannelDeliveryRequest? sent = null;
        var fakeEmailProvider = new FakeEmailChannelProvider(
            isConfigured: true,
            handler: req =>
            {
                sent = req;
                return ChannelDeliveryResult.Success("FakeSmtp", "msg-1");
            });

        var processor = new NotificationDeliveryProcessor(
            db,
            [fakeEmailProvider],
            new NotificationTemplateRenderer(db),
            new NotificationService(db, CreateCalendar()),
            CreateCalendar());

        await processor.ProcessPendingBatchAsync();

        Assert.NotNull(sent);
        Assert.Equal("Membresía suspendida", sent!.Subject);
        Assert.Equal(directBody, sent.PlainTextBody);
        Assert.Contains("2026-10-20", sent.Body);
    }

    [Fact]
    public async Task DispatchAsync_DropsOversizedMetadataAndTruncatesContent_ToFitColumnLimits()
    {
        await using var db = CreateDbContext();
        var student = Person.Create("Bruno", "Sosa", "bruno@test.com", new DateTime(1991, 7, 9, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(student);
        await db.SaveChangesAsync();

        var dispatcher = CreateDispatcher(db);
        var result = await dispatcher.DispatchAsync(new DispatchNotificationCommand(
            TargetPersonId: student.Id,
            Type: NotificationType.ClassSuspended,
            Variables: new Dictionary<string, string?>
            {
                ["activityName"] = "Spinning",
                ["sessionDate"] = "20/10 19:00",
                ["reason"] = new string('á', 2500)
            },
            IdempotencyKey: "session-suspended:long",
            DirectTitleOverride: new string('T', 250),
            DirectBodyOverride: new string('B', 4500)));

        var message = Assert.Single(result.CreatedMessages);
        Assert.Null(message.MetadataJson);
        Assert.Equal(200, message.Title.Length);
        Assert.Equal(4000, message.Body.Length);
    }

    [Fact]
    public async Task DispatchAsync_StoresAccentedMetadataWithoutUnicodeEscapes()
    {
        await using var db = CreateDbContext();
        var student = Person.Create("Inés", "Núñez", "ines@test.com", new DateTime(1993, 5, 5, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(student);
        await db.SaveChangesAsync();

        var dispatcher = CreateDispatcher(db);
        var result = await dispatcher.DispatchAsync(new DispatchNotificationCommand(
            TargetPersonId: student.Id,
            Type: NotificationType.ClassSuspended,
            Variables: new Dictionary<string, string?>
            {
                ["activityName"] = "Meditación",
                ["sessionDate"] = "20/10 19:00",
                ["reason"] = "Refacción"
            },
            IdempotencyKey: "session-suspended:accents"));

        var message = Assert.Single(result.CreatedMessages);
        Assert.Contains("Meditación", message.MetadataJson);
    }

    [Fact]
    public async Task GetFailedDeliveriesPagedAsync_CanExcludeProviderNotConfiguredFailures()
    {
        await using var db = CreateDbContext();
        var student = Person.Create("Mora", "Vega", "mora@test.com", new DateTime(1992, 2, 2, 0, 0, 0, DateTimeKind.Utc));
        db.People.Add(student);
        await db.SaveChangesAsync();

        var nowUtc = CreateCalendar().UtcNow;
        var dispatcher = CreateDispatcher(db);
        foreach (var key in new[] { "res-confirmed:a", "res-confirmed:b" })
        {
            await dispatcher.DispatchAsync(new DispatchNotificationCommand(
                TargetPersonId: student.Id,
                Type: NotificationType.ReservationConfirmed,
                Variables: new Dictionary<string, string?> { ["activityName"] = "Yoga", ["sessionDate"] = "20/10 19:00" },
                IdempotencyKey: key));
        }

        var emailDeliveries = await db.NotificationDeliveries
            .Where(d => d.Channel == NotificationChannel.Email)
            .ToListAsync();
        emailDeliveries[0].RecordPermanentFailure("PROVIDER_NOT_CONFIGURED", "Sin proveedor.", nowUtc);
        emailDeliveries[1].RecordPermanentFailure("INVALID_RECIPIENT_EMAIL", "Dirección inválida.", nowUtc);
        await db.SaveChangesAsync();

        var service = new NotificationService(db, CreateCalendar());

        var all = await service.GetFailedDeliveriesPagedAsync();
        Assert.Equal(2, all.TotalCount);

        var actionable = await service.GetFailedDeliveriesPagedAsync(excludeProviderNotConfigured: true);
        Assert.Equal(1, actionable.TotalCount);
        Assert.Equal("INVALID_RECIPIENT_EMAIL", Assert.Single(actionable.Items).ErrorCode);
    }

    private sealed class FakeEmailChannelProvider : IEmailNotificationProvider
    {
        private readonly Func<ChannelDeliveryRequest, ChannelDeliveryResult> _handler;

        public FakeEmailChannelProvider(bool isConfigured, Func<ChannelDeliveryRequest, ChannelDeliveryResult> handler)
        {
            IsConfigured = isConfigured;
            _handler = handler;
        }

        public NotificationChannel Channel => NotificationChannel.Email;
        public string ProviderName => "FakeSmtp";
        public bool IsConfigured { get; }

        public Task<ChannelDeliveryResult> SendAsync(ChannelDeliveryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_handler(request));
    }
}
