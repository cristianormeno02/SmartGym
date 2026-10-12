using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Validators;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.UnitTests.Operations;

public class NotificationValidatorTests
{
    [Fact]
    public void SendNotificationRequestValidator_ValidatesRequiredFieldsAndSafeActionUrl()
    {
        var validator = new SendNotificationRequestValidator();

        var invalid = new SendNotificationRequest(
            RecipientPersonId: Guid.Empty,
            Type: NotificationType.ClassSuspended,
            Channel: NotificationChannel.InApp,
            Title: "",
            Body: "",
            ActionUrl: "https://evil.example.com/phishing");

        var result = validator.Validate(invalid);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendNotificationRequest.RecipientPersonId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendNotificationRequest.Title));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendNotificationRequest.Body));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendNotificationRequest.ActionUrl));

        var valid = new SendNotificationRequest(
            RecipientPersonId: Guid.NewGuid(),
            Type: NotificationType.AdministrativeNotice,
            Channel: NotificationChannel.InApp,
            Title: "Aviso operativo",
            Body: "Mantenimiento programado mañana a las 06:00.",
            ActionUrl: "/portal/notificaciones");

        Assert.True(validator.Validate(valid).IsValid);
    }

    [Fact]
    public void UpdateNotificationPreferencesRequestValidator_RejectsEmptyListOrInvalidEnums()
    {
        var validator = new UpdateNotificationPreferencesRequestValidator();

        var emptyResult = validator.Validate(new UpdateNotificationPreferencesRequest([]));
        Assert.False(emptyResult.IsValid);

        var validResult = validator.Validate(new UpdateNotificationPreferencesRequest(
        [
            new UpdateNotificationPreferenceEntry(NotificationCategory.Promotions, NotificationChannel.Email, true),
            new UpdateNotificationPreferenceEntry(NotificationCategory.Reservations, NotificationChannel.Email, false)
        ]));
        Assert.True(validResult.IsValid);
    }

    [Fact]
    public void CreateAndUpdateNotificationTemplateValidators_ValidatePlaceholdersAndUnsafeMarkup()
    {
        var createValidator = new CreateNotificationTemplateRequestValidator();

        var invalidCreate = new CreateNotificationTemplateRequest(
            Code: "",
            Type: NotificationType.ReservationConfirmed,
            Channel: NotificationChannel.Email,
            SubjectTemplate: "Hola {{undeclaredVar}}",
            BodyTemplate: "<p>{{recipientName}}</p><script>bad()</script>",
            AllowedVariables: ["recipientName"],
            Locale: "es-AR",
            Version: 0);

        var invalidResult = createValidator.Validate(invalidCreate);
        Assert.False(invalidResult.IsValid);
        Assert.Contains(invalidResult.Errors, e => e.PropertyName == nameof(CreateNotificationTemplateRequest.Code));
        Assert.Contains(invalidResult.Errors, e => e.PropertyName == nameof(CreateNotificationTemplateRequest.Version));
        Assert.Contains(invalidResult.Errors, e => e.PropertyName == nameof(CreateNotificationTemplateRequest.SubjectTemplate) || e.PropertyName == nameof(CreateNotificationTemplateRequest.BodyTemplate));

        var validCreate = new CreateNotificationTemplateRequest(
            Code: "RESERVATION_CONFIRMED_CUSTOM",
            Type: NotificationType.ReservationConfirmed,
            Channel: NotificationChannel.Email,
            SubjectTemplate: "Reserva confirmada: {{activityName}}",
            BodyTemplate: "<p>Hola {{recipientName}}, te esperamos en {{activityName}}.</p>",
            PlainTextTemplate: "Hola {{recipientName}}, te esperamos en {{activityName}}.",
            AllowedVariables: ["recipientName", "activityName"],
            Locale: "es-AR",
            Version: 2);

        Assert.True(createValidator.Validate(validCreate).IsValid);

        var updateValidator = new UpdateNotificationTemplateRequestValidator();
        var invalidUpdate = new UpdateNotificationTemplateRequest(
            SubjectTemplate: "",
            BodyTemplate: "<img src=x onerror=alert(1)>",
            PlainTextTemplate: null,
            AllowedVariables: ["recipientName"]);
        Assert.False(updateValidator.Validate(invalidUpdate).IsValid);
    }

    [Fact]
    public void RetryNotificationDeliveryRequestValidator_EnforcesReasonMaxLength()
    {
        var validator = new RetryNotificationDeliveryRequestValidator();

        var tooLong = new RetryNotificationDeliveryRequest(new string('x', 501));
        Assert.False(validator.Validate(tooLong).IsValid);

        var valid = new RetryNotificationDeliveryRequest("Reintento manual tras restablecer SMTP");
        Assert.True(validator.Validate(valid).IsValid);
    }
}
