using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Application.Modules.Activities.Validators;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

/// <summary>
/// Los índices únicos parciales (una imagen principal y un logo por actividad) no son diferibles:
/// PostgreSQL los verifica fila por fila. Como la clave del índice (ActivityId) no cambia, EF no puede
/// ordenar los comandos de un mismo SaveChanges, así que liberar el valor y tomarlo deben ir en
/// guardados separados (dentro de una transacción). InMemory no aplica índices únicos: el espía
/// detecta cuándo un mismo guardado los liberaría y tomaría a la vez.
/// </summary>
public class ActivityMediaUniqueOrderingTests
{
    private sealed class UniqueSlotSpyDbContext : SmartGymDbContext
    {
        public List<string> Violations { get; } = new();

        public UniqueSlotSpyDbContext(DbContextOptions<SmartGymDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ChangeTracker.DetectChanges();
            var entries = ChangeTracker.Entries<ActivityMedia>().ToList();

            bool IsPrimaryChangedTo(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ActivityMedia> e, bool value) =>
                e.State == EntityState.Modified &&
                e.Property(m => m.IsPrimary).IsModified &&
                e.Entity.IsPrimary == value;

            var takesPrimary = entries.Any(e =>
                IsPrimaryChangedTo(e, true) || (e.State == EntityState.Added && e.Entity.IsPrimary));
            var releasesPrimary = entries.Any(e =>
                IsPrimaryChangedTo(e, false) ||
                (e.State == EntityState.Deleted && e.Property(m => m.IsPrimary).OriginalValue));

            if (takesPrimary && releasesPrimary)
            {
                Violations.Add("Un mismo SaveChanges libera y toma la imagen principal.");
            }

            var addsLogo = entries.Any(e => e.State == EntityState.Added && e.Entity.Type == ActivityMediaType.Logo);
            var deletesLogo = entries.Any(e => e.State == EntityState.Deleted && e.Entity.Type == ActivityMediaType.Logo);
            if (addsLogo && deletesLogo)
            {
                Violations.Add("Un mismo SaveChanges borra un logo e inserta otro.");
            }

            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    private readonly UniqueSlotSpyDbContext _dbContext;
    private readonly Mock<IPublicFileStorageService> _mockStorage = new();
    private readonly ActivityService _service;
    private readonly Activity _activity;
    private int _uploads;

    public ActivityMediaUniqueOrderingTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new UniqueSlotSpyDbContext(options);

        _activity = new Activity("ZUMBA", "Zumba");
        _dbContext.Activities.Add(_activity);
        _dbContext.SaveChanges();

        _mockStorage.Setup(s => s.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"activities/x/media/{++_uploads}.webp");
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
            Mock.Of<IAuditService>(),
            NullLogger<ActivityService>.Instance,
            new CreateActivityRequestValidator(),
            new UpdateActivityRequestValidator());
    }

    private Task<Application.Modules.Activities.Dtos.ActivityMediaDto> Upload(ActivityMediaType type) =>
        _service.UploadMediaAsync(_activity.Id, new MemoryStream([1]), "img.png", "image/png", 1, type);

    private List<ActivityMedia> Gallery() =>
        _dbContext.ActivityMedias.AsNoTracking()
            .Where(m => m.ActivityId == _activity.Id && m.Type == ActivityMediaType.GalleryImage)
            .ToList();

    [Fact]
    public async Task SetPrimary_ShouldReleaseCurrentPrimaryBeforeTakingIt()
    {
        var first = await Upload(ActivityMediaType.GalleryImage);
        var second = await Upload(ActivityMediaType.GalleryImage);
        _dbContext.Violations.Clear();

        await _service.SetPrimaryMediaAsync(_activity.Id, second.Id);

        Assert.Empty(_dbContext.Violations);
        var primary = Assert.Single(Gallery(), m => m.IsPrimary);
        Assert.Equal(second.Id, primary.Id);
        Assert.NotEqual(first.Id, primary.Id);
    }

    [Fact]
    public async Task DeletePrimary_ShouldDeleteItBeforePromotingTheNextImage()
    {
        var first = await Upload(ActivityMediaType.GalleryImage);
        var second = await Upload(ActivityMediaType.GalleryImage);
        _dbContext.Violations.Clear();

        await _service.DeleteMediaAsync(_activity.Id, first.Id);

        Assert.Empty(_dbContext.Violations);
        var remaining = Assert.Single(Gallery());
        Assert.Equal(second.Id, remaining.Id);
        Assert.True(remaining.IsPrimary);
    }

    [Fact]
    public async Task ReplaceLogo_ShouldDeletePreviousLogoBeforeInsertingTheNewOne()
    {
        var previous = await Upload(ActivityMediaType.Logo);
        _dbContext.Violations.Clear();

        var current = await Upload(ActivityMediaType.Logo);

        Assert.Empty(_dbContext.Violations);
        var logo = Assert.Single(_dbContext.ActivityMedias.AsNoTracking(), m => m.Type == ActivityMediaType.Logo);
        Assert.Equal(current.Id, logo.Id);
        _mockStorage.Verify(s => s.DeleteFileAsync("activities/x/media/1.webp", It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotEqual(previous.Id, current.Id);
    }
}
