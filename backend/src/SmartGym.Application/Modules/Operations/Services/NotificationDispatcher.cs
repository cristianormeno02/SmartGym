using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly INotificationRecipientResolver _recipientResolver;
    private readonly INotificationTemplateRenderer _templateRenderer;
    private readonly INotificationService _notificationService;
    private readonly IGymCalendar _gymCalendar;
    private readonly NotificationsOptions _options;
    private readonly ILogger<NotificationDispatcher>? _logger;

    // Límites de columna de NotificationMessage (ver NotificationMessageConfiguration).
    private const int MaxTitleLength = 200;
    private const int MaxBodyLength = 4000;
    private const int MaxMetadataLength = 2000;

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public NotificationDispatcher(
        ISmartGymDbContext dbContext,
        INotificationRecipientResolver recipientResolver,
        INotificationTemplateRenderer templateRenderer,
        INotificationService notificationService,
        IGymCalendar? gymCalendar = null,
        IOptions<NotificationsOptions>? options = null,
        ILogger<NotificationDispatcher>? logger = null)
    {
        _dbContext = dbContext;
        _recipientResolver = recipientResolver;
        _templateRenderer = templateRenderer;
        _notificationService = notificationService;
        _gymCalendar = gymCalendar ?? new GymCalendar(Options.Create(new GymOptions()));
        _options = options?.Value ?? new NotificationsOptions();
        _logger = logger;
    }

    public async Task<DispatchNotificationResult> DispatchAsync(
        DispatchNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var category = command.Type.GetCategory();
        var recipients = await _recipientResolver.ResolveAsync(command.TargetPersonId, category, cancellationToken);

        var createdMessages = new List<NotificationMessage>();
        var deduplicatedCount = 0;
        var suppressedCount = 0;

        var requestedChannels = command.RequestedChannels is { Count: > 0 }
            ? command.RequestedChannels
            : new[] { NotificationChannel.InApp, NotificationChannel.Email };

        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? null
            : command.IdempotencyKey.Trim();

        foreach (var recipient in recipients)
        {
            if (normalizedIdempotencyKey is not null)
            {
                var existsInLocal = _dbContext.NotificationMessages.Local.Any(m =>
                    m.RecipientPersonId == recipient.RecipientPersonId &&
                    m.IdempotencyKey == normalizedIdempotencyKey);

                var existsInDb = existsInLocal || await _dbContext.NotificationMessages
                    .AsNoTracking()
                    .AnyAsync(
                        m => m.RecipientPersonId == recipient.RecipientPersonId &&
                             m.IdempotencyKey == normalizedIdempotencyKey,
                        cancellationToken);

                if (existsInDb)
                {
                    deduplicatedCount++;
                    continue;
                }
            }

            var enabledChannels = new List<NotificationChannel>();
            foreach (var channel in requestedChannels.Distinct())
            {
                if (await _notificationService.IsChannelEnabledAsync(
                        recipient.RecipientPersonId,
                        category,
                        channel,
                        cancellationToken))
                {
                    enabledChannels.Add(channel);
                }
            }

            if (enabledChannels.Count == 0)
            {
                suppressedCount++;
                continue;
            }

            var recipientVariables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in command.Variables)
            {
                recipientVariables[kvp.Key] = kvp.Value;
            }

            if (!recipientVariables.ContainsKey("recipientName") || recipient.IsGuardianCopy)
            {
                recipientVariables["recipientName"] = recipient.FullName;
            }

            if (recipient.IsGuardianCopy && !recipientVariables.ContainsKey("studentName"))
            {
                recipientVariables["studentName"] = recipient.SubjectFullName;
            }

            string title;
            string body;
            if (!string.IsNullOrWhiteSpace(command.DirectTitleOverride) &&
                !string.IsNullOrWhiteSpace(command.DirectBodyOverride))
            {
                title = command.DirectTitleOverride.Trim();
                body = command.DirectBodyOverride.Trim();
            }
            else
            {
                var renderedInApp = await _templateRenderer.RenderAsync(
                    command.Type,
                    NotificationChannel.InApp,
                    recipientVariables,
                    "es-AR",
                    cancellationToken);
                title = renderedInApp.Subject;
                body = renderedInApp.Body;
            }

            title = Truncate(title, MaxTitleLength);
            body = Truncate(body, MaxBodyLength);

            var metadataJson = !string.IsNullOrWhiteSpace(command.MetadataJson)
                ? command.MetadataJson.Trim()
                : JsonSerializer.Serialize(recipientVariables, MetadataJsonOptions);

            if (metadataJson.Length > MaxMetadataLength)
            {
                // Sin metadatos, los canales externos envían el título y cuerpo ya redactados.
                _logger?.LogWarning(
                    "Metadatos de notificación {Type} descartados por superar {MaxLength} caracteres (IdempotencyKey={IdempotencyKey}).",
                    command.Type,
                    MaxMetadataLength,
                    normalizedIdempotencyKey);
                metadataJson = null;
            }

            var message = NotificationMessage.Create(
                recipientPersonId: recipient.RecipientPersonId,
                type: command.Type,
                title: title,
                body: body,
                nowUtc: nowUtc,
                relatedEntityType: command.RelatedEntityType,
                relatedEntityId: command.RelatedEntityId,
                actionUrl: command.ActionUrl,
                idempotencyKey: normalizedIdempotencyKey,
                metadataJson: metadataJson,
                correlationId: command.CorrelationId,
                expiresAtUtc: command.ExpiresAtUtc);

            var deliveriesForRecipient = new List<NotificationDelivery>();

            foreach (var channel in enabledChannels)
            {
                if (channel == NotificationChannel.InApp)
                {
                    var inAppDelivery = NotificationDelivery.CreatePending(
                        message.Id,
                        NotificationChannel.InApp,
                        "InApp",
                        recipient.RecipientPersonId.ToString(),
                        nowUtc,
                        maxAttempts: 1);
                    inAppDelivery.MarkDelivered($"inapp:{message.Id}", nowUtc);
                    deliveriesForRecipient.Add(inAppDelivery);
                }
                else if (channel == NotificationChannel.Email)
                {
                    var emailDelivery = NotificationDelivery.CreatePending(
                        message.Id,
                        NotificationChannel.Email,
                        "Email",
                        recipient.Email,
                        nowUtc,
                        maxAttempts: Math.Max(1, _options.MaxDeliveryAttempts));
                    deliveriesForRecipient.Add(emailDelivery);
                }
                else
                {
                    var externalDelivery = NotificationDelivery.CreatePending(
                        message.Id,
                        channel,
                        channel.ToString(),
                        null,
                        nowUtc,
                        maxAttempts: Math.Max(1, _options.MaxDeliveryAttempts));
                    deliveriesForRecipient.Add(externalDelivery);
                }
            }

            _dbContext.NotificationMessages.Add(message);
            foreach (var delivery in deliveriesForRecipient)
            {
                _dbContext.NotificationDeliveries.Add(delivery);
            }

            if (!command.SaveChanges)
            {
                createdMessages.Add(message);
                continue;
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                createdMessages.Add(message);
            }
            catch (DbUpdateException ex)
            {
                // Las entidades que no se pudieron guardar no deben quedar en el contexto:
                // envenenarían los siguientes SaveChanges de quien comparte este scope.
                if (_dbContext is DbContext efDbContext)
                {
                    efDbContext.Entry(message).State = EntityState.Detached;
                    foreach (var delivery in deliveriesForRecipient)
                    {
                        efDbContext.Entry(delivery).State = EntityState.Detached;
                    }
                }

                if (normalizedIdempotencyKey is null || !IsUniqueIdempotencyViolation(ex))
                {
                    throw;
                }

                _logger?.LogInformation(
                    "Notificación deduplicada por concurrencia para RecipientPersonId={RecipientPersonId}, IdempotencyKey={IdempotencyKey}.",
                    recipient.RecipientPersonId,
                    normalizedIdempotencyKey);

                deduplicatedCount++;
            }
        }

        return new DispatchNotificationResult(createdMessages, deduplicatedCount, suppressedCount);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static bool IsUniqueIdempotencyViolation(DbUpdateException ex)
    {
        var fullMessage = ex.ToString();
        return fullMessage.Contains("UX_NotificationMessages_Recipient_IdempotencyKey", StringComparison.OrdinalIgnoreCase) ||
               fullMessage.Contains("23505", StringComparison.OrdinalIgnoreCase);
    }
}
