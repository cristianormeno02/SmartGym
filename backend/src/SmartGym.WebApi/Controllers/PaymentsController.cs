using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartGym.Application.Modules.Operations.Interfaces;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Enums;

namespace SmartGym.WebApi.Controllers;

/// <summary>Contrato de error de <c>/api/payments</c>: código estable, mensaje y detalle.</summary>
public record PaymentApiError(string Code, string Message, object? Details = null);

/// <summary>
/// Las políticas habilitan cada escritura por permiso. Las consultas de un pago, de sus devoluciones y del saldo
/// de una membresía admiten además al alumno titular, que se valida aquí contra el usuario autenticado.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string WebhookRateLimitPolicy = "payment-webhooks";

    private readonly IPaymentService _paymentService;
    private readonly ICurrentUserService _currentUserService;

    public PaymentsController(
        IPaymentService paymentService,
        ICurrentUserService currentUserService)
    {
        _paymentService = paymentService;
        _currentUserService = currentUserService;
    }

    // ---- Consultas ----

    [HttpGet]
    [Authorize(Policy = Policies.PaymentsRead)]
    public Task<IActionResult> GetAll(
        [FromQuery] Guid? studentId = null,
        [FromQuery] Guid? membershipId = null,
        [FromQuery] Guid? classSessionId = null,
        [FromQuery] PaymentStatus? status = null,
        [FromQuery] PaymentMethod? method = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] PaymentProvider? provider = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await _paymentService.GetPagedAsync(
            new PaymentListFilter(studentId, membershipId, classSessionId, status, method, fromUtc, toUtc, page, pageSize, provider),
            cancellationToken)));

    [HttpGet("pending")]
    [Authorize(Policy = Policies.PaymentsRead)]
    public Task<IActionResult> GetPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await _paymentService.GetPagedAsync(
            new PaymentListFilter(Status: PaymentStatus.Pending, Page: page, PageSize: pageSize),
            cancellationToken)));

    [HttpGet("{id:guid}")]
    [Authorize]
    public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var payment = await _paymentService.GetByIdAsync(id, cancellationToken);
            if (payment == null)
            {
                return PaymentNotFound();
            }

            return CanAccess(payment.PayerPersonId) ? Ok(payment) : Forbidden();
        });

    [HttpGet("student/{studentId:guid}")]
    [HttpGet("person/{studentId:guid}")]
    [Authorize]
    public Task<IActionResult> GetByPersonId(Guid studentId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => CanAccess(studentId)
            ? Ok(await _paymentService.GetByPersonIdAsync(studentId, cancellationToken))
            : Forbidden());

    [HttpGet("{id:guid}/refunds")]
    [Authorize]
    public Task<IActionResult> GetRefunds(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var payment = await _paymentService.GetByIdAsync(id, cancellationToken);
            if (payment == null)
            {
                return PaymentNotFound();
            }

            return CanAccess(payment.PayerPersonId)
                ? Ok(await _paymentService.GetRefundsAsync(id, cancellationToken))
                : Forbidden();
        });

    [HttpGet("obligations/membership/{membershipId:guid}")]
    [Authorize]
    public Task<IActionResult> GetMembershipBalance(Guid membershipId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var balance = await _paymentService.GetMembershipBalanceAsync(membershipId, cancellationToken);
            return CanAccess(balance.StudentId) ? Ok(balance) : Forbidden();
        });

    // ---- Operaciones ----

    [HttpPost]
    [Authorize(Policy = Policies.PaymentsCreate)]
    public Task<IActionResult> RegisterPayment([FromBody] RegisterPaymentRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var result = await _paymentService.RegisterPaymentAsync(request, CurrentUserId, IdempotencyKey, cancellationToken);
            return result.Created
                ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
                : Ok(result.Value);
        });

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = Policies.PaymentsConfirm)]
    public Task<IActionResult> ConfirmPayment(
        Guid id,
        [FromBody] ConfirmPaymentRequest? request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _paymentService.ConfirmPaymentAsync(id, request, CurrentUserId, cancellationToken)));

    [HttpPost("{id:guid}/fail")]
    [Authorize(Policy = Policies.PaymentsConfirm)]
    public Task<IActionResult> FailPayment(
        Guid id,
        [FromBody] FailPaymentRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _paymentService.FailPaymentAsync(id, request, CurrentUserId, cancellationToken)));

    /// <summary>
    /// El personal cancela cualquier pago pendiente (<c>PAYMENTS_CANCEL</c>); el alumno, sólo su propio checkout en
    /// línea pendiente. Por eso la autorización se resuelve aquí y no con una política.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    public Task<IActionResult> CancelPayment(
        Guid id,
        [FromBody] CancelPaymentRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            if (!IsStaff)
            {
                var payment = await _paymentService.GetByIdAsync(id, cancellationToken);
                var isOwnOpenCheckout = payment != null &&
                                        CanAccess(payment.PayerPersonId) &&
                                        payment.Provider != PaymentProvider.Manual &&
                                        payment.Status == PaymentStatus.Pending;
                if (!isOwnOpenCheckout)
                {
                    return Forbidden();
                }
            }

            return Ok(await _paymentService.CancelPaymentAsync(id, request, CurrentUserId, cancellationToken));
        });

    // ---- Pasarela externa ----

    /// <summary>
    /// Inicia el pago en línea de una membresía. El importe lo calcula el servidor (saldo pendiente); lo puede
    /// iniciar el personal o el alumno titular.
    /// </summary>
    [HttpPost("checkout")]
    [Authorize]
    public Task<IActionResult> CreateCheckout([FromBody] CreatePaymentCheckoutRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var balance = await _paymentService.GetMembershipBalanceAsync(request.MembershipId, cancellationToken);
            if (!CanAccess(balance.StudentId))
            {
                return Forbidden();
            }

            var result = await _paymentService.CreateCheckoutAsync(request, CurrentUserId, IdempotencyKey, cancellationToken);
            return result.Created
                ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
                : Ok(result.Value);
        });

    /// <summary>
    /// Notificaciones del proveedor. No lleva usuario: la autenticidad la da la firma, que se verifica antes de
    /// cualquier otro procesamiento. Responde 200 a todo evento verificado para que el proveedor no lo reintente.
    /// </summary>
    [HttpPost("webhooks/{provider}")]
    [AllowAnonymous]
    [EnableRateLimiting(WebhookRateLimitPolicy)]
    [RequestSizeLimit(64 * 1024)]
    public Task<IActionResult> ReceiveWebhook(string provider, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            // Sólo nombres de proveedores externos: ni números ni Manual
            if (!Enum.TryParse<PaymentProvider>(provider, ignoreCase: true, out var paymentProvider) ||
                !Enum.IsDefined(paymentProvider) ||
                paymentProvider == PaymentProvider.Manual ||
                int.TryParse(provider, out _))
            {
                return NotFound(new PaymentApiError(PaymentErrorCodes.ProviderNotSupported, "Proveedor de pagos desconocido."));
            }

            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync(cancellationToken);

            var webhookRequest = new WebhookRequest(
                rawBody,
                Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase));

            var result = await _paymentService.ProcessWebhookAsync(paymentProvider, webhookRequest, cancellationToken);

            return result == WebhookProcessingResult.InvalidSignature
                ? Unauthorized(new PaymentApiError(PaymentErrorCodes.WebhookSignatureInvalid, "Firma de notificación inválida."))
                : Ok(new { status = result.ToString() });
        });

    [HttpPost("{id:guid}/reconcile")]
    [Authorize(Policy = Policies.PaymentsReconcile)]
    public Task<IActionResult> Reconcile(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _paymentService.ReconcileAsync(id, CurrentUserId, cancellationToken)));

    [HttpGet("discrepancies")]
    [Authorize(Policy = Policies.PaymentsReconcile)]
    public Task<IActionResult> GetDiscrepancies(
        [FromQuery] PaymentDiscrepancyStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await _paymentService.GetDiscrepanciesAsync(status, page, pageSize, cancellationToken)));

    [HttpPost("discrepancies/{id:guid}/resolve")]
    [Authorize(Policy = Policies.PaymentsReconcile)]
    public Task<IActionResult> ResolveDiscrepancy(
        Guid id,
        [FromBody] ResolvePaymentDiscrepancyRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _paymentService.ResolveDiscrepancyAsync(id, request, CurrentUserId, cancellationToken)));

    [HttpPost("{id:guid}/apply")]
    [Authorize(Policy = Policies.PaymentsAllocate)]
    public Task<IActionResult> ApplyPayment(
        Guid id,
        [FromBody] ApplyPaymentRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _paymentService.ApplyPaymentAsync(id, request, CurrentUserId, cancellationToken)));

    [HttpPost("{id:guid}/refunds")]
    [Authorize(Policy = Policies.PaymentsRefund)]
    public Task<IActionResult> CreateRefund(
        Guid id,
        [FromBody] CreatePaymentRefundRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var result = await _paymentService.CreateRefundAsync(id, request, CurrentUserId, IdempotencyKey, cancellationToken);
            return result.Created
                ? CreatedAtAction(nameof(GetRefunds), new { id }, result.Value)
                : Ok(result.Value);
        });

    // ---- Auxiliares ----

    private Guid CurrentUserId => _currentUserService.UserId ?? Guid.Empty;

    // HttpContext es nulo cuando el controlador se instancia fuera del pipeline (pruebas unitarias)
    private string? IdempotencyKey =>
        HttpContext?.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values) == true
            ? values.ToString()
            : null;

    /// <summary>El personal accede a cualquier pago; un alumno, sólo a lo propio. Un instructor no accede.</summary>
    private bool CanAccess(Guid ownerPersonId) =>
        IsStaff || (_currentUserService.IsInRole(Roles.Student) && _currentUserService.PersonId == ownerPersonId);

    private bool IsStaff =>
        _currentUserService.IsInRole(Roles.Administrator) || _currentUserService.IsInRole(Roles.Secretary);

    private IActionResult Forbidden() =>
        StatusCode(StatusCodes.Status403Forbidden, new PaymentApiError("FORBIDDEN", "No tenés acceso a este recurso."));

    private IActionResult PaymentNotFound() =>
        NotFound(new PaymentApiError(PaymentErrorCodes.NotFound, "Pago no encontrado."));

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new PaymentApiError("FORBIDDEN", ex.Message));
        }
        catch (NotFoundException ex)
        {
            return NotFound(new PaymentApiError(ex.Code ?? PaymentErrorCodes.NotFound, ex.Message));
        }
        catch (ConflictException ex)
        {
            return Conflict(new PaymentApiError(ex.Code ?? "CONFLICT", ex.Message, ex.Details));
        }
        catch (ValidationException ex)
        {
            return BadRequest(new PaymentApiError("VALIDATION_ERROR", ex.Message, ex.Errors));
        }
        catch (ExternalServiceException ex)
        {
            // El proveedor de pagos no respondió o rechazó la operación: lo guardado quedó en un estado recuperable
            return StatusCode(StatusCodes.Status502BadGateway, new PaymentApiError(ex.Code, ex.Message));
        }
    }
}
