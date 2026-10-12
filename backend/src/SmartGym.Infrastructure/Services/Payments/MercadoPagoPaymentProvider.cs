using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartGym.Application.Modules.Operations.Interfaces;
using SmartGym.Domain.Enums;

namespace SmartGym.Infrastructure.Services.Payments;

/// <summary>
/// Adaptador de Mercado Pago (Checkout Pro): preferencias de pago, consulta de pagos, reintegros y verificación
/// de la firma de sus webhooks. El resto del sistema sólo conoce el contrato <see cref="IPaymentProvider"/>.
/// </summary>
public class MercadoPagoPaymentProvider : IPaymentProvider
{
    private readonly MercadoPagoOptions _options;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    public MercadoPagoPaymentProvider(
        IOptions<MercadoPagoOptions> options,
        HttpClient httpClient,
        TimeProvider? timeProvider = null)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public PaymentProvider Provider => PaymentProvider.MercadoPago;

    // ---- Checkout ----

    public async Task<PaymentCheckoutResult> CreateCheckoutAsync(PaymentCheckoutContext context, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["items"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["title"] = context.Description,
                    ["quantity"] = 1,
                    ["unit_price"] = context.Amount,
                    ["currency_id"] = context.Currency
                }
            },
            // La referencia vuelve en cada pago de esta preferencia: así se localiza el pago interno
            ["external_reference"] = context.PaymentId.ToString(),
            ["expires"] = true,
            ["expiration_date_to"] = new DateTimeOffset(DateTime.SpecifyKind(context.ExpiresAtUtc, DateTimeKind.Utc))
                .ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(_options.NotificationUrl))
        {
            body["notification_url"] = _options.NotificationUrl;
        }

        if (!string.IsNullOrWhiteSpace(_options.SuccessUrl))
        {
            body["back_urls"] = new Dictionary<string, object?>
            {
                ["success"] = _options.SuccessUrl,
                ["failure"] = _options.FailureUrl ?? _options.SuccessUrl,
                ["pending"] = _options.PendingUrl ?? _options.SuccessUrl
            };
        }

        if (!string.IsNullOrWhiteSpace(context.PayerEmail))
        {
            body["payer"] = new Dictionary<string, object?> { ["email"] = context.PayerEmail };
        }

        using var request = CreateRequest(HttpMethod.Post, "checkout/preferences");
        request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var json = await ReadJsonAsync(response, cancellationToken);
        var id = GetString(json.RootElement, "id");
        var url = GetString(json.RootElement, "init_point");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Mercado Pago no devolvió el identificador o la URL del checkout.");
        }

        return new PaymentCheckoutResult(id, url);
    }

    // ---- Consulta ----

    public async Task<ExternalPaymentSnapshot?> GetPaymentAsync(string externalTransactionId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"v1/payments/{Uri.EscapeDataString(externalTransactionId)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var json = await ReadJsonAsync(response, cancellationToken);
        return ToSnapshot(json.RootElement);
    }

    public async Task<ExternalPaymentSnapshot?> FindPaymentByReferenceAsync(string paymentReference, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"v1/payments/search?external_reference={Uri.EscapeDataString(paymentReference)}&sort=date_created&criteria=desc");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var json = await ReadJsonAsync(response, cancellationToken);
        if (!json.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var snapshots = results.EnumerateArray()
            .Select(ToSnapshot)
            .Where(s => s != null)
            .Select(s => s!)
            .ToList();

        // Un checkout admite varios intentos: el que importa es el aprobado
        return snapshots.FirstOrDefault(s => s.Status == ExternalPaymentStatus.Approved) ?? snapshots.FirstOrDefault();
    }

    // ---- Reintegros ----

    public async Task<ExternalRefundResult> RequestRefundAsync(
        string externalTransactionId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"v1/payments/{Uri.EscapeDataString(externalTransactionId)}/refunds");
        // Evita un reintegro duplicado si la llamada se reintenta
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new Dictionary<string, object?> { ["amount"] = amount });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new ExternalRefundResult(false, null, $"Mercado Pago respondió {(int)response.StatusCode}: {ExtractMessage(content)}");
        }

        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
        var refundId = GetString(json.RootElement, "id");
        var status = GetString(json.RootElement, "status");

        return string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase)
            ? new ExternalRefundResult(true, refundId)
            : new ExternalRefundResult(false, refundId, $"Mercado Pago dejó el reintegro en estado '{status ?? "desconocido"}'");
    }

    // ---- Webhooks ----

    /// <summary>
    /// Mercado Pago firma con HMAC-SHA256 (hex) el manifiesto <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c>,
    /// donde <c>data.id</c> viene en la query string (en minúsculas si es alfanumérico) y <c>ts</c> y el hash <c>v1</c>
    /// vienen en la cabecera <c>x-signature</c>. El cuerpo no está firmado, así que no se usa ningún dato de él.
    /// </summary>
    public ExternalWebhookEvent? VerifyAndParseWebhook(WebhookRequest request)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            return null;
        }

        var signatureHeader = FindValue(request.Headers, "x-signature");
        var requestId = FindValue(request.Headers, "x-request-id");
        var dataId = FindValue(request.Query, "data.id");

        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(dataId))
        {
            return null;
        }

        string? timestamp = null;
        string? hash = null;
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (name == "ts")
            {
                timestamp = value;
            }
            else if (name == "v1")
            {
                hash = value;
            }
        }

        if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(hash) || !IsWithinTolerance(timestamp))
        {
            return null;
        }

        var manifest = $"id:{dataId.ToLowerInvariant()};request-id:{requestId};ts:{timestamp};";
        var expected = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.WebhookSecret), Encoding.UTF8.GetBytes(manifest)));

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(hash.ToLowerInvariant()));

        // x-request-id identifica la notificación y, a diferencia del id del cuerpo, está cubierto por la firma
        return matches
            ? new ExternalWebhookEvent(requestId, dataId, FindValue(request.Query, "type") ?? "payment")
            : null;
    }

    /// <summary>La marca de tiempo llega en segundos o en milisegundos según la notificación.</summary>
    private bool IsWithinTolerance(string timestamp)
    {
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        DateTimeOffset signedAt;
        try
        {
            signedAt = value >= 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var difference = (_timeProvider.GetUtcNow() - signedAt).Duration();
        return difference <= TimeSpan.FromSeconds(Math.Max(0, _options.WebhookToleranceSeconds));
    }

    // ---- Auxiliares ----

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static ExternalPaymentSnapshot? ToSnapshot(JsonElement payment)
    {
        var id = GetString(payment, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var rawStatus = GetString(payment, "status") ?? string.Empty;
        return new ExternalPaymentSnapshot(
            id,
            MapStatus(rawStatus),
            GetDecimal(payment, "transaction_amount"),
            GetString(payment, "currency_id") ?? string.Empty,
            GetDecimal(payment, "transaction_amount_refunded"),
            GetString(payment, "external_reference"),
            rawStatus);
    }

    private static ExternalPaymentStatus MapStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "approved" => ExternalPaymentStatus.Approved,
            // Aún sin cobrar o en revisión: el pago interno sigue pendiente
            "pending" or "in_process" or "authorized" or "in_mediation" => ExternalPaymentStatus.Pending,
            "rejected" => ExternalPaymentStatus.Rejected,
            "cancelled" => ExternalPaymentStatus.Cancelled,
            "refunded" => ExternalPaymentStatus.Refunded,
            "charged_back" => ExternalPaymentStatus.ChargedBack,
            _ => ExternalPaymentStatus.Unknown
        };

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    private static decimal GetDecimal(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : 0m;

    private static string ExtractMessage(string content)
    {
        try
        {
            using var json = JsonDocument.Parse(content);
            return GetString(json.RootElement, "message") ?? "sin detalle";
        }
        catch (JsonException)
        {
            return "sin detalle";
        }
    }

    private static string? FindValue(IReadOnlyDictionary<string, string> values, string key)
    {
        if (values.TryGetValue(key, out var exact))
        {
            return exact;
        }

        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
