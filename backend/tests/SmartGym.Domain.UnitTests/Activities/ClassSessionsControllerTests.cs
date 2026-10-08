using Microsoft.AspNetCore.Mvc;
using Moq;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ClassSessionsControllerTests
{
    private readonly Mock<IClassSessionService> _mockService = new();
    private readonly ClassSessionsController _controller;

    public ClassSessionsControllerTests()
    {
        _controller = new ClassSessionsController(_mockService.Object);
    }

    private static GenerateSessionsRequest Request() =>
        new(new DateOnly(2030, 1, 7), new DateOnly(2030, 1, 7), Guid.NewGuid());

    [Fact]
    public async Task Generate_WhenScheduleBelongsToInactiveActivity_ShouldReturn409()
    {
        _mockService.Setup(s => s.GenerateSessionsFromSchedulesAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("actividad no activa"));

        var result = await _controller.Generate(Request(), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Generate_WhenCapacityIsNotResolvable_ShouldReturn400()
    {
        _mockService.Setup(s => s.GenerateSessionsFromSchedulesAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("sin cupo"));

        var result = await _controller.Generate(Request(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
