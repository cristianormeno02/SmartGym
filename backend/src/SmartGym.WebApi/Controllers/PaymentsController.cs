using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ICurrentUserService _currentUserService;

    public PaymentsController(
        IPaymentService paymentService,
        ICurrentUserService currentUserService)
    {
        _paymentService = paymentService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var list = await _paymentService.GetAllAsync(cancellationToken);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var payment = await _paymentService.GetByIdAsync(id, cancellationToken);
        if (payment == null)
        {
            return NotFound(new { message = "Pago no encontrado." });
        }

        if (_currentUserService.PersonId.HasValue &&
            payment.PayerPersonId != _currentUserService.PersonId.Value &&
            !_currentUserService.IsInRole(Roles.Administrator) &&
            !_currentUserService.IsInRole(Roles.Secretary))
        {
            return Forbid();
        }

        return Ok(payment);
    }

    [HttpGet("person/{personId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetByPersonId(Guid personId, CancellationToken cancellationToken)
    {
        if (_currentUserService.PersonId.HasValue &&
            personId != _currentUserService.PersonId.Value &&
            !_currentUserService.IsInRole(Roles.Administrator) &&
            !_currentUserService.IsInRole(Roles.Secretary))
        {
            return Forbid();
        }

        var list = await _paymentService.GetByPersonIdAsync(personId, cancellationToken);
        return Ok(list);
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> RegisterPayment([FromBody] RegisterPaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _currentUserService.UserId ?? Guid.Empty;
            var payment = await _paymentService.RegisterPaymentAsync(request, userId, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = payment.Id }, payment);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = Policies.RequireAdministrator)]
    public async Task<IActionResult> RefundPayment(Guid id, [FromBody] string reason, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId ?? Guid.Empty;
        var refunded = await _paymentService.RefundPaymentAsync(id, reason, userId, cancellationToken);
        if (refunded == null)
        {
            return NotFound(new { message = "Pago no encontrado." });
        }

        return Ok(refunded);
    }
}
