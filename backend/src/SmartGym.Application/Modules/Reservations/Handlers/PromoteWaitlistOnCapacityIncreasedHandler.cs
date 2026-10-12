using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Events.Activities;

namespace SmartGym.Application.Modules.Reservations.Handlers;

/// <summary>
/// Promueve candidatos de la lista de espera cuando se incrementa la capacidad de una clase concreta abierta.
/// </summary>
public class PromoteWaitlistOnCapacityIncreasedHandler : IDomainEventHandler<ClassSessionCapacityIncreased>
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IReservationService _reservationService;

    public PromoteWaitlistOnCapacityIncreasedHandler(
        ISmartGymDbContext dbContext,
        IReservationService? reservationService = null,
        IGymCalendar? gymCalendar = null)
    {
        _dbContext = dbContext;
        var calendar = gymCalendar ?? new GymCalendar(Options.Create(new GymOptions()));
        _reservationService = reservationService ?? new ReservationService(dbContext, calendar);
    }

    public async Task HandleAsync(ClassSessionCapacityIncreased domainEvent, CancellationToken cancellationToken = default)
    {
        await _dbContext.LockClassSessionForUpdateAsync(domainEvent.SessionId, cancellationToken);

        var session = await _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .Include(cs => cs.Room)
            .FirstOrDefaultAsync(cs => cs.Id == domainEvent.SessionId && cs.IsActive, cancellationToken);

        if (session == null)
        {
            return;
        }

        await _reservationService.PromoteWaitlistAsync(session, domainEvent.ByUserId, cancellationToken);
    }
}
