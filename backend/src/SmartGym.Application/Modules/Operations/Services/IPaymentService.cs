using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Operations.Dtos;

namespace SmartGym.Application.Modules.Operations.Services;

public interface IPaymentService
{
    Task<PagedResult<PaymentDto>> GetPagedAsync(PaymentListFilter filter, CancellationToken cancellationToken = default);
    Task<PaymentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<PaymentDto>> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default);
    Task<List<PaymentRefundDto>> GetRefundsAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<MembershipBalanceDto> GetMembershipBalanceAsync(Guid membershipId, CancellationToken cancellationToken = default);

    Task<PaymentDto> RegisterPaymentAsync(RegisterPaymentRequest request, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra un pago con clave de idempotencia (única por pagador). Repetir la clave con los mismos datos
    /// devuelve el pago existente; con datos distintos lanza <c>IDEMPOTENCY_KEY_MISMATCH</c>.
    /// </summary>
    Task<IdempotentResult<PaymentDto>> RegisterPaymentAsync(RegisterPaymentRequest request, Guid currentUserId, string? idempotencyKey, CancellationToken cancellationToken = default);

    Task<PaymentDto> ConfirmPaymentAsync(Guid id, ConfirmPaymentRequest? request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<PaymentDto> FailPaymentAsync(Guid id, FailPaymentRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<PaymentDto> CancelPaymentAsync(Guid id, CancelPaymentRequest request, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>Aplica a una membresía o a una clase un pago que todavía no tiene concepto.</summary>
    Task<PaymentDto> ApplyPaymentAsync(Guid id, ApplyPaymentRequest request, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<IdempotentResult<PaymentRefundDto>> CreateRefundAsync(Guid paymentId, CreatePaymentRefundRequest request, Guid currentUserId, string? idempotencyKey, CancellationToken cancellationToken = default);
}
