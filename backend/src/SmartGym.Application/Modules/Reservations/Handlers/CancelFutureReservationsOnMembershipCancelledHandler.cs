using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Memberships;

namespace SmartGym.Application.Modules.Reservations.Handlers;

/// <summary>
/// Cancela las reservas futuras asociadas a una membresía cuando ésta es cancelada definitivamente,
/// liberando los cupos en las sesiones correspondientes.
/// </summary>
public class CancelFutureReservationsOnMembershipCancelledHandler : IDomainEventHandler<MembershipCancelledEvent>
{
    private readonly ISmartGymDbContext _dbContext;

    public CancelFutureReservationsOnMembershipCancelledHandler(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task HandleAsync(MembershipCancelledEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var futureReservations = await _dbContext.Reservations
            .Include(r => r.ClassSession)
            .Where(r => r.MembershipId == domainEvent.MembershipId &&
                        r.IsActive &&
                        (r.Status == ReservationStatus.Reserved ||
                         r.Status == ReservationStatus.Confirmed ||
                         r.Status == ReservationStatus.WaitList))
            .ToListAsync(cancellationToken);

        foreach (var reservation in futureReservations)
        {
            if (reservation.ClassSession == null || reservation.ClassSession.StartsAtUtc <= domainEvent.CancelledAtUtc)
            {
                continue;
            }

            if (reservation.Status != ReservationStatus.WaitList)
            {
                reservation.ClassSession.DecrementReservedCount();
            }

            reservation.Cancel(
                $"Membresía cancelada: {domainEvent.Reason}",
                isLateCancellation: false,
                refundMovementId: null);
            reservation.WaitListPosition = null;
        }
    }
}
