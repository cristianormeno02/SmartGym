using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Activities;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class SchedulesControllerTests
{
    private readonly Mock<IRecurringScheduleService> _mockService = new();
    private readonly SchedulesController _controller;

    public SchedulesControllerTests()
    {
        _controller = new SchedulesController(_mockService.Object);
    }

    [Fact]
    public void Create_Update_And_CreateBatch_ShouldHaveAuthorizePolicy_RequireStaff()
    {
        var createMethod = typeof(SchedulesController).GetMethod(nameof(SchedulesController.Create));
        var authAttrCreate = createMethod?.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttrCreate);
        Assert.Equal(Policies.RequireStaff, authAttrCreate.Policy);

        var updateMethod = typeof(SchedulesController).GetMethod(nameof(SchedulesController.Update));
        var authAttrUpdate = updateMethod?.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttrUpdate);
        Assert.Equal(Policies.RequireStaff, authAttrUpdate.Policy);

        var batchMethod = typeof(SchedulesController).GetMethod(nameof(SchedulesController.CreateBatch));
        var authAttrBatch = batchMethod?.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttrBatch);
        Assert.Equal(Policies.RequireStaff, authAttrBatch.Policy);
    }

    [Fact]
    public async Task Create_WhenScheduleConflictException_Returns409WithConflictsAndMessage()
    {
        var conflicts = new List<ScheduleConflictDto>
        {
            new(DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0), ScheduleConflictKind.Room, Guid.NewGuid(), "Funcional", new TimeOnly(18, 0), new TimeOnly(19, 0))
        };
        var ex = new ScheduleConflictException("Ya existe una clase asignada en la misma sala durante ese día y rango horario.", conflicts);

        var request = new CreateRecurringScheduleRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            new DateOnly(2026, 3, 1), null, 20);

        _mockService.Setup(s => s.CreateAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

        var result = await _controller.Create(request, CancellationToken.None);

        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflictResult.StatusCode);

        // Verify message and conflicts exist on payload
        var payload = conflictResult.Value;
        Assert.NotNull(payload);
        var messageProp = payload.GetType().GetProperty("message")?.GetValue(payload)?.ToString();
        var conflictsProp = payload.GetType().GetProperty("conflicts")?.GetValue(payload) as IReadOnlyList<ScheduleConflictDto>;

        Assert.Equal(ex.Message, messageProp);
        Assert.NotNull(conflictsProp);
        Assert.Single(conflictsProp);
    }

    [Fact]
    public async Task Update_WhenScheduleConflictException_Returns409WithConflictsAndMessage()
    {
        var conflicts = new List<ScheduleConflictDto>
        {
            new(DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0), ScheduleConflictKind.Instructor, Guid.NewGuid(), "Spinning", new TimeOnly(18, 0), new TimeOnly(19, 0))
        };
        var ex = new ScheduleConflictException("El instructor ya tiene otra clase asignada durante ese día y rango horario.", conflicts);

        var id = Guid.NewGuid();
        var request = new UpdateRecurringScheduleRequest(
            Guid.NewGuid(), Guid.NewGuid(), null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            new DateOnly(2026, 3, 1), null, 20, IsActive: true);

        _mockService.Setup(s => s.UpdateAsync(id, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

        var result = await _controller.Update(id, request, CancellationToken.None);

        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflictResult.StatusCode);

        var payload = conflictResult.Value;
        Assert.NotNull(payload);
        var messageProp = payload.GetType().GetProperty("message")?.GetValue(payload)?.ToString();
        var conflictsProp = payload.GetType().GetProperty("conflicts")?.GetValue(payload) as IReadOnlyList<ScheduleConflictDto>;

        Assert.Equal(ex.Message, messageProp);
        Assert.NotNull(conflictsProp);
        Assert.Single(conflictsProp);
    }

    [Fact]
    public async Task CreateBatch_WhenSuccess_Returns201WithSchedules()
    {
        var request = new CreateBatchRecurringScheduleRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            new DateOnly(2026, 3, 1), null, 20,
            new List<BatchScheduleSlotRequest>
            {
                new(new[] { DayOfWeek.Monday }, new TimeOnly(18, 0), new TimeOnly(19, 0))
            });

        var createdList = new List<RecurringScheduleDto>
        {
            new(Guid.NewGuid(), request.ActivityId, "Funcional", request.RoomId, "Sala A", request.InstructorId, "Prof. Juan", null, null, DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0), request.ValidFrom, null, 20, true)
        };

        _mockService.Setup(s => s.CreateBatchAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdList);

        var result = await _controller.CreateBatch(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, objectResult.StatusCode);
        Assert.Equal(createdList, objectResult.Value);
    }

    [Fact]
    public async Task CreateBatch_WhenArgumentException_Returns400()
    {
        var request = new CreateBatchRecurringScheduleRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            new DateOnly(2026, 3, 1), null, 20, Array.Empty<BatchScheduleSlotRequest>());

        _mockService.Setup(s => s.CreateBatchAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Debe especificar al menos un grupo de horarios."));

        var result = await _controller.CreateBatch(request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task CreateBatch_WhenScheduleConflictException_Returns409WithConflictsAndMessage()
    {
        var conflicts = new List<ScheduleConflictDto>
        {
            new(DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0), ScheduleConflictKind.Room, Guid.NewGuid(), "Funcional", new TimeOnly(18, 0), new TimeOnly(19, 0))
        };
        var ex = new ScheduleConflictException("Se detectaron uno o más conflictos en el lote de horarios recurrentes.", conflicts);

        var request = new CreateBatchRecurringScheduleRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            new DateOnly(2026, 3, 1), null, 20,
            new List<BatchScheduleSlotRequest>
            {
                new(new[] { DayOfWeek.Monday }, new TimeOnly(18, 0), new TimeOnly(19, 0))
            });

        _mockService.Setup(s => s.CreateBatchAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

        var result = await _controller.CreateBatch(request, CancellationToken.None);

        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflictResult.StatusCode);

        var payload = conflictResult.Value;
        Assert.NotNull(payload);
        var messageProp = payload.GetType().GetProperty("message")?.GetValue(payload)?.ToString();
        var conflictsProp = payload.GetType().GetProperty("conflicts")?.GetValue(payload) as IReadOnlyList<ScheduleConflictDto>;

        Assert.Equal(ex.Message, messageProp);
        Assert.NotNull(conflictsProp);
        Assert.Single(conflictsProp);
    }
}
