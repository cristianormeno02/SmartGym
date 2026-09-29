using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Operations.Services;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MedicalCertificatesController : ControllerBase
{
    private readonly IMedicalCertificateService _medicalService;
    private readonly ICurrentUserService _currentUserService;

    public MedicalCertificatesController(
        IMedicalCertificateService medicalService,
        ICurrentUserService currentUserService)
    {
        _medicalService = medicalService;
        _currentUserService = currentUserService;
    }

    [HttpGet("status/{personId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetStatus(Guid personId, CancellationToken cancellationToken)
    {
        if (_currentUserService.PersonId.HasValue &&
            personId != _currentUserService.PersonId.Value &&
            !_currentUserService.IsInRole(Roles.Administrator) &&
            !_currentUserService.IsInRole(Roles.Secretary))
        {
            return Forbid();
        }

        var status = await _medicalService.GetStatusAsync(personId, cancellationToken);
        if (status == null)
        {
            return NotFound(new { message = "Alumno o persona no encontrada." });
        }

        return Ok(status);
    }

    [HttpPost("upload/{personId:guid}")]
    [Authorize]
    public async Task<IActionResult> Upload(
        Guid personId,
        IFormFile file,
        [FromForm] DateTime expirationDate,
        CancellationToken cancellationToken)
    {
        if (_currentUserService.PersonId.HasValue &&
            personId != _currentUserService.PersonId.Value &&
            !_currentUserService.IsInRole(Roles.Administrator) &&
            !_currentUserService.IsInRole(Roles.Secretary))
        {
            return Forbid();
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "Debe proporcionar un archivo de certificado válido." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var status = await _medicalService.UploadCertificateAsync(
                personId,
                stream,
                file.FileName,
                file.ContentType,
                expirationDate,
                cancellationToken
            );

            return Ok(status);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("can-attend/{personId:guid}")]
    [Authorize]
    public async Task<IActionResult> CanAttendClass(Guid personId, CancellationToken cancellationToken)
    {
        var canAttend = await _medicalService.CanPersonAttendClassAsync(personId, cancellationToken);
        return Ok(new { personId, canAttend });
    }
}
