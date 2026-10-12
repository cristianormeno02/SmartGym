using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public record DispatchNotificationCommand(
    Guid TargetPersonId,
    NotificationType Type,
    IReadOnlyDictionary<string, string?> Variables,
    string? IdempotencyKey = null,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    string? ActionUrl = null,
    DateTime? ExpiresAtUtc = null,
    string? MetadataJson = null,
    string? CorrelationId = null,
    IReadOnlyCollection<NotificationChannel>? RequestedChannels = null,
    string? DirectTitleOverride = null,
    string? DirectBodyOverride = null,
    bool SaveChanges = true
);

public record DispatchNotificationResult(
    IReadOnlyList<NotificationMessage> CreatedMessages,
    int DeduplicatedCount,
    int SuppressedByPreferenceCount
);

public interface INotificationDispatcher
{
    Task<DispatchNotificationResult> DispatchAsync(
        DispatchNotificationCommand command,
        CancellationToken cancellationToken = default);
}
