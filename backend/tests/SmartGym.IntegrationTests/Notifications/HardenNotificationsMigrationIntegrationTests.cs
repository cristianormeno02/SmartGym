using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SmartGym.Domain.Enums;
using SmartGym.IntegrationTests.Fixtures;
using Xunit;

namespace SmartGym.IntegrationTests.Notifications;

[Collection("PostgresIntegration")]
public class HardenNotificationsMigrationIntegrationTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public HardenNotificationsMigrationIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HardenNotificationsModule_MigratesExistingRows_SeedsTemplates_AndEnforcesConstraints()
    {
        var connString = await _fixture.CreateIsolatedDatabaseAsync(applyAllMigrations: false);

        // 1. Migrate up to 20261012032939_HardenPromotionsModule (just before HardenNotificationsModule)
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20261012032939_HardenPromotionsModule");
        }

        var studentId = Guid.NewGuid();
        var notifClassSuspendedReadId = Guid.NewGuid();
        var notifReservationConfirmedUnreadId = Guid.NewGuid();
        var notifMembershipActivatedReadNoUpdatedId = Guid.NewGuid();
        var notifPromotionalUnreadId = Guid.NewGuid();
        var notifPaymentConfirmedUnreadId = Guid.NewGuid();

        var sentAt = new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 10, 11, 12, 30, 0, DateTimeKind.Utc);

        // 2. Seed pre-existing rows using the schema before HardenNotificationsModule
        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
