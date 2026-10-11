using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationModelConfigurationTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options;
        using var dbContext = new SmartGymDbContext(options);
        return dbContext.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void Reservation_PropertiesCheckConstraintsIndexesAndFks_ShouldBeConfigured()
    {
        var model = BuildModel();
        var reservationType = model.FindEntityType(typeof(Reservation))!;

        Assert.Equal("Reservations", reservationType.GetTableName());

        // RowVersion (xmin)
        var versionProp = reservationType.FindProperty(nameof(Reservation.Version))!;
        Assert.True(versionProp.IsConcurrencyToken);

        // Max lengths
        Assert.Equal(50, reservationType.FindProperty(nameof(Reservation.RequestedPromotionCode))!.GetMaxLength());
        Assert.Equal(500, reservationType.FindProperty(nameof(Reservation.CancellationReason))!.GetMaxLength());
        Assert.Equal(500, reservationType.FindProperty(nameof(Reservation.PolicyOverrideReason))!.GetMaxLength());
        Assert.Equal(1000, reservationType.FindProperty(nameof(Reservation.Notes))!.GetMaxLength());
        Assert.Equal(100, reservationType.FindProperty(nameof(Reservation.IdempotencyKey))!.GetMaxLength());

        // Seven CHECK constraints
        var checks = reservationType.GetCheckConstraints().ToList();
        Assert.Contains(checks, c => c.ModelName == "CK_Reservations_Status" && c.Sql == "\"Status\" IN (1, 3, 4, 5, 6, 7)");
        Assert.Contains(checks, c => c.ModelName == "CK_Reservations_Origin" && c.Sql == "\"Origin\" IN (1, 2, 3)");
        Assert.Contains(checks, c => c.ModelName == "CK_Reservations_AccessType" && c.Sql == "\"AccessType\" IN (1, 2, 3, 4, 5)");
        Assert.Contains(checks, c => c.ModelName == "CK_Reservations_WaitlistPosition" && c.Sql == "((\"Status\" = 6) = (\"WaitListPosition\" IS NOT NULL AND \"WaitListPosition\" >= 1))");
        Assert.Contains(checks, c => c.ModelName == "CK_Reservations_PendingOffer" && c.Sql == "((\"Status\" = 7) = (\"WaitlistOfferExpiresAtUtc\" IS NOT NULL))");
        Assert.Contains(checks, c => c.ModelName == "CK_Reservations_Cancellation" && c.Sql == "((\"Status\" = 3) = (\"CancelledAtUtc\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL))");
        Assert.Contains(checks, c => c.ModelName == "CK_Reservations_PolicyOverride" && c.Sql == "(NOT \"IsPolicyOverride\" OR \"PolicyOverrideReason\" IS NOT NULL)");

        // Four partial unique indexes
        var activeSessionStudentIdx = Assert.Single(
            reservationType.GetIndexes(),
            i => i.GetDatabaseName() == "UX_Reservations_Active_Session_Student");
        Assert.True(activeSessionStudentIdx.IsUnique);
        Assert.Equal("\"Status\" IN (1, 6, 7) AND \"IsActive\"", activeSessionStudentIdx.GetFilter());

        var idempotencyIdx = Assert.Single(
            reservationType.GetIndexes(),
            i => i.GetDatabaseName() == "UX_Reservations_Student_IdempotencyKey");
        Assert.True(idempotencyIdx.IsUnique);
        Assert.Equal("\"IdempotencyKey\" IS NOT NULL", idempotencyIdx.GetFilter());

        var heldAccessMovementIdx = Assert.Single(
            reservationType.GetIndexes(),
            i => i.GetDatabaseName() == "UX_Reservations_Held_AccessMovement");
        Assert.True(heldAccessMovementIdx.IsUnique);
        Assert.Equal("\"SingleClassTicketMovementId\" IS NOT NULL AND \"IsActive\" AND (\"Status\" IN (1, 4, 5, 7) OR (\"Status\" = 3 AND \"IsLateCancellation\"))", heldAccessMovementIdx.GetFilter());

        var promoRedemptionIdx = Assert.Single(
            reservationType.GetIndexes(),
            i => i.GetDatabaseName() == "UX_Reservations_PromotionRedemption");
        Assert.True(promoRedemptionIdx.IsUnique);
        Assert.Equal("\"PromotionRedemptionId\" IS NOT NULL", promoRedemptionIdx.GetFilter());

        // Operational indexes
        var studentStatusReservedAtIdx = Assert.Single(
            reservationType.GetIndexes(),
            i => i.GetDatabaseName() == "IX_Reservations_Student_Status_ReservedAt");
        Assert.Equal(new[] { false, false, true }, studentStatusReservedAtIdx.IsDescending);

        Assert.Contains(reservationType.GetIndexes(), i => i.GetDatabaseName() == "IX_Reservations_Session_Status");

        var sessionWaitlistIdx = Assert.Single(
            reservationType.GetIndexes(),
            i => i.GetDatabaseName() == "IX_Reservations_Session_Waitlist");
        Assert.Equal("\"Status\" = 6 AND \"IsActive\"", sessionWaitlistIdx.GetFilter());

        var pendingOffersIdx = Assert.Single(
            reservationType.GetIndexes(),
            i => i.GetDatabaseName() == "IX_Reservations_PendingOffers");
        Assert.Equal("\"Status\" = 7 AND \"IsActive\"", pendingOffersIdx.GetFilter());

        // Foreign keys on Reservation must all be Restrict
        var fks = reservationType.GetForeignKeys().ToList();
        Assert.NotEmpty(fks);
        foreach (var fk in fks)
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }

        // Verify navigation/FK presence for new FK properties
        Assert.Contains(fks, fk => fk.Properties.Any(p => p.Name == nameof(Reservation.StudentId)));
        Assert.Contains(fks, fk => fk.Properties.Any(p => p.Name == nameof(Reservation.ClassSessionId)));
        Assert.Contains(fks, fk => fk.Properties.Any(p => p.Name == nameof(Reservation.MembershipId)));
        Assert.Contains(fks, fk => fk.Properties.Any(p => p.Name == nameof(Reservation.CreditMovementId)));
        Assert.Contains(fks, fk => fk.Properties.Any(p => p.Name == nameof(Reservation.RefundCreditMovementId)));
        Assert.Contains(fks, fk => fk.Properties.Any(p => p.Name == nameof(Reservation.SingleClassTicketMovementId)));
        Assert.Contains(fks, fk => fk.Properties.Any(p => p.Name == nameof(Reservation.PaymentId)));
        Assert.Contains(fks, fk => fk.Properties.Any(p => p.Name == nameof(Reservation.PromotionRedemptionId)));
        Assert.Contains(fks, fk => fk.Properties.Any(p => p.Name == nameof(Reservation.CreatedByUserId)));
        Assert.Contains(fks, fk => fk.Properties.Any(p => p.Name == nameof(Reservation.CancelledByUserId)));
        Assert.Contains(fks, fk => fk.Properties.Any(p => p.Name == nameof(Reservation.RescheduledToSessionId)));
    }

    [Fact]
    public void ReservationStatusChange_CreditMovement_AndPromotionRedemption_ShouldBeConfigured()
    {
        var model = BuildModel();

        // ReservationStatusChange
        var statusChangeType = model.FindEntityType(typeof(ReservationStatusChange));
        Assert.NotNull(statusChangeType);
        Assert.Equal("ReservationStatusChanges", statusChangeType!.GetTableName());
        Assert.Equal(500, statusChangeType.FindProperty(nameof(ReservationStatusChange.Reason))!.GetMaxLength());
        foreach (var fk in statusChangeType.GetForeignKeys())
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }

        // MembershipCreditMovement extensions
        var creditMovementType = model.FindEntityType(typeof(MembershipCreditMovement))!;
        Assert.NotNull(creditMovementType.FindProperty("ReservationId"));
        Assert.NotNull(creditMovementType.FindProperty("ExpiresOn"));
        Assert.NotNull(creditMovementType.FindProperty("ActivityId"));

        var reservationFkOnMovement = Assert.Single(
            creditMovementType.GetForeignKeys(),
            fk => fk.Properties.Any(p => p.Name == "ReservationId"));
        Assert.Equal(DeleteBehavior.Restrict, reservationFkOnMovement.DeleteBehavior);

        var uxCreditMovementReservationType = Assert.Single(
            creditMovementType.GetIndexes(),
            i => i.GetDatabaseName() == "UX_CreditMovements_Reservation_Type");
        Assert.True(uxCreditMovementReservationType.IsUnique);
        Assert.Equal("\"ReservationId\" IS NOT NULL AND \"Type\" IN (2, 3)", uxCreditMovementReservationType.GetFilter());

        // PromotionRedemption extensions
        var promoRedemptionType = model.FindEntityType(typeof(PromotionRedemption))!;
        Assert.NotNull(promoRedemptionType.FindProperty("ReservationId"));
        var reservationFkOnPromo = Assert.Single(
            promoRedemptionType.GetForeignKeys(),
            fk => fk.Properties.Any(p => p.Name == "ReservationId"));
        Assert.Equal(DeleteBehavior.Restrict, reservationFkOnPromo.DeleteBehavior);

        var uxPromoReservation = Assert.Single(
            promoRedemptionType.GetIndexes(),
            i => i.GetDatabaseName() == "UX_PromotionRedemptions_Reservation");
        Assert.True(uxPromoReservation.IsUnique);
        Assert.Equal("\"ReservationId\" IS NOT NULL", uxPromoReservation.GetFilter());
    }
}
