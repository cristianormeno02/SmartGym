using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class NotificationModelConfigurationTests
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
    public void NotificationMessage_ShouldConfigureRestrictFkAndPartialIdempotencyIndex()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(NotificationMessage))!;

        Assert.Equal("NotificationMessages", entity.GetTableName());
        Assert.Equal(4000, entity.FindProperty(nameof(NotificationMessage.Body))!.GetMaxLength());
        Assert.Equal(180, entity.FindProperty(nameof(NotificationMessage.IdempotencyKey))!.GetMaxLength());

        var recipientFk = Assert.Single(
            entity.GetForeignKeys(),
            fk => fk.Properties.Any(p => p.Name == nameof(NotificationMessage.RecipientPersonId)));
        Assert.Equal(DeleteBehavior.Restrict, recipientFk.DeleteBehavior);

        var idempotencyIdx = Assert.Single(
            entity.GetIndexes(),
            i => i.GetDatabaseName() == "UX_NotificationMessages_Recipient_IdempotencyKey");
        Assert.True(idempotencyIdx.IsUnique);
        Assert.Equal("\"IdempotencyKey\" IS NOT NULL", idempotencyIdx.GetFilter());
    }

    [Fact]
    public void NotificationDelivery_ShouldConfigureTableCheckConstraintAndUniqueChannelIndex()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(NotificationDelivery));
        Assert.NotNull(entity);
        Assert.Equal("NotificationDeliveries", entity!.GetTableName());

        var uniqueChannelIdx = Assert.Single(
            entity.GetIndexes(),
            i => i.GetDatabaseName() == "UX_NotificationDeliveries_Notification_Channel");
        Assert.True(uniqueChannelIdx.IsUnique);

        var checks = entity.GetCheckConstraints().ToList();
        Assert.Contains(checks, c =>
            c.ModelName == "CK_NotificationDeliveries_Attempts" &&
            c.Sql == "\"AttemptCount\" >= 0 AND \"MaxAttempts\" >= 1");
    }

    [Fact]
    public void NotificationPreference_ShouldConfigureUniquePersonCategoryChannelIndex()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(NotificationPreference));
        Assert.NotNull(entity);
        Assert.Equal("NotificationPreferences", entity!.GetTableName());

        var uniqueIdx = Assert.Single(
            entity.GetIndexes(),
            i => i.GetDatabaseName() == "UX_NotificationPreferences_Person_Category_Channel");
        Assert.True(uniqueIdx.IsUnique);
    }

    [Fact]
    public void NotificationTemplate_ShouldConfigureRowVersionAndUniqueActiveIndex()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(NotificationTemplate));
        Assert.NotNull(entity);
        Assert.Equal("NotificationTemplates", entity!.GetTableName());

        var versionProp = entity.FindProperty(nameof(NotificationTemplate.VersionToken))!;
        Assert.True(versionProp.IsConcurrencyToken);
        Assert.Equal("xmin", versionProp.GetColumnName());

        var codeVersionIdx = Assert.Single(
            entity.GetIndexes(),
            i => i.GetDatabaseName() == "UX_NotificationTemplates_Code_Channel_Locale_Version");
        Assert.True(codeVersionIdx.IsUnique);

        var activeIdx = Assert.Single(
            entity.GetIndexes(),
            i => i.GetDatabaseName() == "UX_NotificationTemplates_Active_Type_Channel_Locale");
        Assert.True(activeIdx.IsUnique);
        Assert.Equal("\"Status\" = 2 AND \"IsActive\"", activeIdx.GetFilter());
    }
}
