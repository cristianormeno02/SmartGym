using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActivitiesCatalogRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Respaldo de medios legados (LogoUrl e ImageUrls)
            migrationBuilder.CreateTable(
                name: "ActivityLegacyMediaBackup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    BackedUpAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLegacyMediaBackup", x => x.Id);
                });

            migrationBuilder.Sql(@"
INSERT INTO ""ActivityLegacyMediaBackup"" (""Id"", ""ActivityId"", ""MediaType"", ""Url"", ""BackedUpAtUtc"")
SELECT gen_random_uuid(), ""Id"", 'Logo', ""LogoUrl"", NOW()
FROM ""Activities""
WHERE ""LogoUrl"" IS NOT NULL AND ""LogoUrl"" <> '';

-- ImageUrls se persistía como un array JSON serializado (List<string>)
INSERT INTO ""ActivityLegacyMediaBackup"" (""Id"", ""ActivityId"", ""MediaType"", ""Url"", ""BackedUpAtUtc"")
SELECT gen_random_uuid(), a.""Id"", 'Gallery', TRIM(u.value), NOW()
FROM ""Activities"" a
CROSS JOIN LATERAL jsonb_array_elements_text(
    CASE WHEN a.""ImageUrls"" IS NULL OR TRIM(a.""ImageUrls"") = '' THEN '[]'::jsonb
         ELSE a.""ImageUrls""::jsonb END
) AS u(value)
WHERE TRIM(u.value) <> '';
");

            migrationBuilder.DropForeignKey(
                name: "FK_Activities_Rooms_DefaultRoomId",
                table: "Activities");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipPlanActivities_Activities_ActivityId",
                table: "MembershipPlanActivities");

            migrationBuilder.DropIndex(
                name: "IX_Activities_DefaultRoomId",
                table: "Activities");

            migrationBuilder.RenameColumn(
                name: "Summary",
                table: "Activities",
                newName: "ShortDescription");

            // EquipmentNotes es una columna nueva: LogoUrl ya fue respaldada y se elimina más abajo
            migrationBuilder.AddColumn<string>(
                name: "EquipmentNotes",
                table: "Activities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Permitir NULL temporalmente para rellenar datos antes de hacer NOT NULL
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Activities",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ColorHex",
                table: "Activities",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultCapacity",
                table: "Activities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Activities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Activities",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            // 2. Data Migration: copiar MaxCapacity a DefaultCapacity, poblar Code y NormalizedName, recalcular IsActive
            migrationBuilder.Sql(@"
-- Copiar MaxCapacity a DefaultCapacity
UPDATE ""Activities""
SET ""DefaultCapacity"" = ""MaxCapacity""
WHERE ""MaxCapacity"" > 0;

-- Normalizar NormalizedName (minúsculas, sin acentos / diacríticos)
UPDATE ""Activities""
SET ""NormalizedName"" = LOWER(
    TRANSLATE(
        TRIM(""Name""),
        'ÁÉÍÓÚÜáéíóúüÑñ',
        'aeiouuaeiouunn'
    )
);

-- Generar Code inicial desde Name: mayúsculas, transliteración, solo alfanuméricos y guión bajo
WITH GeneratedCodes AS (
    SELECT
        ""Id"",
        ""CreatedAtUtc"",
        RPAD(
            SUBSTRING(
                REGEXP_REPLACE(
                    REGEXP_REPLACE(
                        UPPER(TRANSLATE(TRIM(""Name""), 'ÁÉÍÓÚÜÑ', 'AEIOUUN')),
                        '[^A-Z0-9]', '_', 'g'
                    ),
                    '_+', '_', 'g'
                ),
                1, 46
            ),
            3, '_'
        ) AS RawCode
    FROM ""Activities""
),
RankedCodes AS (
    SELECT
        ""Id"",
        RawCode,
        ROW_NUMBER() OVER (PARTITION BY RawCode ORDER BY ""CreatedAtUtc"", ""Id"") AS Seq
    FROM GeneratedCodes
)
UPDATE ""Activities"" a
SET ""Code"" = CASE
    WHEN r.Seq = 1 THEN r.RawCode
    ELSE r.RawCode || '_' || r.Seq
END
FROM RankedCodes r
WHERE a.""Id"" = r.""Id"";

-- Recalcular IsActive consistente con Status (1 = Active)
UPDATE ""Activities""
SET ""IsActive"" = (""Status"" = 1);
");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Activities",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "Activities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "DefaultRoomId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "ImageUrls",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "MaxCapacity",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "MinCapacity",
                table: "Activities");

            migrationBuilder.CreateTable(
                name: "ActivityMedias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityMedias", x => x.Id);
                    table.CheckConstraint("CK_ActivityMedias_LogoNotPrimary", "\"Type\" = 2 OR \"IsPrimary\" = FALSE");
                    table.CheckConstraint("CK_ActivityMedias_SizeBytes", "\"SizeBytes\" > 0");
                    table.ForeignKey(
                        name: "FK_ActivityMedias_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_Code",
                table: "Activities",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Activities_Status",
                table: "Activities",
                column: "Status");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Activities_Age",
                table: "Activities",
                sql: "(\"MinAge\" IS NULL OR \"MinAge\" >= 0) AND (\"MaxAge\" IS NULL OR \"MaxAge\" >= 0) AND (\"MinAge\" IS NULL OR \"MaxAge\" IS NULL OR \"MinAge\" <= \"MaxAge\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Activities_Capacity",
                table: "Activities",
                sql: "\"DefaultCapacity\" IS NULL OR \"DefaultCapacity\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Activities_Code_Format",
                table: "Activities",
                sql: "\"Code\" ~ '^[A-Z0-9_]{3,50}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Activities_ColorHex",
                table: "Activities",
                sql: "\"ColorHex\" IS NULL OR \"ColorHex\" ~ '^#[0-9A-F]{6}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Activities_Status_IsActive",
                table: "Activities",
                sql: "(\"Status\" = 1 AND \"IsActive\" = TRUE) OR (\"Status\" <> 1 AND \"IsActive\" = FALSE)");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityMedias_ActivityId_IsPrimary",
                table: "ActivityMedias",
                column: "ActivityId",
                unique: true,
                filter: "\"IsPrimary\" = TRUE AND \"Type\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityMedias_ActivityId_Logo",
                table: "ActivityMedias",
                column: "ActivityId",
                unique: true,
                filter: "\"Type\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityMedias_ActivityId_SortOrder",
                table: "ActivityMedias",
                columns: new[] { "ActivityId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityMedias_ObjectKey",
                table: "ActivityMedias",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipPlanActivities_Activities_ActivityId",
                table: "MembershipPlanActivities",
                column: "ActivityId",
                principalTable: "Activities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MembershipPlanActivities_Activities_ActivityId",
                table: "MembershipPlanActivities");

            migrationBuilder.DropTable(
                name: "ActivityMedias");

            migrationBuilder.DropIndex(
                name: "IX_Activities_Code",
                table: "Activities");

            migrationBuilder.DropIndex(
                name: "IX_Activities_Status",
                table: "Activities");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Activities_Age",
                table: "Activities");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Activities_Capacity",
                table: "Activities");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Activities_Code_Format",
                table: "Activities");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Activities_ColorHex",
                table: "Activities");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Activities_Status_IsActive",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "ColorHex",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "DefaultCapacity",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Activities");

            migrationBuilder.RenameColumn(
                name: "ShortDescription",
                table: "Activities",
                newName: "Summary");

            migrationBuilder.DropColumn(
                name: "EquipmentNotes",
                table: "Activities");

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Activities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultRoomId",
                table: "Activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrls",
                table: "Activities",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MaxCapacity",
                table: "Activities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinCapacity",
                table: "Activities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Activities_DefaultRoomId",
                table: "Activities",
                column: "DefaultRoomId");

            migrationBuilder.AddForeignKey(
                name: "FK_Activities_Rooms_DefaultRoomId",
                table: "Activities",
                column: "DefaultRoomId",
                principalTable: "Rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.DropTable(
                name: "ActivityLegacyMediaBackup");
        }
    }
}
