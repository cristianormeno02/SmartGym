using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public record ResolvedNotificationRecipient(
    Guid RecipientPersonId,
    string FullName,
    string? Email,
    bool IsGuardianCopy,
    Guid SubjectPersonId,
    string SubjectFullName
);

public interface INotificationRecipientResolver
{
    Task<IReadOnlyList<ResolvedNotificationRecipient>> ResolveAsync(
        Guid targetPersonId,
        NotificationCategory category,
        CancellationToken cancellationToken = default);
}
