using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Promotions;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Enums;

namespace SmartGym.WebApi.Controllers;

/// <summary>Contrato de error de <c>/api/promotions</c>: código estable, mensaje y detalle de validación.</summary>
public record PromotionApiError(string Code, string Message, object? Details = null);

/// <summary>
/// Las políticas habilitan cada endpoint por permiso; el ámbito (alumno titular o personal) de la evaluación
/// previa y de los canjes por persona lo valida <see cref="IPromotionService"/> con el usuario autenticado.
/// No existe una operación de canje: el canje sólo ocurre al contratar una membresía o confirmar una reserva.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PromotionsController : ControllerBase
{
    private readonly IPromotionService _promotionService;

    public PromotionsController(IPromotionService promotionService)
    {
        _promotionService = promotionService;
    }

    // ---- Consultas ----

    [HttpGet]
    [Authorize(Policy = Policies.PromotionsRead)]
    public Task<IActionResult> GetAll(
        [FromQuery] bool onlyActive = false,
        [FromQuery] PromotionStatus? status = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await _promotionService.GetAllAsync(onlyActive, status, cancellationToken)));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.PromotionsRead)]
    public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => OkOrNotFound(await _promotionService.GetByIdAsync(id, cancellationToken)));

    [HttpGet("code/{code}")]
    [Authorize(Policy = Policies.PromotionsRead)]
    public Task<IActionResult> GetByCode(string code, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => OkOrNotFound(await _promotionService.GetByCodeAsync(code, cancellationToken)));

    [HttpGet("{id:guid}/redemptions")]
    [Authorize(Policy = Policies.PromotionsRedemptionsRead)]
    public Task<IActionResult> GetRedemptions(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await _promotionService.GetRedemptionsByPromotionAsync(id, page, pageSize, cancellationToken)));

    [HttpGet("redemptions/person/{personId:guid}")]
    [Authorize]
    public Task<IActionResult> GetRedemptionsByPerson(Guid personId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _promotionService.GetRedemptionsByPersonAsync(personId, cancellationToken)));

    // ---- Administración ----

    [HttpPost]
    [Authorize(Policy = Policies.PromotionsManage)]
    public Task<IActionResult> Create([FromBody] CreatePromotionRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var created = await _promotionService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        });

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.PromotionsManage)]
    public Task<IActionResult> Update(Guid id, [FromBody] UpdatePromotionRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _promotionService.UpdateAsync(id, request, cancellationToken)));

    [HttpPost("{id:guid}/pause")]
    [Authorize(Policy = Policies.PromotionsManage)]
    public Task<IActionResult> Pause(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _promotionService.PauseAsync(id, cancellationToken)));

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Policies.PromotionsManage)]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _promotionService.ActivateAsync(id, cancellationToken)));

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = Policies.PromotionsManage)]
    public Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _promotionService.ArchiveAsync(id, cancellationToken)));

    // ---- Evaluación previa ----

    [HttpPost("evaluate")]
    [Authorize]
    public Task<IActionResult> Evaluate([FromBody] EvaluatePromotionRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await _promotionService.PreviewForMembershipAsync(request, cancellationToken)));

    private IActionResult OkOrNotFound(PromotionDto? promotion) =>
        promotion == null
            ? NotFound(new PromotionApiError(PromotionErrorCodes.NotFound, "La promoción especificada no existe."))
            : Ok(promotion);

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new PromotionApiError("FORBIDDEN", ex.Message));
        }
        catch (NotFoundException ex)
        {
            return NotFound(new PromotionApiError(PromotionErrorCodes.NotFound, ex.Message));
        }
        catch (ConflictException ex)
        {
            return Conflict(new PromotionApiError(ex.Code ?? "CONFLICT", ex.Message, ex.Details));
        }
        catch (ValidationException ex)
        {
            return BadRequest(new PromotionApiError("VALIDATION_ERROR", ex.Message, ex.Errors));
        }
    }
}
