using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Services;

public class ActivityService : IActivityService
{
    private const string ConcurrentModificationMessage =
        "La actividad fue modificada concurrentemente. Por favor, recargue la información e intente nuevamente.";

    private readonly ISmartGymDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPublicFileStorageService _publicFileStorageService;
    private readonly IImageProcessor _imageProcessor;
    private readonly IAuditService _auditService;
    private readonly ILogger<ActivityService> _logger;
    private readonly IValidator<CreateActivityRequest> _createValidator;
    private readonly IValidator<UpdateActivityRequest> _updateValidator;

    public ActivityService(
        ISmartGymDbContext dbContext,
        ICurrentUserService currentUserService,
        IPublicFileStorageService publicFileStorageService,
        IImageProcessor imageProcessor,
        IAuditService auditService,
        ILogger<ActivityService> logger,
        IValidator<CreateActivityRequest> createValidator,
        IValidator<UpdateActivityRequest> updateValidator)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _publicFileStorageService = publicFileStorageService;
        _imageProcessor = imageProcessor;
        _auditService = auditService;
        _logger = logger;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<PagedResult<ActivityListItemDto>> GetPagedAsync(
        string? search,
        ActivityStatus? status,
        int? age,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var query = _dbContext.Activities
            .Include(a => a.Media)
            .AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }
        else
        {
            // Sin filtro explícito de estado: se excluyen las archivadas (muestra Active e Inactive)
            query = query.Where(a => a.Status != ActivityStatus.Archived);
        }

        if (age.HasValue)
        {
            query = query.Where(a =>
                (!a.MinAge.HasValue || age.Value >= a.MinAge.Value) &&
                (!a.MaxAge.HasValue || age.Value <= a.MaxAge.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedTerm = TextNormalizer.NormalizeForSearch(search);
            var upperCodeTerm = search.Trim().ToUpperInvariant();

            query = query.Where(a =>
                a.NormalizedName.Contains(normalizedTerm) ||
                a.Code.Contains(upperCodeTerm));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(a => a.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(MapToListItemDto).ToList();

        return new PagedResult<ActivityListItemDto>(dtos, totalCount, page, pageSize);
    }

    public async Task<ActivityDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var activity = await _dbContext.Activities
            .Include(a => a.Media)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        return activity == null ? null : MapToDetailDto(activity);
    }

    public async Task<ActivityDetailDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var normalizedCode = Activity.NormalizeCode(code);
        var activity = await _dbContext.Activities
            .Include(a => a.Media)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == normalizedCode, cancellationToken);

        return activity == null ? null : MapToDetailDto(activity);
    }

    public async Task<ActivityDetailDto> CreateAsync(CreateActivityRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var failures = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new SmartGym.Application.Common.Exceptions.ValidationException(failures);
        }

        var normalizedCode = Activity.NormalizeCode(request.Code);
        var codeExists = await _dbContext.Activities
            .AnyAsync(a => a.Code == normalizedCode, cancellationToken);

        if (codeExists)
        {
            throw new ConflictException($"Ya existe una actividad con el código '{normalizedCode}'.");
        }

        var activity = new Activity(
            code: request.Code,
            name: request.Name,
            shortDescription: request.ShortDescription,
            description: request.Description,
            equipmentNotes: request.EquipmentNotes,
            colorHex: request.ColorHex,
            defaultCapacity: request.DefaultCapacity,
            minAge: request.MinAge,
            maxAge: request.MaxAge);

        _dbContext.Activities.Add(activity);
        await SaveChangesMappingConflictsAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "ACTIVITY_CREATED",
            entityName: "Activity",
            entityId: activity.Id.ToString(),
            oldValues: null,
            newValues: JsonSerializer.Serialize(new
            {
                activity.Code,
                activity.Name,
                activity.ShortDescription,
                activity.DefaultCapacity,
                activity.MinAge,
                activity.MaxAge,
                Status = activity.Status.ToString()
            }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        return MapToDetailDto(activity);
    }

