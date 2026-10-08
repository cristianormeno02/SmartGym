using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SmartGym.Infrastructure.Persistence.Migrations;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

/// <summary>
/// Inspecciona las operaciones de la migración ActivitiesCatalogRefactor. InMemory no ejecuta
/// migraciones, por lo que se verifica su estructura y el SQL de datos que contiene.
/// </summary>
public class ActivitiesCatalogMigrationTests
{
    private static IReadOnlyList<MigrationOperation> UpOperations() =>
        new ActivitiesCatalogRefactor { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.UpOperations;

    private static IReadOnlyList<MigrationOperation> DownOperations() =>
        new ActivitiesCatalogRefactor { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.DownOperations;

    [Fact]
    public void Up_DoesNotRenameLogoUrlIntoEquipmentNotes()
    {
        var operations = UpOperations();

        Assert.DoesNotContain(operations.OfType<RenameColumnOperation>(),
            op => op.Table == "Activities" && op.Name == "LogoUrl");

        var addEquipmentNotes = Assert.Single(operations.OfType<AddColumnOperation>(),
            op => op.Table == "Activities" && op.Name == "EquipmentNotes");
        Assert.True(addEquipmentNotes.IsNullable);
        Assert.Equal(500, addEquipmentNotes.MaxLength);
    }

    [Fact]
    public void Up_DropsLogoUrlOnlyAfterBackingItUp()
    {
        var operations = UpOperations().ToList();

        var backupIndex = operations.FindIndex(op =>
            op is SqlOperation sql && sql.Sql.Contains("\"LogoUrl\"") && sql.Sql.Contains("ActivityLegacyMediaBackup"));
        var dropIndex = operations.FindIndex(op =>
            op is DropColumnOperation drop && drop.Table == "Activities" && drop.Name == "LogoUrl");

        Assert.True(backupIndex >= 0, "Debe existir el respaldo de LogoUrl.");
        Assert.True(dropIndex > backupIndex, "LogoUrl debe eliminarse después de respaldarse.");
    }

    [Fact]
    public void Up_BacksUpImageUrlsParsingTheJsonArray()
    {
        var backupSql = Assert.Single(UpOperations().OfType<SqlOperation>(),
            op => op.Sql.Contains("\"ImageUrls\"") && op.Sql.Contains("ActivityLegacyMediaBackup")).Sql;

        Assert.Contains("jsonb_array_elements_text", backupSql);
        Assert.DoesNotContain("string_to_array", backupSql);
    }

    [Fact]
    public void Down_DoesNotRenameEquipmentNotesBackIntoLogoUrl()
    {
        var operations = DownOperations();

        Assert.DoesNotContain(operations.OfType<RenameColumnOperation>(),
            op => op.Table == "Activities" && op.Name == "EquipmentNotes");
        Assert.Contains(operations.OfType<DropColumnOperation>(),
            op => op.Table == "Activities" && op.Name == "EquipmentNotes");
        Assert.Contains(operations.OfType<AddColumnOperation>(),
            op => op.Table == "Activities" && op.Name == "LogoUrl");
    }
}
