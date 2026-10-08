using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Application.Modules.Activities.Validators;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

/// <summary>
/// Compensación en storage y traducción de violaciones de unicidad de PostgreSQL a 409.
/// InMemory no aplica índices únicos, así que la violación se simula con el mismo mensaje que produce Npgsql.
/// </summary>
public class ActivityMediaPersistenceTests
{
    private const string UploadedKey = "activities/x/gallery/new.webp";

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
    private readonly Mock<IPublicFileStorageService> _mockStorage = new();
    private readonly Mock<IAuditService> _mockAudit = new();
    private readonly ActivityService _service;
    private readonly Activity _activity;

    public ActivityMediaPersistenceTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new FailingSaveDbContext(options);

        _activity = new Activity("ZUMBA", "Zumba");
        _dbContext.Activities.Add(_activity);
        _dbContext.SaveChanges();

        _mockStorage.Setup(s => s.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadedKey);
        _mockStorage.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockStorage.Setup(s => s.GetPublicUrl(It.IsAny<string>())).Returns<string>(key => $"https://cdn/{key}");

        var processor = new Mock<IImageProcessor>();
        processor.Setup(p => p.ProcessImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<ImageProcessingProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ProcessedImageResult(new MemoryStream([1, 2, 3]), "image/webp", 10, 10, 3));

        _service = new ActivityService(
            _dbContext,
            Mock.Of<ICurrentUserService>(),
            _mockStorage.Object,
            processor.Object,
            _mockAudit.Object,
            NullLogger<ActivityService>.Instance,
            new CreateActivityRequestValidator(),
            new UpdateActivityRequestValidator());
    }

    private static DbUpdateException UniqueViolation(string constraintName) =>
        new("An error occurred while saving the entity changes. See the inner exception for details.",
            new Exception($"23505: duplicate key value violates unique constraint \"{constraintName}\""));

    private Task<ActivityMediaDto> UploadGalleryImage() =>
        _service.UploadMediaAsync(_activity.Id, new MemoryStream([1]), "foto.jpg", "image/jpeg", 1, ActivityMediaType.GalleryImage);

    private void SetupAuditFailure() =>
        _mockAudit.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit store unavailable"));

    // --- Punto 4: compensación ---

    [Fact]
    public async Task UploadMedia_WhenDatabaseSaveFails_ShouldDeleteUploadedObject()
    {
        _dbContext.NextSaveException = new DbUpdateException("db down");

        await Assert.ThrowsAsync<DbUpdateException>(UploadGalleryImage);

        _mockStorage.Verify(s => s.DeleteFileAsync(UploadedKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadMedia_WhenAuditFailsAfterCommit_ShouldKeepUploadedObject()
    {
        SetupAuditFailure();

        await Assert.ThrowsAsync<InvalidOperationException>(UploadGalleryImage);

        // La fila ya está confirmada: borrar el objeto dejaría una referencia rota
        Assert.Single(_dbContext.ActivityMedias, m => m.ObjectKey == UploadedKey);
        _mockStorage.Verify(s => s.DeleteFileAsync(UploadedKey, It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Punto 5: violaciones de unicidad → ConflictException ---

    [Fact]
    public async Task CreateActivity_WhenCodeUniqueIndexIsViolated_ShouldThrowConflict()
    {
        _dbContext.NextSaveException = UniqueViolation(ActivityConstraintNames.CodeUnique);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CreateAsync(new CreateActivityRequest("YOGA", "Yoga", null, null, null, null, null, null, null)));
    }

    [Fact]
    public async Task UploadLogo_WhenLogoUniqueIndexIsViolated_ShouldThrowConflictAndDeleteUploadedObject()
    {
        _dbContext.NextSaveException = UniqueViolation(ActivityConstraintNames.SingleLogo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UploadMediaAsync(_activity.Id, new MemoryStream([1]), "logo.png", "image/png", 1, ActivityMediaType.Logo));

        _mockStorage.Verify(s => s.DeleteFileAsync(UploadedKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetPrimaryMedia_WhenPrimaryUniqueIndexIsViolated_ShouldThrowConflict()
    {
        await UploadGalleryImage();
        _mockStorage.Setup(s => s.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("activities/x/gallery/second.webp");
        var second = await UploadGalleryImage();

        _dbContext.NextSaveException = UniqueViolation(ActivityConstraintNames.SinglePrimaryImage);

        await Assert.ThrowsAsync<ConflictException>(() => _service.SetPrimaryMediaAsync(_activity.Id, second.Id));
    }

    [Fact]
    public async Task SaveChanges_WithUnrelatedDatabaseError_ShouldNotBeMaskedAsConflict()
    {
        _dbContext.NextSaveException = new DbUpdateException("save failed", new Exception("53300: too many connections"));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            _service.CreateAsync(new CreateActivityRequest("YOGA", "Yoga", null, null, null, null, null, null, null)));
    }
}
