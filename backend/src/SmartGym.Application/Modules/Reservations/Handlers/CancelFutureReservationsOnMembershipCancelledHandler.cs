using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Memberships;

namespace SmartGym.Application.Modules.Reservations.Handlers;

/// <summary>
/// Cancela las reservas futuras asociadas a una membresía cuando ésta es cancelada definitivamente,
/// liberando los cupos en las sesiones correspondientes y promoviendo la lista de espera.
/// </summary>
public class CancelFutureReservationsOnMembershipCancelledHandler : IDomainEventHandler<MembershipCancelledEvent>
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly IReservationService _reservationService;
    private readonly IAuditService _auditService;

    public CancelFutureReservationsOnMembershipCancelledHandler(
        ISmartGymDbContext dbContext,
        IGymCalendar? gymCalendar = null,
        IReservationService? reservationService = null,
        IAuditService? auditService = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar ?? new GymCalendar(Options.Create(new GymOptions()));
        _auditService = auditService ?? new AuditService(dbContext);
        _reservationService = reservationService ?? new ReservationService(dbContext, _gymCalendar, auditService: _auditService);
    }

    public async Task HandleAsync(MembershipCancelledEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var futureReservations = await _dbContext.Reservations
            .Include(r => r.ClassSession)
                .ThenInclude(cs => cs.Activity)
            .Include(r => r.StatusChanges)
            .Where(r => r.MembershipId == domainEvent.MembershipId &&
                        r.IsActive &&
                        (r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.Waitlisted ||
                         r.Status == ReservationStatus.Pending))
            .ToListAsync(cancellationToken);

        var affected = futureReservations
            .Where(r => r.ClassSession != null && r.ClassSession.StartsAtUtc > domainEvent.CancelledAtUtc)
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

            reservation.Cancel(
                $"Membresía cancelada: {domainEvent.Reason}",
                isLateCancellation: false,
                nowUtc: domainEvent.CancelledAtUtc,
                refundMovementId: null,
                cancelledByUserId: domainEvent.CancelledByUserId,
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
                domainEvent.CancelledByUserId,
                null,
                null,
                cancellationToken);
        }

        foreach (var sessionId in sessionIds)
        {
            if (sessionsWithFreedSpots.TryGetValue(sessionId, out var session))
            {
                await _reservationService.PromoteWaitlistAsync(session, domainEvent.CancelledByUserId, cancellationToken);
            }
            else if (sessionsNeedingCompaction.Contains(sessionId))
            {
                await _reservationService.CompactWaitlistPositionsAsync(sessionId, cancellationToken);
            }
        }
    }
}
