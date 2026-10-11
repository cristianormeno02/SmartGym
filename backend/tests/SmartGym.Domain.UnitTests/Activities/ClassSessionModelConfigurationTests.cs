using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ClassSessionModelConfigurationTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options;
        using var dbContext = new SmartGymDbContext(options);
        return dbContext.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void ClassSession_PropertiesAndConstraints_ShouldBeConfigured()
    {
        var model = BuildModel();
        var sessionType = model.FindEntityType(typeof(ClassSession))!;

        // Table
        Assert.Equal("ClassSessions", sessionType.GetTableName());

        // RowVersion
        var versionProp = sessionType.FindProperty(nameof(ClassSession.Version))!;
        Assert.True(versionProp.IsConcurrencyToken);

        // Check constraints
        var checks = sessionType.GetCheckConstraints().ToList();
        Assert.Contains(checks, c => c.ModelName == "CK_ClassSessions_Dates");
        Assert.Contains(checks, c => c.ModelName == "CK_ClassSessions_Capacity");
        Assert.Contains(checks, c => c.ModelName == "CK_ClassSessions_Status");
        Assert.Contains(checks, c => c.ModelName == "CK_ClassSessions_Suspended");
        Assert.Contains(checks, c => c.ModelName == "CK_ClassSessions_Suspension_Other");
        Assert.Contains(checks, c => c.ModelName == "CK_ClassSessions_Cancelled");
        Assert.Contains(checks, c => c.ModelName == "CK_ClassSessions_Cancellation_Other");
        Assert.Contains(checks, c => c.ModelName == "CK_ClassSessions_Rescheduled");

        // Unique index
        var uniqueIndex = Assert.Single(sessionType.GetIndexes(), i => i.GetDatabaseName() == "UX_ClassSessions_Schedule_StartsAt");
        Assert.True(uniqueIndex.IsUnique);
        Assert.NotNull(uniqueIndex.GetFilter());

        // Foreign keys restrict
        foreach (var fk in sessionType.GetForeignKeys())
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }
    }

    [Fact]
    public void ClassSessionStaffChange_Configuration_ShouldBeConfigured()
    {
        var model = BuildModel();
        var changeType = model.FindEntityType(typeof(ClassSessionStaffChange))!;

        Assert.Equal("ClassSessionStaffChanges", changeType.GetTableName());

        var checks = changeType.GetCheckConstraints().ToList();
        Assert.Contains(checks, c => c.ModelName == "CK_ClassSessionStaffChanges_Role");

        foreach (var fk in changeType.GetForeignKeys())
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }
    }

    [Fact]
    public void RecurringSchedule_SecretaryId_ShouldBeConfigured()
    {
        var model = BuildModel();
        var scheduleType = model.FindEntityType(typeof(RecurringSchedule))!;

        var secretaryProp = scheduleType.FindProperty(nameof(RecurringSchedule.SecretaryId));
        Assert.NotNull(secretaryProp);
        Assert.True(secretaryProp.IsNullable);

        var secretaryFk = scheduleType.GetForeignKeys().FirstOrDefault(fk => fk.Properties.Any(p => p.Name == nameof(RecurringSchedule.SecretaryId)));
        Assert.NotNull(secretaryFk);
        Assert.Equal(DeleteBehavior.Restrict, secretaryFk.DeleteBehavior);
    }
}
