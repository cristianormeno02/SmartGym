using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Validators;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public class NotificationService : INotificationService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly ICurrentUserService? _currentUserService;

    private static readonly NotificationCategory[] AllCategories =
    [
        NotificationCategory.Reservations,
        NotificationCategory.ClassChanges,
        NotificationCategory.MembershipsAndCredits,
        NotificationCategory.Payments,
        NotificationCategory.Promotions,
        NotificationCategory.Security,
        NotificationCategory.Administrative
    ];

    private static readonly NotificationChannel[] ConfigurableChannels =
    [
        NotificationChannel.InApp,
        NotificationChannel.Email
    ];

    public NotificationService(
        ISmartGymDbContext dbContext,
        IGymCalendar? gymCalendar = null,
        ICurrentUserService? currentUserService = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar ?? new GymCalendar(Options.Create(new GymOptions()));
        _currentUserService = currentUserService;
    }

    public async Task<List<NotificationDto>> GetNotificationsForPersonAsync(
        Guid personId,
        bool onlyUnread = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.NotificationMessages
            .Include(n => n.RecipientPerson)
            .AsNoTracking()
            .Where(n => n.IsActive && n.RecipientPersonId == personId);

        if (onlyUnread)
        {
            query = query.Where(n => !n.IsRead);
        }

        var list = await query
            .OrderByDescending(n => n.SentAtUtc)
            .ThenByDescending(n => n.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<PagedResult<NotificationDto>> GetMyPagedAsync(
        Guid personId,
        NotificationQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = _dbContext.NotificationMessages
            .Include(n => n.RecipientPerson)
            .AsNoTracking()
            .Where(n => n.IsActive && n.RecipientPersonId == personId);

        if (filter.IsRead.HasValue)
        {
            query = query.Where(n => n.IsRead == filter.IsRead.Value);
        }

        if (filter.Category.HasValue)
        {
            query = query.Where(n => n.Category == filter.Category.Value);
        }

        if (filter.Type.HasValue)
        {
            query = query.Where(n => n.Type == filter.Type.Value);
        }

        if (filter.FromUtc.HasValue)
        {
            query = query.Where(n => n.SentAtUtc >= filter.FromUtc.Value);
        }

        if (filter.ToUtc.HasValue)
        {
            query = query.Where(n => n.SentAtUtc <= filter.ToUtc.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(n => n.SentAtUtc)
            .ThenByDescending(n => n.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationDto>(
            items.Select(MapToDto).ToList(),
            totalCount,
            page,
            pageSize);
    }

    public async Task<NotificationDto> GetByIdForPersonAsync(
        Guid notificationId,
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.NotificationMessages
            .Include(n => n.RecipientPerson)
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.IsActive, cancellationToken);

        if (message == null)
        {
            throw new NotFoundException("La notificación solicitada no existe.");
        }

        if (message.RecipientPersonId != personId)
        {
            throw new ForbiddenException("No tiene permisos para acceder a una notificación de otro destinatario.");
        }

        return MapToDto(message);
    }

    public async Task<UnreadNotificationsCountDto> GetUnreadCountAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var count = await _dbContext.NotificationMessages
            .AsNoTracking()
            .CountAsync(n => n.IsActive && n.RecipientPersonId == personId && !n.IsRead, cancellationToken);

        return new UnreadNotificationsCountDto(count);
    }

    public async Task<NotificationDto> SendNotificationAsync(
        SendNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var validator = new SendNotificationRequestValidator();
        await ValidateAndThrowAsync(validator, request, cancellationToken);

        var recipient = await _dbContext.People
            .FirstOrDefaultAsync(p => p.Id == request.RecipientPersonId && p.IsActive, cancellationToken);
        if (recipient == null)
        {
            throw new ArgumentException("El destinatario de la notificación no existe.");
        }

        var nowUtc = _gymCalendar.UtcNow;
        var message = NotificationMessage.Create(
            recipientPersonId: request.RecipientPersonId,
            type: request.Type,
            title: request.Title,
            body: request.Body,
            nowUtc: nowUtc,
            relatedEntityType: request.RelatedEntityType,
            relatedEntityId: request.RelatedEntityId,
            actionUrl: request.ActionUrl,
            idempotencyKey: request.IdempotencyKey,
            correlationId: request.CorrelationId);
        message.Channel = request.Channel;

        _dbContext.NotificationMessages.Add(message);

        var delivery = NotificationDelivery.CreatePending(
            message.Id,
            request.Channel,
            request.Channel == NotificationChannel.InApp ? "InApp" : request.Channel.ToString(),
            request.Channel == NotificationChannel.InApp ? recipient.Id.ToString() : recipient.Email,
            nowUtc,
            maxAttempts: request.Channel == NotificationChannel.InApp ? 1 : 4);

        if (request.Channel == NotificationChannel.InApp)
        {
            delivery.MarkDelivered($"inapp:{message.Id}", nowUtc);
        }

        _dbContext.NotificationDeliveries.Add(delivery);
        await _dbContext.SaveChangesAsync(cancellationToken);

        message.RecipientPerson = recipient;
        return MapToDto(message);
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.NotificationMessages
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.IsActive, cancellationToken);
        if (message == null)
        {
            return false;
        }

        message.MarkAsRead(_gymCalendar.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<NotificationDto> MarkAsReadForPersonAsync(
        Guid notificationId,
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.NotificationMessages
            .Include(n => n.RecipientPerson)
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.IsActive, cancellationToken);

        if (message == null)
        {
            throw new NotFoundException("La notificación solicitada no existe.");
        }

        if (message.RecipientPersonId != personId)
        {
            throw new ForbiddenException("No tiene permisos para modificar una notificación de otro destinatario.");
        }

        message.MarkAsRead(_gymCalendar.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(message);
    }

    public async Task<NotificationDto> MarkAsUnreadForPersonAsync(
        Guid notificationId,
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.NotificationMessages
            .Include(n => n.RecipientPerson)
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.IsActive, cancellationToken);

        if (message == null)
        {
            throw new NotFoundException("La notificación solicitada no existe.");
        }

        if (message.RecipientPersonId != personId)
        {
            throw new ForbiddenException("No tiene permisos para modificar una notificación de otro destinatario.");
        }

        message.MarkAsUnread(_gymCalendar.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(message);
    }

    public async Task<MarkAllNotificationsReadResultDto> MarkAllAsReadForPersonAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var unreadList = await _dbContext.NotificationMessages
            .Where(n => n.IsActive && n.RecipientPersonId == personId && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unreadList.Count == 0)
        {
            return new MarkAllNotificationsReadResultDto(0);
        }

        var nowUtc = _gymCalendar.UtcNow;
        foreach (var message in unreadList)
        {
            message.MarkAsRead(nowUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new MarkAllNotificationsReadResultDto(unreadList.Count);
    }

    public async Task<IReadOnlyList<NotificationPreferenceItemDto>> GetPreferencesAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var saved = await _dbContext.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.PersonId == personId && p.IsActive)
            .ToListAsync(cancellationToken);

        var lookup = saved.ToDictionary(p => (p.Category, p.Channel), p => p.Enabled);
        var result = new List<NotificationPreferenceItemDto>(AllCategories.Length * ConfigurableChannels.Length);

        foreach (var category in AllCategories)
        {
            foreach (var channel in ConfigurableChannels)
            {
                var isLocked = NotificationPreference.IsMandatoryChannel(category, channel);
                var isTransactional = category.IsTransactional();
                var isEnabled = isLocked
                    ? true
                    : lookup.TryGetValue((category, channel), out var customValue)
                        ? customValue
                        : NotificationPreference.GetDefaultEnabled(category, channel);

                result.Add(new NotificationPreferenceItemDto(
                    Category: category,
                    Channel: channel,
                    IsEnabled: isEnabled,
                    IsLocked: isLocked,
                    IsTransactional: isTransactional));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<NotificationPreferenceItemDto>> UpdatePreferencesAsync(
        Guid personId,
        UpdateNotificationPreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        var validator = new UpdateNotificationPreferencesRequestValidator();
        await ValidateAndThrowAsync(validator, request, cancellationToken);

        var existing = await _dbContext.NotificationPreferences
            .Where(p => p.PersonId == personId)
            .ToListAsync(cancellationToken);

        var nowUtc = _gymCalendar.UtcNow;

        foreach (var entry in request.Preferences)
        {
            if (!entry.IsEnabled && NotificationPreference.IsMandatoryChannel(entry.Category, entry.Channel))
            {
                throw ConflictException.WithCode(
                    NotificationErrorCodes.PreferenceMandatoryChannelLocked,
                    $"El canal {entry.Channel} para la categoría {entry.Category} es obligatorio y no puede deshabilitarse.");
            }

            var current = existing.FirstOrDefault(p => p.Category == entry.Category && p.Channel == entry.Channel);
            if (current == null)
            {
                var created = NotificationPreference.CreateOrUpdate(
                    personId,
                    entry.Category,
                    entry.Channel,
                    entry.IsEnabled,
                    nowUtc);
                _dbContext.NotificationPreferences.Add(created);
                existing.Add(created);
            }
            else
            {
                current.SetEnabled(entry.IsEnabled, nowUtc);
                current.IsActive = true;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetPreferencesAsync(personId, cancellationToken);
    }

    public async Task<bool> IsChannelEnabledAsync(
        Guid personId,
        NotificationCategory category,
        NotificationChannel channel,
        CancellationToken cancellationToken = default)
    {
        if (NotificationPreference.IsMandatoryChannel(category, channel))
        {
            return true;
        }

        var pref = await _dbContext.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.PersonId == personId &&
                     p.Category == category &&
                     p.Channel == channel &&
                     p.IsActive,
                cancellationToken);

        return pref?.Enabled ?? NotificationPreference.GetDefaultEnabled(category, channel);
    }

    public async Task<PagedResult<NotificationDto>> GetAdminPagedAsync(
        AdminNotificationQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = _dbContext.NotificationMessages
            .Include(n => n.RecipientPerson)
            .AsNoTracking()
            .Where(n => n.IsActive);

        if (filter.RecipientPersonId.HasValue)
        {
            query = query.Where(n => n.RecipientPersonId == filter.RecipientPersonId.Value);
        }

        if (filter.Category.HasValue)
        {
            query = query.Where(n => n.Category == filter.Category.Value);
        }

        if (filter.Type.HasValue)
        {
            query = query.Where(n => n.Type == filter.Type.Value);
        }

        if (filter.IsRead.HasValue)
        {
            query = query.Where(n => n.IsRead == filter.IsRead.Value);
        }

        if (filter.Channel.HasValue)
        {
            var ch = filter.Channel.Value;
            query = query.Where(n =>
                n.Channel == ch ||
                _dbContext.NotificationDeliveries.Any(d => d.NotificationId == n.Id && d.Channel == ch && d.IsActive));
        }

        if (filter.DeliveryStatus.HasValue)
        {
            var status = filter.DeliveryStatus.Value;
            query = query.Where(n =>
                _dbContext.NotificationDeliveries.Any(d => d.NotificationId == n.Id && d.Status == status && d.IsActive));
        }

        if (filter.FromUtc.HasValue)
        {
            query = query.Where(n => n.SentAtUtc >= filter.FromUtc.Value);
        }

        if (filter.ToUtc.HasValue)
        {
            query = query.Where(n => n.SentAtUtc <= filter.ToUtc.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(n => n.SentAtUtc)
            .ThenByDescending(n => n.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationDto>(
            items.Select(MapToDto).ToList(),
            totalCount,
            page,
            pageSize);
    }

    public async Task<NotificationDto> GetAdminByIdAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.NotificationMessages
            .Include(n => n.RecipientPerson)
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.IsActive, cancellationToken);

        if (message == null)
        {
            throw new NotFoundException("La notificación solicitada no existe.");
        }

        return MapToDto(message);
    }

    public async Task<IReadOnlyList<NotificationDeliveryDto>> GetDeliveriesByNotificationIdAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var notificationExists = await _dbContext.NotificationMessages
            .AsNoTracking()
            .AnyAsync(n => n.Id == notificationId && n.IsActive, cancellationToken);

        if (!notificationExists)
        {
            throw new NotFoundException("La notificación solicitada no existe.");
        }

        var deliveries = await _dbContext.NotificationDeliveries
            .AsNoTracking()
            .Where(d => d.NotificationId == notificationId && d.IsActive)
            .OrderBy(d => d.Channel)
            .ThenByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return deliveries.Select(MapDeliveryToDto).ToList();
    }

    public async Task<PagedResult<NotificationDeliveryDto>> GetFailedDeliveriesPagedAsync(
        int page = 1,
        int pageSize = 20,
        NotificationChannel? channel = null,
        bool excludeProviderNotConfigured = false,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.NotificationDeliveries
            .AsNoTracking()
            .Where(d => d.IsActive &&
                        (d.Status == NotificationDeliveryStatus.Failed ||
                         d.Status == NotificationDeliveryStatus.Skipped));

        if (channel.HasValue)
        {
            query = query.Where(d => d.Channel == channel.Value);
        }

        if (excludeProviderNotConfigured)
        {
            // Fallos esperables en entornos sin proveedor: no requieren revisión entrega por entrega.
            query = query.Where(d => d.LastErrorCode != NotificationErrorCodes.ProviderNotConfigured);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(d => d.LastAttemptAtUtc ?? d.CreatedAtUtc)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationDeliveryDto>(
            items.Select(MapDeliveryToDto).ToList(),
            totalCount,
            safePage,
            safePageSize);
    }

    public async Task<NotificationDeliveryDto> RetryDeliveryAsync(
        Guid deliveryId,
        RetryNotificationDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var validator = new RetryNotificationDeliveryRequestValidator();
        await ValidateAndThrowAsync(validator, request, cancellationToken);

        var delivery = await _dbContext.NotificationDeliveries
            .FirstOrDefaultAsync(d => d.Id == deliveryId && d.IsActive, cancellationToken);

        if (delivery == null)
        {
            throw new NotFoundException("El registro de entrega solicitado no existe.");
        }

        var nowUtc = _gymCalendar.UtcNow;
        var previousStatus = delivery.Status;
        var previousAttempts = delivery.AttemptCount;

        try
        {
            delivery.RequestManualRetry(_currentUserService?.UserId ?? Guid.Empty, request.Reason, nowUtc);
        }
        catch (InvalidOperationException ex)
        {
            throw ConflictException.WithCode(
                NotificationErrorCodes.DeliveryInvalidRetryState,
                ex.Message);
        }

        RecordAudit(
            action: "NOTIFICATION_DELIVERY_MANUAL_RETRY",
            entityName: "NotificationDelivery",
            entityId: delivery.Id.ToString(),
            oldValues: JsonSerializer.Serialize(new { Status = previousStatus.ToString(), AttemptCount = previousAttempts }),
            newValues: JsonSerializer.Serialize(new
            {
                Status = delivery.Status.ToString(),
                Reason = request.Reason?.Trim(),
                RetriedByUserId = _currentUserService?.UserId
            }),
            nowUtc: nowUtc);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapDeliveryToDto(delivery);
    }

    public async Task<IReadOnlyList<NotificationTemplateDto>> GetTemplatesAsync(
        NotificationTemplateQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.NotificationTemplates
            .AsNoTracking()
            .Where(t => t.IsActive);

        if (filter.Type.HasValue)
        {
            query = query.Where(t => t.Type == filter.Type.Value);
        }

        if (filter.Channel.HasValue)
        {
            query = query.Where(t => t.Channel == filter.Channel.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Locale))
        {
            var locale = filter.Locale.Trim();
            query = query.Where(t => t.Locale == locale);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(t => t.Status == filter.Status.Value);
        }

        var list = await query
            .OrderBy(t => t.Type)
            .ThenBy(t => t.Channel)
            .ThenByDescending(t => t.Version)
            .ToListAsync(cancellationToken);

        return list.Select(MapTemplateToDto).ToList();
    }

    public async Task<NotificationTemplateDto> GetTemplateByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _dbContext.NotificationTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.IsActive, cancellationToken);

        if (template == null)
        {
            throw new NotFoundException("La plantilla de notificación solicitada no existe.");
        }

        return MapTemplateToDto(template);
    }

    public async Task<NotificationTemplateDto> CreateTemplateAsync(
        CreateNotificationTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var validator = new CreateNotificationTemplateRequestValidator();
        await ValidateAndThrowAsync(validator, request, cancellationToken);

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var normalizedLocale = string.IsNullOrWhiteSpace(request.Locale) ? "es-AR" : request.Locale.Trim();

        int version;
        if (request.Version.HasValue)
        {
            version = request.Version.Value;
        }
        else
        {
            var maxVersion = await _dbContext.NotificationTemplates
                .AsNoTracking()
                .Where(t => t.Code == normalizedCode && t.Channel == request.Channel && t.Locale == normalizedLocale)
                .Select(t => (int?)t.Version)
                .MaxAsync(cancellationToken);
            version = (maxVersion ?? 0) + 1;
        }

        var duplicateExists = await _dbContext.NotificationTemplates
            .AsNoTracking()
            .AnyAsync(
                t => t.Code == normalizedCode &&
                     t.Channel == request.Channel &&
                     t.Locale == normalizedLocale &&
                     t.Version == version,
                cancellationToken);

        if (duplicateExists)
        {
            throw ConflictException.WithCode(
                NotificationErrorCodes.TemplateDuplicateVersion,
                $"Ya existe la versión {version} para la plantilla '{normalizedCode}' ({request.Channel}, {normalizedLocale}).");
        }

        var nowUtc = _gymCalendar.UtcNow;
        var allowedVariablesCsv = request.AllowedVariables is { Count: > 0 }
            ? string.Join(",", request.AllowedVariables)
            : string.Empty;

        NotificationTemplate template;
        try
        {
            template = NotificationTemplate.Create(
                code: normalizedCode,
                type: request.Type,
                channel: request.Channel,
                locale: normalizedLocale,
                subjectTemplate: request.SubjectTemplate,
                bodyTemplate: request.BodyTemplate,
                allowedVariables: allowedVariablesCsv,
                version: version,
                createdByUserId: _currentUserService?.UserId,
                nowUtc: nowUtc,
                activateImmediately: false);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Template"] = [ex.Message]
            });
        }

        // El índice único de plantilla activa se valida sentencia por sentencia: las hermanas activas
        // deben quedar desactivadas en la base antes de activar ésta, dentro de la misma transacción.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (request.ActivateImmediately)
        {
            await DeactivateActiveTemplatesForSlotAsync(
                template.Type,
                template.Channel,
                template.Locale,
                exceptTemplateId: template.Id,
                nowUtc: nowUtc,
                cancellationToken: cancellationToken);
            template.Activate(nowUtc);
        }

        _dbContext.NotificationTemplates.Add(template);

        RecordAudit(
            action: "NOTIFICATION_TEMPLATE_CREATED",
            entityName: "NotificationTemplate",
            entityId: template.Id.ToString(),
            oldValues: null,
            newValues: JsonSerializer.Serialize(new
            {
                template.Code,
                Type = template.Type.ToString(),
                Channel = template.Channel.ToString(),
                template.Locale,
                template.Version,
                Status = template.Status.ToString()
            }),
            nowUtc: nowUtc);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapTemplateToDto(template);
    }

    public async Task<NotificationTemplateDto> UpdateTemplateAsync(
        Guid id,
        UpdateNotificationTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var validator = new UpdateNotificationTemplateRequestValidator();
        await ValidateAndThrowAsync(validator, request, cancellationToken);

        var template = await _dbContext.NotificationTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.IsActive, cancellationToken);

        if (template == null)
        {
            throw new NotFoundException("La plantilla de notificación solicitada no existe.");
        }

        var nowUtc = _gymCalendar.UtcNow;
        var oldValues = JsonSerializer.Serialize(new
        {
            template.SubjectTemplate,
            template.BodyTemplate,
            AllowedVariables = template.GetAllowedVariableSet().ToArray()
        });

        var allowedVariablesCsv = request.AllowedVariables is { Count: > 0 }
            ? string.Join(",", request.AllowedVariables)
            : template.AllowedVariables;

        try
        {
            template.UpdateContent(
                request.SubjectTemplate,
                request.BodyTemplate,
                allowedVariablesCsv,
                nowUtc);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Template"] = [ex.Message]
            });
        }

        RecordAudit(
            action: "NOTIFICATION_TEMPLATE_UPDATED",
            entityName: "NotificationTemplate",
            entityId: template.Id.ToString(),
            oldValues: oldValues,
            newValues: JsonSerializer.Serialize(new
            {
                template.SubjectTemplate,
                template.BodyTemplate,
                AllowedVariables = template.GetAllowedVariableSet().ToArray()
            }),
            nowUtc: nowUtc);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapTemplateToDto(template);
    }

    public async Task<NotificationTemplateDto> ActivateTemplateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _dbContext.NotificationTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.IsActive, cancellationToken);

        if (template == null)
        {
            throw new NotFoundException("La plantilla de notificación solicitada no existe.");
        }

        var nowUtc = _gymCalendar.UtcNow;
        var previousStatus = template.Status;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await DeactivateActiveTemplatesForSlotAsync(
            template.Type,
            template.Channel,
            template.Locale,
            exceptTemplateId: template.Id,
            nowUtc: nowUtc,
            cancellationToken: cancellationToken);

        template.Activate(nowUtc);

        RecordAudit(
            action: "NOTIFICATION_TEMPLATE_ACTIVATED",
            entityName: "NotificationTemplate",
            entityId: template.Id.ToString(),
            oldValues: JsonSerializer.Serialize(new { Status = previousStatus.ToString() }),
            newValues: JsonSerializer.Serialize(new
            {
                Status = template.Status.ToString(),
                template.Code,
                template.Version
            }),
            nowUtc: nowUtc);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapTemplateToDto(template);
    }

    public async Task<NotificationTemplateDto> DeactivateTemplateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _dbContext.NotificationTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.IsActive, cancellationToken);

        if (template == null)
        {
            throw new NotFoundException("La plantilla de notificación solicitada no existe.");
        }

        var nowUtc = _gymCalendar.UtcNow;
        var previousStatus = template.Status;

        template.Deactivate(nowUtc);

        RecordAudit(
            action: "NOTIFICATION_TEMPLATE_DEACTIVATED",
            entityName: "NotificationTemplate",
            entityId: template.Id.ToString(),
            oldValues: JsonSerializer.Serialize(new { Status = previousStatus.ToString() }),
            newValues: JsonSerializer.Serialize(new
            {
                Status = template.Status.ToString(),
                template.Code,
                template.Version
            }),
            nowUtc: nowUtc);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapTemplateToDto(template);
    }

    private async Task DeactivateActiveTemplatesForSlotAsync(
        NotificationType type,
        NotificationChannel channel,
        string locale,
        Guid exceptTemplateId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var activeSiblings = await _dbContext.NotificationTemplates
            .Where(t => t.IsActive &&
                        t.Id != exceptTemplateId &&
                        t.Type == type &&
                        t.Channel == channel &&
                        t.Locale == locale &&
                        t.Status == NotificationTemplateStatus.Active)
            .ToListAsync(cancellationToken);

        if (activeSiblings.Count == 0)
        {
            return;
        }

        foreach (var sibling in activeSiblings)
        {
            sibling.Deactivate(nowUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private void RecordAudit(
        string action,
        string entityName,
        string entityId,
        string? oldValues,
        string? newValues,
        DateTime nowUtc)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = _currentUserService?.UserId,
            UserEmail = _currentUserService?.Email,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            TimestampUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            IsActive = true
        });
    }

    private static async Task ValidateAndThrowAsync<T>(
        FluentValidation.IValidator<T> validator,
        T instance,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (!result.IsValid)
        {
            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }
    }

    private static NotificationDto MapToDto(NotificationMessage n) =>
        new(
            n.Id,
            n.RecipientPersonId,
            n.RecipientPerson?.FullName ?? string.Empty,
            n.Type,
            n.Channel,
            n.Title,
            n.Body,
            n.IsRead,
            n.SentAtUtc,
            n.Category,
            n.RelatedEntityType,
            n.RelatedEntityId,
            n.ActionUrl,
            n.ReadAtUtc,
            n.ExpiresAtUtc,
            n.MetadataJson,
            n.IdempotencyKey,
            n.CorrelationId
        );

    private static NotificationDeliveryDto MapDeliveryToDto(NotificationDelivery d) =>
        new(
            Id: d.Id,
            NotificationMessageId: d.NotificationId,
            Channel: d.Channel,
            Status: d.Status,
            Destination: d.RecipientAddress,
            ProviderName: d.Provider,
            ProviderMessageId: d.ProviderMessageId,
            AttemptCount: d.AttemptCount,
            MaxAttempts: d.MaxAttempts,
            LastAttemptAtUtc: d.LastAttemptAtUtc,
            NextAttemptAtUtc: d.NextAttemptAtUtc,
            DeliveredAtUtc: d.DeliveredAtUtc ?? d.SentAtUtc,
            ErrorCode: d.LastErrorCode,
            ErrorMessage: d.LastErrorMessage,
            IsPermanentFailure: d.Status == NotificationDeliveryStatus.Failed,
            RetriedByUserId: d.RetriedByUserId,
            CreatedAtUtc: d.CreatedAtUtc
        );

    private static NotificationTemplateDto MapTemplateToDto(NotificationTemplate t) =>
        new(
            Id: t.Id,
            Code: t.Code,
            Type: t.Type,
            Category: t.Type.GetCategory(),
            Channel: t.Channel,
            Locale: t.Locale,
            Version: t.Version,
            Status: t.Status,
            SubjectTemplate: t.SubjectTemplate ?? string.Empty,
            BodyTemplate: t.BodyTemplate,
            PlainTextTemplate: null,
            AllowedVariables: t.GetAllowedVariableSet().ToList(),
            CreatedByUserId: t.CreatedByUserId,
            CreatedAtUtc: t.CreatedAtUtc,
            UpdatedAtUtc: t.UpdatedAtUtc
        );
}
