using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PromotionsController : ControllerBase
{
    private readonly IPromotionService _promotionService;

    public PromotionsController(IPromotionService promotionService)
    {
        _promotionService = promotionService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> GetAll([FromQuery] bool onlyActive = false, CancellationToken cancellationToken = default)
    {
        var list = await _promotionService.GetAllAsync(onlyActive, cancellationToken);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var promo = await _promotionService.GetByIdAsync(id, cancellationToken);
        if (promo == null)
        {
            return NotFound(new { message = "Promoción no encontrada." });
        }

        return Ok(promo);
    }

    [HttpGet("code/{code}")]
    [Authorize]
    public async Task<IActionResult> GetByCode(string code, CancellationToken cancellationToken)
    {
        var promo = await _promotionService.GetByCodeAsync(code, cancellationToken);
        if (promo == null)
        {
            return NotFound(new { message = "Código de promoción no encontrado." });
        }

        return Ok(promo);
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireStaff)]
    public async Task<IActionResult> Create([FromBody] CreatePromotionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _promotionService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("evaluate")]
    [Authorize]
    public async Task<IActionResult> Evaluate([FromBody] ApplyPromotionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _promotionService.EvaluatePromotionAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("redeem")]
    [Authorize]
    public async Task<IActionResult> Redeem([FromBody] ApplyPromotionRequest request, [FromQuery] Guid? membershipId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _promotionService.RedeemPromotionAsync(request, membershipId, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("redemptions/person/{personId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetRedemptionsByPerson(Guid personId, CancellationToken cancellationToken)
    {
        var list = await _promotionService.GetRedemptionsByPersonAsync(personId, cancellationToken);
        return Ok(list);
    }
}
