using FluentValidation;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Validators;

public class SendNotificationRequestValidator : AbstractValidator<SendNotificationRequest>
{
    public SendNotificationRequestValidator()
    {
        RuleFor(x => x.RecipientPersonId)
            .NotEmpty().WithMessage("El destinatario es obligatorio.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("El tipo de notificación no es válido.");

        RuleFor(x => x.Channel)
            .IsInEnum().WithMessage("El canal de notificación no es válido.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("El título de la notificación es obligatorio.")
            .MaximumLength(200).WithMessage("El título no puede superar los 200 caracteres.");

        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("El cuerpo de la notificación es obligatorio.")
            .MaximumLength(4000).WithMessage("El cuerpo no puede superar los 4000 caracteres.");

        RuleFor(x => x.ActionUrl)
            .MaximumLength(500).WithMessage("La URL de acción no puede superar los 500 caracteres.")
            .Must(NotificationMessage.IsValidInternalActionUrl)
            .WithMessage("La URL de acción debe ser una ruta interna relativa que comience con '/'.")
            .WithErrorCode(NotificationErrorCodes.InvalidActionUrl);

        RuleFor(x => x.RelatedEntityType)
            .MaximumLength(80).WithMessage("El tipo de entidad relacionada no puede superar los 80 caracteres.");

        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(180).WithMessage("La clave de idempotencia no puede superar los 180 caracteres.");
    }
}

public class UpdateNotificationPreferencesRequestValidator : AbstractValidator<UpdateNotificationPreferencesRequest>
{
    public UpdateNotificationPreferencesRequestValidator()
    {
        RuleFor(x => x.Preferences)
            .NotNull().WithMessage("La lista de preferencias es obligatoria.")
            .NotEmpty().WithMessage("Debe especificar al menos una preferencia.");

        RuleForEach(x => x.Preferences).ChildRules(entry =>
        {
            entry.RuleFor(p => p.Category)
                .IsInEnum().WithMessage("La categoría de notificación no es válida.");

            entry.RuleFor(p => p.Channel)
                .IsInEnum().WithMessage("El canal de notificación no es válido.");
        });
    }
}

public class CreateNotificationTemplateRequestValidator : AbstractValidator<CreateNotificationTemplateRequest>
{
    public CreateNotificationTemplateRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("El código de la plantilla es obligatorio.")
            .MaximumLength(100).WithMessage("El código no puede superar los 100 caracteres.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("El tipo de notificación no es válido.");

        RuleFor(x => x.Channel)
            .IsInEnum().WithMessage("El canal de notificación no es válido.");

        RuleFor(x => x.Locale)
            .NotEmpty().WithMessage("El idioma/locale es obligatorio.")
            .MaximumLength(10).WithMessage("El locale no puede superar los 10 caracteres.");

        RuleFor(x => x.Version)
            .Must(v => !v.HasValue || v.Value >= 1)
            .WithMessage("La versión debe ser mayor o igual a 1.");

        RuleFor(x => x.SubjectTemplate)
            .NotEmpty().WithMessage("El asunto de la plantilla es obligatorio.")
            .MaximumLength(200).WithMessage("El asunto no puede superar los 200 caracteres.")
            .Must(s => !NotificationTemplateRenderer.HasUnsafeContent(s))
            .WithMessage("El asunto contiene contenido inseguro.")
            .WithErrorCode(NotificationErrorCodes.TemplateUnsafeContent)
            .Must((req, subject) => ValidatePlaceholders(subject, req.AllowedVariables, req.Type))
            .WithMessage("El asunto utiliza variables no permitidas.")
            .WithErrorCode(NotificationErrorCodes.TemplateInvalidVariables);

        RuleFor(x => x.BodyTemplate)
            .NotEmpty().WithMessage("El cuerpo de la plantilla es obligatorio.")
            .MaximumLength(4000).WithMessage("El cuerpo no puede superar los 4000 caracteres.")
            .Must(b => !NotificationTemplateRenderer.HasUnsafeContent(b))
            .WithMessage("El cuerpo contiene etiquetas o atributos HTML/JavaScript no permitidos.")
            .WithErrorCode(NotificationErrorCodes.TemplateUnsafeContent)
            .Must((req, body) => ValidatePlaceholders(body, req.AllowedVariables, req.Type))
            .WithMessage("El cuerpo utiliza variables no permitidas.")
            .WithErrorCode(NotificationErrorCodes.TemplateInvalidVariables);
    }

    internal static bool ValidatePlaceholders(string? text, IReadOnlyList<string>? allowedVariables, NotificationType? type = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        HashSet<string> allowed;
        if (allowedVariables != null && allowedVariables.Count > 0)
        {
            allowed = allowedVariables
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToHashSet(StringComparer.Ordinal);
        }
        else if (type.HasValue)
        {
            allowed = DefaultNotificationTemplates
                .GetFallback(type.Value, NotificationChannel.InApp)
                .GetAllowedVariableSet();
        }
        else
        {
            allowed = new HashSet<string>(StringComparer.Ordinal);
        }

        return NotificationTemplateRenderer.ExtractInvalidVariables(text, allowed).Count == 0;
    }
}

public class UpdateNotificationTemplateRequestValidator : AbstractValidator<UpdateNotificationTemplateRequest>
{
    public UpdateNotificationTemplateRequestValidator()
    {
        RuleFor(x => x.SubjectTemplate)
            .NotEmpty().WithMessage("El asunto de la plantilla es obligatorio.")
            .MaximumLength(200).WithMessage("El asunto no puede superar los 200 caracteres.")
            .Must(s => !NotificationTemplateRenderer.HasUnsafeContent(s))
            .WithMessage("El asunto contiene contenido inseguro.")
            .WithErrorCode(NotificationErrorCodes.TemplateUnsafeContent)
            .Must((req, subject) =>
                req.AllowedVariables == null ||
                CreateNotificationTemplateRequestValidator.ValidatePlaceholders(subject, req.AllowedVariables))
            .WithMessage("El asunto utiliza variables no permitidas.")
            .WithErrorCode(NotificationErrorCodes.TemplateInvalidVariables);

        RuleFor(x => x.BodyTemplate)
            .NotEmpty().WithMessage("El cuerpo de la plantilla es obligatorio.")
            .MaximumLength(4000).WithMessage("El cuerpo no puede superar los 4000 caracteres.")
            .Must(b => !NotificationTemplateRenderer.HasUnsafeContent(b))
            .WithMessage("El cuerpo contiene etiquetas o atributos HTML/JavaScript no permitidos.")
            .WithErrorCode(NotificationErrorCodes.TemplateUnsafeContent)
            .Must((req, body) =>
                req.AllowedVariables == null ||
                CreateNotificationTemplateRequestValidator.ValidatePlaceholders(body, req.AllowedVariables))
            .WithMessage("El cuerpo utiliza variables no permitidas.")
            .WithErrorCode(NotificationErrorCodes.TemplateInvalidVariables);
    }
}

public class RetryNotificationDeliveryRequestValidator : AbstractValidator<RetryNotificationDeliveryRequest>
{
    public RetryNotificationDeliveryRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("El motivo de reintento no puede superar los 500 caracteres.");
    }
}
