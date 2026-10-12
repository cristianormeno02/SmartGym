using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Activities;

namespace SmartGym.Application.Modules.Reservations.Handlers;

/// <summary>
/// Cancela reservas activas y reembolsa créditos/anula canjes cuando una sesión es suspendida, cancelada o reprogramada.
/// Se ejecuta en la misma transacción dentro del ciclo de despacho de domain events en SaveChangesAsync.
/// </summary>
public class CancelReservationsOnSessionVoidedHandler :
    IDomainEventHandler<ClassSessionSuspended>,
    IDomainEventHandler<ClassSessionCancelled>,
    IDomainEventHandler<ClassSessionRescheduled>
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly ICreditLedgerService _creditLedgerService;
    private readonly IPromotionService _promotionService;
    private readonly IGymCalendar? _gymCalendar;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService? _currentUserService;

    public CancelReservationsOnSessionVoidedHandler(
        ISmartGymDbContext dbContext,
        ICreditLedgerService? creditLedgerService = null,
        IPromotionService? promotionService = null,
        IGymCalendar? gymCalendar = null,
        IAuditService? auditService = null,
        ICurrentUserService? currentUserService = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
        _creditLedgerService = creditLedgerService ?? new CreditLedgerService(dbContext, gymCalendar);
        _promotionService = promotionService ?? new PromotionService(dbContext, gymCalendar);
        _auditService = auditService ?? new AuditService(dbContext);
        _currentUserService = currentUserService;
    }

    public Task HandleAsync(ClassSessionSuspended domainEvent, CancellationToken cancellationToken = default) =>
        HandleSessionVoidedAsync(
            domainEvent.SessionId,
            $"Clase suspendida: {domainEvent.Reason}",
            domainEvent.ByUserId,
            domainEvent.AtUtc,
            rescheduledToSessionId: null,
            cancellationToken);

    public Task HandleAsync(ClassSessionCancelled domainEvent, CancellationToken cancellationToken = default) =>
        HandleSessionVoidedAsync(
            domainEvent.SessionId,
            $"Clase cancelada: {domainEvent.Reason}",
            domainEvent.ByUserId,
            domainEvent.AtUtc,
            rescheduledToSessionId: null,
            cancellationToken);

    public Task HandleAsync(ClassSessionRescheduled domainEvent, CancellationToken cancellationToken = default) =>
        HandleSessionVoidedAsync(
            domainEvent.OriginalSessionId,
            "Clase reprogramada",
            domainEvent.ByUserId,
            atUtc: null,
            rescheduledToSessionId: domainEvent.NewSessionId,
            cancellationToken);

    private async Task HandleSessionVoidedAsync(
        Guid sessionId,
        string cancellationReason,
        Guid? byUserId,
        DateTime? atUtc,
        Guid? rescheduledToSessionId,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.ClassSessions
            .FirstOrDefaultAsync(cs => cs.Id == sessionId, cancellationToken);

        if (session == null)
        {
            return;
        }

        var activeReservations = await _dbContext.Reservations
            .Include(r => r.Membership)
                .ThenInclude(m => m!.MembershipPlan)
            .Include(r => r.StatusChanges)
            .Where(r => r.ClassSessionId == sessionId && r.IsActive &&
                        (r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Waitlisted ||
                         r.Status == ReservationStatus.Pending))
            .ToListAsync(cancellationToken);

        if (activeReservations.Count == 0)
        {
            session.ResetReservedCount();
            return;
        }

        // Bloquear membresías de los alumnos afectados en orden ascendente de StudentId
        var distinctStudentIds = activeReservations
            .Select(r => r.StudentId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        foreach (var studentId in distinctStudentIds)
        {
            await _dbContext.LockStudentMembershipsForUpdateAsync(studentId, cancellationToken);
        }

        var effectiveNowUtc = atUtc ?? _gymCalendar?.UtcNow ?? DateTime.UtcNow;
        var effectiveActorId = byUserId ?? _currentUserService?.UserId;

        foreach (var reservation in activeReservations)
        {
            Guid? refundMovementId = null;
            var wasConfirmed = reservation.Status == ReservationStatus.Confirmed;

            if (wasConfirmed)
            {
                if (reservation.Membership != null)
                {
                    var refund = await _creditLedgerService.RefundCreditForReservationAsync(
                        reservation.Membership,
                        session.Id,
                        $"Devolución por sesión cancelada/suspendida ({cancellationReason})",
                        performedByUserId: effectiveActorId,
                        reservationId: reservation.Id,
                        cancellationToken: cancellationToken);
                    refundMovementId = refund.Id;
                }

                if (reservation.PromotionRedemptionId.HasValue)
                {
                    await _promotionService.VoidRedemptionAsync(reservation.PromotionRedemptionId.Value, cancellationToken);
                }
            }

            reservation.Cancel(
                cancellationReason,
                isLateCancellation: false,
                nowUtc: effectiveNowUtc,
                refundMovementId: refundMovementId,
                cancelledByUserId: effectiveActorId,
                source: ReservationCancellationSource.ClassSessionChange,
                trigger: ReservationChangeTrigger.ClassSessionChange,
                affectedBySessionChange: true,
                rescheduledToSessionId: rescheduledToSessionId);

            await _auditService.LogAsync(
                "RESERVATION_CANCELLED_BY_SESSION_CHANGE",
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
                    reservation.AffectedBySessionChange,
                    reservation.RescheduledToSessionId,
                    reservation.RefundCreditMovementId
                }),
                effectiveActorId,
                _currentUserService?.Email,
                null,
                cancellationToken);
        }

        session.ResetReservedCount();
    }
}
