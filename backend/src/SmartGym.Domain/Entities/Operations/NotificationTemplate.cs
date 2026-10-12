using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Operations;

public class NotificationTemplate : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; }
    public string Locale { get; set; } = "es-AR";

    public string? SubjectTemplate { get; set; }
    public string BodyTemplate { get; set; } = string.Empty;
    public string AllowedVariables { get; set; } = string.Empty;

    public int Version { get; set; } = 1;
    public NotificationTemplateStatus Status { get; set; } = NotificationTemplateStatus.Draft;
    public Guid? CreatedByUserId { get; set; }

    public uint VersionToken { get; set; }

    public static NotificationTemplate Create(
        string code,
        NotificationType type,
        NotificationChannel channel,
        string locale,
        string? subjectTemplate,
        string bodyTemplate,
        string allowedVariables,
        int version,
        Guid? createdByUserId,
        DateTime nowUtc,
        bool activateImmediately = false)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("El código de la plantilla es obligatorio.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(bodyTemplate))
        {
            throw new ArgumentException("El cuerpo de la plantilla es obligatorio.", nameof(bodyTemplate));
        }

        if (channel == NotificationChannel.Email && string.IsNullOrWhiteSpace(subjectTemplate))
        {
            throw new ArgumentException("El asunto es obligatorio para plantillas de correo electrónico.", nameof(subjectTemplate));
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "La versión de la plantilla debe ser mayor o igual a 1.");
        }

        return new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
            Type = type,
            Channel = channel,
            Locale = string.IsNullOrWhiteSpace(locale) ? "es-AR" : locale.Trim(),
            SubjectTemplate = string.IsNullOrWhiteSpace(subjectTemplate) ? null : subjectTemplate.Trim(),
            BodyTemplate = bodyTemplate.Trim(),
            AllowedVariables = NormalizeAllowedVariables(allowedVariables),
            Version = version,
            Status = activateImmediately ? NotificationTemplateStatus.Active : NotificationTemplateStatus.Draft,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            IsActive = true
        };
    }

    public void UpdateContent(
        string? subjectTemplate,
        string bodyTemplate,
        string allowedVariables,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(bodyTemplate))
        {
            throw new ArgumentException("El cuerpo de la plantilla es obligatorio.", nameof(bodyTemplate));
        }

        if (Channel == NotificationChannel.Email && string.IsNullOrWhiteSpace(subjectTemplate))
        {
            throw new ArgumentException("El asunto es obligatorio para plantillas de correo electrónico.", nameof(subjectTemplate));
        }

        SubjectTemplate = string.IsNullOrWhiteSpace(subjectTemplate) ? null : subjectTemplate.Trim();
        BodyTemplate = bodyTemplate.Trim();
        AllowedVariables = NormalizeAllowedVariables(allowedVariables);
        UpdatedAtUtc = nowUtc;
    }

    public void Activate(DateTime nowUtc)
    {
        Status = NotificationTemplateStatus.Active;
        UpdatedAtUtc = nowUtc;
    }

    public void Deactivate(DateTime nowUtc)
    {
        Status = NotificationTemplateStatus.Inactive;
        UpdatedAtUtc = nowUtc;
    }

    public HashSet<string> GetAllowedVariableSet()
    {
        if (string.IsNullOrWhiteSpace(AllowedVariables))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return AllowedVariables
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string NormalizeAllowedVariables(string? allowedVariables)
    {
        if (string.IsNullOrWhiteSpace(allowedVariables))
        {
            return string.Empty;
        }

        return string.Join(
            ",",
            allowedVariables
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal));
    }
}
