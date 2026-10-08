using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class PublicActivitiesControllerTests
{
    private readonly Mock<IActivityService> _mockService = new();
    private readonly PublicActivitiesController _controller;

    public PublicActivitiesControllerTests()
    {
        _controller = new PublicActivitiesController(_mockService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public void Controller_ShouldAllowAnonymous()
    {
        var anonAttr = typeof(PublicActivitiesController).GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.NotNull(anonAttr);
    }

    [Fact]
    public async Task GetCatalog_ShouldSetCacheControlHeader_AndReturnList()
    {
        var items = new List<PublicActivityDto>
        {
            new("ZUMBA", "Zumba", "Resumen", "Desc", "Notas", "#FFFFFF", 10, 60, "http://cdn/logo.webp", "http://cdn/primary.webp", new List<string>())
        };

        _mockService.Setup(s => s.GetPublicCatalogAsync(18, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var result = await _controller.GetCatalog(18, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(items, okResult.Value);
        Assert.Equal("public, max-age=300", _controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task GetByCode_WhenFound_ShouldReturnOk()
    {
        var item = new PublicActivityDto("ZUMBA", "Zumba", "Resumen", "Desc", "Notas", "#FFFFFF", 10, 60, null, null, new List<string>());

        _mockService.Setup(s => s.GetPublicByCodeAsync("ZUMBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var result = await _controller.GetByCode("ZUMBA", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(item, okResult.Value);
    }

    [Fact]
    public async Task GetByCode_WhenNotFound_ShouldReturnNotFound()
    {
        _mockService.Setup(s => s.GetPublicByCodeAsync("INEXISTENTE", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublicActivityDto?)null);

        var result = await _controller.GetByCode("INEXISTENTE", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
