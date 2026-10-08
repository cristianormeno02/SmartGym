using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Domain.Enums;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ActivitiesControllerTests
{
    private readonly Mock<IActivityService> _mockService = new();
    private readonly ActivitiesController _controller;

    public ActivitiesControllerTests()
    {
        _controller = new ActivitiesController(_mockService.Object);
    }

    [Fact]
    public void Controller_ShouldHaveAuthorizePolicy_RequireStaff()
    {
        var authAttr = typeof(ActivitiesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttr);
        Assert.Equal(Policies.RequireStaff, authAttr.Policy);
    }

    [Fact]
    public async Task GetPaged_ShouldReturnOkWithPagedResult()
    {
        var paged = new PagedResult<ActivityListItemDto>(
            new List<ActivityListItemDto>
            {
                new(Guid.NewGuid(), "ZUMBA", "Zumba", "Resumen", "#FFFFFF", 20, 10, 60, ActivityStatus.Active, null, null, DateTime.UtcNow)
            },
            1, 10, 1);

        _mockService.Setup(s => s.GetPagedAsync(null, null, null, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        var result = await _controller.GetPaged(null, null, null, 1, 10, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(paged, okResult.Value);
    }

    [Fact]
    public async Task GetById_WhenFound_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var detail = new ActivityDetailDto(
            id, "ZUMBA", "Zumba", null, null, null, null, null, null, null,
            ActivityStatus.Active, 1, new List<ActivityMediaDto>(), DateTime.UtcNow, null);

        _mockService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.GetById(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(detail, okResult.Value);
    }

    [Fact]
    public async Task GetById_WhenNotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActivityDetailDto?)null);

        var result = await _controller.GetById(id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetByCode_WhenFound_ShouldReturnOk()
    {
        var detail = new ActivityDetailDto(
            Guid.NewGuid(), "ZUMBA", "Zumba", null, null, null, null, null, null, null,
            ActivityStatus.Active, 1, new List<ActivityMediaDto>(), DateTime.UtcNow, null);

        _mockService.Setup(s => s.GetByCodeAsync("ZUMBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var result = await _controller.GetByCode("ZUMBA", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(detail, okResult.Value);
    }

    [Fact]
    public async Task Create_ShouldReturnCreatedWithLocation()
    {
        var request = new CreateActivityRequest("ZUMBA", "Zumba", null, null, null, null, null, null, null);
        var created = new ActivityDetailDto(
            Guid.NewGuid(), "ZUMBA", "Zumba", null, null, null, null, null, null, null,
            ActivityStatus.Active, 1, new List<ActivityMediaDto>(), DateTime.UtcNow, null);

        _mockService.Setup(s => s.CreateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var result = await _controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(ActivitiesController.GetById), createdResult.ActionName);
        Assert.Equal(created.Id, createdResult.RouteValues!["id"]);
        Assert.Same(created, createdResult.Value);
    }

    [Fact]
    public async Task Delete_WhenConflictOccurs_ShouldReturn409WithDetails()
    {
        var id = Guid.NewGuid();
        var details = new ActivityDependenciesDto(0, 0, 2, 5, 1);
        _mockService.Setup(s => s.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Bloqueado por dependencias", details));

        var result = await _controller.Delete(id, CancellationToken.None);

        var statusResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, statusResult.StatusCode);
    }
}
