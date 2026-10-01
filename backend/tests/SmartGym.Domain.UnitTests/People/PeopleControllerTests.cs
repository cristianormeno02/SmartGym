using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.People.Dtos;
using SmartGym.Application.Modules.People.Services;
using SmartGym.Domain.Enums;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.People;

public class PeopleControllerTests
{
    private readonly Mock<IPeopleService> _mockService;
    private readonly PeopleController _controller;

    public PeopleControllerTests()
    {
        _mockService = new Mock<IPeopleService>();
        _controller = new PeopleController(_mockService.Object);
    }

    private static PersonDetailDto CreateDetailDto(Guid? id = null, string? photoUrl = null) =>
        new(
            id ?? Guid.NewGuid(),
            "Juan",
            "Pérez",
            new IdentificationDocumentDto(DocumentType.Dni, "12345678", "AR", "12345678"),
            new DateOnly(1990, 1, 1),
            Gender.Male,
            "juan@example.com",
            "11223344",
            null,
            null,
            null,
            photoUrl,
            PersonStatus.Active,
            null,
            null,
            null,
            DateTime.UtcNow,
            1,
            false
        );

    private static PersonSummaryDto CreateSummaryDto(Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            "Juan",
            "Pérez",
            new IdentificationDocumentDto(DocumentType.Dni, "12345678", "AR", "12345678"),
            "juan@example.com",
            "11223344",
            null,
            PersonStatus.Active,
            false
        );

