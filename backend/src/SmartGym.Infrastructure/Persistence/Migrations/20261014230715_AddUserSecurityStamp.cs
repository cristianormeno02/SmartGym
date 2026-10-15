using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValueSql: "md5(random()::text || clock_timestamp()::text)");

            migrationBuilder.Sql(@"
UPDATE ""Users""
SET ""SecurityStamp"" = md5(random()::text || clock_timestamp()::text)
WHERE ""SecurityStamp"" = '' OR ""SecurityStamp"" IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");
        }
    }
}
