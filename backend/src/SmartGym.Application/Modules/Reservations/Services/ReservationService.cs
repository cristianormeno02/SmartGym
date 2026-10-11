using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Reservations.Services;

public class ReservationService : IReservationService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly ICreditLedgerService _creditLedgerService;
    private readonly IMembershipEligibilityService _eligibilityService;
    private readonly IPromotionService _promotionService;
    private readonly IReservationAccessResolver _accessResolver;
    private readonly ICurrentUserService? _currentUserService;
    private readonly IAuditService _auditService;
    private readonly ReservationsOptions _options;

    public ReservationService(
        ISmartGymDbContext dbContext,
        IGymCalendar gymCalendar,
        ICreditLedgerService? creditLedgerService = null,
        IMembershipEligibilityService? eligibilityService = null,
        IPromotionService? promotionService = null,
        ICurrentUserService? currentUserService = null,
        IAuditService? auditService = null,
        Microsoft.Extensions.Options.IOptions<ReservationsOptions>? reservationsOptions = null,
        Microsoft.Extensions.Options.IOptions<GymOptions>? gymOptions = null,
        IReservationAccessResolver? accessResolver = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
        _creditLedgerService = creditLedgerService ?? new CreditLedgerService(dbContext, gymCalendar);
        _eligibilityService = eligibilityService ?? new MembershipEligibilityService(dbContext, gymCalendar, gymOptions);
        _promotionService = promotionService ?? new PromotionService(dbContext, gymCalendar);
        _accessResolver = accessResolver ?? new ReservationAccessResolver(dbContext, _eligibilityService, _creditLedgerService, _promotionService, gymOptions);
        _currentUserService = currentUserService;
        _auditService = auditService ?? new AuditService(dbContext);
        _options = reservationsOptions?.Value ?? new ReservationsOptions();
    }

    public async Task<ReservationDto> CreateReservationAsync(
        CreateReservationRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var isStaff = _currentUserService != null &&
                      _currentUserService.IsAuthenticated &&
                      (_currentUserService.IsInRole(Roles.Administrator) || _currentUserService.IsInRole(Roles.Secretary));

        if (_currentUserService != null && _currentUserService.IsAuthenticated && !isStaff)
        {
            if (request.StudentId.HasValue &&
                _currentUserService.PersonId.HasValue &&
                request.StudentId.Value != _currentUserService.PersonId.Value)
            {
                throw new ForbiddenException("Un alumno sólo puede crear reservas para sí mismo.");
            }

            if (request.OverrideBookingWindow)
            {
                throw new ForbiddenException("No posee permiso para omitir la ventana de anticipación.");
            }
        }

        if (request.OverrideBookingWindow && string.IsNullOrWhiteSpace(request.PolicyOverrideReason))
        {
            throw new ArgumentException("Debe indicar un motivo obligatorio para omitir la ventana de anticipación.");
        }

        var targetStudentId = request.StudentId
            ?? _currentUserService?.PersonId
            ?? throw new ArgumentException("Debe indicar el alumno para crear la reserva.");

        var origin = isStaff ? ReservationOrigin.StaffDesk : ReservationOrigin.StudentSelfService;
        var trigger = isStaff ? ReservationChangeTrigger.StaffAction : ReservationChangeTrigger.StudentAction;
        Guid? actorUserId = currentUserId != Guid.Empty ? currentUserId : _currentUserService?.UserId;
        var normalizedKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // 1. Bloquear la clase antes de leerla (orden único de bloqueo: ClassSessions -> Memberships)
        await LockClassSessionTranslatingConcurrencyAsync(request.ClassSessionId, cancellationToken);

        // 2. Verificar idempotencia antes de cualquier validación de negocio
        if (normalizedKey != null)
        {
            var existingByKey = await _dbContext.Reservations
                .Include(r => r.ClassSession)
                    .ThenInclude(cs => cs.Activity)
                .Include(r => r.ClassSession)
                    .ThenInclude(cs => cs.Room)
                .Include(r => r.Student)
                .Include(r => r.StatusChanges)
                .FirstOrDefaultAsync(r => r.StudentId == targetStudentId && r.IdempotencyKey == normalizedKey, cancellationToken);

            if (existingByKey != null)
            {
                if (existingByKey.ClassSessionId != request.ClassSessionId)
                {
                    throw ConflictException.WithCode(
                        ReservationErrorCodes.IdempotencyKeyMismatch,
                        "La clave de idempotencia ya fue utilizada para otra clase.");
                }

                return MapToDto(existingByKey) with { WasIdempotentReplay = true };
            }
        }

        // 3. Validar la clase y la ventana de anticipación
        var session = await _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .Include(cs => cs.Room)
            .FirstOrDefaultAsync(cs => cs.Id == request.ClassSessionId && cs.IsActive, cancellationToken);

        if (session == null)
        {
            throw new NotFoundException("La clase especificada no existe o no está activa.");
        }

        if (session.Status != ClassSessionStatus.Open)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.ClassNotOpen,
                $"No se pueden realizar reservas para una clase en estado {session.Status}.");
        }

        var nowUtc = _gymCalendar.UtcNow;
        if (nowUtc >= session.StartsAtUtc)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.ClassAlreadyStarted,
                "No se pueden realizar reservas para una clase que ya ha iniciado o pasado.");
        }

        if (!request.OverrideBookingWindow)
        {
            var minutesUntilStart = (session.StartsAtUtc - nowUtc).TotalMinutes;
            var isBeforeMinAdvance = _options.MinAdvanceBookingMinutes > 0 && minutesUntilStart < _options.MinAdvanceBookingMinutes;
            var isAfterMaxAdvance = _options.MaxAdvanceBookingDays > 0 && session.StartsAtUtc > nowUtc.AddDays(_options.MaxAdvanceBookingDays);

            if (isBeforeMinAdvance || isAfterMaxAdvance)
            {
                throw ConflictException.WithCode(
                    ReservationErrorCodes.BookingWindowClosed,
                    "La clase se encuentra fuera de la ventana de anticipación permitida para reservar.");
            }
        }

        // 4. Obtener alumno y validar duplicidad activa
        var student = await _dbContext.People
            .Include(p => p.PersonRoles)
                .ThenInclude(pr => pr.Role)
            .FirstOrDefaultAsync(p => p.Id == targetStudentId, cancellationToken);

        if (student == null)
        {
            throw new NotFoundException("El alumno especificado no existe.");
        }

        var existingReservation = await _dbContext.Reservations
            .AnyAsync(r => r.ClassSessionId == session.Id &&
                           r.StudentId == student.Id &&
                           r.IsActive &&
                           (r.Status == ReservationStatus.Confirmed ||
                            r.Status == ReservationStatus.Waitlisted ||
                            r.Status == ReservationStatus.Pending),
                      cancellationToken);

        if (existingReservation)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.AlreadyReserved,
                "El alumno ya posee una reserva o se encuentra en lista de espera para esta clase.");
        }

        // 5. Bloquear membresías del alumno y resolver el derecho de acceso
        await LockStudentMembershipsTranslatingConcurrencyAsync(student.Id, cancellationToken);

        var sessionLocalDate = DateOnly.FromDateTime(_gymCalendar.ToLocal(session.StartsAtUtc).DateTime);
        var resolvedAccess = await _accessResolver.ResolveAsync(
            student,
            session,
            sessionLocalDate,
            new AccessResolutionRequest(
                request.MembershipId,
                request.SingleClassTicketMovementId,
                request.PromotionCode),
            excludingReservationId: null,
            cancellationToken: cancellationToken);

        // 6. Control de cupos, consumo al confirmar o ingreso en lista de espera
        Reservation reservation;

        if (session.ReservedCount < session.Capacity)
        {
            reservation = Reservation.CreateConfirmed(
                classSessionId: session.Id,
                studentId: student.Id,
                origin: origin,
                accessType: resolvedAccess.AccessType,
                nowUtc: nowUtc,
                createdByUserId: actorUserId,
                membershipId: resolvedAccess.SelectedMembership?.Id,
                isPolicyOverride: request.OverrideBookingWindow,
                policyOverrideReason: request.OverrideBookingWindow ? request.PolicyOverrideReason?.Trim() : null,
                notes: request.Notes,
                idempotencyKey: normalizedKey);

            var consumed = await _accessResolver.ConsumeAsync(
                resolvedAccess,
                student,
                session,
                sessionLocalDate,
                reservation.Id,
                actorUserId,
                cancellationToken);

            reservation.AccessType = consumed.AccessType;
            reservation.MembershipId = consumed.MembershipId;
            reservation.CreditMovementId = consumed.CreditMovementId;
            reservation.SingleClassTicketMovementId = consumed.SingleClassTicketMovementId;
            reservation.PaymentId = consumed.PaymentId;
            reservation.PromotionRedemptionId = consumed.PromotionRedemptionId;

            session.IncrementReservedCount();
        }
        else
        {
            if (!_options.EnableWaitlist || !request.AllowWaitlist)
            {
                throw ConflictException.WithCode(
                    ReservationErrorCodes.ClassFull,
                    "La clase no posee cupos disponibles.");
            }

            var currentWaitlistCount = await _dbContext.Reservations
                .CountAsync(r => r.ClassSessionId == session.Id && r.IsActive && r.Status == ReservationStatus.Waitlisted, cancellationToken);

            if (currentWaitlistCount >= _options.MaxWaitlistSize)
            {
                throw ConflictException.WithCode(
                    ReservationErrorCodes.WaitlistFull,
                    "La lista de espera de la clase se encuentra completa.");
            }

            reservation = Reservation.CreateWaitlisted(
                classSessionId: session.Id,
                studentId: student.Id,
                position: currentWaitlistCount + 1,
                origin: origin,
                accessType: resolvedAccess.AccessType,
                nowUtc: nowUtc,
                createdByUserId: actorUserId,
                membershipId: resolvedAccess.SelectedMembership?.Id ?? request.MembershipId,
                singleClassTicketMovementId: resolvedAccess.SelectedMovement?.Id ?? request.SingleClassTicketMovementId,
                requestedPromotionCode: resolvedAccess.PromotionCode ?? request.PromotionCode,
                isPolicyOverride: request.OverrideBookingWindow,
                policyOverrideReason: request.OverrideBookingWindow ? request.PolicyOverrideReason?.Trim() : null,
                notes: request.Notes,
                idempotencyKey: normalizedKey);
        }

        _dbContext.Reservations.Add(reservation);
        await SaveChangesTranslatingConcurrencyAsync(cancellationToken);

        var auditAction = reservation.Status == ReservationStatus.Waitlisted
            ? "RESERVATION_WAITLISTED"
            : (request.OverrideBookingWindow ? "RESERVATION_CREATED_POLICY_OVERRIDE" : "RESERVATION_CREATED");

        await _auditService.LogAsync(
            auditAction,
            "Reservation",
            reservation.Id.ToString(),
            null,
            JsonSerializer.Serialize(new
            {
                reservation.StudentId,
                reservation.ClassSessionId,
                reservation.Status,
                reservation.Origin,
                reservation.AccessType,
                reservation.MembershipId,
                reservation.CreditMovementId,
                reservation.SingleClassTicketMovementId,
                reservation.PromotionRedemptionId,
                reservation.CreatedByUserId,
                reservation.IsPolicyOverride,
                reservation.PolicyOverrideReason
            }),
            actorUserId,
            _currentUserService?.Email,
            null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        reservation.ClassSession = session;
        reservation.Student = student;

        return MapToDto(reservation);
    }

    private bool IsStaffCaller =>
        _currentUserService != null &&
        _currentUserService.IsAuthenticated &&
        (_currentUserService.IsInRole(Roles.Administrator) || _currentUserService.IsInRole(Roles.Secretary));

    public async Task<ReservationDto> CancelReservationAsync(
        Guid reservationId,
        CancelReservationRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var isStaff = IsStaffCaller;

        if (_currentUserService != null && _currentUserService.IsAuthenticated && !isStaff && request.OverridePolicy)
        {
            throw new ForbiddenException("No posee permiso para omitir la política de cancelación.");
        }

        if (request.OverridePolicy && string.IsNullOrWhiteSpace(request.PolicyOverrideReason))
        {
            throw new ArgumentException("Debe indicar un motivo obligatorio para omitir la política de cancelación.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var reservationKeys = await _dbContext.Reservations
            .Where(r => r.Id == reservationId && r.IsActive)
            .Select(r => new { r.ClassSessionId, r.StudentId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Reserva no encontrada.");

        if (_currentUserService != null &&
            _currentUserService.IsAuthenticated &&
            !isStaff &&
            _currentUserService.PersonId.HasValue &&
            reservationKeys.StudentId != _currentUserService.PersonId.Value)
        {
            throw new ForbiddenException("Un alumno sólo puede cancelar sus propias reservas.");
        }

        var classSessionId = reservationKeys.ClassSessionId;

        // Bloquear la clase antes de leer la reserva (orden único de bloqueo: ClassSessions -> Memberships)
        await LockClassSessionTranslatingConcurrencyAsync(classSessionId, cancellationToken);
        await LockStudentMembershipsTranslatingConcurrencyAsync(reservationKeys.StudentId, cancellationToken);

        var reservation = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .Include(r => r.Membership)
                .ThenInclude(m => m!.MembershipPlan)
            .Include(r => r.StatusChanges)
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.IsActive, cancellationToken)
            ?? throw new NotFoundException("Reserva no encontrada.");

        if (reservation.Status != ReservationStatus.Confirmed &&
            reservation.Status != ReservationStatus.Waitlisted &&
            reservation.Status != ReservationStatus.Pending)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.InvalidStateTransition,
                $"No se puede cancelar una reserva en estado {reservation.Status}.");
        }

        var session = reservation.ClassSession;
        var nowUtc = _gymCalendar.UtcNow;

        if (nowUtc >= session.StartsAtUtc)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.ClassAlreadyStarted,
                "No se puede cancelar una reserva para una clase que ya ha iniciado o pasado.");
        }

        var wasConfirmed = reservation.Status == ReservationStatus.Confirmed;
        var wasPending = reservation.Status == ReservationStatus.Pending;
        var wasWaitlisted = reservation.Status == ReservationStatus.Waitlisted;

        var hoursRemaining = (session.StartsAtUtc - nowUtc).TotalHours;
        var isLateCancellation = wasConfirmed &&
                                 hoursRemaining < _options.FreeCancellationHoursBeforeStart &&
                                 !request.OverridePolicy;

        Guid? actorUserId = currentUserId != Guid.Empty ? currentUserId : _currentUserService?.UserId;
        var source = isStaff ? ReservationCancellationSource.Staff : ReservationCancellationSource.Student;
        var trigger = isStaff ? ReservationChangeTrigger.StaffAction : ReservationChangeTrigger.StudentAction;
        var effectiveReason = string.IsNullOrWhiteSpace(request.Reason)
            ? (isStaff ? "Cancelada por el personal" : "Cancelada por el alumno")
            : request.Reason.Trim();

        Guid? refundMovementId = null;

        if (wasConfirmed && !isLateCancellation)
        {
            if (reservation.Membership != null)
            {
                var refundMovement = await _creditLedgerService.RefundCreditForReservationAsync(
                    reservation.Membership,
                    session.Id,
                    $"Devolución por cancelación anticipada ({hoursRemaining:F1}h antes)",
                    actorUserId,
                    reservation.Id,
                    cancellationToken);
                refundMovementId = refundMovement.Id;
            }

            if (reservation.PromotionRedemptionId.HasValue)
            {
                await _promotionService.VoidRedemptionAsync(reservation.PromotionRedemptionId.Value, cancellationToken);
            }
        }

        reservation.Cancel(
            effectiveReason,
            isLateCancellation,
            nowUtc,
            refundMovementId,
            actorUserId,
            source,
            trigger,
            request.OverridePolicy,
            request.OverridePolicy ? request.PolicyOverrideReason?.Trim() : null);

        if (wasConfirmed || wasPending)
        {
            session.DecrementReservedCount();
            await PromoteWaitlistAsync(session, actorUserId, cancellationToken);
        }
        else if (wasWaitlisted)
        {
            await CompactWaitlistPositionsAsync(session.Id, cancellationToken);
        }

        await SaveChangesTranslatingConcurrencyAsync(cancellationToken);

        var auditAction = request.OverridePolicy ? "RESERVATION_CANCELLED_POLICY_OVERRIDE" : "RESERVATION_CANCELLED";
        await _auditService.LogAsync(
            auditAction,
            "Reservation",
            reservation.Id.ToString(),
            null,
            JsonSerializer.Serialize(new
            {
                reservation.StudentId,
                reservation.ClassSessionId,
                reservation.Status,
                reservation.CancellationReason,
                reservation.CancellationSource,
                reservation.IsLateCancellation,
                reservation.RefundCreditMovementId,
                reservation.IsPolicyOverride,
                reservation.PolicyOverrideReason
            }),
            actorUserId,
            _currentUserService?.Email,
            null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return MapToDto(reservation);
    }

    public async Task<int> PromoteWaitlistAsync(
        ClassSession session,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableWaitlist || session.Status != ClassSessionStatus.Open)
        {
            return 0;
        }

        var nowUtc = _gymCalendar.UtcNow;
        var minutesUntilStart = (session.StartsAtUtc - nowUtc).TotalMinutes;
        if (minutesUntilStart < _options.MinMinutesBeforeStartForWaitlistPromotion)
        {
            return 0;
        }

        var freeSpots = session.Capacity - session.ReservedCount;
        if (freeSpots <= 0)
        {
            return 0;
        }

        var queriedWaitList = await _dbContext.Reservations
            .Include(r => r.Student)
                .ThenInclude(s => s.PersonRoles)
                    .ThenInclude(pr => pr.Role)
            .Include(r => r.StatusChanges)
            .Where(r => r.ClassSessionId == session.Id && r.IsActive && r.Status == ReservationStatus.Waitlisted)
            .ToListAsync(cancellationToken);

        var waitList = queriedWaitList
            .Where(r => r.IsActive && r.Status == ReservationStatus.Waitlisted)
            .OrderBy(r => r.WaitListPosition)
            .ThenBy(r => r.WaitlistEnteredAtUtc)
            .ThenBy(r => r.Id)
            .ToList();

        if (waitList.Count == 0)
        {
            return 0;
        }

        var sessionLocalDate = DateOnly.FromDateTime(_gymCalendar.ToLocal(session.StartsAtUtc).DateTime);
        var promotedCount = 0;

        foreach (var candidate in waitList)
        {
            if (session.ReservedCount >= session.Capacity)
            {
                break;
            }

            await LockStudentMembershipsTranslatingConcurrencyAsync(candidate.StudentId, cancellationToken);

            ResolvedReservationAccess resolvedAccess;
            try
            {
                resolvedAccess = await _accessResolver.ResolveAsync(
                    candidate.Student,
                    session,
                    sessionLocalDate,
                    new AccessResolutionRequest(
                        candidate.MembershipId,
                        candidate.SingleClassTicketMovementId,
                        candidate.RequestedPromotionCode),
                    excludingReservationId: candidate.Id,
                    cancellationToken: cancellationToken);
            }
            catch (ConflictException)
            {
                // El candidato perdió elegibilidad transitoriamente: conserva su lugar y se evalúa al siguiente
                continue;
            }
            catch (NotFoundException)
            {
                continue;
            }

            if (_options.WaitlistPromotionMode == WaitlistPromotionMode.OfferWithConfirmation)
            {
                var expiresAtUtc = nowUtc.AddMinutes(_options.WaitlistOfferExpirationMinutes);
                if (expiresAtUtc > session.StartsAtUtc)
                {
                    expiresAtUtc = session.StartsAtUtc;
                }

                if (expiresAtUtc <= nowUtc)
                {
                    break;
                }

                candidate.OfferSpot(
                    nowUtc,
                    expiresAtUtc,
                    actorUserId,
                    ReservationChangeTrigger.WaitlistPromotion);

                session.IncrementReservedCount();
                promotedCount++;
            }
            else
            {
                var consumed = await _accessResolver.ConsumeAsync(
                    resolvedAccess,
                    candidate.Student,
                    session,
                    sessionLocalDate,
                    candidate.Id,
                    actorUserId,
                    cancellationToken);

                candidate.Confirm(
                    nowUtc: nowUtc,
                    changedByUserId: actorUserId,
                    trigger: ReservationChangeTrigger.WaitlistPromotion,
                    accessType: consumed.AccessType,
                    membershipId: consumed.MembershipId,
                    creditMovementId: consumed.CreditMovementId,
                    singleClassTicketMovementId: consumed.SingleClassTicketMovementId,
                    paymentId: consumed.PaymentId,
                    promotionRedemptionId: consumed.PromotionRedemptionId);

                session.IncrementReservedCount();
                promotedCount++;
            }
        }

        if (promotedCount > 0)
        {
            await CompactWaitlistPositionsAsync(session.Id, cancellationToken);
        }

        return promotedCount;
    }

    public async Task CompactWaitlistPositionsAsync(
        Guid classSessionId,
        CancellationToken cancellationToken = default)
    {
        var queried = await _dbContext.Reservations
            .Where(r => r.ClassSessionId == classSessionId && r.IsActive && r.Status == ReservationStatus.Waitlisted)
            .ToListAsync(cancellationToken);

        var remaining = queried
            .Where(r => r.IsActive && r.Status == ReservationStatus.Waitlisted)
            .OrderBy(r => r.WaitListPosition)
            .ThenBy(r => r.WaitlistEnteredAtUtc)
            .ThenBy(r => r.Id)
            .ToList();

        var nowUtc = _gymCalendar.UtcNow;
        var changed = new List<(Reservation Entry, int TargetPosition)>();

        for (var i = 0; i < remaining.Count; i++)
        {
            var targetPosition = i + 1;
            if (remaining[i].WaitListPosition != targetPosition)
            {
                changed.Add((remaining[i], targetPosition));
            }
        }

        if (changed.Count == 0)
        {
            return;
        }

        // No existe índice único sobre la posición: se renumera directamente, sin valores intermedios
        // (que violarían CK_Reservations_WaitlistPosition).
        foreach (var (entry, targetPosition) in changed)
        {
            entry.WaitListPosition = targetPosition;
            entry.UpdatedAtUtc = nowUtc;
        }
    }

    private static bool IsConcurrencyOrDeadlockException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
            {
                return true;
            }

            var sqlStateProp = current.GetType().GetProperty("SqlState");
            if (sqlStateProp?.GetValue(current) is string sqlState &&
                string.Equals(sqlState, "40P01", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<T> ExecuteTranslatingConcurrencyAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ConflictException)
        {
            throw;
        }
        catch (Exception ex) when (IsConcurrencyOrDeadlockException(ex))
        {
            throw new ConflictException(
                "La membresía o la clase fue modificada por otra operación. Intente nuevamente.",
                ex)
            {
                Code = ReservationErrorCodes.ConcurrencyConflict
            };
        }
    }

    private Task LockClassSessionTranslatingConcurrencyAsync(Guid classSessionId, CancellationToken cancellationToken)
        => ExecuteTranslatingConcurrencyAsync(async () =>
        {
            await _dbContext.LockClassSessionForUpdateAsync(classSessionId, cancellationToken);
            return true;
        });

    private Task LockStudentMembershipsTranslatingConcurrencyAsync(Guid studentId, CancellationToken cancellationToken)
        => ExecuteTranslatingConcurrencyAsync(async () =>
        {
            await _dbContext.LockStudentMembershipsForUpdateAsync(studentId, cancellationToken);
            return true;
        });

    private Task SaveChangesTranslatingConcurrencyAsync(CancellationToken cancellationToken)
        => ExecuteTranslatingConcurrencyAsync(() => _dbContext.SaveChangesAsync(cancellationToken));

    public async Task<ReservationDto> RecordAttendanceAsync(
        RecordAttendanceRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var sessionKey = await _dbContext.Reservations
            .Where(r => r.Id == request.ReservationId && r.IsActive)
            .Select(r => new { r.ClassSessionId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Reserva no encontrada.");

        await LockClassSessionTranslatingConcurrencyAsync(sessionKey.ClassSessionId, cancellationToken);

        var reservation = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .Include(r => r.StatusChanges)
            .FirstOrDefaultAsync(r => r.Id == request.ReservationId && r.IsActive, cancellationToken)
            ?? throw new NotFoundException("Reserva no encontrada.");

        var session = reservation.ClassSession;
        EnsureInstructorOrStaffScopeForSession(session);

        if (!session.IsActive ||
            session.Status is ClassSessionStatus.Suspended or ClassSessionStatus.Cancelled or ClassSessionStatus.Rescheduled)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.ClassNotOpen,
                "No se puede registrar asistencia en una clase suspendida, cancelada o reprogramada.");
        }

        if (reservation.Status != ReservationStatus.Confirmed)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.InvalidStateTransition,
                $"Sólo se puede registrar asistencia sobre una reserva confirmada (estado actual: {reservation.Status}).");
        }

        if (!reservation.Student.IsActive)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.NoAccessRight,
                "No se puede registrar asistencia para un alumno que no se encuentra activo.");
        }

        var nowUtc = _gymCalendar.UtcNow;
        var windowStartUtc = session.StartsAtUtc.AddMinutes(-_options.CheckInOpensMinutesBeforeStart);
        var windowEndUtc = session.EndsAtUtc.AddHours(_options.AttendanceRegistrationHoursAfterEnd);
        if (nowUtc < windowStartUtc || nowUtc > windowEndUtc)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.AttendanceWindowClosed,
                "El registro de presencia se encuentra fuera del plazo habilitado.");
        }

        Guid? actorUserId = currentUserId != Guid.Empty ? currentUserId : _currentUserService?.UserId;
        reservation.MarkAttended(request.Source, actorUserId, nowUtc);
        session.IncrementAttendedCount();

        await SaveChangesTranslatingConcurrencyAsync(cancellationToken);

        await _auditService.LogAsync(
            "RESERVATION_ATTENDED",
            "Reservation",
            reservation.Id.ToString(),
            null,
            JsonSerializer.Serialize(new
            {
                reservation.StudentId,
                reservation.ClassSessionId,
                reservation.Status,
                reservation.AttendedAtUtc,
                reservation.AttendanceSource,
                reservation.CheckedInByUserId
            }),
            actorUserId,
            _currentUserService?.Email,
            null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return MapToDto(reservation);
    }

    public async Task<ReservationDto> RecordNoShowAsync(
        Guid reservationId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var sessionKey = await _dbContext.Reservations
            .Where(r => r.Id == reservationId && r.IsActive)
            .Select(r => new { r.ClassSessionId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Reserva no encontrada.");

        await LockClassSessionTranslatingConcurrencyAsync(sessionKey.ClassSessionId, cancellationToken);

        var reservation = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .Include(r => r.StatusChanges)
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.IsActive, cancellationToken)
            ?? throw new NotFoundException("Reserva no encontrada.");

        var session = reservation.ClassSession;
        EnsureInstructorOrStaffScopeForSession(session);

        if (!session.IsActive ||
            session.Status is ClassSessionStatus.Suspended or ClassSessionStatus.Cancelled or ClassSessionStatus.Rescheduled)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.ClassNotOpen,
                "No se puede registrar ausencia en una clase suspendida, cancelada o reprogramada.");
        }

        if (reservation.Status != ReservationStatus.Confirmed)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.InvalidStateTransition,
                $"Sólo se puede registrar ausencia sobre una reserva confirmada (estado actual: {reservation.Status}).");
        }

        var nowUtc = _gymCalendar.UtcNow;
        var windowStartUtc = session.EndsAtUtc;
        var windowEndUtc = session.EndsAtUtc.AddHours(_options.AttendanceRegistrationHoursAfterEnd);
        if (nowUtc < windowStartUtc || nowUtc > windowEndUtc)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.AttendanceWindowClosed,
                "El registro de ausencia sólo está permitido una vez finalizada la clase y dentro del plazo de registro.");
        }

        Guid? actorUserId = currentUserId != Guid.Empty ? currentUserId : _currentUserService?.UserId;
        reservation.MarkNoShow(nowUtc, actorUserId);

        await SaveChangesTranslatingConcurrencyAsync(cancellationToken);

        await _auditService.LogAsync(
            "RESERVATION_NO_SHOW",
            "Reservation",
            reservation.Id.ToString(),
            null,
            JsonSerializer.Serialize(new
            {
                reservation.StudentId,
                reservation.ClassSessionId,
                reservation.Status,
                ChangedAtUtc = nowUtc,
                ChangedByUserId = actorUserId
            }),
            actorUserId,
            _currentUserService?.Email,
            null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return MapToDto(reservation);
    }

    private void EnsureInstructorOrStaffScopeForSession(ClassSession session)
    {
        if (_currentUserService == null || !_currentUserService.IsAuthenticated || IsStaffCaller)
        {
            return;
        }

        if (_currentUserService.IsInRole(Roles.Instructor))
        {
            var instructorPersonId = _currentUserService.PersonId;
            if (instructorPersonId.HasValue &&
                (session.ScheduledInstructorId == instructorPersonId.Value ||
                 session.EffectiveInstructorId == instructorPersonId.Value))
            {
                return;
            }

            throw new NotFoundException("Clase o reserva no encontrada.");
        }

        throw new ForbiddenException("No posee permisos para operar sobre esta clase.");
    }

    public Task<List<ReservationDto>> GetByStudentAsync(Guid studentId, CancellationToken cancellationToken = default)
        => GetByStudentAsync(studentId, null, cancellationToken);

    public async Task<List<ReservationDto>> GetByClassSessionAsync(Guid classSessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ClassSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(cs => cs.Id == classSessionId && cs.IsActive, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        EnsureInstructorOrStaffScopeForSession(session);

        var list = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .AsNoTracking()
            .Where(r => r.ClassSessionId == classSessionId && r.IsActive)
            .OrderBy(r => r.Status)
            .ThenBy(r => r.WaitListPosition)
            .ThenBy(r => r.ReservedAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<SessionAttendanceSummaryDto> GetSessionAttendanceSummaryAsync(Guid classSessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .Include(cs => cs.Room)
            .AsNoTracking()
            .FirstOrDefaultAsync(cs => cs.Id == classSessionId && cs.IsActive, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        EnsureInstructorOrStaffScopeForSession(session);

        var attendees = await GetByClassSessionAsync(classSessionId, cancellationToken);
        var localStart = _gymCalendar.ToLocal(session.StartsAtUtc);

        return new SessionAttendanceSummaryDto(
            session.Id,
            session.Activity?.Name ?? string.Empty,
            DateOnly.FromDateTime(localStart.DateTime),
            session.Capacity,
            session.ReservedCount,
            session.AttendedCount,
            attendees
        );
    }

    public async Task<ReservationDto> JoinWaitlistAsync(
        Guid classSessionId,
        JoinWaitlistRequest request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableWaitlist)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.ClassFull,
                "La lista de espera no se encuentra habilitada.");
        }

        return await CreateReservationAsync(
            new CreateReservationRequest(
                ClassSessionId: classSessionId,
                StudentId: request.StudentId,
                MembershipId: request.MembershipId,
                SingleClassTicketMovementId: request.SingleClassTicketMovementId,
                PromotionCode: request.PromotionCode,
                AllowWaitlist: true,
                IdempotencyKey: request.IdempotencyKey),
            currentUserId,
            cancellationToken);
    }

    public async Task<ReservationDto> LeaveWaitlistAsync(
        Guid classSessionId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var studentId = _currentUserService?.PersonId
            ?? throw new ArgumentException("No se pudo determinar el alumno autenticado para salir de la lista de espera.");

        var waitlistedReservation = await _dbContext.Reservations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.ClassSessionId == classSessionId &&
                     r.StudentId == studentId &&
                     r.IsActive &&
                     (r.Status == ReservationStatus.Waitlisted || r.Status == ReservationStatus.Pending),
                cancellationToken)
            ?? throw new NotFoundException("El alumno no se encuentra en la lista de espera de esta clase.");

        return await CancelReservationAsync(
            waitlistedReservation.Id,
            new CancelReservationRequest("Salida voluntaria de la lista de espera"),
            currentUserId,
            cancellationToken);
    }

    public async Task<ReservationDto> AcceptWaitlistOfferAsync(
        Guid reservationId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var isStaff = IsStaffCaller;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var reservationKeys = await _dbContext.Reservations
            .Where(r => r.Id == reservationId && r.IsActive)
            .Select(r => new { r.ClassSessionId, r.StudentId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Reserva no encontrada.");

        if (_currentUserService != null &&
            _currentUserService.IsAuthenticated &&
            !isStaff &&
            _currentUserService.PersonId.HasValue &&
            reservationKeys.StudentId != _currentUserService.PersonId.Value)
        {
            throw new ForbiddenException("Un alumno sólo puede aceptar ofertas de sus propias reservas.");
        }

        await LockClassSessionTranslatingConcurrencyAsync(reservationKeys.ClassSessionId, cancellationToken);
        await LockStudentMembershipsTranslatingConcurrencyAsync(reservationKeys.StudentId, cancellationToken);

        var reservation = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
                .ThenInclude(s => s.PersonRoles)
                    .ThenInclude(pr => pr.Role)
            .Include(r => r.StatusChanges)
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.IsActive, cancellationToken)
            ?? throw new NotFoundException("Reserva no encontrada.");

        if (reservation.Status != ReservationStatus.Pending)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.InvalidStateTransition,
                $"Sólo se puede aceptar una oferta cuando la reserva está en estado Pending (estado actual: {reservation.Status}).");
        }

        var session = reservation.ClassSession;
        var nowUtc = _gymCalendar.UtcNow;
        Guid? actorUserId = currentUserId != Guid.Empty ? currentUserId : _currentUserService?.UserId;
        var trigger = isStaff ? ReservationChangeTrigger.StaffAction : ReservationChangeTrigger.StudentAction;

        if (nowUtc >= session.StartsAtUtc ||
            (reservation.WaitlistOfferExpiresAtUtc.HasValue && nowUtc > reservation.WaitlistOfferExpiresAtUtc.Value))
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.WaitlistOfferExpired,
                "La oferta de cupo desde la lista de espera ha vencido.");
        }

        var sessionLocalDate = DateOnly.FromDateTime(_gymCalendar.ToLocal(session.StartsAtUtc).DateTime);

        ResolvedReservationAccess resolvedAccess;
        try
        {
            resolvedAccess = await _accessResolver.ResolveAsync(
                reservation.Student,
                session,
                sessionLocalDate,
                new AccessResolutionRequest(
                    reservation.MembershipId,
                    reservation.SingleClassTicketMovementId,
                    reservation.RequestedPromotionCode),
                excludingReservationId: reservation.Id,
                cancellationToken: cancellationToken);
        }
        catch (ConflictException)
        {
            reservation.Cancel(
                "Oferta cancelada por pérdida de elegibilidad al aceptar",
                isLateCancellation: false,
                nowUtc: nowUtc,
                refundMovementId: null,
                cancelledByUserId: actorUserId,
                source: ReservationCancellationSource.WaitlistOfferExpired,
                trigger: trigger);

            session.DecrementReservedCount();
            await PromoteWaitlistAsync(session, actorUserId, cancellationToken);
            await SaveChangesTranslatingConcurrencyAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw;
        }

        var consumed = await _accessResolver.ConsumeAsync(
            resolvedAccess,
            reservation.Student,
            session,
            sessionLocalDate,
            reservation.Id,
            actorUserId,
            cancellationToken);

        reservation.AcceptOffer(
            nowUtc: nowUtc,
            changedByUserId: actorUserId,
            trigger: trigger,
            accessType: consumed.AccessType,
            membershipId: consumed.MembershipId,
            creditMovementId: consumed.CreditMovementId,
            singleClassTicketMovementId: consumed.SingleClassTicketMovementId,
            paymentId: consumed.PaymentId,
            promotionRedemptionId: consumed.PromotionRedemptionId);

        await SaveChangesTranslatingConcurrencyAsync(cancellationToken);

        await _auditService.LogAsync(
            "WAITLIST_OFFER_ACCEPTED",
            "Reservation",
            reservation.Id.ToString(),
            null,
            JsonSerializer.Serialize(new
            {
                reservation.StudentId,
                reservation.ClassSessionId,
                reservation.Status,
                reservation.AccessType,
                reservation.MembershipId,
                reservation.CreditMovementId
            }),
            actorUserId,
            _currentUserService?.Email,
            null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return MapToDto(reservation);
    }

    public async Task<ReservationDto> DeclineWaitlistOfferAsync(
        Guid reservationId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var isStaff = IsStaffCaller;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var reservationKeys = await _dbContext.Reservations
            .Where(r => r.Id == reservationId && r.IsActive)
            .Select(r => new { r.ClassSessionId, r.StudentId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Reserva no encontrada.");

        if (_currentUserService != null &&
            _currentUserService.IsAuthenticated &&
            !isStaff &&
            _currentUserService.PersonId.HasValue &&
            reservationKeys.StudentId != _currentUserService.PersonId.Value)
        {
            throw new ForbiddenException("Un alumno sólo puede rechazar ofertas de sus propias reservas.");
        }

        await LockClassSessionTranslatingConcurrencyAsync(reservationKeys.ClassSessionId, cancellationToken);
        await LockStudentMembershipsTranslatingConcurrencyAsync(reservationKeys.StudentId, cancellationToken);

        var reservation = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .Include(r => r.StatusChanges)
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.IsActive, cancellationToken)
            ?? throw new NotFoundException("Reserva no encontrada.");

        if (reservation.Status != ReservationStatus.Pending)
        {
            throw ConflictException.WithCode(
                ReservationErrorCodes.InvalidStateTransition,
                $"Sólo se puede rechazar una oferta cuando la reserva está en estado Pending (estado actual: {reservation.Status}).");
        }

        var session = reservation.ClassSession;
        var nowUtc = _gymCalendar.UtcNow;
        Guid? actorUserId = currentUserId != Guid.Empty ? currentUserId : _currentUserService?.UserId;
        var trigger = isStaff ? ReservationChangeTrigger.StaffAction : ReservationChangeTrigger.StudentAction;

        reservation.Cancel(
            "Oferta de cupo rechazada",
            isLateCancellation: false,
            nowUtc: nowUtc,
            refundMovementId: null,
            cancelledByUserId: actorUserId,
            source: ReservationCancellationSource.WaitlistDeclined,
            trigger: trigger);

        session.DecrementReservedCount();
        await PromoteWaitlistAsync(session, actorUserId, cancellationToken);

        await SaveChangesTranslatingConcurrencyAsync(cancellationToken);

        await _auditService.LogAsync(
            "WAITLIST_OFFER_DECLINED",
            "Reservation",
            reservation.Id.ToString(),
            null,
            JsonSerializer.Serialize(new
            {
                reservation.StudentId,
                reservation.ClassSessionId,
                reservation.Status,
                reservation.CancellationSource
            }),
            actorUserId,
            _currentUserService?.Email,
            null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return MapToDto(reservation);
    }

    public async Task<WaitlistProcessResultDto> ProcessWaitlistAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EnableWaitlist)
        {
            return new WaitlistProcessResultDto(0, 0, 0);
        }

        var nowUtc = _gymCalendar.UtcNow;

        var sessionIdsWithExpiredOffers = await _dbContext.Reservations
            .Where(r => r.IsActive &&
                        r.Status == ReservationStatus.Pending &&
                        ((r.WaitlistOfferExpiresAtUtc.HasValue && r.WaitlistOfferExpiresAtUtc.Value <= nowUtc) ||
                         r.ClassSession.StartsAtUtc <= nowUtc))
            .Select(r => r.ClassSessionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var sessionIdsWithStartedWaitlists = await _dbContext.Reservations
            .Where(r => r.IsActive &&
                        r.Status == ReservationStatus.Waitlisted &&
                        r.ClassSession.StartsAtUtc <= nowUtc)
            .Select(r => r.ClassSessionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var sessionIdsWithFreeSpotsAndWaitlist = await _dbContext.Reservations
            .Where(r => r.IsActive &&
                        r.Status == ReservationStatus.Waitlisted &&
                        r.ClassSession.IsActive &&
                        r.ClassSession.Status == ClassSessionStatus.Open &&
                        r.ClassSession.StartsAtUtc > nowUtc &&
                        r.ClassSession.ReservedCount < r.ClassSession.Capacity)
            .Select(r => r.ClassSessionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var candidateSessionIds = sessionIdsWithExpiredOffers
            .Concat(sessionIdsWithStartedWaitlists)
            .Concat(sessionIdsWithFreeSpotsAndWaitlist)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var expiredOffersCancelled = 0;
        var startedSessionsWaitlistExpired = 0;
        var candidatesPromoted = 0;

        foreach (var sessionId in candidateSessionIds)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            await LockClassSessionTranslatingConcurrencyAsync(sessionId, cancellationToken);

            var session = await _dbContext.ClassSessions
                .Include(cs => cs.Activity)
                .Include(cs => cs.Room)
                .FirstOrDefaultAsync(cs => cs.Id == sessionId && cs.IsActive, cancellationToken);

            if (session == null)
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var expiredOffers = await _dbContext.Reservations
                .Include(r => r.StatusChanges)
                .Where(r => r.ClassSessionId == session.Id &&
                            r.IsActive &&
                            r.Status == ReservationStatus.Pending &&
                            ((r.WaitlistOfferExpiresAtUtc.HasValue && r.WaitlistOfferExpiresAtUtc.Value <= nowUtc) ||
                             session.StartsAtUtc <= nowUtc))
                .ToListAsync(cancellationToken);

            foreach (var offer in expiredOffers)
            {
                offer.Cancel(
                    "Oferta de cupo vencida",
                    isLateCancellation: false,
                    nowUtc: nowUtc,
                    refundMovementId: null,
                    cancelledByUserId: null,
                    source: ReservationCancellationSource.WaitlistOfferExpired,
                    trigger: ReservationChangeTrigger.WaitlistWorker);

                session.DecrementReservedCount();
                expiredOffersCancelled++;
            }

            if (nowUtc >= session.StartsAtUtc)
            {
                var startedWaitlists = await _dbContext.Reservations
                    .Include(r => r.StatusChanges)
                    .Where(r => r.ClassSessionId == session.Id &&
                                r.IsActive &&
                                r.Status == ReservationStatus.Waitlisted)
                    .ToListAsync(cancellationToken);

                foreach (var entry in startedWaitlists)
                {
                    entry.Cancel(
                        "Lista de espera expirada por inicio de clase",
                        isLateCancellation: false,
                        nowUtc: nowUtc,
                        refundMovementId: null,
                        cancelledByUserId: null,
                        source: ReservationCancellationSource.WaitlistOfferExpired,
                        trigger: ReservationChangeTrigger.WaitlistWorker);

                    startedSessionsWaitlistExpired++;
                }
            }
            else if (session.Status == ClassSessionStatus.Open && session.ReservedCount < session.Capacity)
            {
                var promoted = await PromoteWaitlistAsync(session, null, cancellationToken);
                candidatesPromoted += promoted;
            }

            await SaveChangesTranslatingConcurrencyAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return new WaitlistProcessResultDto(
            expiredOffersCancelled,
            startedSessionsWaitlistExpired,
            candidatesPromoted);
    }

    public async Task<SmartGym.Application.Common.Models.PagedResult<ReservationDto>> GetPagedAsync(
        ReservationFilterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUserService != null && _currentUserService.IsAuthenticated && !IsStaffCaller)
        {
            throw new ForbiddenException("No posee permisos para consultar el listado general de reservas.");
        }

        var query = _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .AsNoTracking()
            .Where(r => r.IsActive);

        if (request.StudentId.HasValue)
        {
            query = query.Where(r => r.StudentId == request.StudentId.Value);
        }

        if (request.ClassSessionId.HasValue)
        {
            query = query.Where(r => r.ClassSessionId == request.ClassSessionId.Value);
        }

        if (request.ActivityId.HasValue)
        {
            query = query.Where(r => r.ClassSession.ActivityId == request.ActivityId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(r => r.Status == request.Status.Value);
        }

        if (request.AccessType.HasValue)
        {
            query = query.Where(r => r.AccessType == request.AccessType.Value);
        }

        if (request.Origin.HasValue)
        {
            query = query.Where(r => r.Origin == request.Origin.Value);
        }

        if (request.From.HasValue)
        {
            var (fromUtc, _) = _gymCalendar.LocalDayRange(request.From.Value, request.From.Value);
            query = query.Where(r => r.ClassSession.StartsAtUtc >= fromUtc);
        }

        if (request.To.HasValue)
        {
            var (_, toUtc) = _gymCalendar.LocalDayRange(request.To.Value, request.To.Value);
            query = query.Where(r => r.ClassSession.StartsAtUtc < toUtc);
        }

        if (request.IsLateCancellation.HasValue)
        {
            query = query.Where(r => r.IsLateCancellation == request.IsLateCancellation.Value);
        }

        if (request.AffectedBySessionChange.HasValue)
        {
            query = query.Where(r => r.AffectedBySessionChange == request.AffectedBySessionChange.Value);
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.ReservedAtUtc)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new SmartGym.Application.Common.Models.PagedResult<ReservationDto>(
            items.Select(MapToDto).ToList(),
            totalCount,
            page,
            pageSize);
    }

    public async Task<ReservationDetailDto> GetByIdAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        var reservation = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .Include(r => r.StatusChanges)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reservationId && r.IsActive, cancellationToken)
            ?? throw new NotFoundException("Reserva no encontrada.");

        if (_currentUserService != null && _currentUserService.IsAuthenticated && !IsStaffCaller)
        {
            var personId = _currentUserService.PersonId;
            var isStudentOwner = _currentUserService.IsInRole(Roles.Student) &&
                                 personId.HasValue &&
                                 personId.Value == reservation.StudentId;

            var isAssignedInstructor = _currentUserService.IsInRole(Roles.Instructor) &&
                                       personId.HasValue &&
                                       (reservation.ClassSession.ScheduledInstructorId == personId.Value ||
                                        reservation.ClassSession.EffectiveInstructorId == personId.Value);

            if (!isStudentOwner && !isAssignedInstructor)
            {
                if (_currentUserService.IsInRole(Roles.Instructor) && !_currentUserService.IsInRole(Roles.Student))
                {
                    throw new NotFoundException("Reserva no encontrada.");
                }

                throw new ForbiddenException("No posee permisos para consultar el detalle de esta reserva.");
            }
        }

        var localStart = _gymCalendar.ToLocal(reservation.ClassSession.StartsAtUtc);
        var localEnd = _gymCalendar.ToLocal(reservation.ClassSession.EndsAtUtc);
        var history = reservation.StatusChanges
            .OrderBy(sc => sc.ChangedAtUtc)
            .Select(sc => new ReservationStatusChangeDto(
                sc.Id,
                sc.ReservationId,
                sc.PreviousStatus,
                sc.NewStatus,
                sc.Reason,
                sc.ChangedByUserId,
                sc.Trigger,
                sc.ChangedAtUtc))
            .ToList();

        return new ReservationDetailDto(
            reservation.Id,
            reservation.ClassSessionId,
            reservation.ClassSession.ActivityId,
            reservation.ClassSession.Activity?.Name ?? string.Empty,
            DateOnly.FromDateTime(localStart.DateTime),
            TimeOnly.FromDateTime(localStart.DateTime),
            TimeOnly.FromDateTime(localEnd.DateTime),
            reservation.ClassSession.StartsAtUtc,
            reservation.ClassSession.EndsAtUtc,
            reservation.ClassSession.Room?.Name ?? string.Empty,
            reservation.StudentId,
            reservation.Student.FullName,
            reservation.Student.Dni ?? string.Empty,
            reservation.Student.Document?.Type,
            reservation.Student.Document?.Number,
            reservation.Status,
            reservation.Origin,
            reservation.AccessType,
            reservation.MembershipId,
            reservation.CreditMovementId,
            reservation.RefundCreditMovementId,
            reservation.SingleClassTicketMovementId,
            reservation.PaymentId,
            reservation.PromotionRedemptionId,
            reservation.RequestedPromotionCode,
            reservation.ReservedAtUtc,
            reservation.ConfirmedAtUtc,
            reservation.CreatedByUserId,
            reservation.WaitListPosition,
            reservation.WaitlistEnteredAtUtc,
            reservation.WaitlistPromotedAtUtc,
            reservation.WaitlistOfferExpiresAtUtc,
            reservation.CancelledAtUtc,
            reservation.CancelledByUserId,
            reservation.CancellationReason,
            reservation.CancellationSource,
            reservation.IsLateCancellation,
            reservation.IsPolicyOverride,
            reservation.PolicyOverrideReason,
            reservation.AffectedBySessionChange,
            reservation.RescheduledToSessionId,
            reservation.AttendedAtUtc,
            reservation.CheckedInByUserId,
            reservation.AttendanceSource,
            reservation.Notes,
            reservation.IdempotencyKey,
            history);
    }

    public async Task<List<ReservationDto>> GetByStudentAsync(
        Guid studentId,
        StudentReservationHistoryFilterRequest? filter,
        CancellationToken cancellationToken = default)
    {
        if (_currentUserService != null && _currentUserService.IsAuthenticated && !IsStaffCaller)
        {
            if (!_currentUserService.IsInRole(Roles.Student) ||
                !_currentUserService.PersonId.HasValue ||
                _currentUserService.PersonId.Value != studentId)
            {
                throw new ForbiddenException("No posee permisos para consultar el historial de reservas de este alumno.");
            }
        }

        var query = _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .AsNoTracking()
            .Where(r => r.StudentId == studentId && r.IsActive);

        if (filter?.Status.HasValue == true)
        {
            query = query.Where(r => r.Status == filter.Status.Value);
        }

        if (filter?.From.HasValue == true)
        {
            var (fromUtc, _) = _gymCalendar.LocalDayRange(filter.From.Value, filter.From.Value);
            query = query.Where(r => r.ClassSession.StartsAtUtc >= fromUtc);
        }

        if (filter?.To.HasValue == true)
        {
            var (_, toUtc) = _gymCalendar.LocalDayRange(filter.To.Value, filter.To.Value);
            query = query.Where(r => r.ClassSession.StartsAtUtc < toUtc);
        }

        var list = await query
            .OrderByDescending(r => r.ReservedAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public Task<List<ReservationDto>> GetMyReservationsAsync(
        StudentReservationHistoryFilterRequest? filter = null,
        CancellationToken cancellationToken = default)
    {
        var studentId = _currentUserService?.PersonId
            ?? throw new ForbiddenException("No se pudo identificar al alumno autenticado.");

        return GetByStudentAsync(studentId, filter, cancellationToken);
    }

    public async Task<List<ReservationDto>> GetMyUpcomingAsync(CancellationToken cancellationToken = default)
    {
        var studentId = _currentUserService?.PersonId
            ?? throw new ForbiddenException("No se pudo identificar al alumno autenticado.");

        var nowUtc = _gymCalendar.UtcNow;

        var list = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Room)
            .Include(r => r.Student)
            .AsNoTracking()
            .Where(r => r.StudentId == studentId &&
                        r.IsActive &&
                        (r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Pending ||
                         r.Status == ReservationStatus.Waitlisted) &&
                        r.ClassSession.StartsAtUtc >= nowUtc)
            .OrderBy(r => r.ClassSession.StartsAtUtc)
            .ThenBy(r => r.ReservedAtUtc)
            .ToListAsync(cancellationToken);

        return list.Select(MapToDto).ToList();
    }

    public async Task<List<WaitlistEntryDto>> GetWaitlistAsync(
        Guid classSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ClassSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(cs => cs.Id == classSessionId && cs.IsActive, cancellationToken)
            ?? throw new NotFoundException("Clase no encontrada.");

        EnsureInstructorOrStaffScopeForSession(session);

        var entries = await _dbContext.Reservations
            .Include(r => r.Student)
            .AsNoTracking()
            .Where(r => r.ClassSessionId == classSessionId &&
                        r.IsActive &&
                        (r.Status == ReservationStatus.Waitlisted || r.Status == ReservationStatus.Pending))
            .OrderBy(r => r.Status == ReservationStatus.Pending ? 0 : 1)
            .ThenBy(r => r.WaitListPosition)
            .ThenBy(r => r.WaitlistEnteredAtUtc)
            .Select(r => new WaitlistEntryDto(
                r.Id,
                r.ClassSessionId,
                r.StudentId,
                r.Student.FirstName + " " + r.Student.LastName,
                r.Student.Document != null ? r.Student.Document.Number : string.Empty,
                r.Status,
                r.WaitListPosition,
                r.WaitlistEnteredAtUtc,
                r.WaitlistOfferExpiresAtUtc,
                r.AccessType,
                r.MembershipId))
            .ToListAsync(cancellationToken);

        return entries;
    }

    public async Task<List<ReservableClassSessionDto>> GetAvailableClassesAsync(
        AvailableClassesFilterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.To < request.From)
        {
            throw new ArgumentException("La fecha hasta no puede ser menor a la fecha desde.");
        }

        if ((request.To.DayNumber - request.From.DayNumber) > _options.AvailableClassesMaxRangeDays)
        {
            throw new ArgumentException(
                $"El rango de fechas solicitado supera el máximo permitido de {_options.AvailableClassesMaxRangeDays} días.");
        }

        if (_currentUserService != null && _currentUserService.IsAuthenticated && !IsStaffCaller)
        {
            if (request.StudentId.HasValue &&
                _currentUserService.PersonId.HasValue &&
                request.StudentId.Value != _currentUserService.PersonId.Value)
            {
                throw new ForbiddenException("Un alumno sólo puede consultar clases disponibles para sí mismo.");
            }
        }

        var targetStudentId = request.StudentId
            ?? _currentUserService?.PersonId
            ?? throw new ArgumentException("Debe indicar el alumno para consultar las clases disponibles.");

        var nowUtc = _gymCalendar.UtcNow;
        var (fromUtc, toUtc) = _gymCalendar.LocalDayRange(request.From, request.To);

        var sessionsQuery = _dbContext.ClassSessions
            .AsNoTracking()
            .Where(cs => cs.IsActive &&
                         cs.Status == ClassSessionStatus.Open &&
                         cs.StartsAtUtc > nowUtc &&
                         cs.StartsAtUtc >= fromUtc &&
                         cs.StartsAtUtc < toUtc &&
                         cs.Activity.IsActive &&
                         cs.Activity.Status == ActivityStatus.Active);

        if (request.ActivityId.HasValue)
        {
            sessionsQuery = sessionsQuery.Where(cs => cs.ActivityId == request.ActivityId.Value);
        }

        if (request.RoomId.HasValue)
        {
            sessionsQuery = sessionsQuery.Where(cs => cs.RoomId == request.RoomId.Value);
        }

        if (request.InstructorId.HasValue)
        {
            var instructorId = request.InstructorId.Value;
            sessionsQuery = sessionsQuery.Where(cs =>
                cs.EffectiveInstructorId == instructorId || cs.ScheduledInstructorId == instructorId);
        }

        var projectedSessions = await sessionsQuery
            .OrderBy(cs => cs.StartsAtUtc)
            .Select(cs => new
            {
                Session = cs,
                Activity = cs.Activity,
                RoomName = cs.Room.Name,
                InstructorFirstName = cs.EffectiveInstructor.FirstName,
                InstructorLastName = cs.EffectiveInstructor.LastName,
                WaitlistCount = _dbContext.Reservations.Count(r =>
                    r.ClassSessionId == cs.Id &&
                    r.IsActive &&
                    r.Status == ReservationStatus.Waitlisted),
                OwnReservation = _dbContext.Reservations
                    .Where(r =>
                        r.ClassSessionId == cs.Id &&
                        r.StudentId == targetStudentId &&
                        r.IsActive &&
                        (r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Pending ||
                         r.Status == ReservationStatus.Waitlisted))
                    .Select(r => new
                    {
                        r.Id,
                        r.Status,
                        r.WaitListPosition
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var eligibilityContexts = projectedSessions
            .Select(x => new SmartGym.Application.Modules.Memberships.Dtos.SessionEligibilityContextDto(
                x.Session.Id,
                x.Session.ActivityId,
                DateOnly.FromDateTime(_gymCalendar.ToLocal(x.Session.StartsAtUtc).DateTime),
                x.Activity))
            .ToList();

        var eligibilityBySession = await _eligibilityService.EvaluateForSessionsAsync(
            targetStudentId,
            eligibilityContexts,
            cancellationToken);

        var minStartUtc = nowUtc.AddMinutes(_options.MinAdvanceBookingMinutes);
        var maxStartUtc = _options.MaxAdvanceBookingDays > 0
            ? nowUtc.AddDays(_options.MaxAdvanceBookingDays)
            : DateTime.MaxValue;

        var results = new List<ReservableClassSessionDto>(projectedSessions.Count);

        foreach (var item in projectedSessions)
        {
            var localStart = _gymCalendar.ToLocal(item.Session.StartsAtUtc);
            var localEnd = _gymCalendar.ToLocal(item.Session.EndsAtUtc);
            var classDate = DateOnly.FromDateTime(localStart.DateTime);
            var startTime = TimeOnly.FromDateTime(localStart.DateTime);
            var endTime = TimeOnly.FromDateTime(localEnd.DateTime);
            var availableSpots = Math.Max(0, item.Session.Capacity - item.Session.ReservedCount);
            var isWithinBookingWindow = item.Session.StartsAtUtc >= minStartUtc &&
                                        item.Session.StartsAtUtc <= maxStartUtc;
            var hasActiveReservation = item.OwnReservation != null;
            var eligibility = eligibilityBySession[item.Session.Id];
            var reasons = new List<SmartGym.Application.Modules.Memberships.Dtos.EligibilityIssueDto>(eligibility.Issues);

            if (hasActiveReservation)
            {
                reasons.Add(new(
                    ReservationErrorCodes.AlreadyReserved,
                    "Ya posee una reserva activa para esta clase."));
            }

            if (!isWithinBookingWindow)
            {
                reasons.Add(new(
                    ReservationErrorCodes.BookingWindowClosed,
                    "La clase se encuentra fuera de la ventana de anticipación para reservar."));
            }

            var canReserve = !hasActiveReservation &&
                             isWithinBookingWindow &&
                             eligibility.IsEligible &&
                             availableSpots > 0;

            var canJoinWaitlist = !hasActiveReservation &&
                                  isWithinBookingWindow &&
                                  eligibility.IsEligible &&
                                  availableSpots == 0 &&
                                  _options.EnableWaitlist &&
                                  item.WaitlistCount < _options.MaxWaitlistSize;

            if (!hasActiveReservation &&
                isWithinBookingWindow &&
                eligibility.IsEligible &&
                availableSpots == 0 &&
                !canJoinWaitlist)
            {
                reasons.Add(new(
                    _options.EnableWaitlist ? ReservationErrorCodes.WaitlistFull : ReservationErrorCodes.ClassFull,
                    "La clase no dispone de cupos ni lugares en lista de espera."));
            }

            results.Add(new ReservableClassSessionDto(
                item.Session.Id,
                item.Session.ActivityId,
                item.Activity.Name,
                item.Session.RoomId,
                item.RoomName,
                item.Session.EffectiveInstructorId,
                $"{item.InstructorFirstName} {item.InstructorLastName}".Trim(),
                classDate,
                startTime,
                endTime,
                item.Session.StartsAtUtc,
                item.Session.EndsAtUtc,
                localStart,
                localEnd,
                item.Session.Capacity,
                item.Session.ReservedCount,
                availableSpots,
                item.WaitlistCount,
                isWithinBookingWindow,
                hasActiveReservation,
                item.OwnReservation?.Id,
                item.OwnReservation?.Status,
                item.OwnReservation?.WaitListPosition,
                canReserve,
                canJoinWaitlist,
                eligibility.SuggestedAccessType,
                eligibility.SelectedMembershipId,
                eligibility.UsableCredits,
                reasons));
        }

        return results;
    }

    private ReservationDto MapToDto(Reservation r)
    {
        DateOnly classDate = DateOnly.MinValue;
        TimeOnly startTime = TimeOnly.MinValue;
        TimeOnly endTime = TimeOnly.MinValue;

        if (r.ClassSession != null)
        {
            var localStart = _gymCalendar.ToLocal(r.ClassSession.StartsAtUtc);
            var localEnd = _gymCalendar.ToLocal(r.ClassSession.EndsAtUtc);
            classDate = DateOnly.FromDateTime(localStart.DateTime);
            startTime = TimeOnly.FromDateTime(localStart.DateTime);
            endTime = TimeOnly.FromDateTime(localEnd.DateTime);
        }

        var history = r.StatusChanges?
            .OrderBy(sc => sc.ChangedAtUtc)
            .Select(sc => new ReservationStatusChangeDto(
                sc.Id,
                sc.ReservationId,
                sc.PreviousStatus,
                sc.NewStatus,
                sc.Reason,
                sc.ChangedByUserId,
                sc.Trigger,
                sc.ChangedAtUtc))
            .ToList();

        return new(
            r.Id,
            r.ClassSessionId,
            r.ClassSession?.Activity?.Name ?? string.Empty,
            classDate,
            startTime,
            endTime,
            r.ClassSession?.Room?.Name ?? string.Empty,
            r.StudentId,
            r.Student?.FullName ?? string.Empty,
            r.Student?.Dni ?? string.Empty,
            r.MembershipId,
            r.Status,
            r.ReservedAtUtc,
            r.ConfirmedAtUtc,
            r.CancelledAtUtc,
            r.CancellationReason,
            r.IsLateCancellation,
            r.AttendedAtUtc,
            r.AttendanceSource,
            r.WaitListPosition,
            r.Student?.Document?.Type,
            r.Student?.Document?.Number,
            r.Origin,
            r.AccessType,
            r.CreditMovementId,
            r.RefundCreditMovementId,
            r.SingleClassTicketMovementId,
            r.PaymentId,
            r.PromotionRedemptionId,
            r.RequestedPromotionCode,
            r.CreatedByUserId,
            r.WaitlistEnteredAtUtc,
            r.WaitlistPromotedAtUtc,
            r.WaitlistOfferExpiresAtUtc,
            r.CancelledByUserId,
            r.CancellationSource,
            r.IsPolicyOverride,
            r.PolicyOverrideReason,
            r.AffectedBySessionChange,
            r.RescheduledToSessionId,
            r.CheckedInByUserId,
            r.Notes,
            r.IdempotencyKey,
            false,
            history,
            r.ClassSession?.ActivityId,
            r.ClassSession?.StartsAtUtc,
            r.ClassSession?.EndsAtUtc
        );
    }
}
