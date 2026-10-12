using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Recrea las exclusiones de solapamiento como DEFERRABLE INITIALLY IMMEDIATE. Siguen verificándose por
    /// sentencia, pero la reprogramación puede diferirlas dentro de su transacción: inserta la sesión nueva
    /// antes de marcar la original como REPROGRAMADA, y ambas se superpondrían momentáneamente.
    /// PostgreSQL no permite ALTER CONSTRAINT sobre exclusiones, por eso se eliminan y se vuelven a crear.
    /// </summary>
    public partial class ClassSessionOverlapConstraintsDeferrable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BuildConstraintsSql(deferrable: true));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BuildConstraintsSql(deferrable: false));
        }

        private static string BuildConstraintsSql(bool deferrable)
        {
            var deferrability = deferrable ? "\nDEFERRABLE INITIALLY IMMEDIATE" : string.Empty;

            return $@"
ALTER TABLE ""ClassSessions"" DROP CONSTRAINT ""EX_ClassSessions_Room_NoOverlap"";
ALTER TABLE ""ClassSessions""
ADD CONSTRAINT ""EX_ClassSessions_Room_NoOverlap""
EXCLUDE USING gist (
    ""RoomId"" WITH =,
    tstzrange(""StartsAtUtc"", ""EndsAtUtc"") WITH &&
) WHERE (""IsActive"" = TRUE AND ""Status"" IN (1, 6)){deferrability};

ALTER TABLE ""ClassSessions"" DROP CONSTRAINT ""EX_ClassSessions_Instructor_NoOverlap"";
ALTER TABLE ""ClassSessions""
ADD CONSTRAINT ""EX_ClassSessions_Instructor_NoOverlap""
EXCLUDE USING gist (
    ""EffectiveInstructorId"" WITH =,
    tstzrange(""StartsAtUtc"", ""EndsAtUtc"") WITH &&
) WHERE (""IsActive"" = TRUE AND ""Status"" IN (1, 6)){deferrability};
";
        }
    }
}
