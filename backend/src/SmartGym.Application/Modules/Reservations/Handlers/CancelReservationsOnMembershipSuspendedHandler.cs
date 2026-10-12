using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Memberships;

namespace SmartGym.Application.Modules.Reservations.Handlers;

/// <summary>
/// Cancela con devolución de crédito, liberación de cupo y promoción de lista de espera las reservas futuras
/// de una membresía que caen dentro de la ventana de suspensión (o todas las futuras si la suspensión es abierta).
/// </summary>
public class CancelReservationsOnMembershipSuspendedHandler : IDomainEventHandler<MembershipSuspendedEvent>
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly ICreditLedgerService _creditLedgerService;
    private readonly IGymCalendar _gymCalendar;
    private readonly IReservationService _reservationService;
    private readonly IAuditService _auditService;

    public CancelReservationsOnMembershipSuspendedHandler(
        ISmartGymDbContext dbContext,
        ICreditLedgerService? creditLedgerService = null,
        IGymCalendar? gymCalendar = null,
        IReservationService? reservationService = null,
        IAuditService? auditService = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar ?? new GymCalendar(Options.Create(new GymOptions()));
        _creditLedgerService = creditLedgerService ?? new CreditLedgerService(dbContext, _gymCalendar);
        _auditService = auditService ?? new AuditService(dbContext);
        _reservationService = reservationService ?? new ReservationService(dbContext, _gymCalendar, _creditLedgerService, auditService: _auditService);
    }

    public async Task HandleAsync(MembershipSuspendedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var activeReservations = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.Membership)
            .Include(r => r.StatusChanges)
            .Where(r => r.MembershipId == domainEvent.MembershipId &&
                        r.IsActive &&
                        (r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Waitlisted ||
                         r.Status == ReservationStatus.Pending))
            .ToListAsync(cancellationToken);

        var affected = activeReservations
            .Where(r =>
            {
                if (r.ClassSession == null || r.ClassSession.StartsAtUtc <= domainEvent.SuspendedAtUtc)
                {
                    return false;
                }

                var sessionLocalDate = DateOnly.FromDateTime(_gymCalendar.ToLocal(r.ClassSession.StartsAtUtc).DateTime);
                if (sessionLocalDate < domainEvent.StartDate)
                {
                    return false;
                }

                if (domainEvent.EndDate.HasValue && sessionLocalDate > domainEvent.EndDate.Value)
                {
                    return false;
                }

                return true;
            })
            .ToList();

        if (affected.Count == 0)
        {
            return;
        }

        // Bloquear sesiones afectadas en orden ascendente de ClassSessionId
        var sessionIds = affected
            .Select(r => r.ClassSessionId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        foreach (var sessionId in sessionIds)
        {
            await _dbContext.LockClassSessionForUpdateAsync(sessionId, cancellationToken);
        }

        var sessionsWithFreedSpots = new Dictionary<Guid, ClassSession>();
        var sessionsNeedingCompaction = new HashSet<Guid>();

        foreach (var reservation in affected)
        {
            var session = reservation.ClassSession!;
            var wasConfirmed = reservation.Status == ReservationStatus.Confirmed;
            var wasPending = reservation.Status == ReservationStatus.Pending;
            var wasWaitlisted = reservation.Status == ReservationStatus.Waitlisted;

            Guid? refundMovementId = null;
            if (wasConfirmed && reservation.Membership != null)
            {
                var refund = await _creditLedgerService.RefundCreditForReservationAsync(
                    reservation.Membership,
                    reservation.ClassSessionId,
                    $"Devolución por suspensión de membresía: {domainEvent.Reason}",
                    domainEvent.SuspendedByUserId,
                    reservation.Id,
                    cancellationToken);
                refundMovementId = refund.Id;
            }

            reservation.Cancel(
                $"Membresía suspendida: {domainEvent.Reason}",
                isLateCancellation: false,
                nowUtc: domainEvent.SuspendedAtUtc,
                refundMovementId: refundMovementId,
                cancelledByUserId: domainEvent.SuspendedByUserId,
                source: ReservationCancellationSource.MembershipChange,
                trigger: ReservationChangeTrigger.MembershipChange);

            if (wasConfirmed || wasPending)
            {
                session.DecrementReservedCount();
                sessionsWithFreedSpots[session.Id] = session;
            }
            else if (wasWaitlisted)
            {
                sessionsNeedingCompaction.Add(session.Id);
            }

            await _auditService.LogAsync(
                "RESERVATION_CANCELLED_BY_MEMBERSHIP_CHANGE",
                "Reservation",
                reservation.Id.ToString(),
                null,
                JsonSerializer.Serialize(new
                {
                    reservation.StudentId,
                    reservation.ClassSessionId,
                    reservation.MembershipId,
                    reservation.Status,
                    reservation.CancellationReason,
                    reservation.CancellationSource,
                    reservation.RefundCreditMovementId
                }),
                domainEvent.SuspendedByUserId,
                null,
                null,
                cancellationToken);
        }

        foreach (var sessionId in sessionIds)
        {
            if (sessionsWithFreedSpots.TryGetValue(sessionId, out var session))
            {
                await _reservationService.PromoteWaitlistAsync(session, domainEvent.SuspendedByUserId, cancellationToken);
            }
            else if (sessionsNeedingCompaction.Contains(sessionId))
            {
                await _reservationService.CompactWaitlistPositionsAsync(sessionId, cancellationToken);
            }
        }
    }
}
