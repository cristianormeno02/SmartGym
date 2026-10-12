using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.IntegrationTests.Fixtures;
using Xunit;

namespace SmartGym.IntegrationTests.Notifications;

[Collection("PostgresIntegration")]
public class FixSeededNotificationTemplateChannelsMigrationIntegrationTests
{
    private const string PreviousMigration = "20261012183956_DropNotificationDeliveryMaxAttemptsDefault";

    private readonly PostgresDatabaseFixture _fixture;

    public FixSeededNotificationTemplateChannelsMigrationIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SeededTemplates_ShouldStoreHtmlUnderEmailAndPlainTextUnderInApp_AndDownRestoresPreviousState()
    {
        var connString = await _fixture.CreateIsolatedDatabaseAsync(applyAllMigrations: true);

        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var templates = await LoadSeededTemplatesAsync(dbContext);
            Assert.Equal(48, templates.Count);
            Assert.All(templates, t => Assert.Equal(NotificationTemplateStatus.Active, t.Status));

            var email = templates.Where(t => t.Channel == NotificationChannel.Email).ToList();
            var inApp = templates.Where(t => t.Channel == NotificationChannel.InApp).ToList();
            Assert.Equal(24, email.Count);
            Assert.Equal(24, inApp.Count);

            Assert.All(email, t =>
            {
                Assert.StartsWith("<p>", t.BodyTemplate);
                Assert.StartsWith("SmartGym - ", t.SubjectTemplate);
            });
            Assert.All(inApp, t =>
            {
                Assert.DoesNotContain("<", t.BodyTemplate);
                Assert.False(t.SubjectTemplate!.StartsWith("SmartGym - ", StringComparison.Ordinal));
            });

            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var templates = await LoadSeededTemplatesAsync(dbContext);
            Assert.Equal(48, templates.Count);
            Assert.All(
                templates.Where(t => t.Channel == NotificationChannel.Email),
                t => Assert.DoesNotContain("<", t.BodyTemplate));
            Assert.All(
                templates.Where(t => t.Channel == NotificationChannel.InApp),
                t => Assert.StartsWith("<p>", t.BodyTemplate));
        }
    }

    [Fact]
    public async Task SeededTemplate_ShouldBeDeactivated_WhenAnotherActiveTemplateAlreadyOwnsTheTargetChannel()
    {
        var connString = await _fixture.CreateIsolatedDatabaseAsync(applyAllMigrations: false);

        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        // An administrator replaced the (mis-seeded) Email template of ClassSuspended (Type = 1) with an
        // active version 2: the seeded row stored under Email (plain text, block 0001) became Inactive.
        var adminTemplateId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
UPDATE ""NotificationTemplates"" SET ""Status"" = 3 WHERE ""Id"" = '00000000-0000-0000-0001-000000000001';
INSERT INTO ""NotificationTemplates"" (""Id"", ""Code"", ""Type"", ""Channel"", ""Locale"", ""Version"", ""Status"", ""SubjectTemplate"", ""BodyTemplate"", ""AllowedVariables"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{adminTemplateId}', 'CLASS_SUSPENDED', 1, 1, 'es-AR', 2, 2, 'SmartGym - Aviso', '<p>Versión del administrador</p>', 'recipientName', NOW(), TRUE);";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            await dbContext.Database.MigrateAsync();

            var classSuspended = await dbContext.NotificationTemplates
                .AsNoTracking()
                .Where(t => t.Type == NotificationType.ClassSuspended)
                .ToListAsync();

            var activeEmail = Assert.Single(
                classSuspended,
                t => t.Channel == NotificationChannel.Email && t.Status == NotificationTemplateStatus.Active);
            Assert.Equal(adminTemplateId, activeEmail.Id);

            var seededHtml = Assert.Single(classSuspended, t => t.Id == Guid.Parse("00000000-0000-0000-0002-000000000001"));
            Assert.Equal(NotificationChannel.Email, seededHtml.Channel);
            Assert.Equal(NotificationTemplateStatus.Inactive, seededHtml.Status);

            var seededPlain = Assert.Single(classSuspended, t => t.Id == Guid.Parse("00000000-0000-0000-0001-000000000001"));
            Assert.Equal(NotificationChannel.InApp, seededPlain.Channel);
            Assert.Equal(NotificationTemplateStatus.Inactive, seededPlain.Status);
        }
    }

    private static Task<List<NotificationTemplate>> LoadSeededTemplatesAsync(SmartGym.Infrastructure.Persistence.SmartGymDbContext dbContext) =>
        dbContext.NotificationTemplates
            .AsNoTracking()
            .Where(t => t.Locale == "es-AR" && t.Version == 1)
            .ToListAsync();
}
