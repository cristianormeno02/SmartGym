using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SmartGym.IntegrationTests.Fixtures;
using Xunit;

namespace SmartGym.IntegrationTests.Reservations;

[Collection("PostgresIntegration")]
public class RefactorReservationsMigrationIntegrationTests
{
    private readonly PostgresDatabaseFixture _fixture;

    public RefactorReservationsMigrationIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RefactorReservationsModule_MigratesExistingRows_Correctly()
    {
        var connString = await _fixture.CreateIsolatedDatabaseAsync(applyAllMigrations: false);

        // 1. Migrate up to 20261011151325_RefactorMembershipsModule (just before RefactorReservationsModule)
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20261011151325_RefactorMembershipsModule");
        }

        var student1Id = Guid.NewGuid();
        var student2Id = Guid.NewGuid();
        var instructorId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var session1Id = Guid.NewGuid();
        var session2Id = Guid.NewGuid();
        var session3Id = Guid.NewGuid();
        var planCreditsId = Guid.NewGuid();
        var planUnlimitedId = Guid.NewGuid();
        var membershipCreditsId = Guid.NewGuid();
        var membershipUnlimitedId = Guid.NewGuid();

        var debitMovementId = Guid.NewGuid();
        var refundMovementId = Guid.NewGuid();
        var sharedTicketMovementId = Guid.NewGuid();

        var resConfirmedWithMembershipId = Guid.NewGuid();
        var resLegacyStatus2UnlimitedId = Guid.NewGuid();
        var resCancelledWithoutReasonId = Guid.NewGuid();
        var resWaitlistedId = Guid.NewGuid();
        var resTicketOlderId = Guid.NewGuid();
        var resTicketNewerSharedId = Guid.NewGuid();

        var now = new DateTime(2026, 10, 11, 15, 0, 0, DateTimeKind.Utc);

