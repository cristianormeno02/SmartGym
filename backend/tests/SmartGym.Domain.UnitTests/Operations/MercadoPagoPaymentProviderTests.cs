using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartGym.Application.Modules.Operations.Interfaces;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Services.Payments;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class MercadoPagoPaymentProviderTests
{
    private const string Secret = "test-webhook-secret";
    private const string RequestId = "bb56a2f1-6aae-46ac-982e-9dcd3581d08e";

    // HMAC-SHA256 (hex) de "id:123456;request-id:bb56a2f1-6aae-46ac-982e-9dcd3581d08e;ts:1742505638683;" con Secret,
    // calculado fuera del adaptador.
    private const string SignatureForNumericId = "9912fe642bf0ea310759733dfef36b290480e0dd611e937db2e65052aca627f4";

    // Ídem para "id:ord01jq4s4ky8hwq6na5pxb65b3d3;request-id:...;ts:1742505638;" (id en minúsculas, ts en segundos)
    private const string SignatureForAlphanumericId = "eff254fee686ddf5e0b153c47830a96017656a7c9e30b9493394cde020e8983e";

    private static readonly DateTimeOffset SignedAt = DateTimeOffset.FromUnixTimeMilliseconds(1742505638683);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _respond;
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return _respond(request, body);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static MercadoPagoPaymentProvider CreateProvider(
        StubHandler? handler = null,
        DateTimeOffset? now = null,
        int toleranceSeconds = 300)
    {
        var options = Options.Create(new MercadoPagoOptions
        {
            AccessToken = "TEST-access-token",
            WebhookSecret = Secret,
            NotificationUrl = "https://gym.test/api/payments/webhooks/mercadopago",
            SuccessUrl = "https://gym.test/portal/alumno",
            WebhookToleranceSeconds = toleranceSeconds
        });
        var http = new HttpClient(handler ?? new StubHandler((_, _) => Json(HttpStatusCode.NotFound, "{}")))
        {
            BaseAddress = new Uri("https://api.mercadopago.com/")
        };
        return new MercadoPagoPaymentProvider(options, http, new FixedTimeProvider(now ?? SignedAt.AddSeconds(5)));
    }

    private static WebhookRequest Webhook(string dataId, string? signature, string? requestId = RequestId) =>
        new(
            RawBody: "{\"action\":\"payment.updated\",\"type\":\"payment\",\"data\":{\"id\":\"" + dataId + "\"}}",
            Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                .With("x-signature", signature)
                .With("x-request-id", requestId),
            Query: new Dictionary<string, string> { ["data.id"] = dataId, ["type"] = "payment" });

    // ---- Firma del webhook ----

    [Fact]
    public void VerifyAndParseWebhook_WithValidSignature_ReturnsTheSignedEventData()
    {
        var provider = CreateProvider();

        var evt = provider.VerifyAndParseWebhook(Webhook("123456", $"ts=1742505638683,v1={SignatureForNumericId}"));

        Assert.NotNull(evt);
        Assert.Equal("123456", evt!.ExternalTransactionId);
        // El identificador del evento es el x-request-id, que sí está cubierto por la firma
        Assert.Equal(RequestId, evt.ExternalEventId);
        Assert.Equal("payment", evt.EventType);
    }

    [Fact]
    public void VerifyAndParseWebhook_LowercasesAlphanumericIds_AndAcceptsTimestampsInSeconds()
    {
        var provider = CreateProvider(now: DateTimeOffset.FromUnixTimeSeconds(1742505638).AddSeconds(30));

        var evt = provider.VerifyAndParseWebhook(
            Webhook("ORD01JQ4S4KY8HWQ6NA5PXB65B3D3", $"ts=1742505638, v1={SignatureForAlphanumericId}"));

        Assert.NotNull(evt);
        Assert.Equal("ORD01JQ4S4KY8HWQ6NA5PXB65B3D3", evt!.ExternalTransactionId);
    }

    [Theory]
    [InlineData("ts=1742505638683,v1=0012fe642bf0ea310759733dfef36b290480e0dd611e937db2e65052aca627f4")] // hash alterado
    [InlineData("ts=1742505638684,v1=9912fe642bf0ea310759733dfef36b290480e0dd611e937db2e65052aca627f4")] // ts alterado
    [InlineData("v1=9912fe642bf0ea310759733dfef36b290480e0dd611e937db2e65052aca627f4")]                  // sin ts
    [InlineData("ts=1742505638683")]                                                                    // sin hash
    [InlineData("ts=abc,v1=xyz")]
    [InlineData("")]
    [InlineData(null)]
    public void VerifyAndParseWebhook_WithInvalidOrMissingSignature_ReturnsNull(string? signature)
    {
        var provider = CreateProvider();

        Assert.Null(provider.VerifyAndParseWebhook(Webhook("123456", signature)));
    }

    [Fact]
    public void VerifyAndParseWebhook_WithDifferentTransactionOrRequestId_ReturnsNull()
    {
        var provider = CreateProvider();
        var signature = $"ts=1742505638683,v1={SignatureForNumericId}";

        // La firma cubre el id de la transacción y el x-request-id: cambiarlos la invalida
        Assert.Null(provider.VerifyAndParseWebhook(Webhook("999999", signature)));
        Assert.Null(provider.VerifyAndParseWebhook(Webhook("123456", signature, requestId: "otro-request")));
        Assert.Null(provider.VerifyAndParseWebhook(Webhook("123456", signature, requestId: null)));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(-301)]
    public void VerifyAndParseWebhook_OutsideTheTimestampTolerance_ReturnsNull(int secondsFromSignature)
    {
        var provider = CreateProvider(now: SignedAt.AddSeconds(secondsFromSignature));

        Assert.Null(provider.VerifyAndParseWebhook(Webhook("123456", $"ts=1742505638683,v1={SignatureForNumericId}")));
    }

    [Fact]
    public void VerifyAndParseWebhook_WithoutConfiguredSecret_RejectsEverything()
    {
        var options = Options.Create(new MercadoPagoOptions { AccessToken = "TEST-access-token", WebhookSecret = "" });
        var provider = new MercadoPagoPaymentProvider(options, new HttpClient(), new FixedTimeProvider(SignedAt));

        Assert.Null(provider.VerifyAndParseWebhook(Webhook("123456", $"ts=1742505638683,v1={SignatureForNumericId}")));
    }

    // ---- Consulta de pagos ----

    [Theory]
    [InlineData("approved", ExternalPaymentStatus.Approved)]
    [InlineData("pending", ExternalPaymentStatus.Pending)]
    [InlineData("in_process", ExternalPaymentStatus.Pending)]
    [InlineData("authorized", ExternalPaymentStatus.Pending)]
    [InlineData("in_mediation", ExternalPaymentStatus.Pending)]
    [InlineData("rejected", ExternalPaymentStatus.Rejected)]
    [InlineData("cancelled", ExternalPaymentStatus.Cancelled)]
    [InlineData("refunded", ExternalPaymentStatus.Refunded)]
    [InlineData("charged_back", ExternalPaymentStatus.ChargedBack)]
    [InlineData("something_new", ExternalPaymentStatus.Unknown)]
    public async Task GetPaymentAsync_MapsProviderStatusToTheNormalizedSnapshot(string providerStatus, ExternalPaymentStatus expected)
    {
        var paymentId = Guid.NewGuid();
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, $$"""
            {
              "id": 123456,
              "status": "{{providerStatus}}",
              "transaction_amount": 40000.50,
              "currency_id": "ARS",
              "external_reference": "{{paymentId}}",
              "transaction_amount_refunded": 1000.25
            }
            """));
        var provider = CreateProvider(handler);

        var snapshot = await provider.GetPaymentAsync("123456");

        Assert.NotNull(snapshot);
        Assert.Equal("123456", snapshot!.ExternalTransactionId);
        Assert.Equal(expected, snapshot.Status);
        Assert.Equal(providerStatus, snapshot.RawStatus);
        Assert.Equal(40000.50m, snapshot.Amount);
        Assert.Equal("ARS", snapshot.Currency);
        Assert.Equal(1000.25m, snapshot.RefundedAmount);
        Assert.Equal(paymentId.ToString(), snapshot.PaymentReference);

        var (request, _) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.mercadopago.com/v1/payments/123456", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("TEST-access-token", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task GetPaymentAsync_ReturnsNullWhenUnknown_AndThrowsOnProviderErrors()
    {
        var notFound = CreateProvider(new StubHandler((_, _) => Json(HttpStatusCode.NotFound, "{\"message\":\"not found\"}")));
        Assert.Null(await notFound.GetPaymentAsync("404"));

        var failing = CreateProvider(new StubHandler((_, _) => Json(HttpStatusCode.InternalServerError, "{}")));
        await Assert.ThrowsAsync<HttpRequestException>(() => failing.GetPaymentAsync("500"));
    }

    [Fact]
    public async Task FindPaymentByReferenceAsync_SearchesByExternalReference_AndPrefersTheApprovedTransaction()
    {
        var paymentId = Guid.NewGuid();
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, $$"""
            {
              "results": [
                { "id": 1, "status": "rejected", "transaction_amount": 60000, "currency_id": "ARS", "external_reference": "{{paymentId}}" },
                { "id": 2, "status": "approved", "transaction_amount": 60000, "currency_id": "ARS", "external_reference": "{{paymentId}}" }
              ]
            }
            """));
        var provider = CreateProvider(handler);

        var snapshot = await provider.FindPaymentByReferenceAsync(paymentId.ToString());

        Assert.Equal("2", snapshot!.ExternalTransactionId);
        Assert.Equal(ExternalPaymentStatus.Approved, snapshot.Status);
        var uri = Assert.Single(handler.Requests).Request.RequestUri!.ToString();
        Assert.StartsWith("https://api.mercadopago.com/v1/payments/search?", uri);
        Assert.Contains($"external_reference={paymentId}", uri);

        var empty = CreateProvider(new StubHandler((_, _) => Json(HttpStatusCode.OK, "{\"results\":[]}")));
        Assert.Null(await empty.FindPaymentByReferenceAsync(paymentId.ToString()));
    }

    // ---- Checkout ----

    [Fact]
    public async Task CreateCheckoutAsync_CreatesAnExpiringPreference_ReferencedByThePaymentId()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.Created, """
            { "id": "pref-123", "init_point": "https://www.mercadopago.com.ar/checkout/v1/redirect?pref_id=pref-123" }
            """));
        var provider = CreateProvider(handler);
        var paymentId = Guid.NewGuid();
        var expiresAt = new DateTime(2026, 10, 12, 16, 0, 0, DateTimeKind.Utc);

        var result = await provider.CreateCheckoutAsync(
            new PaymentCheckoutContext(paymentId, 40000.50m, "ARS", "Membresía SmartGym", expiresAt));

        Assert.Equal("pref-123", result.ExternalReference);
        Assert.Equal("https://www.mercadopago.com.ar/checkout/v1/redirect?pref_id=pref-123", result.CheckoutUrl);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.mercadopago.com/checkout/preferences", request.RequestUri!.ToString());
        Assert.Equal("TEST-access-token", request.Headers.Authorization!.Parameter);

        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal(paymentId.ToString(), root.GetProperty("external_reference").GetString());
        Assert.Equal("https://gym.test/api/payments/webhooks/mercadopago", root.GetProperty("notification_url").GetString());
        Assert.True(root.GetProperty("expires").GetBoolean());
        Assert.Equal(expiresAt, root.GetProperty("expiration_date_to").GetDateTimeOffset().UtcDateTime);

        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal("Membresía SmartGym", item.GetProperty("title").GetString());
        Assert.Equal(1, item.GetProperty("quantity").GetInt32());
        Assert.Equal(40000.50m, item.GetProperty("unit_price").GetDecimal());
        Assert.Equal("ARS", item.GetProperty("currency_id").GetString());
    }

    [Fact]
    public async Task CreateCheckoutAsync_ThrowsWhenTheProviderRejectsOrOmitsTheUrl()
    {
        var rejecting = CreateProvider(new StubHandler((_, _) => Json(HttpStatusCode.BadRequest, "{\"message\":\"invalid\"}")));
        await Assert.ThrowsAsync<HttpRequestException>(() => rejecting.CreateCheckoutAsync(
            new PaymentCheckoutContext(Guid.NewGuid(), 100m, "ARS", "x", DateTime.UtcNow.AddHours(1))));

        var incomplete = CreateProvider(new StubHandler((_, _) => Json(HttpStatusCode.Created, "{\"id\":\"pref-1\"}")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => incomplete.CreateCheckoutAsync(
            new PaymentCheckoutContext(Guid.NewGuid(), 100m, "ARS", "x", DateTime.UtcNow.AddHours(1))));
    }

    // ---- Reintegros ----

    [Fact]
    public async Task RequestRefundAsync_PostsAPartialRefund_WithTheIdempotencyKey()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.Created, "{ \"id\": 987, \"status\": \"approved\", \"amount\": 10000 }"));
        var provider = CreateProvider(handler);

        var result = await provider.RequestRefundAsync("123456", 10000m, "refund-key-1");

        Assert.True(result.Succeeded);
        Assert.Equal("987", result.ExternalRefundId);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.mercadopago.com/v1/payments/123456/refunds", request.RequestUri!.ToString());
        Assert.Equal("refund-key-1", Assert.Single(request.Headers.GetValues("X-Idempotency-Key")));
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(10000m, json.RootElement.GetProperty("amount").GetDecimal());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{\"message\":\"Amount attempting to refund is greater than payment\"}")]
    [InlineData(HttpStatusCode.Created, "{ \"id\": 987, \"status\": \"rejected\" }")]
    public async Task RequestRefundAsync_ReportsFailureWhenTheProviderDoesNotApproveIt(HttpStatusCode status, string body)
    {
        var provider = CreateProvider(new StubHandler((_, _) => Json(status, body)));

        var result = await provider.RequestRefundAsync("123456", 10000m, "refund-key-1");

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
    }

    [Fact]
    public void Provider_IdentifiesItselfAsMercadoPago()
    {
        Assert.Equal(PaymentProvider.MercadoPago, CreateProvider().Provider);
    }
}

internal static class DictionaryTestExtensions
{
    public static Dictionary<string, string> With(this Dictionary<string, string> dictionary, string key, string? value)
    {
        if (value != null)
        {
            dictionary[key] = value;
        }

        return dictionary;
    }
}
