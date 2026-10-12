namespace SmartGym.Domain.Enums;

public enum PaymentDiscrepancyType
{
    /// <summary>El proveedor cobró un importe distinto del pago interno.</summary>
    AmountMismatch = 1,

    /// <summary>El proveedor cobró en una moneda distinta de la del pago interno.</summary>
    CurrencyMismatch = 2,

    /// <summary>El proveedor aprobó un cobro sobre un pago interno cancelado, vencido o rechazado.</summary>
    ApprovedAfterTerminalState = 3,

    /// <summary>El cobro se confirmó, pero la membresía quedó con más pagado que su precio.</summary>
    Overpayment = 4,

    /// <summary>El proveedor aprobó otra transacción para un pago que ya estaba confirmado.</summary>
    DuplicateCharge = 5,

    /// <summary>El proveedor informa una devolución o contracargo que el sistema no registró.</summary>
    ExternalRefund = 6,

    /// <summary>El proveedor informa una transacción que no corresponde a ningún pago interno.</summary>
    UnknownPayment = 7
}
