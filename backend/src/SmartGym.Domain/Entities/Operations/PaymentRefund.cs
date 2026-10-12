using SmartGym.Domain.Common;

namespace SmartGym.Domain.Entities.Operations;

public class PaymentRefund : BaseEntity
{
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }

    // Nulo sólo en las devoluciones migradas desde el modelo anterior, que no registraba al responsable.
    public Guid? RequestedByUserId { get; set; }
    public string? IdempotencyKey { get; set; }
}
