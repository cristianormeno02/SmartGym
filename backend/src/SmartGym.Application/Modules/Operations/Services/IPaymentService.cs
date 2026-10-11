using SmartGym.Application.Modules.Operations.Dtos;

namespace SmartGym.Application.Modules.Operations.Services;

public interface IPaymentService
{
    Task<List<PaymentDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PaymentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<PaymentDto>> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<PaymentDto> RegisterPaymentAsync(RegisterPaymentRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<PaymentDto> ConfirmPaymentAsync(Guid id, ConfirmPaymentRequest? request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<PaymentDto> FailPaymentAsync(Guid id, FailPaymentRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<PaymentDto?> RefundPaymentAsync(Guid id, string reason, Guid currentUserId, CancellationToken cancellationToken = default);
}

