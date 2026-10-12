using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SmartGym.Infrastructure.Persistence.Migrations;
using Xunit;

namespace SmartGym.Domain.UnitTests.Promotions;

public class HardenPromotionsMigrationTests
{
    private static List<MigrationOperation> UpOperations() =>
        new HardenPromotionsModule { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.UpOperations.ToList();

    private static List<MigrationOperation> DownOperations() =>
        new HardenPromotionsModule { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.DownOperations.ToList();

    [Fact]
    public void Up_AddsStatusWithActiveDefault_AndDoesNotAddXmin()
    {
        var ops = UpOperations();

        var addedColumns = ops.OfType<AddColumnOperation>().Where(c => c.Table == "Promotions").ToList();
        var status = Assert.Single(addedColumns);
        Assert.Equal("Status", status.Name);
        Assert.False(status.IsNullable);
        Assert.Equal(1, status.DefaultValue);
    }

    [Fact]
    public void Up_MigratesDataBeforeCreatingConstraintsAndIndex()
    {
        var ops = UpOperations();
        var sql = string.Join("\n", ops.OfType<SqlOperation>().Select(s => s.Sql));

        // Promociones desactivadas -> Paused, conservando IsActive = TRUE
        Assert.Contains("SET \"Status\" = 2, \"IsActive\" = TRUE", sql);
        Assert.Contains("WHERE \"IsActive\" = FALSE", sql);

        // Un único canje vigente por membresía, contadores recalculados y tope general elevado
        Assert.Contains("PARTITION BY \"MembershipId\"", sql);
        Assert.Contains("SET \"CurrentUsesTotal\"", sql);
        Assert.Contains("SET \"MaxUsesTotal\" = \"CurrentUsesTotal\"", sql);

        var lastSqlIndex = ops.FindLastIndex(o => o is SqlOperation);
        var statusColumnIndex = ops.FindIndex(o => o is AddColumnOperation { Name: "Status" });
        var usesCheckIndex = ops.FindIndex(o => o is AddCheckConstraintOperation { Name: "CK_Promotions_CurrentUsesTotal" });
        var statusCheckIndex = ops.FindIndex(o => o is AddCheckConstraintOperation { Name: "CK_Promotions_Status" });
        var membershipIndexIndex = ops.FindIndex(o => o is CreateIndexOperation { Name: "UX_PromotionRedemptions_Membership" });

        Assert.True(statusColumnIndex >= 0 && statusColumnIndex < ops.FindIndex(o => o is SqlOperation));
        Assert.True(usesCheckIndex > lastSqlIndex);
        Assert.True(statusCheckIndex > lastSqlIndex);
        Assert.True(membershipIndexIndex > lastSqlIndex);

        // La deduplicación de canjes precede al recálculo de contadores
        Assert.True(sql.IndexOf("PARTITION BY \"MembershipId\"", StringComparison.Ordinal)
            < sql.IndexOf("SET \"CurrentUsesTotal\"", StringComparison.Ordinal));

        var index = (CreateIndexOperation)ops[membershipIndexIndex];
        Assert.True(index.IsUnique);
        Assert.Equal("\"MembershipId\" IS NOT NULL AND \"IsActive\"", index.Filter);
    }

    [Fact]
    public void Down_RestoresIsActiveBeforeDroppingStatus_AndDoesNotDropXmin()
    {
        var ops = DownOperations();

        var restoreIndex = ops.FindIndex(o => o is SqlOperation s
            && s.Sql.Contains("SET \"IsActive\" = FALSE")
            && s.Sql.Contains("WHERE \"Status\" <> 1"));
        var dropStatusIndex = ops.FindIndex(o => o is DropColumnOperation { Name: "Status", Table: "Promotions" });

        Assert.True(restoreIndex >= 0);
        Assert.True(dropStatusIndex > restoreIndex);
        Assert.DoesNotContain(ops.OfType<DropColumnOperation>(), c => c.Name == "xmin");
        Assert.Contains(ops.OfType<DropIndexOperation>(), i => i.Name == "UX_PromotionRedemptions_Membership");
        Assert.Contains(ops.OfType<CreateIndexOperation>(), i => i.Name == "IX_PromotionRedemptions_MembershipId");
    }
}
