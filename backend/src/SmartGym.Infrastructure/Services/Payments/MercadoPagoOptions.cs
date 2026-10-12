namespace SmartGym.Infrastructure.Services.Payments;

/// <summary>
/// Credenciales y direcciones de Mercado Pago. Se cargan desde user-secrets o variables de entorno
/// (<c>MercadoPago:AccessToken</c>, <c>MercadoPago:WebhookSecret</c>...); nunca van a la base ni al repositorio.
/// Sin <see cref="AccessToken"/> y <see cref="WebhookSecret"/> el proveedor no se registra.
/// </summary>
public class MercadoPagoOptions
{
    public const string SectionName = "MercadoPago";

    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Clave secreta de la configuración de Webhooks de la aplicación en Mercado Pago.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.mercadopago.com/";

    /// <summary>URL pública de <c>POST /api/payments/webhooks/mercadopago</c>.</summary>
    public string? NotificationUrl { get; set; }

    /// <summary>Adonde vuelve el pagador al terminar el checkout. No confirma el pago: eso lo hace el webhook.</summary>
    public string? SuccessUrl { get; set; }
    public string? FailureUrl { get; set; }
    public string? PendingUrl { get; set; }

    /// <summary>Diferencia máxima entre la marca de tiempo firmada de una notificación y la hora actual.</summary>
    public int WebhookToleranceSeconds { get; set; } = 300;

    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(WebhookSecret);
}
