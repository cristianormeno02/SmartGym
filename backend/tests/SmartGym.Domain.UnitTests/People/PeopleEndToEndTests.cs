using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Identity.Dtos;
using SmartGym.Application.Modules.Identity.Services;
using SmartGym.Application.Modules.People.Dtos;
using SmartGym.Application.Modules.People.Services;
using SmartGym.Application.Modules.People.Validators;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.Infrastructure.Services.FileStorage;
using Xunit;

namespace SmartGym.Domain.UnitTests.People;

public class PeopleEndToEndTests : IDisposable
{
    private readonly string _tempStorageDir;
    private readonly SmartGymDbContext _dbContext;
    private readonly LocalStorageService _fileStorageService;
    private readonly ProfileImageProcessor _imageProcessor;
    private readonly Mock<ICurrentUserService> _mockCurrentUser;
    private readonly Mock<IPasswordHasher> _mockPasswordHasher;
    private readonly Mock<IJwtTokenGenerator> _mockJwtTokenGenerator;
    private readonly Mock<IGoogleTokenValidator> _mockGoogleTokenValidator;

    private readonly PeopleService _peopleService;
    private readonly AuthService _authService;
    private readonly ReservationService _reservationService;

    private readonly Guid _adminUserId = Guid.NewGuid();

    public PeopleEndToEndTests()
    {
        _tempStorageDir = Path.Combine(Path.GetTempPath(), "smartgym_e2e_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempStorageDir);

        _fileStorageService = new LocalStorageService();
        _imageProcessor = new ProfileImageProcessor();

        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new SmartGymDbContext(options);

        // Seed Roles
        var adminRole = new Role { Id = (int)RoleType.Administrator, Name = RoleType.Administrator.ToString() };
        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString() };
        _dbContext.Roles.AddRange(adminRole, studentRole);
        _dbContext.SaveChanges();

        _mockCurrentUser = new Mock<ICurrentUserService>();
        _mockCurrentUser.Setup(u => u.UserId).Returns(_adminUserId);
        _mockCurrentUser.Setup(u => u.IsInRole(Roles.Administrator)).Returns(true);
        _mockCurrentUser.Setup(u => u.IsInRole(Roles.Secretary)).Returns(false);

        _mockPasswordHasher = new Mock<IPasswordHasher>();
        _mockPasswordHasher.Setup(p => p.HashPassword(It.IsAny<string>())).Returns("hashed_pwd");
        _mockPasswordHasher.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        _mockJwtTokenGenerator = new Mock<IJwtTokenGenerator>();
        _mockJwtTokenGenerator.Setup(j => j.GenerateToken(It.IsAny<User>(), It.IsAny<Person>(), It.IsAny<IEnumerable<string>>())).Returns("jwt_token_123");

        _mockGoogleTokenValidator = new Mock<IGoogleTokenValidator>();

        _peopleService = new PeopleService(
            _dbContext,
            _mockCurrentUser.Object,
            _fileStorageService,
            _imageProcessor,
            NullLogger<PeopleService>.Instance,
            new CreatePersonRequestValidator(),
            new UpdatePersonRequestValidator(),
            new ChangePersonStatusRequestValidator(),
            new UpdateOwnContactRequestValidator()
        );

        _authService = new AuthService(
            _dbContext,
            _mockPasswordHasher.Object,
            _mockJwtTokenGenerator.Object,
            _mockGoogleTokenValidator.Object
        );

        _reservationService = new ReservationService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        if (Directory.Exists(_tempStorageDir))
        {
            try { Directory.Delete(_tempStorageDir, true); } catch { }
        }
    }

