using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Interfaces;

/// <summary>Estado de una transacción en el proveedor, normalizado para el resto del sistema.</summary>
public enum ExternalPaymentStatus
{
    Unknown = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Refunded = 5,
    ChargedBack = 6
}

/// <param name="PaymentId">Identificador del pago interno: se envía al proveedor como referencia externa.</param>
public record PaymentCheckoutContext(
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string Description,
    DateTime ExpiresAtUtc,
    string? PayerEmail = null
);

/// <param name="ExternalReference">Identificador del checkout en el proveedor.</param>
public record PaymentCheckoutResult(
    string ExternalReference,
    string CheckoutUrl
);

/// <param name="PaymentReference">La referencia que se envió al crear el checkout: el Id del pago interno.</param>
/// <param name="RawStatus">Estado tal como lo informa el proveedor, para dejarlo en discrepancias y auditoría.</param>
public record ExternalPaymentSnapshot(
    string ExternalTransactionId,
    ExternalPaymentStatus Status,
    decimal Amount,
    string Currency,
    decimal RefundedAmount,
    string? PaymentReference,
    string RawStatus
);

public record ExternalRefundResult(
    bool Succeeded,
    string? ExternalRefundId,
    string? FailureReason = null
);

/// <summary>
/// La notificación completa: la firma de un proveedor puede cubrir cabeceras y parámetros de la URL, no sólo el cuerpo.
/// </summary>
public record WebhookRequest(
    string RawBody,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Query
);

/// <summary>Datos de una notificación cuya firma ya se verificó. No incluye el estado: se consulta al proveedor.</summary>
public record ExternalWebhookEvent(
    string ExternalEventId,
    string ExternalTransactionId,
    string? EventType
);

public interface IPaymentProvider
{
    PaymentProvider Provider { get; }

    Task<PaymentCheckoutResult> CreateCheckoutAsync(PaymentCheckoutContext context, CancellationToken cancellationToken = default);

    /// <summary>Devuelve <c>null</c> si el proveedor no conoce la transacción.</summary>
    Task<ExternalPaymentSnapshot?> GetPaymentAsync(string externalTransactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca la transacción por la referencia del pago interno, para conciliar uno que aún no tiene
    /// identificador de transacción. Si hay varias, devuelve la aprobada.
    /// </summary>
    Task<ExternalPaymentSnapshot?> FindPaymentByReferenceAsync(string paymentReference, CancellationToken cancellationToken = default);

    Task<ExternalRefundResult> RequestRefundAsync(
        string externalTransactionId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Verifica la firma de la notificación. Devuelve <c>null</c> si no es auténtica.</summary>
    ExternalWebhookEvent? VerifyAndParseWebhook(WebhookRequest request);
}

public interface IPaymentProviderResolver
{
    /// <summary>Lanza <c>PROVIDER_NOT_SUPPORTED</c> si no hay una implementación configurada.</summary>
    IPaymentProvider Resolve(PaymentProvider provider);

    bool TryResolve(PaymentProvider provider, out IPaymentProvider? paymentProvider);
}
