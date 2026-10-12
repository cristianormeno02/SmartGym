using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public interface INotificationService
{
    Task<List<NotificationDto>> GetNotificationsForPersonAsync(Guid personId, bool onlyUnread = false, CancellationToken cancellationToken = default);
    Task<PagedResult<NotificationDto>> GetMyPagedAsync(Guid personId, NotificationQueryFilter filter, CancellationToken cancellationToken = default);
    Task<NotificationDto> GetByIdForPersonAsync(Guid notificationId, Guid personId, CancellationToken cancellationToken = default);
    Task<UnreadNotificationsCountDto> GetUnreadCountAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<NotificationDto> SendNotificationAsync(SendNotificationRequest request, CancellationToken cancellationToken = default);
    Task<bool> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task<NotificationDto> MarkAsReadForPersonAsync(Guid notificationId, Guid personId, CancellationToken cancellationToken = default);
    Task<NotificationDto> MarkAsUnreadForPersonAsync(Guid notificationId, Guid personId, CancellationToken cancellationToken = default);
    Task<MarkAllNotificationsReadResultDto> MarkAllAsReadForPersonAsync(Guid personId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationPreferenceItemDto>> GetPreferencesAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationPreferenceItemDto>> UpdatePreferencesAsync(Guid personId, UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsChannelEnabledAsync(Guid personId, NotificationCategory category, NotificationChannel channel, CancellationToken cancellationToken = default);

    Task<PagedResult<NotificationDto>> GetAdminPagedAsync(AdminNotificationQueryFilter filter, CancellationToken cancellationToken = default);
    Task<NotificationDto> GetAdminByIdAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationDeliveryDto>> GetDeliveriesByNotificationIdAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task<PagedResult<NotificationDeliveryDto>> GetFailedDeliveriesPagedAsync(int page = 1, int pageSize = 20, NotificationChannel? channel = null, bool excludeProviderNotConfigured = false, CancellationToken cancellationToken = default);
    Task<NotificationDeliveryDto> RetryDeliveryAsync(Guid deliveryId, RetryNotificationDeliveryRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationTemplateDto>> GetTemplatesAsync(NotificationTemplateQueryFilter filter, CancellationToken cancellationToken = default);
    Task<NotificationTemplateDto> GetTemplateByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<NotificationTemplateDto> CreateTemplateAsync(CreateNotificationTemplateRequest request, CancellationToken cancellationToken = default);
    Task<NotificationTemplateDto> UpdateTemplateAsync(Guid id, UpdateNotificationTemplateRequest request, CancellationToken cancellationToken = default);
    Task<NotificationTemplateDto> ActivateTemplateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<NotificationTemplateDto> DeactivateTemplateAsync(Guid id, CancellationToken cancellationToken = default);
}
