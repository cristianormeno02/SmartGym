using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Activities;

namespace SmartGym.Application.Modules.Reservations.Handlers;

/// <summary>
/// Cancela reservas activas y reembolsa créditos/compensa cuando una sesión es suspendida, cancelada o reprogramada.
/// Se ejecuta en la misma transacción dentro del ciclo de despacho de domain events en SaveChangesAsync.
/// </summary>
public class CancelReservationsOnSessionVoidedHandler :
    IDomainEventHandler<ClassSessionSuspended>,
    IDomainEventHandler<ClassSessionCancelled>,
    IDomainEventHandler<ClassSessionRescheduled>
{
    private readonly ISmartGymDbContext _dbContext;

    public CancelReservationsOnSessionVoidedHandler(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task HandleAsync(ClassSessionSuspended domainEvent, CancellationToken cancellationToken = default) =>
        HandleSessionVoidedAsync(domainEvent.SessionId, $"Clase suspendida: {domainEvent.Reason}", cancellationToken);

    public Task HandleAsync(ClassSessionCancelled domainEvent, CancellationToken cancellationToken = default) =>
        HandleSessionVoidedAsync(domainEvent.SessionId, $"Clase cancelada: {domainEvent.Reason}", cancellationToken);

    public Task HandleAsync(ClassSessionRescheduled domainEvent, CancellationToken cancellationToken = default) =>
        HandleSessionVoidedAsync(domainEvent.OriginalSessionId, "Clase reprogramada", cancellationToken);

    private async Task HandleSessionVoidedAsync(
        Guid sessionId,
        string cancellationReason,
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
            .Where(r => r.ClassSessionId == sessionId && r.IsActive &&
                        (r.Status == ReservationStatus.Reserved ||
                         r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.WaitList))
            .ToListAsync(cancellationToken);

        if (!activeReservations.Any())
        {
            session.ResetReservedCount();
            return;
        }

        foreach (var reservation in activeReservations)
        {
            Guid? refundMovementId = null;

            // Las reservas por ticket individual (sin membresía) quedan canceladas sin movimiento: se identifican
            // por MembershipId y RefundCreditMovementId nulos y un administrativo las compensa con
            // AddCompensatoryCreditAsync.
            if (reservation.Status != ReservationStatus.WaitList && reservation.Membership != null)
            {
                var refund = reservation.Membership.RefundCredit(session.Id, $"Devolución por sesión cancelada/suspendida ({cancellationReason})");
                _dbContext.MembershipCreditMovements.Add(refund);
                refundMovementId = refund.Id;
            }

            reservation.Cancel(cancellationReason, isLateCancellation: false, refundMovementId: refundMovementId);
            reservation.WaitListPosition = null;
        }

        session.ResetReservedCount();
    }
}
