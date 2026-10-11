using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Infrastructure.Services.BackgroundJobs;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ClassSessionGenerationWorkerTests
{
    [Fact]
    public async Task RunGenerationAndAutoOpenAsync_ExecutesGenerationAndAutoOpen()
    {
        // Arrange
        var mockGenerator = new Mock<IClassSessionGenerator>();
        mockGenerator.Setup(g => g.GenerateAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerationResultDto(Created: 5, Skipped: 2, Conflicts: new List<GenerationConflictDto>()));

        mockGenerator.Setup(g => g.AutoOpenAsync(
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IClassSessionGenerator)))
            .Returns(mockGenerator.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(sf => sf.CreateScope()).Returns(scopeMock.Object);

        var options = Options.Create(new ClassSessionsOptions
        {
            WindowDays = 60,
            GenerationInterval = TimeSpan.FromHours(1),
            AutoOpenLeadTime = TimeSpan.FromDays(7)
        });

        var gymCalendarMock = new Mock<IGymCalendar>();
        gymCalendarMock.Setup(c => c.Today).Returns(new DateOnly(2026, 10, 15));

        var worker = new ClassSessionGenerationWorker(
            scopeFactoryMock.Object,
            options,
            gymCalendarMock.Object,
            NullLogger<ClassSessionGenerationWorker>.Instance);

        // Act
        await worker.RunGenerationAndAutoOpenAsync(CancellationToken.None);

        // Assert
        mockGenerator.Verify(g => g.GenerateAsync(
            new DateOnly(2026, 10, 15),
            new DateOnly(2026, 10, 15).AddDays(60),
            null,
            It.IsAny<CancellationToken>()), Times.Once);

        mockGenerator.Verify(g => g.AutoOpenAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunGenerationAndAutoOpenAsync_WhenGeneratorThrowsException_DoesNotRethrow()
    {
        // Arrange
        var mockGenerator = new Mock<IClassSessionGenerator>();
        mockGenerator.Setup(g => g.GenerateAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated error in generation"));

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IClassSessionGenerator)))
            .Returns(mockGenerator.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(sf => sf.CreateScope()).Returns(scopeMock.Object);

        var options = Options.Create(new ClassSessionsOptions
        {
            WindowDays = 60,
            GenerationInterval = TimeSpan.FromHours(1),
            AutoOpenLeadTime = TimeSpan.FromDays(7)
        });

        var gymCalendarMock = new Mock<IGymCalendar>();
        gymCalendarMock.Setup(c => c.Today).Returns(new DateOnly(2026, 10, 15));

        var worker = new ClassSessionGenerationWorker(
            scopeFactoryMock.Object,
            options,
            gymCalendarMock.Object,
            NullLogger<ClassSessionGenerationWorker>.Instance);

        // Act & Assert (Should not throw)
        var exception = await Record.ExceptionAsync(() => worker.RunGenerationAndAutoOpenAsync(CancellationToken.None));
        Assert.Null(exception);
    }
}
