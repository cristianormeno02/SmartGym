namespace SmartGym.Application.Modules.Operations;

public static class PaymentErrorCodes
{
    public const string NotFound = "PAYMENT_NOT_FOUND";
    public const string ObligationNotFound = "OBLIGATION_NOT_FOUND";
    public const string InvalidTransition = "PAYMENT_INVALID_TRANSITION";
    public const string AlreadyApplied = "PAYMENT_ALREADY_APPLIED";
    public const string ObligationOverpaid = "OBLIGATION_OVERPAID";
    public const string ObligationCancelled = "OBLIGATION_CANCELLED";
    public const string ObligationStudentMismatch = "OBLIGATION_STUDENT_MISMATCH";
    public const string CurrencyMismatch = "CURRENCY_MISMATCH";
    public const string RefundAmountExceeded = "REFUND_AMOUNT_EXCEEDED";
    public const string IdempotencyKeyMismatch = "IDEMPOTENCY_KEY_MISMATCH";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
}
