using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public record ChannelDeliveryRequest(
    Guid NotificationMessageId,
    Guid DeliveryId,
    Guid RecipientPersonId,
    NotificationType Type,
    NotificationCategory Category,
    NotificationChannel Channel,
    string? Destination,
    string Subject,
    string Body,
    string? PlainTextBody
);

public record ChannelDeliveryResult(
    bool Succeeded,
    string ProviderName,
    string? ProviderMessageId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool IsPermanentFailure = false
)
{
    public static ChannelDeliveryResult Success(string providerName, string providerMessageId) =>
        new(true, providerName, ProviderMessageId: providerMessageId);

    public static ChannelDeliveryResult TransientFailure(string providerName, string errorCode, string errorMessage) =>
        new(false, providerName, ErrorCode: errorCode, ErrorMessage: errorMessage, IsPermanentFailure: false);

    public static ChannelDeliveryResult PermanentFailure(string providerName, string errorCode, string errorMessage) =>
        new(false, providerName, ErrorCode: errorCode, ErrorMessage: errorMessage, IsPermanentFailure: true);
}

public interface INotificationChannelProvider
{
    NotificationChannel Channel { get; }
    string ProviderName { get; }
    bool IsConfigured { get; }
    Task<ChannelDeliveryResult> SendAsync(ChannelDeliveryRequest request, CancellationToken cancellationToken = default);
}

public interface IEmailNotificationProvider : INotificationChannelProvider
{
}
