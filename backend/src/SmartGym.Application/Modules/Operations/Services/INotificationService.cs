using SmartGym.Application.Modules.Operations.Dtos;

namespace SmartGym.Application.Modules.Operations.Services;

public interface INotificationService
{
    Task<List<NotificationDto>> GetNotificationsForPersonAsync(Guid personId, bool onlyUnread = false, CancellationToken cancellationToken = default);
    Task<NotificationDto> SendNotificationAsync(SendNotificationRequest request, CancellationToken cancellationToken = default);
    Task<bool> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task NotifyClassSuspendedAsync(Guid classSessionId, string reason, CancellationToken cancellationToken = default);
}
