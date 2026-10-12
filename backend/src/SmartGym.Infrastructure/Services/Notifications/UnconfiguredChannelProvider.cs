using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Enums;

namespace SmartGym.Infrastructure.Services.Notifications;

public class UnconfiguredChannelProvider : INotificationChannelProvider
{
    public UnconfiguredChannelProvider(NotificationChannel channel, string providerName = "Unconfigured")
    {
        Channel = channel;
        ProviderName = providerName;
    }

    public NotificationChannel Channel { get; }
    public string ProviderName { get; }
    public bool IsConfigured => false;

    public Task<ChannelDeliveryResult> SendAsync(
        ChannelDeliveryRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ChannelDeliveryResult.PermanentFailure(
            ProviderName,
            NotificationErrorCodes.ProviderNotConfigured,
            $"El proveedor para el canal '{Channel}' no se encuentra configurado."));
}
