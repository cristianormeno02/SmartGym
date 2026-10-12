using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Memberships;

namespace SmartGym.Application.Modules.Memberships.Services;

public class MembershipService : IMembershipService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly ICreditLedgerService _creditLedgerService;
    private readonly IMembershipActivationService _activationService;
    private readonly IMembershipEligibilityService _eligibilityService;
    private readonly IPaymentService _paymentService;
    private readonly IPromotionService _promotionService;
    private readonly IGymCalendar _gymCalendar;
    private readonly GymOptions _gymOptions;
    private readonly MembershipsOptions _membershipsOptions;
    private readonly IAuditService? _auditService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ILogger<MembershipService> _logger;

    public MembershipService(
        ISmartGymDbContext dbContext,
        ICreditLedgerService? creditLedgerService = null,
        IMembershipActivationService? activationService = null,
        IMembershipEligibilityService? eligibilityService = null,
        IPaymentService? paymentService = null,
        IPromotionService? promotionService = null,
        IGymCalendar? gymCalendar = null,
        IOptions<GymOptions>? gymOptions = null,
        IOptions<MembershipsOptions>? membershipsOptions = null,
        IAuditService? auditService = null,
        ILogger<MembershipService>? logger = null,
        INotificationDispatcher? notificationDispatcher = null)
    {
        _dbContext = dbContext;
        _logger = logger ?? NullLogger<MembershipService>.Instance;
        _gymOptions = gymOptions?.Value ?? new GymOptions();
        _membershipsOptions = membershipsOptions?.Value ?? new MembershipsOptions();
        _gymCalendar = gymCalendar ?? new GymCalendar(Options.Create(_gymOptions));
        _auditService = auditService;
        _notificationDispatcher = notificationDispatcher ?? new NotificationDispatcher(
            dbContext,
            new NotificationRecipientResolver(dbContext, _gymCalendar),
            new NotificationTemplateRenderer(dbContext),
            new NotificationService(dbContext, _gymCalendar),
            _gymCalendar);
        _creditLedgerService = creditLedgerService ?? new CreditLedgerService(dbContext, _gymCalendar);
        _activationService = activationService ?? new MembershipActivationService(dbContext, _creditLedgerService, _gymCalendar, _auditService, _notificationDispatcher);
        _eligibilityService = eligibilityService ?? new MembershipEligibilityService(dbContext, _gymCalendar, Options.Create(_gymOptions));
        _paymentService = paymentService ?? new PaymentService(dbContext);
        _promotionService = promotionService ?? new PromotionService(dbContext, _gymCalendar);
    }

    public async Task<MembershipDetailDto> CreateAsync(
        CreateMembershipRequest request,
        Guid? currentUserId = null,
        bool isCallerAdministrator = false,
        CancellationToken cancellationToken = default)
    {
        return await CreateOrRenewInternalAsync(
            studentId: request.StudentId,
            membershipPlanId: request.MembershipPlanId,
            requestedStartDate: request.StartDate,
            promotionCode: request.PromotionCode,
            initialPayment: request.InitialPayment,
            paymentId: request.PaymentId,
            activateImmediately: request.ActivateImmediately,
            activationReason: request.ActivationReason,
            notes: request.Notes,
            idempotencyKey: request.IdempotencyKey,
            renewedFromMembershipId: null,
            currentUserId: currentUserId,
            isCallerAdministrator: isCallerAdministrator,
            cancellationToken: cancellationToken);
    }

    public async Task<MembershipDetailDto> RenewAsync(
        Guid membershipId,
        RenewMembershipRequest request,
        Guid? currentUserId = null,
        bool isCallerAdministrator = false,
        CancellationToken cancellationToken = default)
    {
        var sourceMembership = await _dbContext.Memberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.IsActive, cancellationToken)
            ?? throw new NotFoundException("La membresía de origen no existe.");

        if (sourceMembership.Status == MembershipStatus.Pending ||
            sourceMembership.Status == MembershipStatus.Cancelled)
        {
            throw new ConflictException($"No se puede renovar una membresía en estado {sourceMembership.Status}.");
        }

        var today = _gymCalendar.Today;
        DateOnly resolvedStartDate;
        if (request.StartDate.HasValue)
        {
            resolvedStartDate = request.StartDate.Value;
        }
        else if (sourceMembership.EndDate >= today)
        {
            resolvedStartDate = sourceMembership.EndDate.AddDays(1);
        }
        else
        {
            resolvedStartDate = today;
        }

        var targetPlanId = request.MembershipPlanId ?? sourceMembership.MembershipPlanId;

        return await CreateOrRenewInternalAsync(
            studentId: sourceMembership.StudentId,
            membershipPlanId: targetPlanId,
            requestedStartDate: resolvedStartDate,
            promotionCode: request.PromotionCode,
            initialPayment: request.InitialPayment,
            paymentId: request.PaymentId,
            activateImmediately: request.ActivateImmediately,
            activationReason: request.ActivationReason,
            notes: request.Notes,
            idempotencyKey: request.IdempotencyKey,
            renewedFromMembershipId: sourceMembership.Id,
            currentUserId: currentUserId,
            isCallerAdministrator: isCallerAdministrator,
            cancellationToken: cancellationToken);
    }

    private async Task<MembershipDetailDto> CreateOrRenewInternalAsync(
        Guid studentId,
        Guid membershipPlanId,
        DateOnly? requestedStartDate,
        string? promotionCode,
        InitialMembershipPaymentRequest? initialPayment,
        Guid? paymentId,
        bool activateImmediately,
        string? activationReason,
        string? notes,
        string? idempotencyKey,
        Guid? renewedFromMembershipId,
        Guid? currentUserId,
        bool isCallerAdministrator,
        CancellationToken cancellationToken)
    {
        if (notes != null && notes.Trim().Length > 1000)
        {
            throw new ValidationException("Las observaciones no pueden superar los 1000 caracteres.");
        }

        if (initialPayment != null && paymentId.HasValue)
        {
            throw new ValidationException("No se puede especificar simultáneamente un pago inicial nuevo y un PaymentId existente.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.LockPersonForUpdateAsync(studentId, cancellationToken);

        var normalizedKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (normalizedKey != null)
        {
            if (normalizedKey.Length > 100)
            {
                throw new ValidationException("La clave de idempotencia no puede superar los 100 caracteres.");
            }

            var existingByKey = await _dbContext.Memberships
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.StudentId == studentId && m.IdempotencyKey == normalizedKey && m.IsActive,
                    cancellationToken);

            if (existingByKey != null)
            {
                var todayForIdempotency = _gymCalendar.Today;
                var expectedStart = requestedStartDate ?? todayForIdempotency;
                if (existingByKey.MembershipPlanId != membershipPlanId ||
                    existingByKey.StartDate != expectedStart ||
                    existingByKey.RenewedFromMembershipId != renewedFromMembershipId)
                {
                    throw new ConflictException(
                        "La clave de idempotencia ya fue utilizada para este alumno con otros parámetros de contratación.",
                        new { ExistingMembershipId = existingByKey.Id });
                }

                await transaction.CommitAsync(cancellationToken);
                return await GetByIdAsync(existingByKey.Id, cancellationToken);
            }
        }

        var student = await _dbContext.People
            .Include(p => p.PersonRoles)
                .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == studentId, cancellationToken)
            ?? throw new NotFoundException("El alumno especificado no existe.");

        if (!student.IsActive || student.Status != PersonStatus.Active)
        {
            throw new ConflictException($"La persona no se encuentra en estado activo para contratar una membresía (estado actual: {student.Status}).");
        }

        var hasStudentRole = student.PersonRoles.Any(pr =>
            pr.IsActive &&
            pr.Role != null &&
            string.Equals(pr.Role.Name, RoleType.Student.ToString(), StringComparison.OrdinalIgnoreCase));

        if (!hasStudentRole)
        {
            throw new ValidationException("La persona especificada no posee el rol de Alumno activo.");
        }

        var plan = await _dbContext.MembershipPlans
            .Include(p => p.AllowedActivities)
            .FirstOrDefaultAsync(p => p.Id == membershipPlanId, cancellationToken)
            ?? throw new NotFoundException("El plan de membresía especificado no existe.");

        if (!plan.IsActive)
        {
            throw new ConflictException("El plan de membresía especificado no se encuentra activo para nuevas contrataciones.");
        }

        var today = _gymCalendar.Today;
        var nowUtc = _gymCalendar.UtcNow;
        var startDate = requestedStartDate ?? today;

        if (startDate < today)
        {
            throw new ValidationException("La fecha de inicio de la membresía no puede ser anterior a la fecha actual del gimnasio.");
        }

        var endDate = startDate.AddDays(plan.DurationDays - 1);

        // Validar solapamiento de membresías del alumno
        var candidateOverlaps = await _dbContext.Memberships
            .Include(m => m.AllowedActivities)
            .Include(m => m.MembershipPlan)
                .ThenInclude(p => p.AllowedActivities)
            .Include(m => m.Suspensions)
            .Where(m => m.StudentId == student.Id &&
                        m.IsActive &&
                        (m.Status == MembershipStatus.Pending ||
                         m.Status == MembershipStatus.Active ||
                         m.Status == MembershipStatus.Suspended))
            .ToListAsync(cancellationToken);

        foreach (var other in candidateOverlaps)
        {
            if (other.GetEffectiveStatus(today) == MembershipStatus.Expired)
            {
                continue;
            }

            var datesOverlap = startDate <= other.EndDate && endDate >= other.StartDate;
            if (!datesOverlap)
            {
                continue;
            }

            if (!SharesAnyAllowedActivity(plan, other))
            {
                continue;
            }

            if (other.HasUsableOrPendingCredits())
            {
                throw new ConflictException(
                    $"El alumno ya posee una membresía superpuesta ({other.Id}) con vigencia del {other.StartDate:yyyy-MM-dd} al {other.EndDate:yyyy-MM-dd}.",
                    new
                    {
                        OverlappingMembershipId = other.Id,
                        other.StartDate,
                        other.EndDate,
                        other.Status
                    });
            }
        }

        // Validar PaymentId existente antes de realizar escrituras
        Payment? linkedPayment = null;
        if (paymentId.HasValue)
        {
            linkedPayment = await _dbContext.Payments
                .FirstOrDefaultAsync(p => p.Id == paymentId.Value && p.IsActive, cancellationToken)
                ?? throw new NotFoundException("El pago especificado no existe.");

            if (linkedPayment.PayerPersonId != student.Id)
            {
                throw new ConflictException("El pago especificado pertenece a otra persona.");
            }

            if (linkedPayment.MembershipId.HasValue || linkedPayment.ClassSessionId.HasValue)
            {
                throw new ConflictException("El pago especificado ya se encuentra vinculado a otro concepto.");
            }

            if (linkedPayment.Status != PaymentStatus.Pending && linkedPayment.Status != PaymentStatus.Completed)
            {
                throw new ConflictException($"El pago especificado no puede vincularse porque su estado es {linkedPayment.Status}.");
            }
        }

        // Evaluar y redimir promoción ANTES de insertar la membresía (para que NewStudent no cuente la membresía en curso)
        decimal originalPrice = plan.Price;
        decimal discountAmount = 0m;
        decimal purchasePrice = plan.Price;
        int extraCreditsGranted = 0;
        Guid? promotionId = null;
        Guid? promotionRedemptionId = null;

        if (!string.IsNullOrWhiteSpace(promotionCode))
        {
            // Los rechazos llegan como ConflictException con su código (PromotionErrorCodes)
            var redemption = await _promotionService.RedeemPromotionAsync(
                new ApplyPromotionRequest(promotionCode.Trim(), student.Id, plan.Id, plan.Price),
                membershipId: null,
                cancellationToken);

            originalPrice = redemption.OriginalAmount;
            discountAmount = redemption.DiscountAmount;
            purchasePrice = redemption.FinalAmount;
            extraCreditsGranted = redemption.ExtraCreditsGranted;
            promotionId = redemption.PromotionId;
            promotionRedemptionId = redemption.Id;
        }

        Membership? createdMembership = null;
        try
        {
            var currency = string.IsNullOrWhiteSpace(_gymOptions.DefaultCurrency) ? "ARS" : _gymOptions.DefaultCurrency;
            createdMembership = Membership.Create(
                studentId: student.Id,
                plan: plan,
                startDate: startDate,
                purchasedAtUtc: nowUtc,
                originalPrice: originalPrice,
                discountAmount: discountAmount,
                purchasePrice: purchasePrice,
                currency: currency,
                extraCreditsGranted: extraCreditsGranted,
                promotionId: promotionId,
                promotionRedemptionId: promotionRedemptionId,
                renewedFromMembershipId: renewedFromMembershipId,
                createdByUserId: currentUserId,
                notes: notes,
                idempotencyKey: normalizedKey);

            _dbContext.Memberships.Add(createdMembership);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (promotionRedemptionId.HasValue)
            {
                await _promotionService.AttachRedemptionToMembershipAsync(
                    promotionRedemptionId.Value,
                    createdMembership.Id,
                    cancellationToken);
            }

            if (initialPayment != null)
            {
                await _paymentService.RegisterPaymentAsync(
                    new RegisterPaymentRequest(
                        PayerPersonId: student.Id,
                        MembershipId: createdMembership.Id,
                        ClassSessionId: null,
                        Amount: initialPayment.Amount,
                        Method: initialPayment.Method,
                        ExternalTransactionId: initialPayment.ExternalTransactionId,
                        Notes: initialPayment.Notes,
                        Status: initialPayment.Status),
                    currentUserId ?? Guid.Empty,
                    cancellationToken);
            }
            else if (linkedPayment != null)
            {
                // La aplicación pasa por Payments: valida titularidad, moneda y saldo, y deja su auditoría
                await _paymentService.ApplyPaymentAsync(
                    linkedPayment.Id,
                    new ApplyPaymentRequest(createdMembership.Id, null),
                    currentUserId ?? Guid.Empty,
                    cancellationToken);
            }

            await _activationService.TryActivateAsync(
                membershipId: createdMembership.Id,
                authorizedByUserId: currentUserId,
                activationReason: activationReason,
                isExplicitActivation: activateImmediately,
                isCallerAdministrator: isCallerAdministrator,
                saveChanges: true,
                cancellationToken: cancellationToken);

            if (_auditService != null)
            {
                var action = renewedFromMembershipId.HasValue ? "MEMBERSHIP_RENEWED" : "MEMBERSHIP_CREATED";
                await _auditService.LogAsync(
                    action,
                    "Membership",
                    createdMembership.Id.ToString(),
                    renewedFromMembershipId.HasValue
                        ? JsonSerializer.Serialize(new { RenewedFromMembershipId = renewedFromMembershipId })
                        : null,
                    JsonSerializer.Serialize(new
                    {
                        createdMembership.StudentId,
                        createdMembership.MembershipPlanId,
                        createdMembership.StartDate,
                        createdMembership.EndDate,
                        createdMembership.OriginalPrice,
                        createdMembership.DiscountAmount,
                        createdMembership.PurchasePrice,
                        createdMembership.PromotionId,
                        Status = createdMembership.Status.ToString()
                    }),
                    currentUserId,
                    null,
                    null,
                    cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (!string.Equals(_dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                await CompensateInMemoryOnFailureAsync(createdMembership?.Id, promotionRedemptionId, linkedPayment, cancellationToken);
            }

            throw;
        }

        return await GetByIdAsync(createdMembership.Id, cancellationToken);
    }

    private async Task CompensateInMemoryOnFailureAsync(
        Guid? membershipId,
        Guid? promotionRedemptionId,
        Payment? linkedPayment,
        CancellationToken cancellationToken)
    {
        if (linkedPayment != null)
        {
            linkedPayment.MembershipId = null;
        }

        if (promotionRedemptionId.HasValue)
        {
            await _promotionService.VoidRedemptionAsync(promotionRedemptionId.Value, cancellationToken);
        }

        if (membershipId.HasValue)
        {
            var movements = await _dbContext.MembershipCreditMovements
                .Where(m => m.MembershipId == membershipId.Value)
                .ToListAsync(cancellationToken);
            _dbContext.MembershipCreditMovements.RemoveRange(movements);

            var payments = await _dbContext.Payments
                .Where(p => p.MembershipId == membershipId.Value && (linkedPayment == null || p.Id != linkedPayment.Id))
                .ToListAsync(cancellationToken);
            _dbContext.Payments.RemoveRange(payments);

            var membership = await _dbContext.Memberships
                .FirstOrDefaultAsync(m => m.Id == membershipId.Value, cancellationToken);
            if (membership != null)
            {
                _dbContext.Memberships.Remove(membership);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool SharesAnyAllowedActivity(MembershipPlan newPlan, Membership existingMembership)
    {
        if (newPlan.AppliesToAllActivities || existingMembership.AppliesToAllActivities)
        {
            return true;
        }

        var newActivityIds = newPlan.AllowedActivities
            .Select(a => a.ActivityId)
            .ToHashSet();

        if (newActivityIds.Count == 0)
        {
            return false;
        }

        return existingMembership.AllowedActivities.Any(a => newActivityIds.Contains(a.ActivityId));
    }

    public async Task<MembershipDetailDto> ActivateAsync(
        Guid membershipId,
        ActivateMembershipRequest request,
        Guid? currentUserId = null,
        bool isCallerAdministrator = false,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.LockMembershipForUpdateAsync(membershipId, cancellationToken);

        await _activationService.TryActivateAsync(
            membershipId: membershipId,
            authorizedByUserId: currentUserId,
            activationReason: request.Reason,
            isExplicitActivation: true,
            isCallerAdministrator: isCallerAdministrator,
            saveChanges: true,
            cancellationToken: cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await GetByIdAsync(membershipId, cancellationToken);
    }

    private async Task LockAffectedReservationSessionsAsync(Guid membershipId, CancellationToken cancellationToken)
    {
        var nowUtc = _gymCalendar.UtcNow;
        var sessionIds = await _dbContext.Reservations
            .Where(r => r.MembershipId == membershipId &&
                        r.IsActive &&
                        (r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Waitlisted ||
                         r.Status == ReservationStatus.Pending) &&
                        r.ClassSession.StartsAtUtc > nowUtc)
            .Select(r => r.ClassSessionId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

        foreach (var sessionId in sessionIds)
        {
            await _dbContext.LockClassSessionForUpdateAsync(sessionId, cancellationToken);
        }
    }

    public async Task<MembershipDetailDto> SuspendAsync(
        Guid membershipId,
        SuspendMembershipRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockAffectedReservationSessionsAsync(membershipId, cancellationToken);
        await _dbContext.LockMembershipForUpdateAsync(membershipId, cancellationToken);

        var membership = await _dbContext.Memberships
            .Include(m => m.Suspensions)
            .Include(m => m.StatusChanges)
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.IsActive, cancellationToken)
            ?? throw new NotFoundException("La membresía especificada no existe.");

        EnsureConcurrencyVersion(membership, request.Version);

        var today = _gymCalendar.Today;
        var nowUtc = _gymCalendar.UtcNow;

        if (membership.GetEffectiveStatus(today) == MembershipStatus.Expired)
        {
            throw new ConflictException("No se puede suspender una membresía cuyo período de vigencia ya finalizó.");
        }

        MembershipSuspension suspension;
        try
        {
            suspension = membership.Suspend(
                today: today,
                endDate: request.EndDate,
                extendExpirationDate: request.ExtendExpirationDate,
                reason: request.Reason,
                notes: request.Notes,
                suspendedByUserId: currentUserId,
                suspendedAtUtc: nowUtc,
                maxSuspensionDays: _membershipsOptions.MaxSuspensionDays);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        // Las reservas alcanzadas por la ventana las cancela CancelReservationsOnMembershipSuspendedHandler
        // al despacharse MembershipSuspendedEvent dentro de SaveChanges (misma transacción).
        var suspensionBody = suspension.EndDate.HasValue
            ? $"Tu membresía fue suspendida desde el {suspension.StartDate:yyyy-MM-dd} hasta el {suspension.EndDate.Value:yyyy-MM-dd}. Motivo: {suspension.Reason}."
            : $"Tu membresía fue suspendida a partir del {suspension.StartDate:yyyy-MM-dd}. Motivo: {suspension.Reason}.";

        var suspensionVariables = new Dictionary<string, string?>
        {
            ["planName"] = "Membresía",
            ["startDate"] = $"{suspension.StartDate:yyyy-MM-dd}",
            ["reason"] = suspension.Reason
        };
        if (suspension.EndDate.HasValue)
        {
            // Sin fecha de fin la plantilla no aplica y los canales externos usan el cuerpo directo.
            suspensionVariables["endDate"] = $"{suspension.EndDate.Value:yyyy-MM-dd}";
        }

        try
        {
            await _notificationDispatcher.DispatchAsync(
                new DispatchNotificationCommand(
                    TargetPersonId: membership.StudentId,
                    Type: NotificationType.MembershipSuspended,
                    Variables: suspensionVariables,
                    IdempotencyKey: $"membership-suspended:{suspension.Id}",
                    RelatedEntityType: "Membership",
                    RelatedEntityId: membership.Id,
                    ActionUrl: "/portal/alumno",
                    DirectTitleOverride: "Membresía suspendida",
                    DirectBodyOverride: suspensionBody,
                    SaveChanges: false),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Fallo aislado al preparar notificación de suspensión para membresía {MembershipId}.", membership.Id);
        }

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "MEMBERSHIP_SUSPENDED",
                "Membership",
                membership.Id.ToString(),
                JsonSerializer.Serialize(new { Status = MembershipStatus.Active.ToString() }),
                JsonSerializer.Serialize(new
                {
                    Status = MembershipStatus.Suspended.ToString(),
                    suspension.StartDate,
                    suspension.EndDate,
                    suspension.ExtendExpirationDate,
                    suspension.Reason
                }),
                currentUserId,
                null,
                null,
                cancellationToken);
        }

        await SaveChangesWithConcurrencyCheckAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetByIdAsync(membershipId, cancellationToken);
    }

    public async Task<MembershipDetailDto> ReactivateAsync(
        Guid membershipId,
        ReactivateMembershipRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.LockMembershipForUpdateAsync(membershipId, cancellationToken);

        var membership = await _dbContext.Memberships
            .Include(m => m.Suspensions)
            .Include(m => m.StatusChanges)
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.IsActive, cancellationToken)
            ?? throw new NotFoundException("La membresía especificada no existe.");

        EnsureConcurrencyVersion(membership, request.Version);

        var today = _gymCalendar.Today;
        var nowUtc = _gymCalendar.UtcNow;
        var previousEndDate = membership.EndDate;

        int extensionDaysApplied;
        try
        {
            extensionDaysApplied = membership.Reactivate(
                reactivationDate: today,
                reactivatedAtUtc: nowUtc,
                reactivatedByUserId: currentUserId,
                reason: string.IsNullOrWhiteSpace(request.Reason) ? "Membresía reactivada manualmente" : request.Reason!,
                notes: request.Notes);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        try
        {
            await _notificationDispatcher.DispatchAsync(
                new DispatchNotificationCommand(
                    TargetPersonId: membership.StudentId,
                    Type: NotificationType.MembershipReactivated,
                    Variables: new Dictionary<string, string?>
                    {
                        ["planName"] = "Membresía",
                        ["endDate"] = $"{membership.EndDate:yyyy-MM-dd}"
                    },
                    IdempotencyKey: $"membership-reactivated:{membership.Id}:{nowUtc.Ticks}",
                    RelatedEntityType: "Membership",
                    RelatedEntityId: membership.Id,
                    ActionUrl: "/portal/alumno",
                    DirectTitleOverride: "Membresía reactivada",
                    DirectBodyOverride: $"Tu membresía fue reactivada y tiene vigencia hasta el {membership.EndDate:yyyy-MM-dd}.",
                    SaveChanges: false),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Fallo aislado al preparar notificación de reactivación para membresía {MembershipId}.", membership.Id);
        }

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "MEMBERSHIP_REACTIVATED",
                "Membership",
                membership.Id.ToString(),
                JsonSerializer.Serialize(new { Status = MembershipStatus.Suspended.ToString(), EndDate = previousEndDate }),
                JsonSerializer.Serialize(new
                {
                    Status = MembershipStatus.Active.ToString(),
                    membership.EndDate,
                    ExtensionDaysApplied = extensionDaysApplied,
                    Reason = request.Reason
                }),
                currentUserId,
                null,
                null,
                cancellationToken);

            if (extensionDaysApplied > 0)
            {
                await _auditService.LogAsync(
                    "MEMBERSHIP_END_DATE_EXTENDED",
                    "Membership",
                    membership.Id.ToString(),
                    JsonSerializer.Serialize(new { EndDate = previousEndDate }),
                    JsonSerializer.Serialize(new { membership.EndDate, ExtensionDaysApplied = extensionDaysApplied }),
                    currentUserId,
                    null,
                    null,
                    cancellationToken);
            }
        }

        await SaveChangesWithConcurrencyCheckAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetByIdAsync(membershipId, cancellationToken);
    }

    public async Task<MembershipDetailDto> CancelAsync(
        Guid membershipId,
        CancelMembershipRequest request,
        Guid? currentUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException("El motivo de cancelación es obligatorio.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockAffectedReservationSessionsAsync(membershipId, cancellationToken);
        await _dbContext.LockMembershipForUpdateAsync(membershipId, cancellationToken);

        var membership = await _dbContext.Memberships
            .Include(m => m.Suspensions)
            .Include(m => m.StatusChanges)
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.IsActive, cancellationToken)
            ?? throw new NotFoundException("La membresía especificada no existe.");

        EnsureConcurrencyVersion(membership, request.Version);

        var previousStatus = membership.Status;
        var previousAvailableCredits = membership.AvailableCredits;
        var nowUtc = _gymCalendar.UtcNow;

        try
        {
            membership.Cancel(
                reason: request.Reason,
                cancelledByUserId: currentUserId,
                cancelledAtUtc: nowUtc,
                notes: request.Notes);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        // Si nunca llegó a activarse (Pending) y tenía una promoción canjeada, se anula el canje
        if (previousStatus == MembershipStatus.Pending &&
            !membership.InitialCreditsLoaded &&
            membership.PromotionRedemptionId.HasValue)
        {
            await _promotionService.VoidRedemptionAsync(membership.PromotionRedemptionId.Value, cancellationToken);
        }

        // Dar de baja créditos remanentes en el ledger
        await _creditLedgerService.ForfeitRemainingCreditsOnCancelAsync(
            membership,
            $"Baja de créditos por cancelación de membresía: {membership.CancellationReason}",
            currentUserId,
            cancellationToken);

        // Las reservas futuras las cancela CancelFutureReservationsOnMembershipCancelledHandler
        // al despacharse MembershipCancelledEvent dentro de SaveChanges (misma transacción).
        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "MEMBERSHIP_CANCELLED",
                "Membership",
                membership.Id.ToString(),
                JsonSerializer.Serialize(new
                {
                    Status = previousStatus.ToString(),
                    AvailableCredits = previousAvailableCredits
                }),
                JsonSerializer.Serialize(new
                {
                    Status = MembershipStatus.Cancelled.ToString(),
                    membership.CancellationReason,
                    membership.AvailableCredits
                }),
                currentUserId,
                null,
                null,
                cancellationToken);
        }

        await SaveChangesWithConcurrencyCheckAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetByIdAsync(membershipId, cancellationToken);
    }

    public async Task<MembershipDetailDto> UpdateNotesAsync(
        Guid membershipId,
        UpdateMembershipNotesRequest request,
        Guid? currentUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (request.Notes != null && request.Notes.Trim().Length > 1000)
        {
            throw new ValidationException("Las observaciones no pueden superar los 1000 caracteres.");
        }

        var membership = await _dbContext.Memberships
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.IsActive, cancellationToken)
            ?? throw new NotFoundException("La membresía especificada no existe.");

        EnsureConcurrencyVersion(membership, request.Version);

        var oldNotes = membership.Notes;
        membership.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        membership.UpdatedAtUtc = _gymCalendar.UtcNow;

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "MEMBERSHIP_NOTES_UPDATED",
                "Membership",
                membership.Id.ToString(),
                JsonSerializer.Serialize(new { Notes = oldNotes }),
                JsonSerializer.Serialize(new { membership.Notes }),
                currentUserId,
                null,
                null,
                cancellationToken);
        }

        await SaveChangesWithConcurrencyCheckAsync(cancellationToken);

        return await GetByIdAsync(membershipId, cancellationToken);
    }

    public async Task<PagedResult<MembershipListItemDto>> GetPagedAsync(
        MembershipListFilter filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var today = _gymCalendar.Today;

        var query = _dbContext.Memberships
            .Include(m => m.Student)
            .Include(m => m.MembershipPlan)
            .Include(m => m.Promotion)
            .Include(m => m.Suspensions)
            .AsNoTracking()
            .Where(m => m.IsActive);

        if (filter.StudentId.HasValue)
        {
            query = query.Where(m => m.StudentId == filter.StudentId.Value);
        }

        if (filter.MembershipPlanId.HasValue)
        {
            query = query.Where(m => m.MembershipPlanId == filter.MembershipPlanId.Value);
        }

        if (filter.Status.HasValue)
        {
            switch (filter.Status.Value)
            {
                case MembershipStatus.Expired:
                    query = query.Where(m =>
                        m.Status == MembershipStatus.Expired ||
                        (m.EndDate < today &&
                         (m.Status == MembershipStatus.Active ||
                          (m.Status == MembershipStatus.Suspended &&
                           !m.Suspensions.Any(s => s.IsActiveSuspension && s.ExtendExpirationDate)))));
                    break;

                case MembershipStatus.Active:
                    query = query.Where(m => m.Status == MembershipStatus.Active && m.EndDate >= today);
                    break;

                case MembershipStatus.Suspended:
                    query = query.Where(m =>
                        m.Status == MembershipStatus.Suspended &&
                        (m.EndDate >= today || m.Suspensions.Any(s => s.IsActiveSuspension && s.ExtendExpirationDate)));
                    break;

                default:
                    query = query.Where(m => m.Status == filter.Status.Value);
                    break;
            }
        }

        if (filter.StartDateFrom.HasValue)
        {
            query = query.Where(m => m.StartDate >= filter.StartDateFrom.Value);
        }

        if (filter.StartDateTo.HasValue)
        {
            query = query.Where(m => m.StartDate <= filter.StartDateTo.Value);
        }

        if (filter.EndDateFrom.HasValue)
        {
            query = query.Where(m => m.EndDate >= filter.EndDateFrom.Value);
        }

        if (filter.EndDateTo.HasValue)
        {
            query = query.Where(m => m.EndDate <= filter.EndDateTo.Value);
        }

        if (filter.PurchasedFrom.HasValue)
        {
            var (fromUtc, _) = _gymCalendar.LocalDayRange(filter.PurchasedFrom.Value, filter.PurchasedFrom.Value);
            query = query.Where(m => m.PurchasedAtUtc >= fromUtc);
        }

        if (filter.PurchasedTo.HasValue)
        {
            var (_, toUtc) = _gymCalendar.LocalDayRange(filter.PurchasedTo.Value, filter.PurchasedTo.Value);
            query = query.Where(m => m.PurchasedAtUtc < toUtc);
        }

        if (filter.ExpiringWithinDays.HasValue)
        {
            var maxEndDate = today.AddDays(filter.ExpiringWithinDays.Value);
            query = query.Where(m =>
                m.Status == MembershipStatus.Active &&
                m.EndDate >= today &&
                m.EndDate <= maxEndDate);
        }

        if (filter.HasAvailableCredits.HasValue)
        {
            if (filter.HasAvailableCredits.Value)
            {
                query = query.Where(m => m.IsUnlimited || (m.InitialCreditsLoaded && m.AvailableCredits > 0));
            }
            else
            {
                query = query.Where(m => !m.IsUnlimited && (!m.InitialCreditsLoaded || m.AvailableCredits <= 0));
            }
        }

        var sortDesc = !string.Equals(filter.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        query = (filter.SortBy?.ToLowerInvariant()) switch
        {
            "startdate" => sortDesc ? query.OrderByDescending(m => m.StartDate) : query.OrderBy(m => m.StartDate),
            "enddate" => sortDesc ? query.OrderByDescending(m => m.EndDate) : query.OrderBy(m => m.EndDate),
            "purchaseprice" => sortDesc ? query.OrderByDescending(m => m.PurchasePrice) : query.OrderBy(m => m.PurchasePrice),
            "status" => sortDesc ? query.OrderByDescending(m => m.Status) : query.OrderBy(m => m.Status),
            _ => sortDesc
                ? query.OrderByDescending(m => m.PurchasedAtUtc).ThenByDescending(m => m.StartDate)
                : query.OrderBy(m => m.PurchasedAtUtc).ThenBy(m => m.StartDate)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var paidAmountsByMembership = await GetCompletedPaidAmountsByMembershipAsync(
            items.Select(m => m.Id).ToList(),
            cancellationToken);

        var dtoItems = items
            .Select(m => MapToListItemDto(
                m,
                today,
                paidAmountsByMembership.GetValueOrDefault(m.Id, 0m)))
            .ToList();

        return new PagedResult<MembershipListItemDto>(dtoItems, totalCount, page, pageSize);
    }

    public async Task<MembershipDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var membership = await _dbContext.Memberships
            .Include(m => m.Student)
            .Include(m => m.MembershipPlan)
            .Include(m => m.Promotion)
            .Include(m => m.AllowedActivities)
            .Include(m => m.Suspensions)
            .Include(m => m.StatusChanges)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.IsActive, cancellationToken)
            ?? throw new NotFoundException("La membresía especificada no existe.");

        var payments = await _dbContext.Payments
            .Include(p => p.PayerPerson)
            .AsNoTracking()
            .Where(p => p.MembershipId == membership.Id && p.IsActive)
            .OrderByDescending(p => p.PaidAtUtc)
            .ToListAsync(cancellationToken);

        var creditsSummary = await _creditLedgerService.GetMembershipCreditsSummaryAsync(membership.Id, cancellationToken);

        return MapToDetailDto(membership, _gymCalendar.Today, payments, creditsSummary);
    }

    public async Task<IReadOnlyList<MembershipSummaryDto>> GetByStudentIdAsync(
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.Memberships
            .Include(m => m.Student)
            .Include(m => m.MembershipPlan)
            .Include(m => m.Promotion)
            .Include(m => m.Suspensions)
            .AsNoTracking()
            .Where(m => m.StudentId == studentId && m.IsActive)
            .OrderByDescending(m => m.StartDate)
            .ThenByDescending(m => m.PurchasedAtUtc)
            .ToListAsync(cancellationToken);

        var paidAmounts = await GetCompletedPaidAmountsByMembershipAsync(
            list.Select(m => m.Id).ToList(),
            cancellationToken);

        var today = _gymCalendar.Today;
        return list
            .Select(m => MapToSummaryDto(m, today, paidAmounts.GetValueOrDefault(m.Id, 0m)))
            .ToList();
    }

    public async Task<IReadOnlyList<MembershipSummaryDto>> GetCurrentByStudentIdAsync(
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var today = _gymCalendar.Today;
        var list = await _dbContext.Memberships
            .Include(m => m.Student)
            .Include(m => m.MembershipPlan)
            .Include(m => m.Promotion)
            .Include(m => m.Suspensions)
            .AsNoTracking()
            .Where(m => m.StudentId == studentId &&
                        m.IsActive &&
                        (m.Status == MembershipStatus.Pending ||
                         m.Status == MembershipStatus.Active ||
                         m.Status == MembershipStatus.Suspended))
            .OrderBy(m => m.StartDate)
            .ThenBy(m => m.PurchasedAtUtc)
            .ToListAsync(cancellationToken);

        var currentAndUpcoming = list
            .Where(m => m.GetEffectiveStatus(today) != MembershipStatus.Expired)
            .ToList();

        var paidAmounts = await GetCompletedPaidAmountsByMembershipAsync(
            currentAndUpcoming.Select(m => m.Id).ToList(),
            cancellationToken);

        return currentAndUpcoming
            .Select(m => MapToSummaryDto(m, today, paidAmounts.GetValueOrDefault(m.Id, 0m)))
            .ToList();
    }

    public async Task<MembershipSummaryDto?> GetActiveMembershipAsync(
        Guid studentId,
        DateOnly? date = null,
        CancellationToken cancellationToken = default)
    {
        var targetDate = date ?? _gymCalendar.Today;
        var today = _gymCalendar.Today;

        var candidates = await _dbContext.Memberships
            .Include(m => m.Student)
            .Include(m => m.MembershipPlan)
            .Include(m => m.Promotion)
            .Include(m => m.Suspensions)
            .AsNoTracking()
            .Where(m => m.StudentId == studentId &&
                        m.IsActive &&
                        m.Status == MembershipStatus.Active &&
                        m.StartDate <= targetDate &&
                        targetDate <= m.EndDate)
            .ToListAsync(cancellationToken);

        var validCandidates = candidates
            .Where(m => m.GetEffectiveStatus(today) == MembershipStatus.Active)
            .OrderByDescending(m => m.IsUnlimited ? 1 : 0)
            .ThenByDescending(m => m.HasAvailableCredit() ? 1 : 0)
            .ThenBy(m => m.EndDate)
            .ThenBy(m => m.PurchasedAtUtc)
            .ToList();

        var chosen = validCandidates.FirstOrDefault();
        if (chosen == null)
        {
            return null;
        }

        var paidAmounts = await GetCompletedPaidAmountsByMembershipAsync(new List<Guid> { chosen.Id }, cancellationToken);
        return MapToSummaryDto(chosen, today, paidAmounts.GetValueOrDefault(chosen.Id, 0m));
    }

    public async Task<MembershipExpirationBatchResultDto> ProcessExpirationsAndWarningsAsync(
        CancellationToken cancellationToken = default)
    {
        var today = _gymCalendar.Today;
        var nowUtc = _gymCalendar.UtcNow;

        int reactivatedCount = 0;
        int expiredCount = 0;
        int autoCancelledCount = 0;
        int warningsSentCount = 0;

        // 1. Reactivación automática de suspensiones finalizadas (suspension.EndDate < today)
        var suspendedCandidateIds = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.IsActive &&
                        m.Status == MembershipStatus.Suspended &&
                        m.Suspensions.Any(s => s.IsActiveSuspension && s.EndDate.HasValue && s.EndDate.Value < today))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in suspendedCandidateIds)
        {
            try
            {
                await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await _dbContext.LockMembershipForUpdateAsync(id, cancellationToken);

                var membership = await _dbContext.Memberships
                    .Include(m => m.Suspensions)
                    .Include(m => m.StatusChanges)
                    .FirstOrDefaultAsync(m => m.Id == id && m.IsActive, cancellationToken);

                var activeSuspension = membership?.Suspensions.FirstOrDefault(s => s.IsActiveSuspension);
                if (membership == null ||
                    membership.Status != MembershipStatus.Suspended ||
                    activeSuspension == null ||
                    !activeSuspension.EndDate.HasValue ||
                    activeSuspension.EndDate.Value >= today)
                {
                    await tx.CommitAsync(cancellationToken);
                    continue;
                }

                var reactivationDate = activeSuspension.EndDate.Value.AddDays(1);
                if (!activeSuspension.ExtendExpirationDate && reactivationDate > membership.EndDate)
                {
                    // Sin extensión y ya vencida al finalizar la suspensión: pasará a Expired en el paso 2
                    await tx.CommitAsync(cancellationToken);
                    continue;
                }

                var previousEndDate = membership.EndDate;
                var extensionDays = membership.Reactivate(
                    reactivationDate: reactivationDate,
                    reactivatedAtUtc: nowUtc,
                    reactivatedByUserId: null,
                    reason: "Reactivación automática por finalización de período de suspensión");

                try
                {
                    await _notificationDispatcher.DispatchAsync(
                        new DispatchNotificationCommand(
                            TargetPersonId: membership.StudentId,
                            Type: NotificationType.MembershipReactivated,
                            Variables: new Dictionary<string, string?>
                            {
                                ["planName"] = "Membresía",
                                ["endDate"] = $"{membership.EndDate:yyyy-MM-dd}"
                            },
                            IdempotencyKey: $"membership-auto-reactivated:{activeSuspension.Id}",
                            RelatedEntityType: "Membership",
                            RelatedEntityId: membership.Id,
                            ActionUrl: "/portal/alumno",
                            DirectTitleOverride: "Membresía reactivada",
                            DirectBodyOverride: $"Tu membresía fue reactivada y tiene vigencia hasta el {membership.EndDate:yyyy-MM-dd}.",
                            SaveChanges: false),
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Fallo aislado al preparar notificación de reactivación automática para membresía {MembershipId}.", membership.Id);
                }

                if (_auditService != null && extensionDays > 0)
                {
                    await _auditService.LogAsync(
                        "MEMBERSHIP_END_DATE_EXTENDED",
                        "Membership",
                        membership.Id.ToString(),
                        JsonSerializer.Serialize(new { EndDate = previousEndDate }),
                        JsonSerializer.Serialize(new { membership.EndDate, ExtensionDaysApplied = extensionDays }),
                        null,
                        null,
                        null,
                        cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                reactivatedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DiscardFailedBatchItem(ex, id, "reactivación automática");
            }
        }

        // 2. Transición a Expired de membresías vencidas (Active o Suspended sin extensión pactada)
        var expiredCandidateIds = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.IsActive &&
                        m.EndDate < today &&
                        (m.Status == MembershipStatus.Active ||
                         (m.Status == MembershipStatus.Suspended &&
                          !m.Suspensions.Any(s => s.IsActiveSuspension && s.ExtendExpirationDate))))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in expiredCandidateIds)
        {
            try
            {
                await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await _dbContext.LockMembershipForUpdateAsync(id, cancellationToken);

                var membership = await _dbContext.Memberships
                    .Include(m => m.Suspensions)
                    .Include(m => m.StatusChanges)
                    .FirstOrDefaultAsync(m => m.Id == id && m.IsActive, cancellationToken);

                if (membership == null || membership.EndDate >= today)
                {
                    await tx.CommitAsync(cancellationToken);
                    continue;
                }

                var hasExtendedOpenSuspension = membership.Status == MembershipStatus.Suspended &&
                                                membership.Suspensions.Any(s => s.IsActiveSuspension && s.ExtendExpirationDate);

                if ((membership.Status != MembershipStatus.Active && membership.Status != MembershipStatus.Suspended) ||
                    hasExtendedOpenSuspension)
                {
                    await tx.CommitAsync(cancellationToken);
                    continue;
                }

                membership.Expire(today, nowUtc);
                await _creditLedgerService.ExpireRemainingCreditsAsync(
                    membership,
                    "Expiración de créditos por finalización de vigencia",
                    cancellationToken);

                if (!membership.ExpiredNotificationSentAtUtc.HasValue)
                {
                    try
                    {
                        await _notificationDispatcher.DispatchAsync(
                            new DispatchNotificationCommand(
                                TargetPersonId: membership.StudentId,
                                Type: NotificationType.MembershipExpired,
                                Variables: new Dictionary<string, string?>
                                {
                                    ["planName"] = "Membresía",
                                    ["endDate"] = $"{membership.EndDate:yyyy-MM-dd}"
                                },
                                IdempotencyKey: $"membership-expired:{membership.Id}",
                                RelatedEntityType: "Membership",
                                RelatedEntityId: membership.Id,
                                ActionUrl: "/portal/alumno",
                                DirectTitleOverride: "Membresía vencida",
                                DirectBodyOverride: $"Tu membresía finalizó su período de vigencia el {membership.EndDate:yyyy-MM-dd}.",
                                SaveChanges: false),
                            cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Fallo aislado al preparar notificación de vencimiento para membresía {MembershipId}.", membership.Id);
                    }

                    membership.ExpiredNotificationSentAtUtc = nowUtc;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                expiredCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DiscardFailedBatchItem(ex, id, "vencimiento");
            }
        }

        // 3. Cancelación automática de membresías Pending impagas fuera del plazo de gracia o vencidas
        var graceDays = Math.Max(1, _membershipsOptions.PendingGraceDays);
        var pendingCandidateIds = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.IsActive &&
                        m.Status == MembershipStatus.Pending &&
                        (today > m.StartDate.AddDays(graceDays) || today > m.EndDate))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in pendingCandidateIds)
        {
            try
            {
                await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await _dbContext.LockMembershipForUpdateAsync(id, cancellationToken);

                var membership = await _dbContext.Memberships
                    .Include(m => m.Suspensions)
                    .Include(m => m.StatusChanges)
                    .FirstOrDefaultAsync(m => m.Id == id && m.IsActive, cancellationToken);

                if (membership == null ||
                    membership.Status != MembershipStatus.Pending ||
                    (today <= membership.StartDate.AddDays(graceDays) && today <= membership.EndDate))
                {
                    await tx.CommitAsync(cancellationToken);
                    continue;
                }

                membership.Cancel(
                    reason: "Cancelación automática por falta de pago",
                    cancelledByUserId: null,
                    cancelledAtUtc: nowUtc);

                if (membership.PromotionRedemptionId.HasValue)
                {
                    await _promotionService.VoidRedemptionAsync(membership.PromotionRedemptionId.Value, cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                autoCancelledCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DiscardFailedBatchItem(ex, id, "cancelación automática por falta de pago");
            }
        }

        // 4. Avisos de próximo vencimiento (excluyendo pases cortos y membresías ya renovadas)
        var warningDays = Math.Max(0, _membershipsOptions.ExpiringWarningDays);
        var warningCutoffDate = today.AddDays(warningDays);

        var warningCandidateIds = await _dbContext.Memberships
            .AsNoTracking()
            .Where(m => m.IsActive &&
                        m.Status == MembershipStatus.Active &&
                        m.EndDate >= today &&
                        m.EndDate <= warningCutoffDate &&
                        m.ExpiringNotificationSentAtUtc == null &&
                        m.DurationDaysSnapshot > warningDays &&
                        !_dbContext.Memberships.Any(r =>
                            r.IsActive &&
                            r.RenewedFromMembershipId == m.Id &&
                            r.Status != MembershipStatus.Cancelled))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in warningCandidateIds)
        {
            try
            {
                await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await _dbContext.LockMembershipForUpdateAsync(id, cancellationToken);

                var membership = await _dbContext.Memberships
                    .FirstOrDefaultAsync(m => m.Id == id && m.IsActive, cancellationToken);

                if (membership == null ||
                    membership.Status != MembershipStatus.Active ||
                    membership.EndDate < today ||
                    membership.EndDate > warningCutoffDate ||
                    membership.ExpiringNotificationSentAtUtc.HasValue ||
                    membership.DurationDaysSnapshot <= warningDays)
                {
                    await tx.CommitAsync(cancellationToken);
                    continue;
                }

                var hasActiveRenewal = await _dbContext.Memberships
                    .AnyAsync(r =>
                        r.IsActive &&
                        r.RenewedFromMembershipId == membership.Id &&
                        r.Status != MembershipStatus.Cancelled,
                        cancellationToken);

                if (hasActiveRenewal)
                {
                    await tx.CommitAsync(cancellationToken);
                    continue;
                }

                try
                {
                    await _notificationDispatcher.DispatchAsync(
                        new DispatchNotificationCommand(
                            TargetPersonId: membership.StudentId,
                            Type: NotificationType.MembershipExpiring,
                            Variables: new Dictionary<string, string?>
                            {
                                ["planName"] = "Membresía",
                                ["endDate"] = $"{membership.EndDate:yyyy-MM-dd}"
                            },
                            IdempotencyKey: $"membership-expiring:{membership.Id}",
                            RelatedEntityType: "Membership",
                            RelatedEntityId: membership.Id,
                            ActionUrl: "/portal/alumno",
                            DirectTitleOverride: "Tu membresía está próxima a vencer",
                            DirectBodyOverride: $"Tu membresía vence el {membership.EndDate:yyyy-MM-dd}.",
                            SaveChanges: false),
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Fallo aislado al preparar notificación de próximo vencimiento para membresía {MembershipId}.", membership.Id);
                }

                membership.ExpiringNotificationSentAtUtc = nowUtc;
                membership.UpdatedAtUtc = nowUtc;

                await _dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                warningsSentCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DiscardFailedBatchItem(ex, id, "aviso de vencimiento próximo");
            }
        }

        if (_auditService != null &&
            (reactivatedCount > 0 || expiredCount > 0 || autoCancelledCount > 0 || warningsSentCount > 0))
        {
            await _auditService.LogAsync(
                "MEMBERSHIPS_EXPIRATION_BATCH",
                "Membership",
                null,
                null,
                JsonSerializer.Serialize(new
                {
                    ReactivatedCount = reactivatedCount,
                    ExpiredCount = expiredCount,
                    AutoCancelledCount = autoCancelledCount,
                    WarningsSentCount = warningsSentCount,
                    ProcessedDate = today
                }),
                null,
                null,
                null,
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new MembershipExpirationBatchResultDto(
            reactivatedCount,
            expiredCount,
            autoCancelledCount,
            warningsSentCount);
    }

    /// <summary>
    /// Un fallo en una membresía no detiene el lote: se registra y se descartan sus cambios pendientes
    /// para que no se persistan junto con los de la siguiente membresía.
    /// </summary>
    private void DiscardFailedBatchItem(Exception exception, Guid membershipId, string step)
    {
        _dbContext.ChangeTracker.Clear();
        _logger.LogError(
            exception,
            "Falló el paso '{Step}' del lote de membresías para la membresía {MembershipId}.",
            step,
            membershipId);
    }

    private static void EnsureConcurrencyVersion(Membership membership, uint? expectedVersion)
    {
        if (expectedVersion.HasValue && membership.Version != expectedVersion.Value)
        {
            throw new ConflictException("La membresía fue modificada por otro usuario. Recargue los datos e intente nuevamente.");
        }
    }

    private async Task SaveChangesWithConcurrencyCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("La membresía fue modificada concurrentemente por otro usuario. Recargue los datos e intente nuevamente.");
        }
    }

    private async Task<Dictionary<Guid, decimal>> GetCompletedPaidAmountsByMembershipAsync(
        IReadOnlyCollection<Guid> membershipIds,
        CancellationToken cancellationToken)
    {
        if (membershipIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var payments = await _dbContext.Payments
            .AsNoTracking()
            .Where(p => p.MembershipId.HasValue &&
                        membershipIds.Contains(p.MembershipId.Value) &&
                        p.IsActive &&
                        (p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.PartiallyRefunded))
            .Select(p => new { MembershipId = p.MembershipId!.Value, NetAmount = p.Amount - p.RefundedAmount })
            .ToListAsync(cancellationToken);

        return payments
            .GroupBy(p => p.MembershipId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.NetAmount));
    }

    private static MembershipSummaryDto MapToSummaryDto(
        Membership m,
        DateOnly today,
        decimal totalPaidAmount)
    {
        var effectiveStatus = m.GetEffectiveStatus(today);
        var pendingBalance = Math.Max(0m, m.PurchasePrice - totalPaidAmount);
        var daysRemaining = effectiveStatus == MembershipStatus.Expired || effectiveStatus == MembershipStatus.Cancelled
            ? 0
            : Math.Max(0, m.EndDate.DayNumber - today.DayNumber + 1);

        return new MembershipSummaryDto(
            Id: m.Id,
            StudentId: m.StudentId,
            StudentName: m.Student?.FullName ?? string.Empty,
            StudentDni: m.Student?.Dni ?? string.Empty,
            MembershipPlanId: m.MembershipPlanId,
            MembershipPlanName: m.MembershipPlan?.Name ?? string.Empty,
            StartDate: m.StartDate,
            EndDate: m.EndDate,
            TotalCredits: m.TotalCredits,
            AvailableCredits: m.AvailableCredits,
            Status: m.Status,
            IsValidToday: effectiveStatus == MembershipStatus.Active && today >= m.StartDate && today <= m.EndDate,
            DocumentType: m.Student?.Document?.Type,
            DocumentNumber: m.Student?.Document?.Number,
            RenewedFromMembershipId: m.RenewedFromMembershipId,
            EffectiveStatus: effectiveStatus,
            PurchasedAtUtc: m.PurchasedAtUtc,
            ActivatedAtUtc: m.ActivatedAtUtc,
            DurationDaysSnapshot: m.DurationDaysSnapshot,
            InitialCredits: m.InitialCredits,
            ExtraCreditsGranted: m.ExtraCreditsGranted,
            IsUnlimited: m.IsUnlimited,
            AppliesToAllActivities: m.AppliesToAllActivities,
            InitialCreditsLoaded: m.InitialCreditsLoaded,
            HasAvailableCredits: m.HasAvailableCredit(),
            IsDepleted: m.IsDepleted,
            StartsInFuture: m.StartDate > today,
            DaysRemaining: daysRemaining,
            OriginalPrice: m.OriginalPrice,
            DiscountAmount: m.DiscountAmount,
            PurchasePrice: m.PurchasePrice,
            Currency: m.Currency,
            TotalPaidAmount: totalPaidAmount,
            PendingBalance: pendingBalance,
            HasOutstandingBalance: pendingBalance > 0m,
            PromotionId: m.PromotionId,
            PromotionCode: m.Promotion?.Code,
            Notes: m.Notes,
            Version: m.Version);
    }

    private static MembershipListItemDto MapToListItemDto(
        Membership m,
        DateOnly today,
        decimal totalPaidAmount)
    {
        var s = MapToSummaryDto(m, today, totalPaidAmount);
        return new MembershipListItemDto(
            Id: s.Id,
            StudentId: s.StudentId,
            StudentName: s.StudentName,
            StudentDni: s.StudentDni,
            MembershipPlanId: s.MembershipPlanId,
            MembershipPlanName: s.MembershipPlanName,
            StartDate: s.StartDate,
            EndDate: s.EndDate,
            TotalCredits: s.TotalCredits,
            AvailableCredits: s.AvailableCredits,
            Status: s.Status,
            IsValidToday: s.IsValidToday,
            DocumentType: s.DocumentType,
            DocumentNumber: s.DocumentNumber,
            RenewedFromMembershipId: s.RenewedFromMembershipId,
            EffectiveStatus: s.EffectiveStatus,
            PurchasedAtUtc: s.PurchasedAtUtc,
            ActivatedAtUtc: s.ActivatedAtUtc,
            DurationDaysSnapshot: s.DurationDaysSnapshot,
            InitialCredits: s.InitialCredits,
            ExtraCreditsGranted: s.ExtraCreditsGranted,
            IsUnlimited: s.IsUnlimited,
            AppliesToAllActivities: s.AppliesToAllActivities,
            InitialCreditsLoaded: s.InitialCreditsLoaded,
            HasAvailableCredits: s.HasAvailableCredits,
            IsDepleted: s.IsDepleted,
            StartsInFuture: s.StartsInFuture,
            DaysRemaining: s.DaysRemaining,
            OriginalPrice: s.OriginalPrice,
            DiscountAmount: s.DiscountAmount,
            PurchasePrice: s.PurchasePrice,
            Currency: s.Currency,
            TotalPaidAmount: s.TotalPaidAmount,
            PendingBalance: s.PendingBalance,
            HasOutstandingBalance: s.HasOutstandingBalance,
            PromotionId: s.PromotionId,
            PromotionCode: s.PromotionCode,
            Notes: s.Notes,
            Version: s.Version);
    }

    private static MembershipDetailDto MapToDetailDto(
        Membership m,
        DateOnly today,
        IReadOnlyList<Payment> payments,
        MembershipCreditsSummaryDto creditsSummary)
    {
        var totalPaidAmount = payments
            .Where(p => p.Status.HasNetAmount())
            .Sum(p => p.NetAmount);

        var summary = MapToSummaryDto(m, today, totalPaidAmount);

        var allowedActivityIds = m.AllowedActivities
            .Select(a => a.ActivityId)
            .Distinct()
            .ToList();

        var paymentDtos = payments
            .Select(p => new PaymentDto(
                p.Id,
                p.PayerPersonId,
                p.PayerPerson?.FullName ?? summary.StudentName,
                p.MembershipId,
                p.ClassSessionId,
                p.Amount,
                p.Method,
                p.Status,
                p.ExternalTransactionId,
                p.ReceiptUrl,
                p.Notes,
                p.PaidAtUtc,
                p.Provider,
                p.Currency,
                p.RefundedAmount,
                p.NetAmount,
                p.ConfirmedAtUtc,
                p.CancelledAtUtc))
            .ToList();

        var suspensionDtos = m.Suspensions
            .OrderByDescending(s => s.SuspendedAtUtc)
            .Select(s => new MembershipSuspensionDto(
                s.Id,
                s.StartDate,
                s.EndDate,
                s.ReactivatedAtDate,
                s.ExtendExpirationDate,
                s.ExtensionDaysApplied,
                s.Reason,
                s.Notes,
                s.SuspendedByUserId,
                s.SuspendedAtUtc,
                s.ReactivatedByUserId,
                s.ReactivatedAtUtc,
                s.IsActiveSuspension))
            .ToList();

        var statusChangeDtos = m.StatusChanges
            .OrderByDescending(sc => sc.ChangedAtUtc)
            .Select(sc => new MembershipStatusChangeDto(
                sc.Id,
                sc.PreviousStatus,
                sc.NewStatus,
                sc.Reason,
                sc.Notes,
                sc.ChangedByUserId,
                sc.ChangedAtUtc))
            .ToList();

        return new MembershipDetailDto(
            Id: summary.Id,
            StudentId: summary.StudentId,
            StudentName: summary.StudentName,
            StudentDni: summary.StudentDni,
            MembershipPlanId: summary.MembershipPlanId,
            MembershipPlanName: summary.MembershipPlanName,
            StartDate: summary.StartDate,
            EndDate: summary.EndDate,
            TotalCredits: summary.TotalCredits,
            AvailableCredits: summary.AvailableCredits,
            Status: summary.Status,
            IsValidToday: summary.IsValidToday,
            DocumentType: summary.DocumentType,
            DocumentNumber: summary.DocumentNumber,
            RenewedFromMembershipId: summary.RenewedFromMembershipId,
            EffectiveStatus: summary.EffectiveStatus,
            PurchasedAtUtc: summary.PurchasedAtUtc,
            ActivatedAtUtc: summary.ActivatedAtUtc,
            DurationDaysSnapshot: summary.DurationDaysSnapshot,
            InitialCredits: summary.InitialCredits,
            ExtraCreditsGranted: summary.ExtraCreditsGranted,
            IsUnlimited: summary.IsUnlimited,
            AppliesToAllActivities: summary.AppliesToAllActivities,
            InitialCreditsLoaded: summary.InitialCreditsLoaded,
            HasAvailableCredits: summary.HasAvailableCredits,
            IsDepleted: summary.IsDepleted,
            StartsInFuture: summary.StartsInFuture,
            DaysRemaining: summary.DaysRemaining,
            OriginalPrice: summary.OriginalPrice,
            DiscountAmount: summary.DiscountAmount,
            PurchasePrice: summary.PurchasePrice,
            Currency: summary.Currency,
            TotalPaidAmount: summary.TotalPaidAmount,
            PendingBalance: summary.PendingBalance,
            HasOutstandingBalance: summary.HasOutstandingBalance,
            PromotionId: summary.PromotionId,
            PromotionCode: summary.PromotionCode,
            PromotionName: m.Promotion?.Name,
            PromotionRedemptionId: m.PromotionRedemptionId,
            CreatedByUserId: m.CreatedByUserId,
            CancelledAtUtc: m.CancelledAtUtc,
            CancelledByUserId: m.CancelledByUserId,
            CancellationReason: m.CancellationReason,
            Notes: summary.Notes,
            Version: summary.Version,
            AllowedActivityIds: allowedActivityIds,
            Payments: paymentDtos,
            Suspensions: suspensionDtos,
            StatusChanges: statusChangeDtos,
            CreditsSummary: creditsSummary);
    }
}