    [Fact]
    public void StaffEndpoints_ShouldRequireStaffPolicy()
    {
        var staffEndpoints = new[]
        {
            nameof(PeopleController.Search),
            nameof(PeopleController.GetById),
            nameof(PeopleController.Create),
            nameof(PeopleController.Update),
            nameof(PeopleController.ChangeStatus),
            nameof(PeopleController.UploadPhoto),
            nameof(PeopleController.DeletePhoto)
        };

        foreach (var endpointName in staffEndpoints)
        {
            var method = typeof(PeopleController).GetMethod(endpointName);
            Assert.NotNull(method);
            var authAttr = method.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authAttr);
            Assert.Equal(Policies.RequireStaff, authAttr.Policy);
        }
    }

    [Fact]
    public void SelfServiceEndpoints_ShouldRequireAuthorize()
    {
        var selfEndpoints = new[]
        {
            nameof(PeopleController.GetOwn),
            nameof(PeopleController.UpdateOwnContact),
            nameof(PeopleController.UploadOwnPhoto),
            nameof(PeopleController.DeleteOwnPhoto)
        };

        foreach (var endpointName in selfEndpoints)
        {
            var method = typeof(PeopleController).GetMethod(endpointName);
            Assert.NotNull(method);
            var authAttr = method.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authAttr);
            Assert.Null(authAttr.Policy);
        }
    }

    [Fact]
    public async Task Search_ShouldReturnOk_WithPagedResult()
    {
        var pagedResult = new PagedResult<PersonSummaryDto>(
            new List<PersonSummaryDto> { CreateSummaryDto() },
            1, 20, 1
        );

        _mockService.Setup(s => s.SearchAsync("juan", PersonStatus.Active, DocumentType.Dni, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        var result = await _controller.Search("juan", PersonStatus.Active, DocumentType.Dni, 1, 20);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(pagedResult, okResult.Value);
    }

    [Fact]
    public async Task GetById_WhenFound_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var detail = CreateDetailDto(id);

        _mockService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.GetById(id);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(detail, okResult.Value);
    }

    [Fact]
    public async Task GetById_WhenNotFound_ShouldReturn404()
    {
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Persona no encontrada."));

        var result = await _controller.GetById(id);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFound.Value);
    }

    [Fact]
    public async Task Create_WhenValid_ShouldReturnCreatedAtAction()
    {
        var request = new CreatePersonRequest("Juan", "Pérez", DocumentType.Dni, "12345678", "AR", Email: "juan@example.com");
        var detail = CreateDetailDto();

        _mockService.Setup(s => s.CreateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.Create(request);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(PeopleController.GetById), created.ActionName);
        Assert.Equal(detail, created.Value);
    }

    [Fact]
    public async Task Create_WhenDuplicateDocument_ShouldReturn409()
    {
        var request = new CreatePersonRequest("Juan", "Pérez", DocumentType.Dni, "12345678", "AR", Email: "juan@example.com");
        _mockService.Setup(s => s.CreateAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Ya existe una persona registrada con ese documento."));

        var result = await _controller.Create(request);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(conflict.Value);
    }

    [Fact]
    public async Task Create_WhenValidationFails_ShouldReturn400()
    {
        var request = new CreatePersonRequest("", "");
        _mockService.Setup(s => s.CreateAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException(new Dictionary<string, string[]> { { "FirstName", new[] { "Requerido" } } }));

        var result = await _controller.Create(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task Update_WhenVersionConflict_ShouldReturn409()
    {
        var id = Guid.NewGuid();
        var request = new UpdatePersonRequest("Juan", "Pérez", 1, DocumentType.Dni, "12345678", "AR");

        _mockService.Setup(s => s.UpdateAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("La ficha de la persona fue modificada concurrentemente."));

        var result = await _controller.Update(id, request);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(conflict.Value);
    }

    [Fact]
    public async Task ChangeStatus_WhenNonAdminTouchesDeceased_ShouldReturn403()
    {
        var id = Guid.NewGuid();
        var request = new ChangePersonStatusRequest(PersonStatus.Deceased, "Fallecimiento informado");

        _mockService.Setup(s => s.ChangeStatusAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Solo un administrador puede gestionar el estado de fallecimiento."));

        var result = await _controller.ChangeStatus(id, request);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task ChangeStatus_WhenInvalidTransition_ShouldReturn409()
    {
        var id = Guid.NewGuid();
        var request = new ChangePersonStatusRequest(PersonStatus.Inactive, "Intento inválido");

        _mockService.Setup(s => s.ChangeStatusAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Deceased person can only be reverted to Active."));

        var result = await _controller.ChangeStatus(id, request);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(conflict.Value);
    }

    [Fact]
    public async Task UploadPhoto_WhenNoFileProvided_ShouldReturn400()
    {
        var id = Guid.NewGuid();
        var result = await _controller.UploadPhoto(id, null);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task UploadPhoto_WhenFileExceeds5Mb_ShouldReturn400()
    {
        var id = Guid.NewGuid();
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(6 * 1024 * 1024);

        var result = await _controller.UploadPhoto(id, fileMock.Object);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task UploadPhoto_WhenValid_ShouldCallServiceAndReturnOk()
    {
        var id = Guid.NewGuid();
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(100);
        fileMock.Setup(f => f.ContentType).Returns("image/jpeg");
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);

        var detail = CreateDetailDto(id, "https://storage.example/avatar.webp");

        _mockService.Setup(s => s.UploadPhotoAsync(id, It.IsAny<Stream>(), "image/jpeg", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.UploadPhoto(id, fileMock.Object);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(detail, okResult.Value);
    }

    [Fact]
    public async Task DeletePhoto_ShouldCallServiceAndReturnOk()
    {
        var id = Guid.NewGuid();
        var detail = CreateDetailDto(id);

        _mockService.Setup(s => s.DeletePhotoAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.DeletePhoto(id);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(detail, okResult.Value);
    }

    [Fact]
    public async Task SelfService_GetOwn_ShouldReturnOk()
    {
        var detail = CreateDetailDto();

        _mockService.Setup(s => s.GetOwnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.GetOwn();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(detail, okResult.Value);
    }

    [Fact]
    public async Task SelfService_UpdateOwnContact_ShouldReturnOk()
    {
        var request = new UpdateOwnContactRequest("11223344", null, null, null);
        var detail = CreateDetailDto();

        _mockService.Setup(s => s.UpdateOwnContactAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.UpdateOwnContact(request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(detail, okResult.Value);
    }
}
