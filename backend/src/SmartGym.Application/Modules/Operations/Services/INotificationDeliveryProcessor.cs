namespace SmartGym.Application.Modules.Operations.Services;

public interface INotificationDeliveryProcessor
{
    Task<int> ProcessPendingBatchAsync(int? batchSize = null, CancellationToken cancellationToken = default);
    Task<int> RecoverStaleProcessingDeliveriesAsync(CancellationToken cancellationToken = default);
}
