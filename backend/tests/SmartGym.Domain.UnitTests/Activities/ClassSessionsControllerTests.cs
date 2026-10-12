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

public class ClassSessionsControllerTests
{
    private readonly Mock<IClassSessionService> _mockService = new();
    private readonly ClassSessionsController _controller;

    public ClassSessionsControllerTests()
    {
        _controller = new ClassSessionsController(_mockService.Object);
    }

    [Fact]
    public void EndpointPolicies_ShouldMatchDesignSpecification()
    {
        var getAllMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.GetAll));
        Assert.Equal(Policies.RequireTeachingAndStaff, getAllMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var getByIdMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.GetById));
        Assert.Equal(Policies.RequireTeachingAndStaff, getByIdMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var getAvailableMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.GetAvailable));
        Assert.Equal(Policies.RequireAllAuthenticated, getAvailableMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var createMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.Create));
        Assert.Equal(Policies.RequireStaff, createMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var updateMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.Update));
        Assert.Equal(Policies.RequireStaff, updateMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var openMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.Open));
        Assert.Equal(Policies.RequireStaff, openMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var suspendMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.Suspend));
        Assert.Equal(Policies.RequireTeachingAndStaff, suspendMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var cancelMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.Cancel));
        Assert.Equal(Policies.RequireStaff, cancelMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var rescheduleMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.Reschedule));
        Assert.Equal(Policies.RequireStaff, rescheduleMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var replaceInstructorMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.ReplaceInstructor));
        Assert.Equal(Policies.RequireStaff, replaceInstructorMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var replaceSecretaryMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.ReplaceSecretary));
        Assert.Equal(Policies.RequireStaff, replaceSecretaryMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var changeRoomMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.ChangeRoom));
        Assert.Equal(Policies.RequireStaff, changeRoomMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);

        var generateMethod = typeof(ClassSessionsController).GetMethod(nameof(ClassSessionsController.Generate));
        Assert.Equal(Policies.RequireStaff, generateMethod?.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    [Fact]
    public void ObsoleteRoutes_ShouldNotExist()
    {
        var type = typeof(ClassSessionsController);
        Assert.Null(type.GetMethod("Start"));
        Assert.Null(type.GetMethod("Finish"));
        Assert.Null(type.GetMethod("Manual"));
        Assert.Null(type.GetMethod("Substitute"));
    }

    [Fact]
    public async Task Create_ShouldReturn201CreatedWithLocation()
    {
        var request = new CreateClassSessionRequest(
            ActivityId: Guid.NewGuid(),
            RoomId: Guid.NewGuid(),
            InstructorId: Guid.NewGuid(),
            SecretaryId: null,
            StartsAt: new DateTimeOffset(2030, 1, 10, 10, 0, 0, TimeSpan.Zero),
            EndsAt: new DateTimeOffset(2030, 1, 10, 11, 0, 0, TimeSpan.Zero),
            Capacity: 20,
            Notes: "Clase especial");

        var createdDto = new ClassSessionDetailDto(
            Id: Guid.NewGuid(),
            RecurringScheduleId: null,
            ActivityId: request.ActivityId,
            ActivityName: "Yoga",
            RoomId: request.RoomId,
            RoomName: "Sala A",
            ScheduledInstructorId: request.InstructorId,
            ScheduledInstructorName: "Instructor 1",
            EffectiveInstructorId: request.InstructorId,
            EffectiveInstructorName: "Instructor 1",
            ScheduledSecretaryId: null,
            ScheduledSecretaryName: null,
            EffectiveSecretaryId: null,
            EffectiveSecretaryName: null,
            StartsAtUtc: request.StartsAt.UtcDateTime,
            EndsAtUtc: request.EndsAt.UtcDateTime,
            StartsAtLocal: request.StartsAt,
            EndsAtLocal: request.EndsAt,
            Capacity: 20,
            ReservedCount: 0,
            AttendedCount: 0,
            AvailableSpots: 20,
            Status: ClassSessionStatus.Scheduled,
            DisplayStatus: ClassSessionDisplayStatus.Scheduled,
            IsDetachedFromSchedule: false,
            Notes: request.Notes,
            SuspendedAtUtc: null,
            SuspendedByUserId: null,
            SuspensionReason: null,
            SuspensionObservations: null,
            CancelledAtUtc: null,
            CancelledByUserId: null,
            CancellationReason: null,
            CancellationObservations: null,
            RescheduledFromSessionId: null,
            RescheduledToSessionId: null,
            StaffChanges: new List<ClassSessionStaffChangeDto>(),
            Version: 1);

        _mockService.Setup(s => s.CreateExtraordinaryAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdDto);

        var result = await _controller.Create(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(ClassSessionsController.GetById), createdResult.ActionName);
        Assert.Equal(createdDto.Id, createdResult.RouteValues!["id"]);
        Assert.Same(createdDto, createdResult.Value);
    }

    [Fact]
    public async Task WhenNotFoundExceptionOccurs_ShouldReturn404()
    {
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Clase no encontrada"));

        var result = await _controller.GetById(id, CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundResult.StatusCode);
    }

    [Fact]
    public async Task WhenConflictExceptionOccurs_ShouldReturn409WithDetails()
    {
        var id = Guid.NewGuid();
        var request = new UpdateClassSessionRequest(15, "Notas", 1);
        var conflictingIds = new List<Guid> { Guid.NewGuid() };
        _mockService.Setup(s => s.UpdateAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Solapamiento de sala detectado", new { conflictingSessionIds = conflictingIds }));

        var result = await _controller.Update(id, request, CancellationToken.None);

        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode);
    }

    [Fact]
    public async Task WhenValidationExceptionOccurs_ShouldReturn400WithErrors()
    {
        var id = Guid.NewGuid();
        var request = new SuspendClassSessionRequest(ClassSessionSuspensionReason.Weather, "Lluvia");
        _mockService.Setup(s => s.SuspendAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException(new Dictionary<string, string[]>
            {
                { "Reason", new[] { "Razón requerida" } }
            }));

        var result = await _controller.Suspend(id, request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task Generate_WhenScheduleBelongsToInactiveActivity_ShouldReturn409()
    {
        _mockService.Setup(s => s.GenerateSessionsAsync(It.IsAny<GenerateSessionsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("actividad no activa"));

        var request = new GenerateSessionsRequest(new DateOnly(2030, 1, 7), new DateOnly(2030, 1, 7), Guid.NewGuid());
        var result = await _controller.Generate(request, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Generate_WhenCapacityIsNotResolvable_ShouldReturn400()
    {
        _mockService.Setup(s => s.GenerateSessionsAsync(It.IsAny<GenerateSessionsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("sin cupo"));

        var request = new GenerateSessionsRequest(new DateOnly(2030, 1, 7), new DateOnly(2030, 1, 7), Guid.NewGuid());
        var result = await _controller.Generate(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