    public async Task<ActivityDetailDto> UpdateAsync(Guid id, UpdateActivityRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var failures = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new SmartGym.Application.Common.Exceptions.ValidationException(failures);
        }

        var activity = await _dbContext.Activities
            .Include(a => a.Media)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (activity == null)
        {
            throw new NotFoundException($"Actividad con ID '{id}' no encontrada.");
        }

        if (activity.Version != request.Version)
        {
            throw new ConflictException(ConcurrentModificationMessage);
        }

        var oldSnapshot = new Dictionary<string, object?>
        {
            ["Name"] = activity.Name,
            ["ShortDescription"] = activity.ShortDescription,
            ["Description"] = activity.Description,
            ["EquipmentNotes"] = activity.EquipmentNotes,
            ["ColorHex"] = activity.ColorHex,
            ["DefaultCapacity"] = activity.DefaultCapacity,
            ["MinAge"] = activity.MinAge,
            ["MaxAge"] = activity.MaxAge
        };

        activity.UpdateDetails(
            name: request.Name,
            shortDescription: request.ShortDescription,
            description: request.Description,
            equipmentNotes: request.EquipmentNotes,
            colorHex: request.ColorHex,
            defaultCapacity: request.DefaultCapacity,
            minAge: request.MinAge,
            maxAge: request.MaxAge);

        await SaveChangesMappingConflictsAsync(cancellationToken);

        // Registrar solo los campos modificados en la auditoría
        var changedOldValues = new Dictionary<string, object?>();
        var changedNewValues = new Dictionary<string, object?>();

        var newSnapshot = new Dictionary<string, object?>
        {
            ["Name"] = activity.Name,
            ["ShortDescription"] = activity.ShortDescription,
            ["Description"] = activity.Description,
            ["EquipmentNotes"] = activity.EquipmentNotes,
            ["ColorHex"] = activity.ColorHex,
            ["DefaultCapacity"] = activity.DefaultCapacity,
            ["MinAge"] = activity.MinAge,
            ["MaxAge"] = activity.MaxAge
        };

        foreach (var key in oldSnapshot.Keys)
        {
            if (!Equals(oldSnapshot[key], newSnapshot[key]))
            {
                changedOldValues[key] = oldSnapshot[key];
                changedNewValues[key] = newSnapshot[key];
            }
        }

        if (changedNewValues.Count > 0)
        {
            await _auditService.LogAsync(
                action: "ACTIVITY_UPDATED",
                entityName: "Activity",
                entityId: activity.Id.ToString(),
                oldValues: JsonSerializer.Serialize(changedOldValues),
                newValues: JsonSerializer.Serialize(changedNewValues),
                userId: _currentUserService.UserId,
                userEmail: _currentUserService.Email,
                ipAddress: null,
                cancellationToken: cancellationToken);
        }

