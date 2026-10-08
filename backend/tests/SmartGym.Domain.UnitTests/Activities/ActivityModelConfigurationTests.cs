using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartGym.Application.Modules.Activities;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ActivityModelConfigurationTests
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
    public void Activity_CodeIndex_ShouldBeUnique()
    {
        var model = BuildModel();
        var activityType = model.FindEntityType(typeof(Activity))!;

        var codeIndex = Assert.Single(activityType.GetIndexes(), i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(Activity.Code));

        Assert.True(codeIndex.IsUnique);
    }

    [Fact]
    public void Activity_CheckConstraints_ShouldBeConfigured()
    {
        var model = BuildModel();
        var activityType = model.FindEntityType(typeof(Activity))!;
        var checkConstraints = activityType.GetCheckConstraints().ToList();

        Assert.Contains(checkConstraints, c => c.ModelName == "CK_Activities_Code_Format");
        Assert.Contains(checkConstraints, c => c.ModelName == "CK_Activities_Capacity");
        Assert.Contains(checkConstraints, c => c.ModelName == "CK_Activities_Age");
        Assert.Contains(checkConstraints, c => c.ModelName == "CK_Activities_ColorHex");
        Assert.Contains(checkConstraints, c => c.ModelName == "CK_Activities_Status_IsActive");
    }

    [Fact]
    public void Activity_Version_ShouldBeRowVersion()
    {
        var model = BuildModel();
        var activityType = model.FindEntityType(typeof(Activity))!;
        var versionProp = activityType.FindProperty(nameof(Activity.Version))!;

        Assert.True(versionProp.IsConcurrencyToken);
    }

    [Fact]
    public void ActivityMedia_IndexesAndConstraints_ShouldBeConfigured()
    {
        var model = BuildModel();
        var mediaType = model.FindEntityType(typeof(ActivityMedia))!;

        // Índice único ObjectKey
        var objectKeyIndex = Assert.Single(mediaType.GetIndexes(), i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(ActivityMedia.ObjectKey));
        Assert.True(objectKeyIndex.IsUnique);

        // Índice único parcial para imagen principal
        var primaryIndex = Assert.Single(mediaType.GetIndexes(), i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(ActivityMedia.ActivityId) && (i.GetFilter() ?? "").Contains("IsPrimary"));
        Assert.True(primaryIndex.IsUnique);

        // Índice único parcial para logo
        var logoIndex = Assert.Single(mediaType.GetIndexes(), i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(ActivityMedia.ActivityId) && (i.GetFilter() ?? "").Contains("Type") && !(i.GetFilter() ?? "").Contains("IsPrimary"));
        Assert.True(logoIndex.IsUnique);

        // Check constraints
        var checks = mediaType.GetCheckConstraints().ToList();
        Assert.Contains(checks, c => c.ModelName == "CK_ActivityMedias_SizeBytes");
        Assert.Contains(checks, c => c.ModelName == "CK_ActivityMedias_LogoNotPrimary");
    }

    [Fact]
    public void MembershipPlanActivity_ForeignKey_ShouldHaveRestrictDeleteBehavior()
    {
        var model = BuildModel();
        var planActivityType = model.FindEntityType(typeof(MembershipPlanActivity))!;

        var fkToActivity = planActivityType.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(Activity));

        Assert.Equal(DeleteBehavior.Restrict, fkToActivity.DeleteBehavior);
    }

    // ActivityService traduce las violaciones de unicidad buscando estos nombres: deben coincidir con el modelo
    [Fact]
    public void UniqueIndexNames_ShouldMatchActivityConstraintNames()
    {
        var model = BuildModel();
        var activityIndexes = model.FindEntityType(typeof(Activity))!.GetIndexes().Select(i => i.GetDatabaseName());
        var mediaIndexes = model.FindEntityType(typeof(ActivityMedia))!.GetIndexes().Select(i => i.GetDatabaseName());

        Assert.Contains(ActivityConstraintNames.CodeUnique, activityIndexes);
        Assert.Contains(ActivityConstraintNames.SinglePrimaryImage, mediaIndexes);
        Assert.Contains(ActivityConstraintNames.SingleLogo, mediaIndexes);
    }
}
