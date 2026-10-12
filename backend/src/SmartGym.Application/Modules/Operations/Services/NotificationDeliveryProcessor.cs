using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public class NotificationDeliveryProcessor : INotificationDeliveryProcessor
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IReadOnlyList<INotificationChannelProvider> _providers;
    private readonly INotificationTemplateRenderer _templateRenderer;
    private readonly INotificationService _notificationService;
    private readonly IGymCalendar _gymCalendar;
    private readonly NotificationsOptions _options;
    private readonly ILogger<NotificationDeliveryProcessor>? _logger;

    public NotificationDeliveryProcessor(
        ISmartGymDbContext dbContext,
        IEnumerable<INotificationChannelProvider> providers,
        INotificationTemplateRenderer templateRenderer,
        INotificationService notificationService,
        IGymCalendar? gymCalendar = null,
        IOptions<NotificationsOptions>? options = null,
        ILogger<NotificationDeliveryProcessor>? logger = null)
    {
        _dbContext = dbContext;
        _providers = providers.ToList();
        _templateRenderer = templateRenderer;
        _notificationService = notificationService;
        _gymCalendar = gymCalendar ?? new GymCalendar(Options.Create(new GymOptions()));
        _options = options?.Value ?? new NotificationsOptions();
        _logger = logger;
    }

    public async Task<int> ProcessPendingBatchAsync(int? batchSize = null, CancellationToken cancellationToken = default)
    {
        var effectiveBatchSize = Math.Max(1, batchSize ?? _options.DeliveryBatchSize);
        var nowUtc = _gymCalendar.UtcNow;

        var claimedDeliveries = await _dbContext.ClaimPendingNotificationDeliveriesAsync(
            effectiveBatchSize,
            nowUtc,
            cancellationToken);
        if (claimedDeliveries.Count == 0)
        {
            return 0;
        }

        foreach (var delivery in claimedDeliveries)
        {
            await ProcessSingleDeliveryAsync(delivery, cancellationToken);
        }

        return claimedDeliveries.Count;
    }

    public async Task<int> RecoverStaleProcessingDeliveriesAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var staleThresholdUtc = nowUtc.AddMinutes(-Math.Max(1, _options.StaleProcessingTimeoutMinutes));

        var staleDeliveries = await _dbContext.NotificationDeliveries
            .Where(d => d.IsActive &&
                        d.Status == NotificationDeliveryStatus.Processing &&
                        d.LastAttemptAtUtc != null &&
                        d.LastAttemptAtUtc <= staleThresholdUtc)
            .ToListAsync(cancellationToken);

        if (staleDeliveries.Count == 0)
        {
            return 0;
        }

        foreach (var delivery in staleDeliveries)
        {
            delivery.RecordTransientFailure(
                "STALE_PROCESSING_RECOVERED",
                "Entrega en estado Processing recuperada automáticamente tras superar el umbral de tiempo.",
                nowUtc,
                _options.BaseRetryDelaySeconds,
                _options.MaxRetryDelaySeconds,
                jitterSeconds: 0);

            if (delivery.Status == NotificationDeliveryStatus.Pending)
            {
                delivery.NextAttemptAtUtc = nowUtc;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return staleDeliveries.Count;
    }

    private async Task ProcessSingleDeliveryAsync(
        NotificationDelivery delivery,
        CancellationToken cancellationToken)
    {
        var nowUtc = _gymCalendar.UtcNow;

        var message = await _dbContext.NotificationMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == delivery.NotificationId, cancellationToken);

        if (message is null || !message.IsActive)
        {
            delivery.MarkCancelled("El mensaje de notificación asociado no existe o está inactivo.", nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var recipient = await _dbContext.People
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == message.RecipientPersonId, cancellationToken);

        if (recipient is null || !recipient.IsActive)
        {
            delivery.MarkSkipped("RECIPIENT_INACTIVE", "El destinatario no se encuentra activo.", nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var isChannelEnabled = await _notificationService.IsChannelEnabledAsync(
            message.RecipientPersonId,
            message.Category,
            delivery.Channel,
            cancellationToken);

        if (!isChannelEnabled)
        {
            delivery.MarkSkipped("PREFERENCE_DISABLED", "El destinatario deshabilitó este canal de notificación.", nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var destination = delivery.Channel == NotificationChannel.Email
            ? (string.IsNullOrWhiteSpace(recipient.Email) ? null : recipient.Email.Trim())
            : delivery.RecipientAddress;

        if (delivery.Channel == NotificationChannel.Email && string.IsNullOrWhiteSpace(destination))
        {
            delivery.MarkSkipped("MISSING_RECIPIENT_EMAIL", "El destinatario no posee dirección de email configurada.", nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        delivery.RecipientAddress = destination;

        var provider = _providers.LastOrDefault(p => p.Channel == delivery.Channel);
        if (provider is null || !provider.IsConfigured)
        {
            delivery.Provider = provider?.ProviderName ?? "Unconfigured";
            delivery.RecordPermanentFailure(
                NotificationErrorCodes.ProviderNotConfigured,
                $"No hay un proveedor configurado y habilitado para el canal '{delivery.Channel}'.",
                nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        delivery.Provider = provider.ProviderName;

        var (subject, body, plainText) = await RenderChannelContentAsync(message, delivery.Channel, cancellationToken);

        try
        {
            var request = new ChannelDeliveryRequest(
                NotificationMessageId: message.Id,
                DeliveryId: delivery.Id,
                RecipientPersonId: message.RecipientPersonId,
                Type: message.Type,
                Category: message.Category,
                Channel: delivery.Channel,
                Destination: destination,
                Subject: subject,
                Body: body,
                PlainTextBody: plainText);

            var result = await provider.SendAsync(request, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.ProviderName))
            {
                delivery.Provider = result.ProviderName.Trim();
            }

            if (result.Succeeded)
            {
                delivery.MarkSent(result.ProviderMessageId, nowUtc);
            }
            else if (result.IsPermanentFailure)
            {
                delivery.RecordPermanentFailure(
                    result.ErrorCode ?? "PERMANENT_DELIVERY_ERROR",
                    result.ErrorMessage ?? "Fallo permanente reportado por el proveedor.",
                    nowUtc);
            }
            else
            {
                var jitter = Random.Shared.Next(0, Math.Max(1, _options.MaxJitterSeconds + 1));
                delivery.RecordTransientFailure(
                    result.ErrorCode ?? "TRANSIENT_DELIVERY_ERROR",
                    result.ErrorMessage ?? "Fallo temporal reportado por el proveedor.",
                    nowUtc,
                    _options.BaseRetryDelaySeconds,
                    _options.MaxRetryDelaySeconds,
                    jitter);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(
                ex,
                "Excepción transitoria al enviar la entrega {DeliveryId} por el canal {Channel}.",
                delivery.Id,
                delivery.Channel);

            var jitter = Random.Shared.Next(0, Math.Max(1, _options.MaxJitterSeconds + 1));
            delivery.RecordTransientFailure(
                "PROVIDER_EXCEPTION",
                ex.Message,
                nowUtc,
                _options.BaseRetryDelaySeconds,
                _options.MaxRetryDelaySeconds,
                jitter);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<(string Subject, string Body, string? PlainText)> RenderChannelContentAsync(
        NotificationMessage message,
        NotificationChannel channel,
        CancellationToken cancellationToken)
    {
        var variables = TryParseVariables(message.MetadataJson);
        if (variables.Count > 0)
        {
            try
            {
                var rendered = await _templateRenderer.RenderAsync(
                    message.Type,
                    channel,
                    variables,
                    "es-AR",
                    cancellationToken);
                if (rendered.MissingVariables is not { Count: > 0 })
                {
                    return (rendered.Subject, rendered.Body, rendered.PlainTextBody);
                }

                // La plantilla quedaría con huecos: se envía el contenido ya redactado del mensaje.
                _logger?.LogDebug(
                    "Plantilla {TemplateCode} sin las variables {MissingVariables} para NotificationMessage {NotificationId}; se usa el contenido directo.",
                    rendered.TemplateCode,
                    string.Join(",", rendered.MissingVariables),
                    message.Id);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(
                    ex,
                    "Usando contenido directo de NotificationMessage {NotificationId} para canal {Channel}.",
                    message.Id,
                    channel);
            }
        }

        var fallbackBody = channel == NotificationChannel.Email
            ? $"<p>{WebUtility.HtmlEncode(message.Body)}</p>"
            : message.Body;

        return (message.Title, fallbackBody, message.Body);
    }

    private static IReadOnlyDictionary<string, string?> TryParseVariables(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new Dictionary<string, string?>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string?>>(metadataJson);
            return parsed ?? new Dictionary<string, string?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>();
        }
    }
}
