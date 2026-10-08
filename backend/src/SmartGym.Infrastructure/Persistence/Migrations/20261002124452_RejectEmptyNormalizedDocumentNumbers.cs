using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RejectEmptyNormalizedDocumentNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EvolvePersonToPeopleModule convirtió valores legados como "0" o "-" en un número normalizado
            // vacío. No identifican a nadie, así que esas personas pasan a no tener documento.
            migrationBuilder.Sql(@"
UPDATE ""People""
SET ""DocumentType"" = NULL,
    ""DocumentIssuingCountry"" = NULL,
    ""DocumentNumber"" = NULL,
    ""DocumentNumberNormalized"" = NULL
WHERE ""DocumentNumberNormalized"" = '';
");

            migrationBuilder.AddCheckConstraint(
                name: "CK_People_DocumentNumberNormalized_NotEmpty",
                table: "People",
                sql: "\"DocumentNumberNormalized\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_People_DocumentNumberNormalized_NotEmpty",
                table: "People");
        }
    }
}
