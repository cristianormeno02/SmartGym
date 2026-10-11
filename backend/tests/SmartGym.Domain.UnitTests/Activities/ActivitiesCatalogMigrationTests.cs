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

    private static string DataMigrationSql() =>
        Assert.Single(UpOperations().OfType<SqlOperation>(),
            op => op.Sql.Contains("\"Code\"") && op.Sql.Contains("\"NormalizedName\"")).Sql;

    [Fact]
    public void Up_PadsGeneratedCodeWithoutTruncatingItToThreeCharacters()
    {
        // En PostgreSQL RPAD(x, 3, '_') también trunca x a 3 caracteres ("Spinning" -> "SPI").
        var sql = DataMigrationSql();

        Assert.DoesNotMatch(@"RPAD\((?:(?!RPAD\().)*,\s*3,\s*'_'\s*\)", sql.ReplaceLineEndings(" "));
        Assert.Contains("GREATEST(LENGTH(", sql);
    }

    [Fact]
    public void Up_SuffixesDuplicateCodesWithDoubleUnderscoreSoTheyCannotCollideWithGeneratedCodes()
    {
        // Los códigos base colapsan '_+' a '_', así que "YOGA__2" nunca coincide con el de "Yoga 2" ("YOGA_2").
        var sql = DataMigrationSql();

        Assert.Contains("'__' || r.Seq", sql);
        Assert.DoesNotContain("r.RawCode || '_' || r.Seq", sql);
    }

    [Fact]
    public void Up_NormalizesNameLikeTextNormalizer()
    {
        // Debe coincidir con TextNormalizer.NormalizeForSearch: sin diacríticos, minúsculas y espacios colapsados.
        var sql = DataMigrationSql();

        Assert.Contains("unaccent(", sql);
        Assert.Contains(@"'\s+', ' ', 'g'", sql);
    }
}