INSERT INTO ""People"" (""Id"", ""FirstName"", ""LastName"", ""SearchName"", ""Email"", ""Status"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{studentId}', 'Clara', 'Suarez', 'clara suarez', 'clara.notif@test.com', 'Active', '{sentAt:O}', TRUE);

INSERT INTO ""NotificationMessages"" (""Id"", ""RecipientPersonId"", ""Type"", ""Channel"", ""Title"", ""Body"", ""IsRead"", ""SentAtUtc"", ""CreatedAtUtc"", ""UpdatedAtUtc"", ""IsActive"")
VALUES
    ('{notifClassSuspendedReadId}', '{studentId}', 1, 1, 'Clase suspendida', 'Se suspendió la clase', TRUE, '{sentAt:O}', '{sentAt:O}', '{updatedAt:O}', TRUE),
    ('{notifReservationConfirmedUnreadId}', '{studentId}', 9, 1, 'Reserva confirmada', 'Tu reserva fue confirmada', FALSE, '{sentAt:O}', '{sentAt:O}', NULL, TRUE),
    ('{notifMembershipActivatedReadNoUpdatedId}', '{studentId}', 6, 1, 'Membresía activada', 'Tu membresía está activa', TRUE, '{sentAt:O}', '{sentAt:O}', NULL, TRUE),
    ('{notifPromotionalUnreadId}', '{studentId}', 4, 1, 'Promo primavera', 'Descuento 20%', FALSE, '{sentAt:O}', '{sentAt:O}', NULL, TRUE),
    ('{notifPaymentConfirmedUnreadId}', '{studentId}', 5, 1, 'Pago confirmado', 'Pago recibido', FALSE, '{sentAt:O}', '{sentAt:O}', NULL, TRUE);
";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Apply HardenNotificationsModule migration
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20261012130000_HardenNotificationsModule");
        }

        // 4. Verify row preservation, Category backfill, ReadAtUtc backfill, and seeded templates
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var messages = await dbContext.NotificationMessages
                .Where(n => n.RecipientPersonId == studentId)
                .ToDictionaryAsync(n => n.Id);

            Assert.Equal(5, messages.Count);

            var classSuspended = messages[notifClassSuspendedReadId];
            Assert.Equal(NotificationCategory.ClassChanges, classSuspended.Category);
            Assert.True(classSuspended.IsRead);
            Assert.Equal(updatedAt, classSuspended.ReadAtUtc);

            var reservationConfirmed = messages[notifReservationConfirmedUnreadId];
            Assert.Equal(NotificationCategory.Reservations, reservationConfirmed.Category);
            Assert.False(reservationConfirmed.IsRead);
            Assert.Null(reservationConfirmed.ReadAtUtc);

            var membershipActivated = messages[notifMembershipActivatedReadNoUpdatedId];
            Assert.Equal(NotificationCategory.MembershipsAndCredits, membershipActivated.Category);
            Assert.True(membershipActivated.IsRead);
            Assert.Equal(sentAt, membershipActivated.ReadAtUtc);

            var promo = messages[notifPromotionalUnreadId];
            Assert.Equal(NotificationCategory.Promotions, promo.Category);
            Assert.Null(promo.ReadAtUtc);

            var payment = messages[notifPaymentConfirmedUnreadId];
            Assert.Equal(NotificationCategory.Payments, payment.Category);
            Assert.Null(payment.ReadAtUtc);

            // Seed templates (24 types x 2 channels = 48 active templates in es-AR v1)
            var templates = await dbContext.NotificationTemplates
                .Where(t => t.Locale == "es-AR" && t.Version == 1 && t.Status == NotificationTemplateStatus.Active && t.IsActive)
                .ToListAsync();
            Assert.Equal(48, templates.Count);
        }

        // 5. Verify database constraints and unique indexes
        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();

            // UX_NotificationMessages_Recipient_IdempotencyKey rejects duplicate (RecipientPersonId, IdempotencyKey)
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            await using (var cmd1 = conn.CreateCommand())
            {
                cmd1.CommandText = $@"
INSERT INTO ""NotificationMessages"" (""Id"", ""RecipientPersonId"", ""Type"", ""Category"", ""Channel"", ""Title"", ""Body"", ""IsRead"", ""SentAtUtc"", ""IdempotencyKey"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{id1}', '{studentId}', 9, 1, 1, 'T1', 'B1', FALSE, '{sentAt:O}', 'res-confirmed:dup-1', '{sentAt:O}', TRUE);";
                await cmd1.ExecuteNonQueryAsync();
            }

            await using (var cmd2 = conn.CreateCommand())
            {
                cmd2.CommandText = $@"
INSERT INTO ""NotificationMessages"" (""Id"", ""RecipientPersonId"", ""Type"", ""Category"", ""Channel"", ""Title"", ""Body"", ""IsRead"", ""SentAtUtc"", ""IdempotencyKey"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{id2}', '{studentId}', 9, 1, 1, 'T2', 'B2', FALSE, '{sentAt:O}', 'res-confirmed:dup-1', '{sentAt:O}', TRUE);";
                var dupMsgEx = await Assert.ThrowsAsync<PostgresException>(() => cmd2.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.UniqueViolation, dupMsgEx.SqlState);
                Assert.Equal("UX_NotificationMessages_Recipient_IdempotencyKey", dupMsgEx.ConstraintName);
            }

            // UX_NotificationPreferences_Person_Category_Channel rejects duplicate (PersonId, Category, Channel)
            var pref1 = Guid.NewGuid();
            var pref2 = Guid.NewGuid();
            await using (var prefCmd1 = conn.CreateCommand())
            {
                prefCmd1.CommandText = $@"
INSERT INTO ""NotificationPreferences"" (""Id"", ""PersonId"", ""Category"", ""Channel"", ""Enabled"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{pref1}', '{studentId}', 1, 2, TRUE, '{sentAt:O}', TRUE);";
                await prefCmd1.ExecuteNonQueryAsync();
            }

            await using (var prefCmd2 = conn.CreateCommand())
            {
                prefCmd2.CommandText = $@"
INSERT INTO ""NotificationPreferences"" (""Id"", ""PersonId"", ""Category"", ""Channel"", ""Enabled"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{pref2}', '{studentId}', 1, 2, FALSE, '{sentAt:O}', TRUE);";
                var dupPrefEx = await Assert.ThrowsAsync<PostgresException>(() => prefCmd2.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.UniqueViolation, dupPrefEx.SqlState);
                Assert.Equal("UX_NotificationPreferences_Person_Category_Channel", dupPrefEx.ConstraintName);
            }
        }
    }
}
