using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Memberships;

namespace SmartGym.Application.Modules.Reservations.Handlers;

/// <summary>
/// Cancela con devolución de crédito y liberación de cupo las reservas futuras de una membresía
/// que caen dentro de la ventana de suspensión (o todas las futuras si la suspensión es abierta).
/// </summary>
public class CancelReservationsOnMembershipSuspendedHandler : IDomainEventHandler<MembershipSuspendedEvent>
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly ICreditLedgerService _creditLedgerService;
    private readonly IGymCalendar _gymCalendar;

    public CancelReservationsOnMembershipSuspendedHandler(
        ISmartGymDbContext dbContext,
        ICreditLedgerService? creditLedgerService = null,
        IGymCalendar? gymCalendar = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar ?? new GymCalendar(Options.Create(new GymOptions()));
        _creditLedgerService = creditLedgerService ?? new CreditLedgerService(dbContext, _gymCalendar);
    }

    public async Task HandleAsync(MembershipSuspendedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var activeReservations = await _dbContext.Reservations
            .Include(r => r.ClassSession)
            .Include(r => r.Membership)
            .Where(r => r.MembershipId == domainEvent.MembershipId &&
                        r.IsActive &&
                        (r.Status == ReservationStatus.Reserved ||
                         r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.WaitList))
            .ToListAsync(cancellationToken);

        foreach (var reservation in activeReservations)
        {
            if (reservation.ClassSession == null)
            {
                continue;
            }

            if (reservation.ClassSession.StartsAtUtc <= domainEvent.SuspendedAtUtc)
            {
                continue;
            }

            var sessionLocalDate = DateOnly.FromDateTime(_gymCalendar.ToLocal(reservation.ClassSession.StartsAtUtc).DateTime);
            if (sessionLocalDate < domainEvent.StartDate)
            {
                continue;
            }

            if (domainEvent.EndDate.HasValue && sessionLocalDate > domainEvent.EndDate.Value)
            {
                continue;
            }

            Guid? refundMovementId = null;
            if (reservation.Status != ReservationStatus.WaitList)
            {
                if (reservation.Membership != null)
                {
                    var refund = await _creditLedgerService.RefundCreditForReservationAsync(
                        reservation.Membership,
                        reservation.ClassSessionId,
                        $"Devolución por suspensión de membresía: {domainEvent.Reason}",
                        domainEvent.SuspendedByUserId,
                        cancellationToken);
                    refundMovementId = refund.Id;
                }

                reservation.ClassSession.DecrementReservedCount();
            }

            reservation.Cancel(
                $"Membresía suspendida: {domainEvent.Reason}",
                isLateCancellation: false,
                refundMovementId: refundMovementId);
            reservation.WaitListPosition = null;
        }
    }
}
