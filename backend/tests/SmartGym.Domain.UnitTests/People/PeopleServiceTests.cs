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

public class PeopleServiceTests
{
    private readonly SmartGymDbContext _dbContext;
    private readonly Mock<ICurrentUserService> _mockCurrentUser;
    private readonly Mock<IFileStorageService> _mockStorage;
    private readonly Mock<IProfileImageProcessor> _mockProcessor;
    private readonly Mock<ILogger<PeopleService>> _mockLogger;
    private readonly PeopleService _service;

    private readonly Guid _currentUserId = Guid.NewGuid();
    private readonly Guid _currentPersonId = Guid.NewGuid();

    public PeopleServiceTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new SmartGymDbContext(options);

        _mockCurrentUser = new Mock<ICurrentUserService>();
        _mockCurrentUser.Setup(u => u.UserId).Returns(_currentUserId);
        _mockCurrentUser.Setup(u => u.PersonId).Returns(_currentPersonId);
        _mockCurrentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _mockCurrentUser.Setup(u => u.IsInRole(Roles.Administrator)).Returns(false);
        _mockCurrentUser.Setup(u => u.IsInRole(Roles.Secretary)).Returns(true);

        _mockStorage = new Mock<IFileStorageService>();
        _mockStorage.Setup(s => s.GetAccessUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, TimeSpan _, CancellationToken _) => $"https://cdn.example.com/{key}");
        _mockStorage.Setup(s => s.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string fileName, string _, string folder, CancellationToken _) => $"{folder}/new_{fileName}");
        _mockStorage.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockProcessor = new Mock<IProfileImageProcessor>();
        _mockProcessor.Setup(p => p.ProcessProfileImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessedImageResult(new MemoryStream([1, 2, 3]), "image/webp", 500, 500, 3));

        _mockLogger = new Mock<ILogger<PeopleService>>();

        _service = new PeopleService(
            _dbContext,
            _mockCurrentUser.Object,
            _mockStorage.Object,
            _mockProcessor.Object,
            _mockLogger.Object,
            new CreatePersonRequestValidator(),
            new UpdatePersonRequestValidator(),
            new ChangePersonStatusRequestValidator(),
            new UpdateOwnContactRequestValidator()
        );
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesPersonAndReturnsDetail()
    {
        var request = new CreatePersonRequest(
            "Carlos",
            "Tevez",
            DocumentType.Dni,
            "12.345.678",
            null,
            null,
            Gender.Male,
            "carlos@example.com",
            "1122334455"
        );

        var result = await _service.CreateAsync(request);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Carlos", result.FirstName);
        Assert.Equal("Tevez", result.LastName);
        Assert.NotNull(result.Document);
        Assert.Equal("12345678", result.Document!.NumberNormalized);
        Assert.Equal("AR", result.Document.IssuingCountry);
        Assert.Equal("carlos@example.com", result.Email);
        Assert.Equal(PersonStatus.Active, result.Status);
    }

    [Fact]
    public async Task CreateAsync_DuplicateDocument_ThrowsConflictException()
    {
        var p1 = Person.Create("Juan", "Perez", document: IdentificationDocument.Create(DocumentType.Dni, "12345678"));
        _dbContext.People.Add(p1);
        await _dbContext.SaveChangesAsync();

        var request = new CreatePersonRequest(
            "Mario",
            "Perez",
            DocumentType.Dni,
            "12.345.678" // same normalized 12345678
        );

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(request));
        Assert.Contains("documento", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ThrowsConflictException()
    {
        var p1 = Person.Create("Juan", "Perez", email: "juan@example.com");
        _dbContext.People.Add(p1);
        await _dbContext.SaveChangesAsync();

        var request = new CreatePersonRequest(
            "Mario",
            "Perez",
            Email: "JUAN@EXAMPLE.COM" // same case-insensitive email
        );

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(request));
        Assert.Contains("correo electrónico", ex.Message);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_PreservingOwnDocumentAndEmail_DoesNotThrowConflict()
    {
        var person = Person.Create("Carlos", "Gomez", email: "carlos@example.com", document: IdentificationDocument.Create(DocumentType.Dni, "22334455"));
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        var request = new UpdatePersonRequest(
            "Carlos Alberto",
            "Gomez",
            person.Version,
            DocumentType.Dni,
            "22.334.455", // same document
            null,
            null,
            null,
            "carlos@example.com" // same email
        );

        var result = await _service.UpdateAsync(person.Id, request);

        Assert.Equal("Carlos Alberto", result.FirstName);
        Assert.Equal("22334455", result.Document!.NumberNormalized);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateDocumentFromOtherPerson_ThrowsConflictException()
    {
        var p1 = Person.Create("Carlos", "Gomez", document: IdentificationDocument.Create(DocumentType.Dni, "11111111"));
        var p2 = Person.Create("Mario", "Lopez", document: IdentificationDocument.Create(DocumentType.Dni, "22222222"));
        _dbContext.People.AddRange(p1, p2);
        await _dbContext.SaveChangesAsync();

        var request = new UpdatePersonRequest(
            "Mario",
            "Lopez",
            p2.Version,
            DocumentType.Dni,
            "11.111.111" // collision with p1
        );

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(p2.Id, request));
        Assert.Contains("documento", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_EmailChangeWhenUserAccountLinked_ThrowsConflictException()
    {
        var person = Person.Create("Carlos", "Gomez", email: "original@example.com");
        _dbContext.People.Add(person);
        var user = new User { Id = Guid.NewGuid(), PersonId = person.Id, Username = "original@example.com" };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var request = new UpdatePersonRequest(
            "Carlos",
            "Gomez",
            person.Version,
            Email: "new@example.com"
        );

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(person.Id, request));
        Assert.Contains("vinculado a una cuenta de usuario", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_ConcurrencyMismatch_ThrowsConflictException()
    {
        var person = Person.Create("Carlos", "Gomez");
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        var request = new UpdatePersonRequest(
            "Carlos",
            "Gomez",
            Version: person.Version + 999
        );

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(person.Id, request));
        Assert.Contains("concurrentemente", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_WhenDeceased_ThrowsConflictException()
    {
        var person = Person.Create("Carlos", "Gomez");
        person.ChangeStatus(PersonStatus.Deceased, "Fallecimiento informado", Guid.NewGuid(), isAdmin: true);
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        var request = new UpdatePersonRequest("Carlos", "Gomez", person.Version);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(person.Id, request));
        Assert.Contains("FALLECIDA", ex.Message);
    }

    [Fact]
    public async Task ChangeStatusAsync_ByStaff_UpdatesStatusAndRecordsAudit()
    {
        var person = Person.Create("Carlos", "Gomez");
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        var request = new ChangePersonStatusRequest(PersonStatus.Blocked, "Falta grave de conducta");

        var result = await _service.ChangeStatusAsync(person.Id, request);

        Assert.Equal(PersonStatus.Blocked, result.Status);
        Assert.Equal("Falta grave de conducta", result.StatusReason);
        Assert.Equal(_currentUserId, result.StatusChangedByUserId);
        Assert.NotNull(result.StatusChangedAtUtc);
    }

    [Fact]
    public async Task ChangeStatusAsync_ToDeceased_ByNonAdmin_ThrowsForbiddenException()
    {
        var person = Person.Create("Carlos", "Gomez");
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        _mockCurrentUser.Setup(u => u.IsInRole(Roles.Administrator)).Returns(false);

        var request = new ChangePersonStatusRequest(PersonStatus.Deceased, "Certificado de defunción");

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _service.ChangeStatusAsync(person.Id, request));
        Assert.Contains("Solo un administrador", ex.Message);
    }

    [Fact]
    public async Task ChangeStatusAsync_ToDeceased_ByAdmin_Succeeds()
    {
        var person = Person.Create("Carlos", "Gomez");
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        _mockCurrentUser.Setup(u => u.IsInRole(Roles.Administrator)).Returns(true);

        var request = new ChangePersonStatusRequest(PersonStatus.Deceased, "Certificado presentado");

        var result = await _service.ChangeStatusAsync(person.Id, request);

        Assert.Equal(PersonStatus.Deceased, result.Status);
        Assert.Equal("Certificado presentado", result.StatusReason);
    }

    [Fact]
    public async Task ChangeStatusAsync_RevertDeceased_ByAdmin_Succeeds()
    {
        var person = Person.Create("Carlos", "Gomez");
        person.ChangeStatus(PersonStatus.Deceased, "Error", Guid.NewGuid(), isAdmin: true);
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        _mockCurrentUser.Setup(u => u.IsInRole(Roles.Administrator)).Returns(true);

        var request = new ChangePersonStatusRequest(PersonStatus.Active, "Corrección de error administrativo");

        var result = await _service.ChangeStatusAsync(person.Id, request);

        Assert.Equal(PersonStatus.Active, result.Status);
    }

    [Fact]
    public async Task ChangeStatusAsync_SameStatus_ThrowsConflictException()
    {
        var person = Person.Create("Carlos", "Gomez"); // already Active
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        var request = new ChangePersonStatusRequest(PersonStatus.Active, null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.ChangeStatusAsync(person.Id, request));
        Assert.Contains("current status", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadPhotoAsync_WhenDeceased_ThrowsConflictException()
    {
        var person = Person.Create("Carlos", "Gomez");
        person.ChangeStatus(PersonStatus.Deceased, "Defunción", Guid.NewGuid(), isAdmin: true);
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UploadPhotoAsync(person.Id, new MemoryStream([1, 2]), "image/jpeg", 2));

        Assert.Contains("FALLECIDA", ex.Message);
    }

    [Fact]
    public async Task UploadPhotoAsync_ValidImage_ReplacesPreviousAndDeletesOldKey()
    {
        var person = Person.Create("Carlos", "Gomez");
        person.SetProfileImage(ProfileImage.Create("avatars/old.webp", "image/webp", 100));
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        using var stream = new MemoryStream([1, 2, 3]);
        var result = await _service.UploadPhotoAsync(person.Id, stream, "image/jpeg", 3);

        Assert.NotNull(result.PhotoUrl);
        _mockStorage.Verify(s => s.DeleteFileAsync("avatars/old.webp", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePhotoAsync_RemovesProfileImageAndCallsStorageDelete()
    {
        var person = Person.Create("Carlos", "Gomez");
        person.SetProfileImage(ProfileImage.Create("avatars/pic.webp", "image/webp", 100));
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        var result = await _service.DeletePhotoAsync(person.Id);

        _mockStorage.Verify(s => s.DeleteFileAsync("avatars/pic.webp", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(result.PhotoUrl);
    }

    [Fact]
    public async Task GetOwnAsync_ReturnsCurrentPersonDetail()
    {
        var person = Person.Create("Carlos", "Gomez");
        typeof(Person).GetProperty(nameof(Person.Id))!.SetValue(person, _currentPersonId);
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOwnAsync();

        Assert.Equal(_currentPersonId, result.Id);
        Assert.Equal("Carlos", result.FirstName);
    }

    [Fact]
    public async Task UpdateOwnContactAsync_UpdatesOnlyContactInfo_ProtectedFieldsUntouched()
    {
        var person = Person.Create("Carlos", "Gomez", email: "carlos@example.com", document: IdentificationDocument.Create(DocumentType.Dni, "12345678"));
        typeof(Person).GetProperty(nameof(Person.Id))!.SetValue(person, _currentPersonId);
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateOwnContactRequest(
            PrimaryPhone: "99887766",
            Address: new AddressDto(Street: "Av. Corrientes", Number: "1234")
        );

        var result = await _service.UpdateOwnContactAsync(request);

        Assert.Equal("99887766", result.PrimaryPhone);
        Assert.Equal("Av. Corrientes", result.Address!.Street);
        // Protected fields untouched
        Assert.Equal("Carlos", result.FirstName);
        Assert.Equal("Gomez", result.LastName);
        Assert.Equal("carlos@example.com", result.Email);
        Assert.Equal("12345678", result.Document!.NumberNormalized);
    }

    [Fact]
    public async Task SearchAsync_MultiWord_ReturnsMatchingPerson()
    {
        var p1 = Person.Create("Juan", "Pérez");
        var p2 = Person.Create("Carlos", "Gómez");
        _dbContext.People.AddRange(p1, p2);
        await _dbContext.SaveChangesAsync();

        var result = await _service.SearchAsync("perez juan", null, null);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(p1.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task SearchAsync_FormattedDni_ReturnsMatchingPerson()
    {
        var p1 = Person.Create("Ana", "Lopez", document: IdentificationDocument.Create(DocumentType.Dni, "34567890"));
        var p2 = Person.Create("Pedro", "García");
        _dbContext.People.AddRange(p1, p2);
        await _dbContext.SaveChangesAsync();

        var result = await _service.SearchAsync("34.567.890", null, null);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(p1.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task SearchAsync_StatusAndTypeFilter_AppliesFilters()
    {
        var p1 = Person.Create("Mario", "Bros", document: IdentificationDocument.Create(DocumentType.Passport, "IT12345", "IT"));
        p1.ChangeStatus(PersonStatus.Blocked, "Falta", Guid.NewGuid(), isAdmin: false);
        var p2 = Person.Create("Luigi", "Bros", document: IdentificationDocument.Create(DocumentType.Dni, "12345678"));
        _dbContext.People.AddRange(p1, p2);
        await _dbContext.SaveChangesAsync();

        var result = await _service.SearchAsync(null, PersonStatus.Blocked, DocumentType.Passport);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(p1.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task SearchAsync_Pagination_ReturnsCorrectPageAndCounts()
    {
        for (int i = 1; i <= 25; i++)
        {
            _dbContext.People.Add(Person.Create($"User{i:D2}", "Test"));
        }
        await _dbContext.SaveChangesAsync();

        var page1 = await _service.SearchAsync("Test", null, null, pageNumber: 1, pageSize: 10);
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(10, page1.Items.Count);
        Assert.True(page1.HasNextPage);
        Assert.False(page1.HasPreviousPage);

        var page3 = await _service.SearchAsync("Test", null, null, pageNumber: 3, pageSize: 10);
        Assert.Equal(5, page3.Items.Count);
        Assert.False(page3.HasNextPage);
        Assert.True(page3.HasPreviousPage);
    }
}
