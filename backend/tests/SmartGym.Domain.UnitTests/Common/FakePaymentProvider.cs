using SmartGym.Application.Modules.Operations.Interfaces;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.UnitTests.Common;

/// <summary>
/// Proveedor de pagos en memoria para las pruebas: registra las llamadas y responde lo que la prueba configure.
/// Una notificación es auténtica si trae la cabecera <c>x-test-signature: valid</c>.
/// </summary>
public sealed class FakePaymentProvider : IPaymentProvider
{
    public const string SignatureHeader = "x-test-signature";

    private readonly object _gate = new();

    public PaymentProvider Provider { get; init; } = PaymentProvider.MercadoPago;

    public List<PaymentCheckoutContext> Checkouts { get; } = new();
    public List<(string ExternalTransactionId, decimal Amount, string IdempotencyKey)> RefundRequests { get; } = new();
    public Dictionary<string, ExternalPaymentSnapshot> Payments { get; } = new();

    public Exception? CheckoutFailure { get; set; }
    public Exception? LookupFailure { get; set; }
    public Exception? RefundFailure { get; set; }
    public Func<decimal, ExternalRefundResult>? RefundResponse { get; set; }

    /// <summary>Corre mientras la llamada al proveedor está en curso, para simular lo que pasa entretanto.</summary>
    public Func<Task>? OnRefundRequested { get; set; }

    public Task<PaymentCheckoutResult> CreateCheckoutAsync(PaymentCheckoutContext context, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Checkouts.Add(context);
        }

        if (CheckoutFailure != null)
        {
            throw CheckoutFailure;
        }

        return Task.FromResult(new PaymentCheckoutResult(
            $"pref-{context.PaymentId:N}",
            $"https://pay.test/checkout/{context.PaymentId:N}"));
    }

    public Task<ExternalPaymentSnapshot?> GetPaymentAsync(string externalTransactionId, CancellationToken cancellationToken = default)
    {
        if (LookupFailure != null)
        {
            throw LookupFailure;
        }

        lock (_gate)
        {
            return Task.FromResult(Payments.TryGetValue(externalTransactionId, out var snapshot) ? snapshot : null);
        }
    }

    public Task<ExternalPaymentSnapshot?> FindPaymentByReferenceAsync(string paymentReference, CancellationToken cancellationToken = default)
    {
        if (LookupFailure != null)
        {
            throw LookupFailure;
        }

        lock (_gate)
        {
            var matches = Payments.Values.Where(p => p.PaymentReference == paymentReference).ToList();
            return Task.FromResult(
                matches.FirstOrDefault(p => p.Status == ExternalPaymentStatus.Approved) ?? matches.FirstOrDefault());
        }
    }

    public async Task<ExternalRefundResult> RequestRefundAsync(
        string externalTransactionId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            RefundRequests.Add((externalTransactionId, amount, idempotencyKey));
        }

        if (OnRefundRequested != null)
        {
            await OnRefundRequested();
        }

        if (RefundFailure != null)
        {
            throw RefundFailure;
        }

        return RefundResponse?.Invoke(amount) ?? new ExternalRefundResult(true, $"ref-{Guid.NewGuid():N}");
    }

    public ExternalWebhookEvent? VerifyAndParseWebhook(WebhookRequest request)
    {
        if (!request.Headers.TryGetValue(SignatureHeader, out var signature) || signature != "valid")
        {
            return null;
        }

        return request.Query.TryGetValue("data.id", out var transactionId) &&
               request.Headers.TryGetValue("x-request-id", out var eventId)
            ? new ExternalWebhookEvent(eventId, transactionId, "payment")
            : null;
    }

    // ---- Ayudas para armar escenarios ----

    public ExternalPaymentSnapshot SetPayment(
        string externalTransactionId,
        Guid? paymentId,
        decimal amount,
        ExternalPaymentStatus status = ExternalPaymentStatus.Approved,
        string currency = "ARS",
        decimal refundedAmount = 0m)
    {
        var snapshot = new ExternalPaymentSnapshot(
            externalTransactionId, status, amount, currency, refundedAmount, paymentId?.ToString(), status.ToString().ToLowerInvariant());
        lock (_gate)
        {
            Payments[externalTransactionId] = snapshot;
        }

        return snapshot;
    }

    public static WebhookRequest Webhook(string externalTransactionId, string? eventId = null, bool validSignature = true) =>
        new(
            RawBody: "{}",
            Headers: new Dictionary<string, string>
            {
                [SignatureHeader] = validSignature ? "valid" : "forged",
                ["x-request-id"] = eventId ?? Guid.NewGuid().ToString()
            },
            Query: new Dictionary<string, string> { ["data.id"] = externalTransactionId });
}
