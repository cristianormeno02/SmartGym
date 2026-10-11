using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SmartGym.Infrastructure.Persistence.Migrations;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ClassSessionsRefactorMigrationTests
{
    private static IReadOnlyList<MigrationOperation> UpOperations() =>
        new ClassSessionsRefactor { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.UpOperations;

    private static IReadOnlyList<MigrationOperation> DownOperations() =>
        new ClassSessionsRefactor { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }.DownOperations;

    [Fact]
    public void Up_Operations_ContainRequiredStepsInOrder()
    {
        var ops = UpOperations().ToList();

        // 1. btree_gist extension
        var btreeIndex = ops.FindIndex(op => op is SqlOperation sql && sql.Sql.Contains("CREATE EXTENSION IF NOT EXISTS btree_gist", StringComparison.OrdinalIgnoreCase));
        Assert.True(btreeIndex >= 0, "Debe crearse la extensión btree_gist.");

        // 2. AT TIME ZONE conversion
        var timeZoneIndex = ops.FindIndex(op => op is SqlOperation sql && sql.Sql.Contains("AT TIME ZONE 'America/Argentina/San_Juan'", StringComparison.OrdinalIgnoreCase));
        Assert.True(timeZoneIndex > btreeIndex, "La conversión de zona horaria debe ocurrir después de btree_gist.");

        // 3. Staff mapping & history insertion
        var staffMappingIndex = ops.FindIndex(op => op is SqlOperation sql && sql.Sql.Contains("ClassSessionStaffChanges", StringComparison.OrdinalIgnoreCase) && sql.Sql.Contains("Migrado desde SubstituteInstructorId"));
        Assert.True(staffMappingIndex > timeZoneIndex, "El mapeo de staff e inserción de historial debe ocurrir después de la conversión de fechas.");

        // 4. Status remapping 1/2/3 -> 6
        var statusRemapIndex = ops.FindIndex(op => op is SqlOperation sql && sql.Sql.Contains("\"Status\" = 6") && (sql.Sql.Contains("IN (1, 2, 3)") || sql.Sql.Contains("IN (1,2,3)")));
        Assert.True(statusRemapIndex > staffMappingIndex, "El remapeo de estados debe ocurrir en orden.");

        // 5. Reasons conversion
        var reasonsIndex = ops.FindIndex(op => op is SqlOperation sql && sql.Sql.Contains("SuspensionReason") && sql.Sql.Contains("CancellationReason"));
        Assert.True(reasonsIndex > statusRemapIndex, "La conversión de motivos debe ocurrir en orden.");

        // 6. Pre-verification with RAISE EXCEPTION
        var preCheckIndex = ops.FindIndex(op => op is SqlOperation sql && sql.Sql.Contains("RAISE EXCEPTION", StringComparison.OrdinalIgnoreCase));
        Assert.True(preCheckIndex > reasonsIndex, "La verificación previa con RAISE EXCEPTION debe ocurrir antes de crear exclusiones.");

        // 7. Exclusions EX_ClassSessions_*
        var exclusionIndex = ops.FindIndex(op => op is SqlOperation sql && sql.Sql.Contains("EX_ClassSessions_Room_NoOverlap") && sql.Sql.Contains("EX_ClassSessions_Instructor_NoOverlap"));
        Assert.True(exclusionIndex > preCheckIndex, "Las restricciones de exclusión deben crearse después de la verificación previa.");

        // 8. Legacy columns dropped
        var dropDateIndex = ops.FindIndex(op => op is DropColumnOperation drop && drop.Table == "ClassSessions" && drop.Name == "Date");
        Assert.True(dropDateIndex > preCheckIndex, "Las columnas legacy deben eliminarse tras migrar datos.");
    }

    [Fact]
    public void Down_Operations_IsNotEmpty()
    {
        var downOps = DownOperations();
        Assert.NotEmpty(downOps);
    }
}
