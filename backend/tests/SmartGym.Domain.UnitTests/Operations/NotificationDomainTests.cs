using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class NotificationDomainTests
{
    [Fact]
    public void NotificationType_ShouldMapToExpectedCategory_AndDistinguishTransactionalFromPromotional()
    {
        Assert.Equal(NotificationCategory.ClassChanges, NotificationType.ClassSuspended.GetCategory());
        Assert.Equal(NotificationCategory.ClassChanges, NotificationType.ClassCancelled.GetCategory());
        Assert.Equal(NotificationCategory.ClassChanges, NotificationType.ClassRescheduled.GetCategory());
        Assert.Equal(NotificationCategory.ClassChanges, NotificationType.ClassRoomChanged.GetCategory());
        Assert.Equal(NotificationCategory.ClassChanges, NotificationType.ClassInstructorChanged.GetCategory());

        Assert.Equal(NotificationCategory.Reservations, NotificationType.ClassReminder.GetCategory());
        Assert.Equal(NotificationCategory.Reservations, NotificationType.ReservationConfirmed.GetCategory());
        Assert.Equal(NotificationCategory.Reservations, NotificationType.ReservationCancelled.GetCategory());
        Assert.Equal(NotificationCategory.Reservations, NotificationType.ReservationWaitlisted.GetCategory());
        Assert.Equal(NotificationCategory.Reservations, NotificationType.ReservationPromotedFromWaitlist.GetCategory());
        Assert.Equal(NotificationCategory.Reservations, NotificationType.WaitlistSpotOffered.GetCategory());
        Assert.Equal(NotificationCategory.Reservations, NotificationType.WaitlistOfferExpired.GetCategory());

        Assert.Equal(NotificationCategory.MembershipsAndCredits, NotificationType.MembershipActivated.GetCategory());
        Assert.Equal(NotificationCategory.MembershipsAndCredits, NotificationType.MembershipSuspended.GetCategory());
        Assert.Equal(NotificationCategory.MembershipsAndCredits, NotificationType.MembershipReactivated.GetCategory());
        Assert.Equal(NotificationCategory.MembershipsAndCredits, NotificationType.MembershipExpiring.GetCategory());
        Assert.Equal(NotificationCategory.MembershipsAndCredits, NotificationType.MembershipExpired.GetCategory());
        Assert.Equal(NotificationCategory.MembershipsAndCredits, NotificationType.CreditsCompensated.GetCategory());

        Assert.Equal(NotificationCategory.Payments, NotificationType.PaymentConfirmed.GetCategory());
        Assert.Equal(NotificationCategory.Payments, NotificationType.PaymentFailed.GetCategory());
        Assert.Equal(NotificationCategory.Payments, NotificationType.PaymentRefunded.GetCategory());

        Assert.Equal(NotificationCategory.Promotions, NotificationType.PromotionAvailable.GetCategory());
        Assert.Equal(NotificationCategory.Administrative, NotificationType.AdministrativeNotice.GetCategory());
        Assert.Equal(NotificationCategory.Security, NotificationType.SecurityAlert.GetCategory());

        Assert.True(NotificationCategory.Reservations.IsTransactional());
        Assert.True(NotificationCategory.ClassChanges.IsTransactional());
        Assert.True(NotificationCategory.MembershipsAndCredits.IsTransactional());
        Assert.True(NotificationCategory.Payments.IsTransactional());
        Assert.True(NotificationCategory.Administrative.IsTransactional());
        Assert.True(NotificationCategory.Security.IsTransactional());
        Assert.False(NotificationCategory.Promotions.IsTransactional());
        Assert.True(NotificationCategory.Promotions.IsPromotional());
    }

    [Fact]
    public void NotificationMessage_MarkAsReadAndUnread_ShouldUpdateReadStateAndTimestamp()
    {
        var createdUtc = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        var readUtc = createdUtc.AddMinutes(10);
        var unreadUtc = createdUtc.AddMinutes(20);

        var msg = NotificationMessage.Create(
            recipientPersonId: Guid.NewGuid(),
            type: NotificationType.ReservationConfirmed,
            title: "Reserva confirmada",
            body: "Tu reserva fue confirmada.",
            nowUtc: createdUtc,
            actionUrl: "/portal/alumno");

        Assert.False(msg.IsRead);
        Assert.Null(msg.ReadAtUtc);
        Assert.Equal(NotificationCategory.Reservations, msg.Category);
        Assert.Equal("/portal/alumno", msg.ActionUrl);

        msg.MarkAsRead(readUtc);
        Assert.True(msg.IsRead);
        Assert.Equal(readUtc, msg.ReadAtUtc);
        Assert.Equal(readUtc, msg.UpdatedAtUtc);

        msg.MarkAsUnread(unreadUtc);
        Assert.False(msg.IsRead);
        Assert.Null(msg.ReadAtUtc);
        Assert.Equal(unreadUtc, msg.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("http://evil.com/phish")]
    [InlineData("https://evil.com/phish")]
    [InlineData("//evil.com/phish")]
    [InlineData("javascript:alert(1)")]
    [InlineData("portal/alumno")]
    public void NotificationMessage_WithUnsafeOrExternalActionUrl_ShouldThrowArgumentException(string invalidUrl)
    {
        Assert.Throws<ArgumentException>(() =>
            NotificationMessage.Create(
                recipientPersonId: Guid.NewGuid(),
                type: NotificationType.ReservationConfirmed,
                title: "Título",
                body: "Cuerpo",
                nowUtc: DateTime.UtcNow,
                actionUrl: invalidUrl));
    }

    [Fact]
    public void NotificationDelivery_TransientFailure_ShouldScheduleExponentialBackoff_UntilMaxAttemptsReached()
    {
        var nowUtc = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        var delivery = NotificationDelivery.CreatePending(
            notificationId: Guid.NewGuid(),
            channel: NotificationChannel.Email,
            provider: "Smtp",
            recipientAddress: "alumno@gym.com",
            nowUtc: nowUtc,
            maxAttempts: 3);

        Assert.Equal(NotificationDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);

        // Intento 1: falla transitoria -> vuelve a Pending con backoff base (30s * 2^0 = 30s)
        delivery.MarkProcessing(nowUtc);
        Assert.Equal(NotificationDeliveryStatus.Processing, delivery.Status);

        delivery.RecordTransientFailure("SMTP_TIMEOUT", "Timeout de red", nowUtc, baseDelaySeconds: 30, maxDelaySeconds: 3600, jitterSeconds: 5);
        Assert.Equal(NotificationDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal(nowUtc.AddSeconds(35), delivery.NextAttemptAtUtc);
        Assert.Equal("SMTP_TIMEOUT", delivery.LastErrorCode);

        // Intento 2: falla transitoria -> vuelve a Pending con backoff (30s * 2^1 = 60s)
        var secondAttemptUtc = nowUtc.AddMinutes(1);
        delivery.MarkProcessing(secondAttemptUtc);
        delivery.RecordTransientFailure("SMTP_TIMEOUT", "Timeout de red 2", secondAttemptUtc, baseDelaySeconds: 30, maxDelaySeconds: 3600, jitterSeconds: 0);
        Assert.Equal(NotificationDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(2, delivery.AttemptCount);
        Assert.Equal(secondAttemptUtc.AddSeconds(60), delivery.NextAttemptAtUtc);

        // Intento 3 (alcanza MaxAttempts = 3): pasa a Failed definitivo sin NextAttemptAtUtc
        var thirdAttemptUtc = secondAttemptUtc.AddMinutes(2);
        delivery.MarkProcessing(thirdAttemptUtc);
        delivery.RecordTransientFailure("SMTP_TIMEOUT", "Timeout final", thirdAttemptUtc, baseDelaySeconds: 30, maxDelaySeconds: 3600, jitterSeconds: 0);
        Assert.Equal(NotificationDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
        Assert.Null(delivery.NextAttemptAtUtc);
    }

    [Fact]
    public void NotificationDelivery_PermanentFailureAndManualRetry_ShouldEnforceStateTransitions()
    {
        var nowUtc = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        var delivery = NotificationDelivery.CreatePending(
            notificationId: Guid.NewGuid(),
            channel: NotificationChannel.Email,
            provider: "Smtp",
            recipientAddress: "alumno@gym.com",
            nowUtc: nowUtc,
            maxAttempts: 3);

        delivery.MarkProcessing(nowUtc);
        delivery.RecordPermanentFailure("PROVIDER_NOT_CONFIGURED", "Proveedor SMTP no configurado password=12345", nowUtc);

        Assert.Equal(NotificationDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Null(delivery.NextAttemptAtUtc);
        Assert.DoesNotContain("12345", delivery.LastErrorMessage);

        // Reintento manual desde Failed restablece a Pending
        var adminUserId = Guid.NewGuid();
        var retryUtc = nowUtc.AddMinutes(15);
        delivery.RequestManualRetry(adminUserId, "Proveedor configurado", retryUtc);

        Assert.Equal(NotificationDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);
        Assert.Equal(retryUtc, delivery.NextAttemptAtUtc);
        Assert.Equal(adminUserId, delivery.RetriedByUserId);
        Assert.Equal("Proveedor configurado", delivery.RetryReason);

        // Una vez enviado (Sent), no se puede reintentar manualmente
        delivery.MarkProcessing(retryUtc);
        delivery.MarkSent("MSG-001", retryUtc.AddSeconds(2));
        Assert.Equal(NotificationDeliveryStatus.Sent, delivery.Status);
        Assert.Equal("MSG-001", delivery.ProviderMessageId);

        Assert.Throws<InvalidOperationException>(() =>
            delivery.RequestManualRetry(adminUserId, "Intento duplicado", retryUtc.AddMinutes(1)));
    }

    [Fact]
    public void NotificationTemplate_Lifecycle_ShouldValidateAndTransitionBetweenStates()
    {
        var nowUtc = DateTime.UtcNow;
        var template = NotificationTemplate.Create(
            code: "RESERVATION_CONFIRMED_EMAIL",
            type: NotificationType.ReservationConfirmed,
            channel: NotificationChannel.Email,
            locale: "es-AR",
            subjectTemplate: "Reserva confirmada: {{activityName}} ({{date}})",
            bodyTemplate: "<p>Hola {{studentName}}, tu reserva para {{activityName}} el {{date}} a las {{time}} está confirmada.</p>",
            allowedVariables: "studentName,activityName,date,time",
            version: 1,
            createdByUserId: Guid.NewGuid(),
            nowUtc: nowUtc,
            activateImmediately: false);

        Assert.Equal(NotificationTemplateStatus.Draft, template.Status);
        Assert.Contains("activityName", template.GetAllowedVariableSet());

        template.Activate(nowUtc.AddMinutes(1));
        Assert.Equal(NotificationTemplateStatus.Active, template.Status);

        template.Deactivate(nowUtc.AddMinutes(2));
        Assert.Equal(NotificationTemplateStatus.Inactive, template.Status);
    }
}
