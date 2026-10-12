using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Enums;
using SmartGym.IntegrationTests.Fixtures;
using Xunit;

namespace SmartGym.IntegrationTests.Notifications;

[Collection("PostgresIntegration")]
public class NotificationTemplateActivationIntegrationTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public NotificationTemplateActivationIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static CreateNotificationTemplateRequest BuildRequest(int version, bool activateImmediately) =>
        new(
            Code: "PAYMENT_REFUNDED_SWAP",
            Type: NotificationType.PaymentRefunded,
            Channel: NotificationChannel.InApp,
            SubjectTemplate: $"Reembolso v{version}",
            BodyTemplate: "Hola {{recipientName}}, registramos tu reembolso.",
            AllowedVariables: ["recipientName"],
            Locale: "es-AR",
            Version: version,
            ActivateImmediately: activateImmediately);

    [Fact]
    public async Task ActivatingAnotherVersion_SwapsActiveTemplate_WithoutViolatingUniqueActiveIndex()
    {
        await using (var cleanupDb = _fixture.CreateDbContext())
        {
            await cleanupDb.NotificationTemplates
                .Where(t => t.Type == NotificationType.PaymentRefunded)
                .ExecuteDeleteAsync();
        }

        var calendar = new GymCalendar(Options.Create(new GymOptions()));
        Guid v1Id;
        Guid v2Id;

        await using (var db = _fixture.CreateDbContext())
        {
            var service = new NotificationService(db, calendar);
            v1Id = (await service.CreateTemplateAsync(BuildRequest(1, activateImmediately: true))).Id;
            v2Id = (await service.CreateTemplateAsync(BuildRequest(2, activateImmediately: false))).Id;
        }

        // El orden de los UPDATE dentro de un mismo SaveChanges depende de las claves:
        // alternar en ambos sentidos cubre los dos órdenes posibles.
        foreach (var targetId in new[] { v2Id, v1Id, v2Id, v1Id })
        {
            await using var db = _fixture.CreateDbContext();
            var service = new NotificationService(db, calendar);

            var activated = await service.ActivateTemplateAsync(targetId);

            Assert.Equal(NotificationTemplateStatus.Active, activated.Status);
            var activeIds = await db.NotificationTemplates
                .AsNoTracking()
                .Where(t => t.Type == NotificationType.PaymentRefunded && t.Status == NotificationTemplateStatus.Active)
                .Select(t => t.Id)
                .ToListAsync();
            Assert.Equal([targetId], activeIds);
        }

        // Crear una versión nueva activándola de inmediato también reemplaza a la activa.
        await using (var db = _fixture.CreateDbContext())
        {
            var service = new NotificationService(db, calendar);
            var v3 = await service.CreateTemplateAsync(BuildRequest(3, activateImmediately: true));

            var activeIds = await db.NotificationTemplates
                .AsNoTracking()
                .Where(t => t.Type == NotificationType.PaymentRefunded && t.Status == NotificationTemplateStatus.Active)
                .Select(t => t.Id)
                .ToListAsync();
            Assert.Equal([v3.Id], activeIds);
        }
    }
}