        // 2. Seed rows using the schema before RefactorReservationsModule
        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
INSERT INTO ""People"" (""Id"", ""FirstName"", ""LastName"", ""SearchName"", ""Email"", ""Status"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    ('{student1Id}', 'Ana', 'Perez', 'ana perez', 'ana.mig@test.com', 'Active', '{now:O}', TRUE),
    ('{student2Id}', 'Luis', 'Gomez', 'luis gomez', 'luis.mig@test.com', 'Active', '{now:O}', TRUE),
    ('{instructorId}', 'Carlos', 'Inst', 'carlos inst', 'carlos.mig@test.com', 'Active', '{now:O}', TRUE);

INSERT INTO ""Rooms"" (""Id"", ""Name"", ""Capacity"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{roomId}', 'Sala Test Mig', 20, '{now:O}', TRUE);

INSERT INTO ""Activities"" (""Id"", ""Code"", ""Name"", ""NormalizedName"", ""ShortDescription"", ""Description"", ""DefaultCapacity"", ""Status"", ""CreatedAtUtc"", ""IsActive"")
VALUES ('{activityId}', 'ACT_MIG', 'Spinning Mig', 'spinning mig', 'Desc', 'Desc', 15, 1, '{now:O}', TRUE);

INSERT INTO ""ClassSessions"" (""Id"", ""ActivityId"", ""RoomId"", ""ScheduledInstructorId"", ""EffectiveInstructorId"", ""StartsAtUtc"", ""EndsAtUtc"", ""Capacity"", ""ReservedCount"", ""AttendedCount"", ""Status"", ""IsDetachedFromSchedule"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    ('{session1Id}', '{activityId}', '{roomId}', '{instructorId}', '{instructorId}', '{now.AddDays(1):O}', '{now.AddDays(1).AddHours(1):O}', 15, 3, 0, 1, TRUE, '{now:O}', TRUE),
    ('{session2Id}', '{activityId}', '{roomId}', '{instructorId}', '{instructorId}', '{now.AddDays(2):O}', '{now.AddDays(2).AddHours(1):O}', 15, 2, 0, 1, TRUE, '{now:O}', TRUE),
    ('{session3Id}', '{activityId}', '{roomId}', '{instructorId}', '{instructorId}', '{now.AddDays(3):O}', '{now.AddDays(3).AddHours(1):O}', 15, 1, 0, 1, TRUE, '{now:O}', TRUE);

INSERT INTO ""MembershipPlans"" (""Id"", ""Name"", ""Description"", ""Price"", ""DurationDays"", ""Credits"", ""IsUnlimited"", ""AppliesToAllActivities"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    ('{planCreditsId}', 'Plan 8', 'Desc', 10000, 30, 8, FALSE, TRUE, '{now:O}', TRUE),
    ('{planUnlimitedId}', 'Plan Libre', 'Desc', 20000, 30, 0, TRUE, TRUE, '{now:O}', TRUE);

INSERT INTO ""Memberships"" (""Id"", ""StudentId"", ""MembershipPlanId"", ""StartDate"", ""EndDate"", ""PurchasedAtUtc"", ""ActivatedAtUtc"", ""DurationDaysSnapshot"", ""InitialCredits"", ""ExtraCreditsGranted"", ""TotalCredits"", ""AvailableCredits"", ""IsUnlimited"", ""AppliesToAllActivities"", ""InitialCreditsLoaded"", ""OriginalPrice"", ""DiscountAmount"", ""PurchasePrice"", ""Currency"", ""Status"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    ('{membershipCreditsId}', '{student1Id}', '{planCreditsId}', '2026-10-01', '2026-10-31', '{now:O}', '{now:O}', 31, 8, 0, 8, 7, FALSE, TRUE, TRUE, 10000, 0, 10000, 'ARS', 1, '{now:O}', TRUE),
    ('{membershipUnlimitedId}', '{student2Id}', '{planUnlimitedId}', '2026-10-01', '2026-10-31', '{now:O}', '{now:O}', 31, 0, 0, 0, 0, TRUE, TRUE, TRUE, 20000, 0, 20000, 'ARS', 1, '{now:O}', TRUE);

INSERT INTO ""MembershipCreditMovements"" (""Id"", ""MembershipId"", ""StudentId"", ""ClassSessionId"", ""Type"", ""Amount"", ""BalanceAfter"", ""Reason"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    ('{debitMovementId}', '{membershipCreditsId}', '{student1Id}', '{session1Id}', 2, -1, 7, 'Débito clase', '{now:O}', TRUE),
    ('{refundMovementId}', '{membershipCreditsId}', '{student1Id}', '{session2Id}', 3, 1, 8, 'Devolución clase', '{now:O}', TRUE),
    ('{sharedTicketMovementId}', NULL, '{student1Id}', '{session2Id}', 5, 1, 1, 'Pase individual', '{now:O}', TRUE);

INSERT INTO ""Reservations"" (""Id"", ""ClassSessionId"", ""StudentId"", ""MembershipId"", ""CreditMovementId"", ""RefundCreditMovementId"", ""Status"", ""ReservedAtUtc"", ""ConfirmedAtUtc"", ""CancelledAtUtc"", ""CancellationReason"", ""IsLateCancellation"", ""WaitListPosition"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    -- 1. Status 1 (Reserved -> Confirmed) with membership & debit movement
    ('{resConfirmedWithMembershipId}', '{session1Id}', '{student1Id}', '{membershipCreditsId}', '{debitMovementId}', NULL, 1, '{now.AddHours(-5):O}', NULL, NULL, NULL, FALSE, NULL, '{now.AddHours(-5):O}', TRUE),
    -- 2. Status 2 (old Confirmed -> 1 Confirmed) with unlimited membership
    ('{resLegacyStatus2UnlimitedId}', '{session1Id}', '{student2Id}', '{membershipUnlimitedId}', NULL, NULL, 2, '{now.AddHours(-4):O}', NULL, NULL, NULL, FALSE, NULL, '{now.AddHours(-4):O}', TRUE),
    -- 3. Status 3 (Cancelled without reason or CancelledAtUtc) with refund movement
    ('{resCancelledWithoutReasonId}', '{session2Id}', '{student1Id}', '{membershipCreditsId}', '{debitMovementId}', '{refundMovementId}', 3, '{now.AddHours(-3):O}', NULL, NULL, NULL, FALSE, NULL, '{now.AddHours(-3):O}', TRUE),
    -- 4. Status 6 (Waitlisted)
    ('{resWaitlistedId}', '{session3Id}', '{student2Id}', '{membershipUnlimitedId}', NULL, NULL, 6, '{now.AddHours(-2):O}', NULL, NULL, NULL, FALSE, 1, '{now.AddHours(-2):O}', TRUE),
    -- 5. Shared single class ticket (older reservation)
    ('{resTicketOlderId}', '{session2Id}', '{student1Id}', NULL, '{sharedTicketMovementId}', NULL, 1, '{now.AddHours(-6):O}', NULL, NULL, NULL, FALSE, NULL, '{now.AddHours(-6):O}', TRUE),
    -- 6. Shared single class ticket (newer reservation on another session)
    ('{resTicketNewerSharedId}', '{session3Id}', '{student1Id}', NULL, '{sharedTicketMovementId}', NULL, 1, '{now.AddHours(-1):O}', NULL, NULL, NULL, FALSE, NULL, '{now.AddHours(-1):O}', TRUE);
";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Apply RefactorReservationsModule migration
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            await dbContext.Database.MigrateAsync();
        }

        // 4. Verify each data migration step
        await using (var dbContext = _fixture.CreateDbContext(connectionString: connString))
        {
            var r1 = await dbContext.Reservations.Include(r => r.StatusChanges).SingleAsync(r => r.Id == resConfirmedWithMembershipId);
            Assert.Equal(SmartGym.Domain.Enums.ReservationStatus.Confirmed, r1.Status);
            Assert.NotNull(r1.ConfirmedAtUtc);
            Assert.Equal(SmartGym.Domain.Enums.ReservationOrigin.Legacy, r1.Origin);
            Assert.Equal(SmartGym.Domain.Enums.ReservationAccessType.MembershipCredit, r1.AccessType);
            Assert.Single(r1.StatusChanges);
            Assert.Equal("Estado migrado", r1.StatusChanges.First().Reason);
            Assert.Equal(SmartGym.Domain.Enums.ReservationChangeTrigger.DataMigration, r1.StatusChanges.First().Trigger);

            var r2 = await dbContext.Reservations.SingleAsync(r => r.Id == resLegacyStatus2UnlimitedId);
            Assert.Equal(SmartGym.Domain.Enums.ReservationStatus.Confirmed, r2.Status);
            Assert.Equal(SmartGym.Domain.Enums.ReservationAccessType.UnlimitedMembership, r2.AccessType);

            var r3 = await dbContext.Reservations.SingleAsync(r => r.Id == resCancelledWithoutReasonId);
            Assert.Equal(SmartGym.Domain.Enums.ReservationStatus.Cancelled, r3.Status);
            Assert.Equal("Cancelación previa a migración", r3.CancellationReason);
            Assert.NotNull(r3.CancelledAtUtc);

            var r4 = await dbContext.Reservations.SingleAsync(r => r.Id == resWaitlistedId);
            Assert.Equal(SmartGym.Domain.Enums.ReservationStatus.Waitlisted, r4.Status);
            Assert.NotNull(r4.WaitlistEnteredAtUtc);
            Assert.Equal(1, r4.WaitListPosition);

            var rOlderTicket = await dbContext.Reservations.SingleAsync(r => r.Id == resTicketOlderId);
            Assert.Equal(SmartGym.Domain.Enums.ReservationAccessType.SingleClassTicket, rOlderTicket.AccessType);
            Assert.Null(rOlderTicket.CreditMovementId);
            Assert.Equal(sharedTicketMovementId, rOlderTicket.SingleClassTicketMovementId);

            var rNewerTicket = await dbContext.Reservations.SingleAsync(r => r.Id == resTicketNewerSharedId);
            Assert.Equal(SmartGym.Domain.Enums.ReservationAccessType.SingleClassTicket, rNewerTicket.AccessType);
            Assert.Null(rNewerTicket.CreditMovementId);
            Assert.Null(rNewerTicket.SingleClassTicketMovementId);
            Assert.Contains("Pase compartido con otra reserva antes de la migración", rNewerTicket.Notes);

            var debitMov = await dbContext.MembershipCreditMovements.SingleAsync(m => m.Id == debitMovementId);
            Assert.Equal(resConfirmedWithMembershipId, debitMov.ReservationId);

            var refundMov = await dbContext.MembershipCreditMovements.SingleAsync(m => m.Id == refundMovementId);
            Assert.Equal(resCancelledWithoutReasonId, refundMov.ReservationId);
        }
    }
}