    [Fact]
    public async Task EndToEnd_CompletePeopleLifecycleWorkflow()
    {
        // 1. Alta en mostrador sin email ni cuenta de usuario
        var createRequest = new CreatePersonRequest(
            FirstName: "Esteban",
            LastName: "Quito",
            DocumentType: DocumentType.Dni,
            DocumentNumber: "30.123.456", // formateado con puntos
            DocumentIssuingCountry: "AR",
            PrimaryPhone: "11-4567-8901"
        );

        var createdPerson = await _peopleService.CreateAsync(createRequest);
        Assert.NotNull(createdPerson);
        Assert.Equal("Esteban", createdPerson.FirstName);
        Assert.Equal("Quito", createdPerson.LastName);
        Assert.Equal("30123456", createdPerson.Document?.NumberNormalized);
        Assert.Equal(PersonStatus.Active, createdPerson.Status);
        Assert.False(createdPerson.HasUserAccount);
        Assert.Null(createdPerson.Email);

        // 2. Rechazo de duplicado de documento (intentando registrar con DNI sin puntos)
        var duplicateRequest = new CreatePersonRequest(
            FirstName: "Esteban",
            LastName: "Otro",
            DocumentType: DocumentType.Dni,
            DocumentNumber: "30123456", // mismo número normalizado
            DocumentIssuingCountry: "AR"
        );

        await Assert.ThrowsAsync<ConflictException>(() =>
            _peopleService.CreateAsync(duplicateRequest));

        // 3. Actualización de email para posterior vinculación con Google
        var updateRequest = new UpdatePersonRequest(
            FirstName: "Esteban",
            LastName: "Quito",
            Version: createdPerson.Version,
            DocumentType: DocumentType.Dni,
            DocumentNumber: "30.123.456",
            DocumentIssuingCountry: "AR",
            Email: "esteban.quito@gmail.com",
            PrimaryPhone: "11-4567-8901"
        );
        var updatedWithEmail = await _peopleService.UpdateAsync(createdPerson.Id, updateRequest);
        Assert.Equal("esteban.quito@gmail.com", updatedWithEmail.Email);

        // 4. Vinculación posterior mediante inicio de sesión con Google
        _mockGoogleTokenValidator.Setup(v => v.ValidateAsync("valid_google_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleAuthPayload(
                SubjectId: "google-sub-789",
                Email: "esteban.quito@gmail.com",
                GivenName: "Esteban",
                FamilyName: "Quito",
                PictureUrl: "https://lh3.googleusercontent.com/avatar.jpg",
                EmailVerified: true
            ));

        var authResponse = await _authService.LoginWithGoogleAsync(new GoogleLoginRequest("valid_google_token"));
        Assert.Equal(createdPerson.Id, authResponse.PersonId);
        Assert.Equal("esteban.quito@gmail.com", authResponse.Email);

        // Verificar que la persona ahora tiene cuenta vinculada y foto de Google en ExternalAvatarUrl
        var personAfterGoogle = await _peopleService.GetByIdAsync(createdPerson.Id);
        Assert.True(personAfterGoogle.HasUserAccount);
        Assert.Equal("https://lh3.googleusercontent.com/avatar.jpg", personAfterGoogle.PhotoUrl);

        // 5. Carga y reemplazo de foto de perfil propia usando LocalStorageService
        using var testImageStream = CreateValidTestJpegStream();
        var photoDetail = await _peopleService.UploadPhotoAsync(
            createdPerson.Id,
            testImageStream,
            "image/jpeg",
            testImageStream.Length
        );

        Assert.NotNull(photoDetail.PhotoUrl);
        Assert.NotEqual("https://lh3.googleusercontent.com/avatar.jpg", photoDetail.PhotoUrl); // Foto propia prevalece sobre Google
        Assert.Contains(createdPerson.Id.ToString(), photoDetail.PhotoUrl);

        // Eliminar foto y verificar fallback a la foto de Google
        var afterDeletePhoto = await _peopleService.DeletePhotoAsync(createdPerson.Id);
        Assert.Equal("https://lh3.googleusercontent.com/avatar.jpg", afterDeletePhoto.PhotoUrl);

        // 6. Transición de estado a BLOQUEADA con auditoría
        // Asignar rol Student a la persona para probar reserva
        var personEntity = await _dbContext.People.Include(p => p.PersonRoles).FirstAsync(p => p.Id == createdPerson.Id);
        var studentRole = await _dbContext.Roles.FirstAsync(r => r.Id == (int)RoleType.Student);
        personEntity.AddRole(studentRole);
        await _dbContext.SaveChangesAsync();

        // Configurar clase y actividad
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala A", Capacity = 20 };
        var activity = new Activity("CROSSFIT", "CrossFit", defaultCapacity: 15);
        var instructor = Person.Create("Coach", "Fitness", "coach@gym.com");
        var classSession = new ClassSession
        {
            Id = Guid.NewGuid(),
            ActivityId = activity.Id,
            Activity = activity,
            RoomId = room.Id,
            Room = room,
            InstructorId = instructor.Id,
            Instructor = instructor,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(11, 0),
            MaxCapacity = 15
        };
        _dbContext.AddRange(room, activity, instructor, classSession);
        await _dbContext.SaveChangesAsync();

        // Bloquear a la persona
        var blockedPerson = await _peopleService.ChangeStatusAsync(
            createdPerson.Id,
            new ChangePersonStatusRequest(PersonStatus.Blocked, "Falta disciplinaria grave")
        );

        Assert.Equal(PersonStatus.Blocked, blockedPerson.Status);
        Assert.Equal("Falta disciplinaria grave", blockedPerson.StatusReason);
        Assert.Equal(_adminUserId, blockedPerson.StatusChangedByUserId);
        Assert.NotNull(blockedPerson.StatusChangedAtUtc);

        // Verificar que la persona BLOQUEADA NO PUEDE reservar
        var reservationException = await Assert.ThrowsAsync<ArgumentException>(() =>
            _reservationService.CreateReservationAsync(
                new CreateReservationRequest(classSession.Id, createdPerson.Id),
                Guid.NewGuid()
            )
        );
        Assert.Contains("no tiene el rol de Alumno activo", reservationException.Message);
    }

    private static MemoryStream CreateValidTestJpegStream()
    {
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(50, 50));
        var canvas = surface.Canvas;
        canvas.Clear(SkiaSharp.SKColors.Blue);

        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80);

        var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return ms;
    }
}
