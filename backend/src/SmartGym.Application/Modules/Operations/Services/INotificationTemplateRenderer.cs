using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public interface INotificationTemplateRenderer
{
    Task<RenderedNotificationContent> RenderAsync(
        NotificationType type,
        NotificationChannel channel,
        IReadOnlyDictionary<string, string?> variables,
        string locale = "es-AR",
        CancellationToken cancellationToken = default);

    RenderedNotificationContent RenderTemplate(
        NotificationTemplate template,
        IReadOnlyDictionary<string, string?> variables);

    void ValidateTemplateDefinition(
        string subjectTemplate,
        string bodyTemplate,
        string? plainTextTemplate,
        IReadOnlyCollection<string> allowedVariables,
        NotificationChannel channel);
}
