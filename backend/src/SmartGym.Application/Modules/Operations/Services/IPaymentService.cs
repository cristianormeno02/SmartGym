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

    // ---- Pasarela externa ----

    /// <summary>
    /// Crea un pago pendiente del proveedor por el saldo pendiente de la membresía y devuelve su URL de pago.
    /// </summary>
    Task<IdempotentResult<PaymentDto>> CreateCheckoutAsync(CreatePaymentCheckoutRequest request, Guid currentUserId, string? idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Cancela los checkouts externos pendientes ya vencidos. Devuelve cuántos canceló.</summary>
    Task<int> ExpireCheckoutsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Procesa una notificación del proveedor: verifica su firma, consulta al proveedor el estado real de la
    /// transacción y recién entonces actúa sobre el pago.
    /// </summary>
    Task<WebhookProcessingResult> ProcessWebhookAsync(SmartGym.Domain.Enums.PaymentProvider provider, SmartGym.Application.Modules.Operations.Interfaces.WebhookRequest request, CancellationToken cancellationToken = default);

    /// <summary>Contrasta un pago externo con el proveedor y resuelve sus devoluciones pendientes.</summary>
    Task<PaymentDto> ReconcileAsync(Guid id, Guid currentUserId, CancellationToken cancellationToken = default);

    Task<PagedResult<PaymentDiscrepancyDto>> GetDiscrepanciesAsync(SmartGym.Domain.Enums.PaymentDiscrepancyStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PaymentDiscrepancyDto> ResolveDiscrepancyAsync(Guid id, ResolvePaymentDiscrepancyRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
}