        return MapToDetailDto(activity);
    }

    public async Task<bool> ChangeStatusAsync(Guid id, ChangeActivityStatusRequest request, CancellationToken cancellationToken = default)
    {
        var activity = await _dbContext.Activities.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (activity == null)
        {
            throw new NotFoundException($"Actividad con ID '{id}' no encontrada.");
        }

        if (activity.Status == request.Status)
        {
            return false;
        }

        var oldStatus = activity.Status;

        // Guarda de inactivación (Active -> Inactive): verificar horarios vigentes y clases futuras no terminales
        if (oldStatus == ActivityStatus.Active && request.Status == ActivityStatus.Inactive)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var activeSchedulesCount = await _dbContext.RecurringSchedules
                .CountAsync(s => s.ActivityId == id && s.IsActive && (!s.ValidTo.HasValue || s.ValidTo.Value >= today), cancellationToken);

            var futureSessionsCount = await _dbContext.ClassSessions
                .CountAsync(cs => cs.ActivityId == id && cs.Date >= today &&
                    (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.InProgress), cancellationToken);

            if (activeSchedulesCount > 0 || futureSessionsCount > 0)
            {
                var deps = new ActivityDependenciesDto(
                    ActiveSchedules: activeSchedulesCount,
                    FutureSessions: futureSessionsCount,
                    TotalSchedules: activeSchedulesCount,
                    TotalSessions: futureSessionsCount,
                    MembershipPlans: 0);

                throw new ConflictException(
                    "No se puede inactivar la actividad porque posee horarios vigentes o clases futuras programadas.",
                    deps);
            }
        }

        if (activity.Version != request.Version)
        {
            throw new ConflictException(ConcurrentModificationMessage);
        }

        activity.ChangeStatus(request.Status);

        await SaveChangesMappingConflictsAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "ACTIVITY_STATUS_CHANGED",
            entityName: "Activity",
            entityId: activity.Id.ToString(),
            oldValues: JsonSerializer.Serialize(new { Status = oldStatus.ToString() }),
            newValues: JsonSerializer.Serialize(new { Status = activity.Status.ToString() }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        return true;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var activity = await _dbContext.Activities
            .Include(a => a.Media)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (activity == null)
        {
            throw new NotFoundException($"Actividad con ID '{id}' no encontrada.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var activeSchedules = await _dbContext.RecurringSchedules
            .CountAsync(s => s.ActivityId == id && s.IsActive && (!s.ValidTo.HasValue || s.ValidTo.Value >= today), cancellationToken);

        var totalSchedules = await _dbContext.RecurringSchedules
            .CountAsync(s => s.ActivityId == id, cancellationToken);

        var futureSessions = await _dbContext.ClassSessions
            .CountAsync(cs => cs.ActivityId == id && cs.Date >= today &&
                (cs.Status == ClassSessionStatus.Scheduled || cs.Status == ClassSessionStatus.InProgress), cancellationToken);

        var totalSessions = await _dbContext.ClassSessions
            .CountAsync(cs => cs.ActivityId == id, cancellationToken);

        var membershipPlansCount = await _dbContext.MembershipPlanActivities
            .CountAsync(mpa => mpa.ActivityId == id, cancellationToken);

        if (totalSchedules > 0 || totalSessions > 0 || membershipPlansCount > 0)
        {
            var deps = new ActivityDependenciesDto(
                ActiveSchedules: activeSchedules,
                FutureSessions: futureSessions,
                TotalSchedules: totalSchedules,
                TotalSessions: totalSessions,
                MembershipPlans: membershipPlansCount);

            throw new ConflictException(
                "No se puede eliminar la actividad porque posee historial de horarios, clases o vinculación con planes de membresía. Considere inactivarla o archivarla.",
                deps);
        }

        var mediaKeysToDelete = activity.Media.Select(m => m.ObjectKey).ToList();

        _dbContext.Activities.Remove(activity);
        await SaveChangesMappingConflictsAsync(cancellationToken);

        // Post-commit: borrar objetos del bucket en modo best-effort
        foreach (var key in mediaKeysToDelete)
        {
            try
            {
                await _publicFileStorageService.DeleteFileAsync(key, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al eliminar el objeto '{ObjectKey}' del storage tras eliminar la actividad '{ActivityId}'.", key, id);
            }
        }

        await _auditService.LogAsync(
            action: "ACTIVITY_DELETED",
            entityName: "Activity",
            entityId: id.ToString(),
            oldValues: JsonSerializer.Serialize(new { activity.Code, activity.Name }),
            newValues: null,
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);
    }

    public async Task<List<ActivityMediaDto>> GetMediaAsync(Guid activityId, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Activities.AnyAsync(a => a.Id == activityId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException($"Actividad con ID '{activityId}' no encontrada.");
        }

        var mediaList = await _dbContext.ActivityMedias
            .Where(m => m.ActivityId == activityId && m.IsActive)
            .OrderBy(m => m.Type)
            .ThenBy(m => m.SortOrder)
            .ToListAsync(cancellationToken);

        return mediaList.Select(MapToMediaDto).ToList();
    }

    public async Task<ActivityMediaDto> UploadMediaAsync(
        Guid activityId,
        Stream fileStream,
        string fileName,
        string contentType,
        long sizeBytes,
        ActivityMediaType type,
        CancellationToken cancellationToken = default)
    {
        var activity = await _dbContext.Activities
            .Include(a => a.Media)
            .FirstOrDefaultAsync(a => a.Id == activityId, cancellationToken);

        if (activity == null)
        {
            throw new NotFoundException($"Actividad con ID '{activityId}' no encontrada.");
        }

        var profile = type == ActivityMediaType.Logo
            ? ImageProcessingProfile.ActivityLogo
            : ImageProcessingProfile.ActivityGallery;

        if (type == ActivityMediaType.GalleryImage)
        {
            var galleryCount = activity.Media.Count(m => m.Type == ActivityMediaType.GalleryImage && m.IsActive);
            if (galleryCount >= Activity.MaxGalleryImages)
            {
                throw new ArgumentException($"La actividad ya ha alcanzado el límite máximo de {Activity.MaxGalleryImages} imágenes de galería.");
            }
        }

        // 1. Procesamiento de imagen a WebP
        var processed = await _imageProcessor.ProcessImageAsync(fileStream, contentType, sizeBytes, profile, cancellationToken);

        // 2. Subida a almacenamiento público
        var folder = type == ActivityMediaType.Logo ? $"activities/{activityId}/logo" : $"activities/{activityId}/gallery";
        var objectKey = await _publicFileStorageService.UploadFileAsync(
            processed.Stream,
            $"{Guid.NewGuid():N}.webp",
            "image/webp",
            folder,
            cancellationToken);

        string? previousLogoKeyToDelete = null;
        ActivityMedia media;

        // La compensación cubre solo la persistencia: tras el commit, la fila ya referencia el objeto subido
        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (type == ActivityMediaType.Logo)
            {
                var existingLogo = activity.Media.FirstOrDefault(m => m.Type == ActivityMediaType.Logo && m.IsActive);
                if (existingLogo != null)
                {
                    // El índice único de logo se verifica fila por fila: el previo se borra en un guardado propio
                    previousLogoKeyToDelete = existingLogo.ObjectKey;
                    _dbContext.ActivityMedias.Remove(existingLogo);
                    await SaveChangesMappingConflictsAsync(cancellationToken);
                }

                media = new ActivityMedia(
                    activityId: activityId,
                    type: ActivityMediaType.Logo,
                    objectKey: objectKey,
                    originalFileName: fileName,
                    contentType: "image/webp",
                    sizeBytes: processed.SizeInBytes,
                    width: processed.Width,
                    height: processed.Height,
                    sortOrder: 0);
            }
            else
            {
                var currentGallery = activity.Media.Where(m => m.Type == ActivityMediaType.GalleryImage && m.IsActive).ToList();
                var nextSortOrder = currentGallery.Count == 0 ? 1 : currentGallery.Max(m => m.SortOrder) + 1;
                var isFirstImage = currentGallery.Count == 0;

                media = new ActivityMedia(
                    activityId: activityId,
                    type: ActivityMediaType.GalleryImage,
                    objectKey: objectKey,
                    originalFileName: fileName,
                    contentType: "image/webp",
                    sizeBytes: processed.SizeInBytes,
                    width: processed.Width,
                    height: processed.Height,
                    sortOrder: nextSortOrder);

                if (isFirstImage)
                {
                    media.SetPrimary(true);
                }
            }

            _dbContext.ActivityMedias.Add(media);
            await SaveChangesMappingConflictsAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Compensación: Si falló la inserción en base de datos, borrar el objeto del storage
            try
            {
                await _publicFileStorageService.DeleteFileAsync(objectKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error compensatorio al borrar el objeto subido '{ObjectKey}'.", objectKey);
            }
            throw;
        }

        // Post-commit: borrar el logo previo si se reemplazó
        if (previousLogoKeyToDelete != null)
        {
            try
            {
                await _publicFileStorageService.DeleteFileAsync(previousLogoKeyToDelete, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al eliminar el logo previo '{ObjectKey}' tras reemplazo.", previousLogoKeyToDelete);
            }
        }

        await _auditService.LogAsync(
            action: "ACTIVITY_MEDIA_ADDED",
            entityName: "Activity",
            entityId: activityId.ToString(),
            oldValues: null,
            newValues: JsonSerializer.Serialize(new
            {
                MediaId = media.Id,
                Type = media.Type.ToString(),
                media.ObjectKey,
                media.SortOrder,
                media.IsPrimary
            }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);

        return MapToMediaDto(media);
    }

    public async Task DeleteMediaAsync(Guid activityId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var media = await _dbContext.ActivityMedias
            .FirstOrDefaultAsync(m => m.Id == mediaId && m.ActivityId == activityId && m.IsActive, cancellationToken);

        if (media == null)
        {
            throw new NotFoundException($"Medio con ID '{mediaId}' no encontrado en la actividad especificada.");
        }

        var objectKeyToDelete = media.ObjectKey;
        var wasPrimary = media.IsPrimary;
        var mediaType = media.Type;

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // El índice único de imagen principal se verifica fila por fila: primero se libera, luego se promueve
            _dbContext.ActivityMedias.Remove(media);
            await SaveChangesMappingConflictsAsync(cancellationToken);

            // Si se elimina la imagen principal, promover la de menor SortOrder
            if (wasPrimary && mediaType == ActivityMediaType.GalleryImage)
            {
                var nextPrimary = await _dbContext.ActivityMedias
                    .Where(m => m.ActivityId == activityId && m.Id != mediaId && m.Type == ActivityMediaType.GalleryImage && m.IsActive)
                    .OrderBy(m => m.SortOrder)
                    .FirstOrDefaultAsync(cancellationToken);

                if (nextPrimary != null)
                {
                    nextPrimary.SetPrimary(true);
                    await SaveChangesMappingConflictsAsync(cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // Post-commit: borrar del bucket
        try
        {
            await _publicFileStorageService.DeleteFileAsync(objectKeyToDelete, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al borrar el medio '{ObjectKey}' del storage.", objectKeyToDelete);
        }

        await _auditService.LogAsync(
            action: "ACTIVITY_MEDIA_DELETED",
            entityName: "Activity",
            entityId: activityId.ToString(),
            oldValues: JsonSerializer.Serialize(new { MediaId = mediaId, Type = mediaType.ToString(), WasPrimary = wasPrimary }),
            newValues: null,
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);
    }

    public async Task SetPrimaryMediaAsync(Guid activityId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var targetMedia = await _dbContext.ActivityMedias
            .FirstOrDefaultAsync(m => m.Id == mediaId && m.ActivityId == activityId && m.IsActive, cancellationToken);

        if (targetMedia == null)
        {
            throw new NotFoundException($"Medio con ID '{mediaId}' no encontrado en la actividad especificada.");
        }

        if (targetMedia.Type != ActivityMediaType.GalleryImage)
        {
            throw new ArgumentException("Solo una imagen de galería puede establecerse como imagen principal.");
        }

        if (targetMedia.IsPrimary)
        {
            return;
        }

        var currentPrimary = await _dbContext.ActivityMedias
            .Where(m => m.ActivityId == activityId && m.Type == ActivityMediaType.GalleryImage && m.IsPrimary && m.IsActive)
            .ToListAsync(cancellationToken);

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // El índice único de imagen principal se verifica fila por fila: primero se libera, luego se toma
            foreach (var p in currentPrimary)
            {
                p.SetPrimary(false);
            }

            await SaveChangesMappingConflictsAsync(cancellationToken);

            targetMedia.SetPrimary(true);
            await SaveChangesMappingConflictsAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        await _auditService.LogAsync(
            action: "ACTIVITY_MEDIA_PRIMARY_CHANGED",
            entityName: "Activity",
            entityId: activityId.ToString(),
            oldValues: null,
            newValues: JsonSerializer.Serialize(new { NewPrimaryMediaId = mediaId }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);
    }

    public async Task SortMediaAsync(Guid activityId, SortActivityMediaRequest request, CancellationToken cancellationToken = default)
    {
        var galleryMedias = await _dbContext.ActivityMedias
            .Where(m => m.ActivityId == activityId && m.Type == ActivityMediaType.GalleryImage && m.IsActive)
            .ToListAsync(cancellationToken);

        var existingIds = galleryMedias.Select(m => m.Id).ToHashSet();
        var incomingIds = request.MediaIds.ToHashSet();

        if (existingIds.Count != incomingIds.Count || !existingIds.SetEquals(incomingIds))
        {
            throw new ArgumentException("La lista de IDs enviada no coincide exactamente con el conjunto de imágenes de galería de la actividad.");
        }

        for (int i = 0; i < request.MediaIds.Count; i++)
        {
            var id = request.MediaIds[i];
            var media = galleryMedias.First(m => m.Id == id);
            media.SetSortOrder(i + 1);
        }

        await SaveChangesMappingConflictsAsync(cancellationToken);

        await _auditService.LogAsync(
            action: "ACTIVITY_MEDIA_REORDERED",
            entityName: "Activity",
            entityId: activityId.ToString(),
            oldValues: null,
            newValues: JsonSerializer.Serialize(new { SortedMediaIds = request.MediaIds }),
            userId: _currentUserService.UserId,
            userEmail: _currentUserService.Email,
            ipAddress: null,
            cancellationToken: cancellationToken);
    }

    public async Task<List<PublicActivityDto>> GetPublicCatalogAsync(int? age = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Activities
            .Include(a => a.Media)
            .AsNoTracking()
            .Where(a => a.Status == ActivityStatus.Active);

        if (age.HasValue)
        {
            query = query.Where(a =>
                (!a.MinAge.HasValue || age.Value >= a.MinAge.Value) &&
                (!a.MaxAge.HasValue || age.Value <= a.MaxAge.Value));
        }

        var list = await query
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        return list.Select(MapToPublicDto).ToList();
    }

    public async Task<PublicActivityDto?> GetPublicByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var normalizedCode = Activity.NormalizeCode(code);
        var activity = await _dbContext.Activities
            .Include(a => a.Media)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == normalizedCode && a.Status == ActivityStatus.Active, cancellationToken);

        return activity == null ? null : MapToPublicDto(activity);
    }

    private ActivityListItemDto MapToListItemDto(Activity a)
    {
        var logo = a.Media.FirstOrDefault(m => m.Type == ActivityMediaType.Logo && m.IsActive);
        var primaryImage = a.Media.FirstOrDefault(m => m.Type == ActivityMediaType.GalleryImage && m.IsPrimary && m.IsActive);

        return new ActivityListItemDto(
            Id: a.Id,
            Code: a.Code,
            Name: a.Name,
            ShortDescription: a.ShortDescription,
            ColorHex: a.ColorHex,
            DefaultCapacity: a.DefaultCapacity,
            MinAge: a.MinAge,
            MaxAge: a.MaxAge,
            Status: a.Status,
            LogoUrl: logo != null ? _publicFileStorageService.GetPublicUrl(logo.ObjectKey) : null,
            PrimaryImageUrl: primaryImage != null ? _publicFileStorageService.GetPublicUrl(primaryImage.ObjectKey) : null,
            CreatedAtUtc: a.CreatedAtUtc
        );
    }

    private ActivityDetailDto MapToDetailDto(Activity a)
    {
        var mediaDtos = a.Media
            .Where(m => m.IsActive)
            .OrderBy(m => m.Type)
            .ThenBy(m => m.SortOrder)
            .Select(MapToMediaDto)
            .ToList();

        return new ActivityDetailDto(
            Id: a.Id,
            Code: a.Code,
            Name: a.Name,
            ShortDescription: a.ShortDescription,
            Description: a.Description,
            EquipmentNotes: a.EquipmentNotes,
            ColorHex: a.ColorHex,
            DefaultCapacity: a.DefaultCapacity,
            MinAge: a.MinAge,
            MaxAge: a.MaxAge,
            Status: a.Status,
            Version: a.Version,
            Media: mediaDtos,
            CreatedAtUtc: a.CreatedAtUtc,
            UpdatedAtUtc: a.UpdatedAtUtc
        );
    }

    private PublicActivityDto MapToPublicDto(Activity a)
    {
        var logo = a.Media.FirstOrDefault(m => m.Type == ActivityMediaType.Logo && m.IsActive);
        var primaryImage = a.Media.FirstOrDefault(m => m.Type == ActivityMediaType.GalleryImage && m.IsPrimary && m.IsActive);
        var galleryUrls = a.Media
            .Where(m => m.Type == ActivityMediaType.GalleryImage && m.IsActive)
            .OrderBy(m => m.SortOrder)
            .Select(m => _publicFileStorageService.GetPublicUrl(m.ObjectKey))
            .ToList();

        return new PublicActivityDto(
            Code: a.Code,
            Name: a.Name,
            ShortDescription: a.ShortDescription,
            Description: a.Description,
            EquipmentNotes: a.EquipmentNotes,
            ColorHex: a.ColorHex,
            MinAge: a.MinAge,
            MaxAge: a.MaxAge,
            LogoUrl: logo != null ? _publicFileStorageService.GetPublicUrl(logo.ObjectKey) : null,
            PrimaryImageUrl: primaryImage != null ? _publicFileStorageService.GetPublicUrl(primaryImage.ObjectKey) : null,
            GalleryImageUrls: galleryUrls
        );
    }

    private ActivityMediaDto MapToMediaDto(ActivityMedia m)
    {
        return new ActivityMediaDto(
            Id: m.Id,
            Type: m.Type,
            Url: _publicFileStorageService.GetPublicUrl(m.ObjectKey),
            OriginalFileName: m.OriginalFileName,
            ContentType: m.ContentType,
            SizeBytes: m.SizeBytes,
            Width: m.Width,
            Height: m.Height,
            SortOrder: m.SortOrder,
            IsPrimary: m.IsPrimary,
            CreatedAtUtc: m.CreatedAtUtc
        );
    }

    private async Task SaveChangesMappingConflictsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException(ConcurrentModificationMessage, ex);
        }
        catch (DbUpdateException ex)
        {
            if (IsUniqueViolationOf(ex, ActivityConstraintNames.CodeUnique))
            {
                throw new ConflictException("Ya existe una actividad con el código especificado.", ex);
            }

            if (IsUniqueViolationOf(ex, ActivityConstraintNames.SingleLogo))
            {
                throw new ConflictException("La actividad ya tiene un logo; reintente la operación.", ex);
            }

            if (IsUniqueViolationOf(ex, ActivityConstraintNames.SinglePrimaryImage))
            {
                throw new ConflictException("La actividad ya tiene una imagen principal; reintente la operación.", ex);
            }

            throw;
        }
    }

    // Application no depende de Npgsql: la restricción se identifica por su nombre en el mensaje de la
    // excepción del proveedor (23505: duplicate key value violates unique constraint "IX_...").
    private static bool IsUniqueViolationOf(DbUpdateException ex, string constraintName)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current.Message.Contains($"\"{constraintName}\"", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
