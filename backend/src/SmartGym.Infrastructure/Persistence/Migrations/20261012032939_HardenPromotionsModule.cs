using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenPromotionsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PromotionRedemptions_MembershipId",
                table: "PromotionRedemptions");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Promotions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // "xmin" es una columna de sistema de PostgreSQL: no se agrega.

            migrationBuilder.Sql(@"
-- 1. Las promociones desactivadas pasan a Paused (reversible); IsActive deja de expresar la aplicabilidad
UPDATE ""Promotions""
SET ""Status"" = 2, ""IsActive"" = TRUE
WHERE ""IsActive"" = FALSE;

-- 2. Una membresía conserva un único canje vigente: el que ella referencia o, en su defecto, el más reciente
WITH ranked AS (
    SELECT r.""Id"",
           ROW_NUMBER() OVER (
               PARTITION BY ""MembershipId""
               ORDER BY (m.""PromotionRedemptionId"" = r.""Id"") DESC NULLS LAST, r.""RedeemedAtUtc"" DESC, r.""Id""
           ) AS rn
    FROM ""PromotionRedemptions"" r
    JOIN ""Memberships"" m ON m.""Id"" = r.""MembershipId""
    WHERE r.""MembershipId"" IS NOT NULL AND r.""IsActive""
)
UPDATE ""PromotionRedemptions"" r
SET ""IsActive"" = FALSE, ""UpdatedAtUtc"" = NOW()
FROM ranked
WHERE ranked.""Id"" = r.""Id"" AND ranked.rn > 1;

-- 3. Recalcular el contador de usos a partir de los canjes vigentes
UPDATE ""Promotions"" p
SET ""CurrentUsesTotal"" = (
    SELECT COUNT(*)
    FROM ""PromotionRedemptions"" r
    WHERE r.""PromotionId"" = p.""Id"" AND r.""IsActive""
);

-- 4. Elevar el tope general donde ya fue superado, para poder crear la restricción
UPDATE ""Promotions""
SET ""MaxUsesTotal"" = ""CurrentUsesTotal""
WHERE ""MaxUsesTotal"" IS NOT NULL AND ""CurrentUsesTotal"" > ""MaxUsesTotal"";
");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotions_CurrentUsesTotal",
                table: "Promotions",
                sql: "\"CurrentUsesTotal\" >= 0 AND (\"MaxUsesTotal\" IS NULL OR \"CurrentUsesTotal\" <= \"MaxUsesTotal\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotions_Status",
                table: "Promotions",
                sql: "\"Status\" IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "UX_PromotionRedemptions_Membership",
                table: "PromotionRedemptions",
                column: "MembershipId",
                unique: true,
                filter: "\"MembershipId\" IS NOT NULL AND \"IsActive\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotions_CurrentUsesTotal",
                table: "Promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotions_Status",
                table: "Promotions");

            migrationBuilder.DropIndex(
                name: "UX_PromotionRedemptions_Membership",
                table: "PromotionRedemptions");

            migrationBuilder.Sql(@"
UPDATE ""Promotions""
SET ""IsActive"" = FALSE
WHERE ""Status"" <> 1;
");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Promotions");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRedemptions_MembershipId",
                table: "PromotionRedemptions",
                column: "MembershipId");
        }
    }
}
