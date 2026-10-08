using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.People.Dtos;
using SmartGym.Application.Modules.People.Services;
using SmartGym.Application.Modules.People.Validators;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.People;

/// <summary>
/// Errores de persistencia que antes terminaban en 500 deben llegar al controller como excepciones
/// de aplicación mapeables (409/403).
/// </summary>
public class PeopleServiceErrorMappingTests
{
    private sealed class FailingSaveDbContext : SmartGymDbContext
    {
        public Exception? NextSaveException { get; set; }

        public FailingSaveDbContext(DbContextOptions<SmartGymDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (NextSaveException is { } ex)
            {
                NextSaveException = null;
                throw ex;
            }

            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    private readonly FailingSaveDbContext _dbContext;
    private readonly Mock<ICurrentUserService> _mockCurrentUser = new();
    private readonly Mock<IFileStorageService> _mockStorage = new();
    private readonly PeopleService _service;
    private readonly Person _person;

    public PeopleServiceErrorMappingTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FailingSaveDbContext(options);

        _person = Person.Create("Ana", "Gómez");
        _dbContext.People.Add(_person);
        _dbContext.SaveChanges();

        _mockCurrentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        _mockCurrentUser.Setup(u => u.PersonId).Returns(_person.Id);
        _mockCurrentUser.Setup(u => u.IsInRole(Roles.Administrator)).Returns(true);

        _mockStorage.Setup(s => s.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("avatars/new.webp");
        _mockStorage.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var processor = new Mock<IProfileImageProcessor>();
        processor.Setup(p => p.ProcessProfileImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessedImageResult(new MemoryStream([1, 2, 3]), "image/webp", 10, 10, 3));

        _service = new PeopleService(
            _dbContext,
            _mockCurrentUser.Object,
            _mockStorage.Object,
            processor.Object,
            Mock.Of<ILogger<PeopleService>>(),
            new CreatePersonRequestValidator(),
            new UpdatePersonRequestValidator(),
            new ChangePersonStatusRequestValidator(),
            new UpdateOwnContactRequestValidator());
    }

    private void FailNextSaveWithConcurrencyConflict() =>
        _dbContext.NextSaveException = new DbUpdateConcurrencyException("row changed");

    [Fact]
    public async Task ChangeStatusAsync_WhenRowChangedConcurrently_ThrowsConflict()
    {
        FailNextSaveWithConcurrencyConflict();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ChangeStatusAsync(_person.Id, new ChangePersonStatusRequest(PersonStatus.Inactive)));
    }

    [Fact]
    public async Task UploadPhotoAsync_WhenRowChangedConcurrently_ThrowsConflictAndRemovesUploadedObject()
    {
        FailNextSaveWithConcurrencyConflict();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UploadPhotoAsync(_person.Id, new MemoryStream([1]), "image/jpeg", 1));

        _mockStorage.Verify(s => s.DeleteFileAsync("avatars/new.webp", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePhotoAsync_WhenRowChangedConcurrently_ThrowsConflict()
    {
        FailNextSaveWithConcurrencyConflict();

        await Assert.ThrowsAsync<ConflictException>(() => _service.DeletePhotoAsync(_person.Id));
    }

    [Fact]
    public async Task UpdateOwnContactAsync_WhenRowChangedConcurrently_ThrowsConflict()
    {
        FailNextSaveWithConcurrencyConflict();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateOwnContactAsync(new UpdateOwnContactRequest(PrimaryPhone: "1122334455")));
    }

    [Fact]
    public async Task GetOwnAsync_WhenUserHasNoLinkedPerson_ThrowsForbidden()
    {
        _mockCurrentUser.Setup(u => u.PersonId).Returns((Guid?)null);

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetOwnAsync());
    }
}
