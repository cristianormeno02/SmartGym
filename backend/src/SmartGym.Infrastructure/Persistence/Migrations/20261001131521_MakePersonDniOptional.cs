using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakePersonDniOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_People_Dni",
                table: "People");

            migrationBuilder.AlterColumn<string>(
                name: "Dni",
                table: "People",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            // Las personas creadas vía Google tenían DNI vacío: pasan a "sin informar".
            migrationBuilder.Sql("UPDATE \"People\" SET \"Dni\" = NULL WHERE TRIM(\"Dni\") = '';");

            migrationBuilder.CreateIndex(
                name: "IX_People_Dni",
                table: "People",
                column: "Dni",
                unique: true,
                filter: "\"Dni\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_People_Dni",
                table: "People");

            // Reversión con pérdida: si hay más de una persona sin DNI, el índice único original fallará.
            migrationBuilder.Sql("UPDATE \"People\" SET \"Dni\" = '' WHERE \"Dni\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "Dni",
                table: "People",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_Dni",
                table: "People",
                column: "Dni",
                unique: true);
        }
    }
}
