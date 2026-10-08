using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.People.Dtos;
using SmartGym.Application.Modules.People.Services;
using SmartGym.Domain.Enums;
using SmartGym.WebApi.Common;

namespace SmartGym.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PeopleController : ControllerBase
{
    private const long MaxPhotoSizeBytes = 5 * 1024 * 1024; // 5 MB

    // Tope del request completo: deja margen para las cabeceras multipart, de modo que los archivos
    // de hasta 5 MB lleguen al controller y los que superan ese tamaño reciban un 400 explícito.
    private const long MaxPhotoRequestBytes = 10 * 1024 * 1024;
    private readonly IPeopleService _peopleService;

    public PeopleController(IPeopleService peopleService)
    {
        _peopleService = peopleService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.RequireStaff)]
    [ProducesResponseType(typeof(PagedResult<PersonSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Search(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? documentType,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        PersonStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!ApiEnumCodes.TryParse<PersonStatus>(status, out var parsedStatus))
            {
                return BadRequest(new { message = $"Estado desconocido: '{status}'. Valores admitidos: ACTIVA, INACTIVA, BLOQUEADA, FALLECIDA." });
            }

            statusFilter = parsedStatus;
        }

        DocumentType? documentTypeFilter = null;
        if (!string.IsNullOrWhiteSpace(documentType))
        {
            if (!ApiEnumCodes.TryParse<DocumentType>(documentType, out var parsedDocumentType))
            {
                return BadRequest(new { message = $"Tipo de documento desconocido: '{documentType}'. Valores admitidos: DNI, PASAPORTE, CI, OTRO." });
            }

            documentTypeFilter = parsedDocumentType;
        }

        try
        {
            var result = await _peopleService.SearchAsync(search, statusFilter, documentTypeFilter, pageNumber, pageSize, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.RequireStaff)]
    [ProducesResponseType(typeof(PersonDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _peopleService.GetByIdAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireStaff)]
    [ProducesResponseType(typeof(PersonDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePersonRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _peopleService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.RequireStaff)]
    [ProducesResponseType(typeof(PersonDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePersonRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _peopleService.UpdateAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Policies.RequireStaff)]
    [ProducesResponseType(typeof(PersonDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangePersonStatusRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _peopleService.ChangeStatusAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpPut("{id:guid}/photo")]
    [Authorize(Policy = Policies.RequireStaff)]
    [RequestSizeLimit(MaxPhotoRequestBytes)]
    [ProducesResponseType(typeof(PersonDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadPhoto(Guid id, IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No se ha proporcionado ningún archivo de imagen." });
        }

        if (file.Length > MaxPhotoSizeBytes)
        {
            return BadRequest(new { message = "El archivo excede el tamaño máximo permitido de 5 MB." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _peopleService.UploadPhotoAsync(id, stream, file.ContentType, file.Length, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpDelete("{id:guid}/photo")]
    [Authorize(Policy = Policies.RequireStaff)]
    [ProducesResponseType(typeof(PersonDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeletePhoto(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _peopleService.DeletePhotoAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(PersonDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOwn(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _peopleService.GetOwnAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpPut("me/contact")]
    [Authorize]
    [ProducesResponseType(typeof(PersonDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateOwnContact([FromBody] UpdateOwnContactRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _peopleService.UpdateOwnContactAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpPut("me/photo")]
    [Authorize]
    [RequestSizeLimit(MaxPhotoRequestBytes)]
    [ProducesResponseType(typeof(PersonDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadOwnPhoto(IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No se ha proporcionado ningún archivo de imagen." });
        }

        if (file.Length > MaxPhotoSizeBytes)
        {
            return BadRequest(new { message = "El archivo excede el tamaño máximo permitido de 5 MB." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _peopleService.UploadOwnPhotoAsync(stream, file.ContentType, file.Length, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    [HttpDelete("me/photo")]
    [Authorize]
    [ProducesResponseType(typeof(PersonDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteOwnPhoto(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _peopleService.DeleteOwnPhotoAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (TryMapException(ex, out var error))
        {
            return error;
        }
    }

    // Sólo se capturan las excepciones con una respuesta HTTP definida; el resto sigue su curso
    // sin perder el stack trace original.
    private bool TryMapException(Exception ex, out IActionResult result)
    {
        IActionResult? mapped = ex switch
        {
            ValidationException vex => BadRequest(new { message = vex.Message, errors = vex.Errors }),
            NotFoundException nfe => NotFound(new { message = nfe.Message }),
            ForbiddenException fe => StatusCode(StatusCodes.Status403Forbidden, new { message = fe.Message }),
            ConflictException ce => Conflict(new { message = ce.Message }),
            ArgumentException aex => BadRequest(new { message = aex.Message }),
            InvalidOperationException ioex => Conflict(new { message = ioex.Message }),
            _ => null
        };

        result = mapped!;
        return mapped != null;
    }
}
