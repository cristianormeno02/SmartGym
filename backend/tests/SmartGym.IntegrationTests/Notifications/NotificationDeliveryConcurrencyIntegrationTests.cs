using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.IntegrationTests.Fixtures;
using Xunit;

namespace SmartGym.IntegrationTests.Notifications;

[Collection("PostgresIntegration")]
public class NotificationDeliveryConcurrencyIntegrationTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public NotificationDeliveryConcurrencyIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static IGymCalendar CreateCalendar() =>
        new GymCalendar(Options.Create(new GymOptions()));

    private static NotificationDispatcher CreateDispatcher(SmartGymDbContext db)
    {
        var calendar = CreateCalendar();
        return new NotificationDispatcher(
            db,
            new NotificationRecipientResolver(db, calendar),
            new NotificationTemplateRenderer(db),
            new NotificationService(db, calendar),
            calendar);
    }

    [Fact]
    public async Task ConcurrentWorkers_ClaimDisjointPendingDeliveriesViaSkipLocked_AndSendEachOnce()
    {
        var studentId = Guid.NewGuid();

        await using (var seedDb = _fixture.CreateDbContext())
        {
            await seedDb.NotificationDeliveries.ExecuteDeleteAsync();

            var student = Person.Create("Diego", "Morales", $"diego.{studentId:N}@test.com", new DateTime(1990, 6, 15, 0, 0, 0, DateTimeKind.Utc));
            student.Id = studentId;
            seedDb.People.Add(student);
            await seedDb.SaveChangesAsync();

            var dispatcher = CreateDispatcher(seedDb);
            for (var i = 1; i <= 4; i++)
            {
                await dispatcher.DispatchAsync(new DispatchNotificationCommand(
                    TargetPersonId: studentId,
                    Type: NotificationType.ReservationConfirmed,
                    Variables: new Dictionary<string, string?>
                    {
                        ["recipientName"] = student.FullName,
                        ["activityName"] = $"Funcional #{i}",
                        ["sessionDate"] = "25/10 18:00",
                        ["roomName"] = "Sala A"
                    },
                    IdempotencyKey: $"conc-res:{studentId:N}:{i}"));
            }
        }

        var sentDeliveryIds = new ConcurrentBag<Guid>();
        var provider = new RecordingEmailProvider(async req =>
        {
            await Task.Delay(20);
            sentDeliveryIds.Add(req.DeliveryId);
            return ChannelDeliveryResult.Success("ConcurrentSmtp", $"msg-{req.DeliveryId:N}");
        });

        Task<int> RunWorkerLoopAsync() => Task.Run(async () =>
        {
            var total = 0;
            for (var iteration = 0; iteration < 5; iteration++)
            {
                await using var workerDb = _fixture.CreateDbContext();
                var calendar = CreateCalendar();
                var processor = new NotificationDeliveryProcessor(
                    workerDb,
                    [provider],
                    new NotificationTemplateRenderer(workerDb),
                    new NotificationService(workerDb, calendar),
                    calendar);

                var count = await processor.ProcessPendingBatchAsync(batchSize: 2);
                if (count == 0)
                {
                    break;
                }

                total += count;
            }

            return total;
        });

        var results = await Task.WhenAll(RunWorkerLoopAsync(), RunWorkerLoopAsync());

        Assert.Equal(4, results.Sum());
        Assert.Equal(4, sentDeliveryIds.Count);
        Assert.Equal(4, sentDeliveryIds.Distinct().Count());

        await using var verifyDb = _fixture.CreateDbContext();
        var emailDeliveries = await verifyDb.NotificationDeliveries
            .Where(d => d.Channel == NotificationChannel.Email && d.Notification.RecipientPersonId == studentId)
            .ToListAsync();

        Assert.Equal(4, emailDeliveries.Count);
        Assert.All(emailDeliveries, d =>
        {
            Assert.Equal(NotificationDeliveryStatus.Sent, d.Status);
            Assert.Equal(1, d.AttemptCount);
        });
    }

    [Fact]
    public async Task StaleProcessingDelivery_IsRecoveredAndSentOnNextCycle()
    {
        var nowUtc = DateTime.UtcNow;
        var studentId = Guid.NewGuid();
        Guid emailDeliveryId;

        await using (var seedDb = _fixture.CreateDbContext())
        {
            await seedDb.NotificationDeliveries.ExecuteDeleteAsync();

            var student = Person.Create("Elena", "Pardo", $"elena.{studentId:N}@test.com", new DateTime(1992, 3, 10, 0, 0, 0, DateTimeKind.Utc));
            student.Id = studentId;
            seedDb.People.Add(student);
            await seedDb.SaveChangesAsync();

            var dispatcher = CreateDispatcher(seedDb);
            await dispatcher.DispatchAsync(new DispatchNotificationCommand(
                TargetPersonId: student.Id,
                Type: NotificationType.PaymentConfirmed,
                Variables: new Dictionary<string, string?>
                {
                    ["recipientName"] = student.FullName,
                    ["amount"] = "18000",
                    ["currency"] = "ARS"
                },
                IdempotencyKey: $"stale-pay:{studentId:N}"));

            var emailDelivery = await seedDb.NotificationDeliveries
                .SingleAsync(d => d.Channel == NotificationChannel.Email && d.Notification.RecipientPersonId == studentId);
            emailDeliveryId = emailDelivery.Id;

            // Simulate a worker crash leaving the row in Processing 10 minutes ago
            emailDelivery.MarkProcessing(nowUtc.AddMinutes(-10));
            await seedDb.SaveChangesAsync();
        }

        var provider = new RecordingEmailProvider(_ =>
            Task.FromResult(ChannelDeliveryResult.Success("RecoverySmtp", "recovered-msg-1")));

        await using (var workerDb = _fixture.CreateDbContext())
        {
            var calendar = CreateCalendar();
            var processor = new NotificationDeliveryProcessor(
                workerDb,
                [provider],
                new NotificationTemplateRenderer(workerDb),
                new NotificationService(workerDb, calendar),
                calendar,
                Options.Create(new NotificationsOptions { StaleProcessingTimeoutMinutes = 5 }));

            var recovered = await processor.RecoverStaleProcessingDeliveriesAsync();
            Assert.Equal(1, recovered);

            var processed = await processor.ProcessPendingBatchAsync();
            Assert.Equal(1, processed);
        }

        await using var verifyDb = _fixture.CreateDbContext();
        var finalDelivery = await verifyDb.NotificationDeliveries.SingleAsync(d => d.Id == emailDeliveryId);
        Assert.Equal(NotificationDeliveryStatus.Sent, finalDelivery.Status);
        Assert.Equal("recovered-msg-1", finalDelivery.ProviderMessageId);
        Assert.Equal(2, finalDelivery.AttemptCount);
    }

    [Fact]
    public async Task ConcurrentDispatches_WithSameRecipientAndIdempotencyKey_PersistSingleNotificationMessage()
    {
        var studentId = Guid.NewGuid();
        var idempotencyKey = $"concurrent-membership-activated:{studentId:N}";

        await using (var seedDb = _fixture.CreateDbContext())
        {
            var student = Person.Create("Franco", "Luna", $"franco.{studentId:N}@test.com", new DateTime(1994, 12, 1, 0, 0, 0, DateTimeKind.Utc));
            student.Id = studentId;
            seedDb.People.Add(student);
            await seedDb.SaveChangesAsync();
        }

        var command = new DispatchNotificationCommand(
            TargetPersonId: studentId,
            Type: NotificationType.MembershipActivated,
            Variables: new Dictionary<string, string?>
            {
                ["recipientName"] = "Franco Luna",
                ["planName"] = "Plan Mensual",
                ["startDate"] = "01/10/2026",
                ["endDate"] = "31/10/2026"
            },
            IdempotencyKey: idempotencyKey);

        Task<DispatchNotificationResult> DispatchInScopeAsync() => Task.Run(async () =>
        {
            await using var db = _fixture.CreateDbContext();
            var dispatcher = CreateDispatcher(db);
            return await dispatcher.DispatchAsync(command);
        });

        var results = await Task.WhenAll(DispatchInScopeAsync(), DispatchInScopeAsync());

        var totalCreated = results.Sum(r => r.CreatedMessages.Count);
        var totalDeduplicated = results.Sum(r => r.DeduplicatedCount);

        Assert.Equal(1, totalCreated);
        Assert.Equal(1, totalDeduplicated);

        await using var verifyDb = _fixture.CreateDbContext();
        var persistedMessages = await verifyDb.NotificationMessages
            .Where(m => m.RecipientPersonId == studentId && m.IdempotencyKey == idempotencyKey)
            .ToListAsync();
        Assert.Single(persistedMessages);
    }

    private sealed class RecordingEmailProvider : IEmailNotificationProvider
    {
        private readonly Func<ChannelDeliveryRequest, Task<ChannelDeliveryResult>> _handler;

        public RecordingEmailProvider(Func<ChannelDeliveryRequest, Task<ChannelDeliveryResult>> handler)
        {
            _handler = handler;
        }

        public NotificationChannel Channel => NotificationChannel.Email;
        public string ProviderName => "RecordingSmtp";
        public bool IsConfigured => true;

        public Task<ChannelDeliveryResult> SendAsync(ChannelDeliveryRequest request, CancellationToken cancellationToken = default) =>
            _handler(request);
    }
}
