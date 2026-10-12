using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Promotions;

public class PromotionModelConfigurationTests
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
    public void Promotion_StatusVersionAndCheckConstraints_ShouldBeConfigured()
    {
        var model = BuildModel();
        var promotionType = model.FindEntityType(typeof(Promotion))!;

        Assert.Equal("Promotions", promotionType.GetTableName());

        var statusProp = promotionType.FindProperty(nameof(Promotion.Status))!;
        Assert.False(statusProp.IsNullable);

        // RowVersion (xmin)
        var versionProp = promotionType.FindProperty(nameof(Promotion.Version))!;
        Assert.True(versionProp.IsConcurrencyToken);
        Assert.Equal("xmin", versionProp.GetColumnName());

        var checks = promotionType.GetCheckConstraints().ToList();
        Assert.Contains(checks, c => c.ModelName == "CK_Promotions_Status" && c.Sql == "\"Status\" IN (1, 2, 3)");
        Assert.Contains(checks, c =>
            c.ModelName == "CK_Promotions_CurrentUsesTotal" &&
            c.Sql == "\"CurrentUsesTotal\" >= 0 AND (\"MaxUsesTotal\" IS NULL OR \"CurrentUsesTotal\" <= \"MaxUsesTotal\")");
    }

    [Fact]
    public void PromotionRedemption_MembershipPartialUniqueIndex_ShouldBeConfigured()
    {
        var model = BuildModel();
        var redemptionType = model.FindEntityType(typeof(PromotionRedemption))!;

        var membershipIdx = Assert.Single(
            redemptionType.GetIndexes(),
            i => i.GetDatabaseName() == "UX_PromotionRedemptions_Membership");
        Assert.True(membershipIdx.IsUnique);
        Assert.Equal(nameof(PromotionRedemption.MembershipId), Assert.Single(membershipIdx.Properties).Name);
        Assert.Equal("\"MembershipId\" IS NOT NULL AND \"IsActive\"", membershipIdx.GetFilter());

        var reservationIdx = Assert.Single(
            redemptionType.GetIndexes(),
            i => i.GetDatabaseName() == "UX_PromotionRedemptions_Reservation");
        Assert.True(reservationIdx.IsUnique);
    }
}
