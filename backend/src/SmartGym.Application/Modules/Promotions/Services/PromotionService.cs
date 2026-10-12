using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Validators;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;
using ValidationException = SmartGym.Application.Common.Exceptions.ValidationException;

namespace SmartGym.Application.Modules.Promotions.Services;

public class PromotionService : IPromotionService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly SmartGym.Application.Common.IGymCalendar _gymCalendar;
    private readonly ICurrentUserService? _currentUserService;
    private readonly IValidator<CreatePromotionRequest> _createValidator;
    private readonly IValidator<UpdatePromotionRequest> _updateValidator;

    public PromotionService(
        ISmartGymDbContext dbContext,
        SmartGym.Application.Common.IGymCalendar? gymCalendar = null,
        ICurrentUserService? currentUserService = null,
        IValidator<CreatePromotionRequest>? createValidator = null,
        IValidator<UpdatePromotionRequest>? updateValidator = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar ?? new SmartGym.Application.Common.GymCalendar(
            Microsoft.Extensions.Options.Options.Create(new SmartGym.Application.Common.GymOptions()));
        _currentUserService = currentUserService;
        _createValidator = createValidator ?? new CreatePromotionRequestValidator();
        _updateValidator = updateValidator ?? new UpdatePromotionRequestValidator();
    }

    // ---- Consultas ----

    public async Task<List<PromotionDto>> GetAllAsync(
        bool onlyActive = false,
        PromotionStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Promotions
            .Include(p => p.ApplicablePlan)
            .AsNoTracking();

        if (onlyActive)
        {
            var today = _gymCalendar.Today;
            query = query.Where(p =>
                p.IsActive &&
                p.Status == PromotionStatus.Active &&
                p.ValidFrom <= today &&
                (!p.ValidTo.HasValue || today <= p.ValidTo.Value));
        }

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        var list = await query
            .OrderBy(p => p.Code)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<PromotionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var promo = await _dbContext.Promotions
            .Include(p => p.ApplicablePlan)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return promo == null ? null : MapToDto(promo);
    }

    public async Task<PromotionDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeCode(code);
        var promo = await _dbContext.Promotions
            .Include(p => p.ApplicablePlan)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code.ToUpper() == normalizedCode, cancellationToken);

        return promo == null ? null : MapToDto(promo);
    }

    // ---- Administración ----

    public async Task<PromotionDto> CreateAsync(CreatePromotionRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(_createValidator, request, cancellationToken);

        var code = NormalizeCode(request.Code);
        await EnsureCodeIsFreeAsync(code, exceptPromotionId: null, cancellationToken);
        await EnsurePlanIsAvailableAsync(request.ApplicablePlanId, cancellationToken);

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ConditionType = request.ConditionType,
            BenefitType = request.BenefitType,
            BenefitValue = request.BenefitValue,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            MaxUsesTotal = request.MaxUsesTotal,
            MaxUsesPerPerson = request.MaxUsesPerPerson,
            ApplicablePlanId = request.ApplicablePlanId,
            Status = PromotionStatus.Active,
            CreatedAtUtc = _gymCalendar.UtcNow,
            IsActive = true
        };

        _dbContext.Promotions.Add(promo);
        AddAuditLog("PROMOTION_CREATED", nameof(Promotion), promo.Id, oldValues: null, newValues: Snapshot(promo));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(promo.Id, cancellationToken))!;
    }

    public async Task<PromotionDto> UpdateAsync(Guid id, UpdatePromotionRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(_updateValidator, request, cancellationToken);

        var promo = await FindByIdForUpdateAsync(id, cancellationToken);

        if (promo.Status == PromotionStatus.Archived)
        {
            throw ConflictException.WithCode(PromotionErrorCodes.LockedFields, "No se puede editar una promoción archivada.");
        }

        if (promo.Version != request.Version)
        {
            throw ConcurrencyConflict();
        }

        var code = NormalizeCode(request.Code);
        if (code != promo.Code)
        {
            await EnsureCodeIsFreeAsync(code, exceptPromotionId: promo.Id, cancellationToken);
        }

        if (request.ApplicablePlanId != promo.ApplicablePlanId)
        {
            await EnsurePlanIsAvailableAsync(request.ApplicablePlanId, cancellationToken);
        }

        // Los canjes anulados también fijan el significado de la promoción
        var hasRedemptions = await _dbContext.PromotionRedemptions
            .AnyAsync(r => r.PromotionId == promo.Id, cancellationToken);

        var activeUsesPerPerson = await _dbContext.PromotionRedemptions
            .Where(r => r.PromotionId == promo.Id && r.IsActive)
            .GroupBy(r => r.PersonId)
            .Select(g => g.Count())
            .ToListAsync(cancellationToken);

        var oldValues = Snapshot(promo);

        try
        {
            promo.Update(
                code,
                request.Name.Trim(),
                request.Description?.Trim(),
                request.ConditionType,
                request.BenefitType,
                request.BenefitValue,
                request.ValidFrom,
                request.ValidTo,
                request.MaxUsesTotal,
                request.MaxUsesPerPerson,
                request.ApplicablePlanId,
                hasRedemptions,
                maxActiveRedemptionsPerPerson: activeUsesPerPerson.Count == 0 ? 0 : activeUsesPerPerson.Max(),
                _gymCalendar.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw ConflictException.WithCode(PromotionErrorCodes.LockedFields, ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw DomainValidation(ex);
        }

        AddAuditLog("PROMOTION_UPDATED", nameof(Promotion), promo.Id, oldValues, Snapshot(promo));
        await SaveChangesMappingConcurrencyAsync(cancellationToken);

        return (await GetByIdAsync(promo.Id, cancellationToken))!;
    }

    public Task<PromotionDto> PauseAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, (promo, nowUtc) => promo.Pause(nowUtc), "PROMOTION_PAUSED", cancellationToken);

    public Task<PromotionDto> ActivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, (promo, nowUtc) => promo.Activate(nowUtc), "PROMOTION_ACTIVATED", cancellationToken);

    public Task<PromotionDto> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, (promo, nowUtc) => promo.Archive(nowUtc), "PROMOTION_ARCHIVED", cancellationToken);

    private async Task<PromotionDto> ChangeStatusAsync(
        Guid id,
        Action<Promotion, DateTime> transition,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var promo = await FindByIdForUpdateAsync(id, cancellationToken);
        var previousStatus = promo.Status;

        try
        {
            transition(promo, _gymCalendar.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw ConflictException.WithCode(PromotionErrorCodes.InvalidTransition, ex.Message);
        }

        AddAuditLog(
            auditAction,
            nameof(Promotion),
            promo.Id,
            oldValues: new { Status = previousStatus.ToString() },
            newValues: new { Status = promo.Status.ToString() });
        await SaveChangesMappingConcurrencyAsync(cancellationToken);

        return (await GetByIdAsync(promo.Id, cancellationToken))!;
    }

    // ---- Evaluación ----

    public async Task<PromotionApplicationResultDto> PreviewForMembershipAsync(
        EvaluatePromotionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureStaffOrOwner(request.PersonId);

        // El precio base es el del plan: nunca se acepta del cliente
        var plan = await _dbContext.MembershipPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.MembershipPlanId && p.IsActive, cancellationToken);

        if (plan == null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(EvaluatePromotionRequest.MembershipPlanId)] = ["El plan de membresía especificado no existe o no se encuentra activo."]
            });
        }

        return await EvaluatePromotionAsync(
            new ApplyPromotionRequest(request.Code, request.PersonId, plan.Id, plan.Price),
            cancellationToken);
    }

    public async Task<PromotionApplicationResultDto> EvaluatePromotionAsync(ApplyPromotionRequest request, CancellationToken cancellationToken = default)
    {
        var promo = await ResolveForMembershipAsync(request, lockForRedemption: false, cancellationToken);
        return MembershipResult(promo, request.BasePrice);
    }

    public async Task<PromotionRedemptionDto> RedeemPromotionAsync(
        ApplyPromotionRequest request,
        Guid? membershipId,
        CancellationToken cancellationToken = default)
    {
        await using var ownTransaction = await BeginOwnTransactionAsync(cancellationToken);

        var promo = await ResolveForMembershipAsync(request, lockForRedemption: true, cancellationToken);
        var eval = MembershipResult(promo, request.BasePrice);

        var redemption = AddRedemption(promo, request.PersonId, membershipId, reservationId: null, eval);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (ownTransaction != null)
        {
            await ownTransaction.CommitAsync(cancellationToken);
        }

        return await MapRedemptionAsync(redemption, promo.Code, cancellationToken);
    }

    public async Task<PromotionApplicationResultDto> EvaluatePromotionForReservationAsync(
        Guid studentId,
        Guid classSessionId,
        Guid activityId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var promo = await ResolveForReservationAsync(studentId, classSessionId, code, lockForRedemption: false, cancellationToken);
        return ReservationResult(promo);
    }

    public async Task<PromotionRedemptionDto> RedeemPromotionForReservationAsync(
        Guid studentId,
        Guid classSessionId,
        Guid activityId,
        string code,
        Guid reservationId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.PromotionRedemptions
            .Include(r => r.Promotion)
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId && r.IsActive, cancellationToken);

        if (existing != null)
        {
            return await MapRedemptionAsync(existing, existing.Promotion?.Code ?? NormalizeCode(code), cancellationToken);
        }

        // Dentro de la transacción de la reserva, el canje se guarda junto con ella
        await using var ownTransaction = await BeginOwnTransactionAsync(cancellationToken);

        var promo = await ResolveForReservationAsync(studentId, classSessionId, code, lockForRedemption: true, cancellationToken);
        var redemption = AddRedemption(promo, studentId, membershipId: null, reservationId, ReservationResult(promo));

        if (ownTransaction != null)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await ownTransaction.CommitAsync(cancellationToken);
        }

        return await MapRedemptionAsync(redemption, promo.Code, cancellationToken);
    }

    // ---- Canjes ----

    public async Task AttachRedemptionToMembershipAsync(
        Guid redemptionId,
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        var redemption = await _dbContext.PromotionRedemptions
            .FirstOrDefaultAsync(r => r.Id == redemptionId, cancellationToken)
            ?? throw new NotFoundException("El canje promocional especificado no existe.");

        redemption.MembershipId = membershipId;
        redemption.UpdatedAtUtc = _gymCalendar.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task VoidRedemptionAsync(
        Guid redemptionId,
        CancellationToken cancellationToken = default)
    {
        var redemption = await _dbContext.PromotionRedemptions
            .FirstOrDefaultAsync(r => r.Id == redemptionId, cancellationToken)
            ?? throw new NotFoundException("El canje promocional especificado no existe.");

        await using var ownTransaction = await BeginOwnTransactionAsync(cancellationToken);

        // El bloqueo serializa la anulación con los canjes y con otras anulaciones del mismo canje
        var promo = await _dbContext.Promotions.FirstAsync(p => p.Id == redemption.PromotionId, cancellationToken);
        await LockAndRefreshAsync(promo, cancellationToken);
        await RefreshAsync(_dbContext.PromotionRedemptions.Entry(redemption), cancellationToken);

        if (redemption.IsActive)
        {
            redemption.IsActive = false;
            redemption.UpdatedAtUtc = _gymCalendar.UtcNow;
            promo.DecrementUse();

            AddAuditLog(
                "PROMOTION_REDEMPTION_VOIDED",
                nameof(PromotionRedemption),
                redemption.Id,
                oldValues: new { IsActive = true },
                newValues: RedemptionSnapshot(redemption));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (ownTransaction != null)
        {
            await ownTransaction.CommitAsync(cancellationToken);
        }
    }

    public async Task<List<PromotionRedemptionDto>> GetRedemptionsByPersonAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        EnsureStaffOrOwner(personId);

        return await ProjectRedemptions(
                _dbContext.PromotionRedemptions
                    .AsNoTracking()
                    .Where(r => r.PersonId == personId && r.IsActive)
                    .OrderByDescending(r => r.RedeemedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<PromotionRedemptionDto>> GetRedemptionsByPromotionAsync(
        Guid promotionId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var promotionExists = await _dbContext.Promotions.AnyAsync(p => p.Id == promotionId, cancellationToken);
        if (!promotionExists)
        {
            throw PromotionNotFound();
        }

        var query = _dbContext.PromotionRedemptions
            .AsNoTracking()
            .Where(r => r.PromotionId == promotionId);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await ProjectRedemptions(
                query
                    .OrderByDescending(r => r.RedeemedAtUtc)
                    .ThenBy(r => r.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<PromotionRedemptionDto>(items, totalCount, page, pageSize);
    }

    // ---- Resolución de la promoción aplicable ----

    private async Task<Promotion> ResolveForMembershipAsync(
        ApplyPromotionRequest request,
        bool lockForRedemption,
        CancellationToken cancellationToken)
    {
        var promo = await FindApplicablePromotionAsync(request.Code, additionalDate: null, lockForRedemption, cancellationToken);

        if (promo.ApplicablePlanId.HasValue && request.MembershipPlanId.HasValue && promo.ApplicablePlanId.Value != request.MembershipPlanId.Value)
        {
            throw NotApplicable("La promoción no es aplicable para el plan seleccionado.");
        }

        await EnsureEligibleAsync(promo, request.PersonId, cancellationToken);
        return promo;
    }

    private async Task<Promotion> ResolveForReservationAsync(
        Guid studentId,
        Guid classSessionId,
        string code,
        bool lockForRedemption,
        CancellationToken cancellationToken)
    {
        DateOnly? sessionDate = null;

        if (classSessionId != Guid.Empty)
        {
            var session = await _dbContext.ClassSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(cs => cs.Id == classSessionId && cs.IsActive, cancellationToken);
            if (session != null)
            {
                sessionDate = DateOnly.FromDateTime(_gymCalendar.ToLocal(session.StartsAtUtc).DateTime);
            }
        }

        var promo = await FindApplicablePromotionAsync(code, sessionDate, lockForRedemption, cancellationToken);

        if (promo.ApplicablePlanId.HasValue)
        {
            throw NotApplicable("La promoción es exclusiva para la contratación de un plan de membresía.");
        }

        var coversFullAccess =
            (promo.BenefitType == PromotionBenefitType.PercentageDiscount && promo.BenefitValue >= 100m) ||
            (promo.BenefitType == PromotionBenefitType.ExtraCredits && promo.BenefitValue >= 1m);

        if (!coversFullAccess)
        {
            throw NotApplicable("La promoción no cubre el acceso completo a la clase.");
        }

        await EnsureEligibleAsync(promo, studentId, cancellationToken);
        return promo;
    }

    /// <summary>
    /// Busca la promoción por código y verifica lo que no depende de la persona: estado, vigencia en la fecha
    /// local del gimnasio (y en <paramref name="additionalDate"/>, si se indica) y que su condición sea verificable.
    /// Con <paramref name="lockForRedemption"/> bloquea su fila antes de verificar, de modo que los topes que se
    /// lean a continuación ya incluyan los canjes confirmados por otras transacciones.
    /// </summary>
    private async Task<Promotion> FindApplicablePromotionAsync(
        string code,
        DateOnly? additionalDate,
        bool lockForRedemption,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw NotApplicable("El código promocional es obligatorio.");
        }

        var normalizedCode = NormalizeCode(code);
        var promo = await _dbContext.Promotions
            .FirstOrDefaultAsync(p => p.Code.ToUpper() == normalizedCode && p.IsActive, cancellationToken);

        if (promo != null && lockForRedemption)
        {
            await LockAndRefreshAsync(promo, cancellationToken);
        }

        if (promo == null || promo.Status != PromotionStatus.Active)
        {
            throw NotApplicable("El código promocional especificado no existe o no se encuentra activo.");
        }

        if (!promo.IsWithinValidity(_gymCalendar.Today) ||
            (additionalDate.HasValue && !promo.IsWithinValidity(additionalDate.Value)))
        {
            throw NotApplicable("La promoción está vencida o fuera de vigencia.");
        }

        if (!PromotionRules.IsVerifiable(promo.ConditionType))
        {
            throw NotApplicable("La condición de esta promoción todavía no está disponible.");
        }

        return promo;
    }

    /// <summary>
    /// Verifica la condición de la promoción para la persona y, por último, los topes de uso general y por persona.
    /// </summary>
    private async Task EnsureEligibleAsync(Promotion promo, Guid personId, CancellationToken cancellationToken)
    {
        var personExists = await _dbContext.People
            .AnyAsync(p => p.Id == personId && p.IsActive, cancellationToken);
        if (!personExists)
        {
            throw NotApplicable("El alumno o persona especificada no existe o no se encuentra activa.");
        }

        if (promo.ConditionType == PromotionConditionType.NewStudent)
        {
            var hasMemberships = await _dbContext.Memberships
                .AnyAsync(m => m.StudentId == personId && m.IsActive && m.Status != MembershipStatus.Cancelled, cancellationToken);
            if (hasMemberships)
            {
                throw NotApplicable("Esta promoción es de bienvenida y aplica exclusivamente a nuevos alumnos.");
            }
        }
        else if (promo.ConditionType == PromotionConditionType.FamilyGroup)
        {
            var isFamilyMember = await _dbContext.FamilyGroupMembers
                .AnyAsync(m => m.PersonId == personId && m.IsActive, cancellationToken);
            if (!isFamilyMember)
            {
                throw NotApplicable("Esta promoción requiere pertenecer a un grupo familiar activo.");
            }
        }

        if (promo.HasReachedTotalLimit)
        {
            throw LimitReached("La promoción ha superado el tope total de canjes permitidos.");
        }

        // Sólo los canjes vigentes cuentan contra el tope por persona
        var previousUses = await _dbContext.PromotionRedemptions
            .CountAsync(r => r.PromotionId == promo.Id && r.PersonId == personId && r.IsActive, cancellationToken);

        if (!promo.CanBeUsedBy(personId, previousUses))
        {
            throw LimitReached($"Has alcanzado el límite de usos permitidos ({promo.MaxUsesPerPerson}) para esta promoción.");
        }
    }

    // ---- Concurrencia ----

    /// <summary>
    /// Los bloqueos de fila requieren una transacción. Si quien llama ya abrió una (contratación, reserva,
    /// cancelación), se usa esa y es ella quien confirma; si no, la operación abre y confirma la propia.
    /// </summary>
    private async Task<IDbContextTransaction?> BeginOwnTransactionAsync(CancellationToken cancellationToken) =>
        _dbContext.Database.CurrentTransaction == null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    /// <summary>
    /// Bloquea la fila de la promoción (último bloqueo del orden común) y relee sus valores, que pudieron cambiar
    /// mientras se esperaba el bloqueo.
    /// </summary>
    private async Task LockAndRefreshAsync(Promotion promo, CancellationToken cancellationToken)
    {
        await _dbContext.LockPromotionForUpdateAsync(promo.Id, cancellationToken);
        await RefreshAsync(_dbContext.Promotions.Entry(promo), cancellationToken);
    }

    // Una entidad con cambios pendientes ya fue modificada por esta misma transacción (con el bloqueo tomado):
    // releerla descartaría esos cambios.
    private static async Task RefreshAsync<T>(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> entry,
        CancellationToken cancellationToken)
        where T : class
    {
        if (entry.State == EntityState.Unchanged)
        {
            await entry.ReloadAsync(cancellationToken);
        }
    }

    private async Task SaveChangesMappingConcurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrencyConflict();
        }
    }

    // ---- Autorización ----

    /// <summary>
    /// Sin usuario autenticado (procesos internos) no se restringe; el personal opera sobre cualquier persona
    /// y el resto sólo sobre sí mismo.
    /// </summary>
    private void EnsureStaffOrOwner(Guid personId)
    {
        if (_currentUserService == null || !_currentUserService.IsAuthenticated)
        {
            return;
        }

        var isStaff = _currentUserService.IsInRole(Roles.Administrator) || _currentUserService.IsInRole(Roles.Secretary);
        if (!isStaff && _currentUserService.PersonId != personId)
        {
            throw new ForbiddenException("No tenés permiso para operar con promociones de otra persona.");
        }
    }

    // ---- Auxiliares ----

    private static string NormalizeCode(string code) => code.Trim().ToUpper();

    private static ConflictException NotApplicable(string message) =>
        ConflictException.WithCode(PromotionErrorCodes.NotApplicable, message);

    private static ConflictException LimitReached(string message) =>
        ConflictException.WithCode(PromotionErrorCodes.LimitReached, message);

    private static ConflictException ConcurrencyConflict() =>
        ConflictException.WithCode(
            PromotionErrorCodes.ConcurrencyConflict,
            "La promoción fue modificada por otra operación. Recargá los datos e intentá nuevamente.");

    private static NotFoundException PromotionNotFound() => new("La promoción especificada no existe.");

    private static async Task ValidateAsync<T>(IValidator<T> validator, T request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }
    }

    // Traduce una regla de dominio sobre un parámetro (p. ej. "maxUsesTotal") al campo del request.
    private static ValidationException DomainValidation(ArgumentException ex)
    {
        var field = string.IsNullOrEmpty(ex.ParamName)
            ? "General"
            : char.ToUpperInvariant(ex.ParamName[0]) + ex.ParamName[1..];
        var suffixIndex = ex.Message.IndexOf(" (Parameter", StringComparison.Ordinal);
        var message = suffixIndex >= 0 ? ex.Message[..suffixIndex] : ex.Message;

        return new ValidationException(new Dictionary<string, string[]> { [field] = [message] });
    }

    private async Task<Promotion> FindByIdForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Promotions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw PromotionNotFound();

    private async Task EnsureCodeIsFreeAsync(string code, Guid? exceptPromotionId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Promotions
            .AnyAsync(p => p.Code.ToUpper() == code && p.Id != exceptPromotionId, cancellationToken);
        if (exists)
        {
            throw ConflictException.WithCode(
                PromotionErrorCodes.CodeTaken,
                $"Ya existe una promoción con el código '{code}'.");
        }
    }

    private async Task EnsurePlanIsAvailableAsync(Guid? planId, CancellationToken cancellationToken)
    {
        if (!planId.HasValue)
        {
            return;
        }

        var planExists = await _dbContext.MembershipPlans
            .AnyAsync(p => p.Id == planId.Value && p.IsActive, cancellationToken);
        if (!planExists)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(CreatePromotionRequest.ApplicablePlanId)] = ["El plan aplicable no existe o no se encuentra activo."]
            });
        }
    }

    private static PromotionApplicationResultDto MembershipResult(Promotion promo, decimal basePrice)
    {
        var discount = promo.CalculateDiscount(basePrice);
        return new PromotionApplicationResultDto(
            promo.Id,
            promo.Code,
            promo.Name,
            basePrice,
            discount,
            Math.Max(0m, basePrice - discount),
            promo.GetExtraCredits());
    }

    private static PromotionApplicationResultDto ReservationResult(Promotion promo) =>
        new(promo.Id, promo.Code, promo.Name, 0m, 0m, 0m, promo.GetExtraCredits());

    private PromotionRedemption AddRedemption(
        Promotion promo,
        Guid personId,
        Guid? membershipId,
        Guid? reservationId,
        PromotionApplicationResultDto eval)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var redemption = new PromotionRedemption
        {
            Id = Guid.NewGuid(),
            PromotionId = promo.Id,
            PersonId = personId,
            MembershipId = membershipId,
            ReservationId = reservationId,
            OriginalAmount = eval.OriginalAmount,
            DiscountAmount = eval.DiscountAmount,
            FinalAmount = eval.FinalAmount,
            ExtraCreditsGranted = eval.ExtraCreditsGranted,
            RedeemedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            IsActive = true
        };

        promo.IncrementUse();
        _dbContext.PromotionRedemptions.Add(redemption);
        AddAuditLog("PROMOTION_REDEEMED", nameof(PromotionRedemption), redemption.Id, oldValues: null, RedemptionSnapshot(redemption));

        return redemption;
    }

    private async Task<PromotionRedemptionDto> MapRedemptionAsync(
        PromotionRedemption redemption,
        string promotionCode,
        CancellationToken cancellationToken)
    {
        var person = await _dbContext.People.FirstAsync(p => p.Id == redemption.PersonId, cancellationToken);

        return new PromotionRedemptionDto(
            redemption.Id,
            redemption.PromotionId,
            promotionCode,
            redemption.PersonId,
            person.FullName,
            redemption.OriginalAmount,
            redemption.DiscountAmount,
            redemption.FinalAmount,
            redemption.ExtraCreditsGranted,
            redemption.RedeemedAtUtc,
            redemption.ReservationId,
            redemption.MembershipId,
            IsVoided: !redemption.IsActive);
    }

    private static IQueryable<PromotionRedemptionDto> ProjectRedemptions(IQueryable<PromotionRedemption> query) =>
        query.Select(r => new PromotionRedemptionDto(
            r.Id,
            r.PromotionId,
            r.Promotion.Code,
            r.PersonId,
            (r.Person.FirstName + " " + r.Person.LastName).Trim(),
            r.OriginalAmount,
            r.DiscountAmount,
            r.FinalAmount,
            r.ExtraCreditsGranted,
            r.RedeemedAtUtc,
            r.ReservationId,
            r.MembershipId,
            !r.IsActive));

    /// <summary>
    /// La auditoría se agrega al mismo guardado que la operación (no usa <c>IAuditService</c>, que guarda por su
    /// cuenta): así queda en la misma transacción y no adelanta el guardado de quien llama.
    /// </summary>
    private void AddAuditLog(string action, string entityName, Guid entityId, object? oldValues, object? newValues)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var isAuthenticated = _currentUserService is { IsAuthenticated: true };

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Action = action,
            EntityName = entityName,
            EntityId = entityId.ToString(),
            OldValues = oldValues == null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = newValues == null ? null : JsonSerializer.Serialize(newValues),
            UserId = isAuthenticated ? _currentUserService!.UserId : null,
            UserEmail = isAuthenticated ? _currentUserService!.Email : null,
            TimestampUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            IsActive = true
        });
    }

    private static object Snapshot(Promotion p) => new
    {
        p.Code,
        p.Name,
        p.Description,
        ConditionType = p.ConditionType.ToString(),
        BenefitType = p.BenefitType.ToString(),
        p.BenefitValue,
        p.ValidFrom,
        p.ValidTo,
        p.MaxUsesTotal,
        p.MaxUsesPerPerson,
        p.ApplicablePlanId,
        Status = p.Status.ToString()
    };

    private static object RedemptionSnapshot(PromotionRedemption r) => new
    {
        r.PromotionId,
        r.PersonId,
        r.MembershipId,
        r.ReservationId,
        r.DiscountAmount,
        r.ExtraCreditsGranted,
        r.IsActive
    };

    private static PromotionDto MapToDto(Promotion p) =>
        new(
            p.Id,
            p.Code,
            p.Name,
            p.Description,
            p.ConditionType,
            p.BenefitType,
            p.BenefitValue,
            p.ValidFrom,
            p.ValidTo,
            p.MaxUsesTotal,
            p.CurrentUsesTotal,
            p.MaxUsesPerPerson,
            p.ApplicablePlanId,
            p.ApplicablePlan?.Name,
            p.IsActive,
            p.Status,
            p.Version
        );
}
