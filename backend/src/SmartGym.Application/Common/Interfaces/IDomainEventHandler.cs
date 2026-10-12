using System.Threading;
using System.Threading.Tasks;
using SmartGym.Domain.Common;

namespace SmartGym.Application.Common.Interfaces;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
