using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Application.Modules.Activities.Validators;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ActivityServiceTests
{
    private readonly SmartGymDbContext _dbContext;
    private readonly Mock<ICurrentUserService> _mockCurrentUser = new();
    private readonly Mock<IPublicFileStorageService> _mockStorage = new();
    private readonly Mock<IImageProcessor> _mockProcessor = new();
    private readonly Mock<IAuditService> _mockAudit = new();
    private readonly ActivityService _service;

    public ActivityServiceTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new SmartGymDbContext(options);

        _mockCurrentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        _mockStorage.Setup(s => s.GetPublicUrl(It.IsAny<string>()))
            .Returns<string>(key => $"https://cdn.smartgym.local/{key}");

        _service = new ActivityService(
            _dbContext,
            _mockCurrentUser.Object,
            _mockStorage.Object,
            _mockProcessor.Object,
            _mockAudit.Object,
            NullLogger<ActivityService>.Instance,
            new CreateActivityRequestValidator(),
            new UpdateActivityRequestValidator());
    }

    [Fact]
    public async Task CreateAsync_WithValidData_ShouldPersistAndAudit()
    {
        var request = new CreateActivityRequest("ZUMBA", "Zumba Fitness", "Baile", "Descripción", "Toalla", "#FF0000", 25, 12, 70);

        var result = await _service.CreateAsync(request);

        Assert.Equal("ZUMBA", result.Code);
        Assert.Equal("Zumba Fitness", result.Name);
        Assert.Equal(ActivityStatus.Active, result.Status);

        var saved = await _dbContext.Activities.SingleOrDefaultAsync(a => a.Code == "ZUMBA");
        Assert.NotNull(saved);

        _mockAudit.Setup(a => a.LogAsync(
            "ACTIVITY_CREATED",
            "Activity",
            saved.Id.ToString(),
            It.IsAny<string>(),
            null,
            It.IsAny<Guid?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ShouldThrowConflictException()
    {
        var existing = new Activity("ZUMBA", "Zumba Original");
        _dbContext.Activities.Add(existing);
        await _dbContext.SaveChangesAsync();

        var request = new CreateActivityRequest("zumba", "Zumba Clon", null, null, null, null, null, null, null);

        await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(request));
    }

    [Fact]
    public async Task GetPublicCatalogAsync_ShouldOnlyReturnActiveAndConstructUrls()
    {
        var activeAct = new Activity("ZUMBA", "Zumba");
        var inactiveAct = new Activity("POWER", "Power Up");
        inactiveAct.ChangeStatus(ActivityStatus.Inactive);

        var logoMedia = new ActivityMedia(activeAct.Id, ActivityMediaType.Logo, "logos/zumba.webp", "zumba.webp", "image/webp", 1024, 200, 200, 0);
        activeAct.Media.Add(logoMedia);

        _dbContext.Activities.AddRange(activeAct, inactiveAct);
        await _dbContext.SaveChangesAsync();

        var catalog = await _service.GetPublicCatalogAsync();

        Assert.Single(catalog);
        Assert.Equal("ZUMBA", catalog[0].Code);
        Assert.Equal("https://cdn.smartgym.local/logos/zumba.webp", catalog[0].LogoUrl);
    }

    [Fact]
    public async Task DeleteAsync_WithMembershipPlanDependency_ShouldThrowConflictException()
    {
        var activity = new Activity("ZUMBA", "Zumba");
        _dbContext.Activities.Add(activity);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan Pro",
            Description = "Desc",
            DurationDays = 30,
            Price = 100m,
            Credits = 10,
            IsUnlimited = false
        };
        var planActivity = new MembershipPlanActivity
        {
            MembershipPlanId = plan.Id,
            ActivityId = activity.Id
        };
        _dbContext.MembershipPlans.Add(plan);
        _dbContext.MembershipPlanActivities.Add(planActivity);
        await _dbContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.DeleteAsync(activity.Id));
        Assert.NotNull(ex.Details);
        var deps = Assert.IsType<ActivityDependenciesDto>(ex.Details);
        Assert.Equal(1, deps.MembershipPlans);
    }

    [Fact]
    public async Task ChangeStatusAsync_FromActiveToArchivedDirectly_ShouldThrowInvalidOperationException()
    {
        var activity = new Activity("ZUMBA", "Zumba");
        _dbContext.Activities.Add(activity);
        await _dbContext.SaveChangesAsync();

        var request = new ChangeActivityStatusRequest(ActivityStatus.Archived, activity.Version);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ChangeStatusAsync(activity.Id, request));
    }
}
