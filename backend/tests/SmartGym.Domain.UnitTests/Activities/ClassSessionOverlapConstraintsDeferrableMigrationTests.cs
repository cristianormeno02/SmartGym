using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SmartGym.Infrastructure.Persistence.Migrations;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ClassSessionOverlapConstraintsDeferrableMigrationTests
{
    private static string UpSql() => string.Join("\n",
        new ClassSessionOverlapConstraintsDeferrable { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }
            .UpOperations.OfType<SqlOperation>().Select(op => op.Sql));

    private static string DownSql() => string.Join("\n",
        new ClassSessionOverlapConstraintsDeferrable { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" }
            .DownOperations.OfType<SqlOperation>().Select(op => op.Sql));

    [Theory]
    [InlineData("EX_ClassSessions_Room_NoOverlap")]
    [InlineData("EX_ClassSessions_Instructor_NoOverlap")]
    public void Up_RecreatesExclusionAsDeferrableInitiallyImmediate(string constraint)
    {
        var sql = UpSql();

        Assert.Contains($"DROP CONSTRAINT \"{constraint}\"", sql);
        Assert.Contains($"ADD CONSTRAINT \"{constraint}\"", sql);
        Assert.Equal(2, CountOccurrences(sql, "DEFERRABLE INITIALLY IMMEDIATE"));
    }

    [Fact]
    public void Down_RecreatesExclusionsAsNotDeferrable()
    {
        var sql = DownSql();

        Assert.Contains("ADD CONSTRAINT \"EX_ClassSessions_Room_NoOverlap\"", sql);
        Assert.Contains("ADD CONSTRAINT \"EX_ClassSessions_Instructor_NoOverlap\"", sql);
        Assert.DoesNotContain("DEFERRABLE", sql);
    }

    private static int CountOccurrences(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty).Length) / value.Length;
}
