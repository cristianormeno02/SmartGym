namespace SmartGym.Domain.Enums;

public static class PaymentStatusExtensions
{
    /// <summary>
    /// Estados de un cobro que llegó a confirmarse. Para filtrar en consultas EF usar la lista explícita
    /// <see cref="ConfirmedStatuses"/>, que sí se traduce a SQL.
    /// </summary>
    public static bool IsConfirmed(this PaymentStatus status) =>
        status is PaymentStatus.Completed or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;

    /// <summary>Estados confirmados con importe neto positivo: los que cuentan como dinero cobrado.</summary>
    public static bool HasNetAmount(this PaymentStatus status) =>
        status is PaymentStatus.Completed or PaymentStatus.PartiallyRefunded;

    public static readonly PaymentStatus[] ConfirmedStatuses =
        [PaymentStatus.Completed, PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded];

    public static readonly PaymentStatus[] NetAmountStatuses =
        [PaymentStatus.Completed, PaymentStatus.PartiallyRefunded];
}
