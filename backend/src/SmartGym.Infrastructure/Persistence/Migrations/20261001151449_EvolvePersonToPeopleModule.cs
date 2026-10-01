using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvolvePersonToPeopleModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Verificación previa de integridad de datos existentes
            migrationBuilder.Sql(@"
DO $$
BEGIN
    -- Check for DNI with letters or unsupported characters
    IF EXISTS (
        SELECT 1 FROM ""People"" 
        WHERE ""Dni"" IS NOT NULL AND ""Dni"" ~ '[^\d\.\-\s]'
    ) THEN
        RAISE EXCEPTION 'Pre-check failed: Found People rows with invalid characters in Dni.';
    END IF;

    -- Check for duplicate normalized DNIs
    IF EXISTS (
        SELECT ltrim(regexp_replace(""Dni"", '\D', '', 'g'), '0')
        FROM ""People""
        WHERE ""Dni"" IS NOT NULL AND ltrim(regexp_replace(""Dni"", '\D', '', 'g'), '0') <> ''
        GROUP BY ltrim(regexp_replace(""Dni"", '\D', '', 'g'), '0')
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Pre-check failed: Found duplicate normalized DNI values in People.';
    END IF;

    -- Check for duplicate normalized emails
    IF EXISTS (
        SELECT lower(trim(""Email""))
        FROM ""People""
        WHERE ""Email"" IS NOT NULL AND trim(""Email"") <> ''
        GROUP BY lower(trim(""Email""))
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Pre-check failed: Found duplicate normalized email values in People.';
    END IF;
END $$;
");

            // 2. Extensiones de PostgreSQL
            migrationBuilder.Sql(@"
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;
");

            // 3. Eliminar índices anteriores
            migrationBuilder.DropIndex(
                name: "IX_People_Dni",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_Email",
                table: "People");

            // 4. Renombrar columnas existentes según especificación
            migrationBuilder.RenameColumn(
                name: "PhoneNumber",
                table: "People",
                newName: "PrimaryPhone");

            migrationBuilder.RenameColumn(
                name: "PhotoUrl",
                table: "People",
                newName: "ExternalAvatarUrl");

            // 5. Ajustar Email para admitir NULL
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "People",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            // 6. Agregar nuevas columnas
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "People",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "People",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAtUtc",
                table: "People",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StatusChangedByUserId",
                table: "People",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "People",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryPhone",
                table: "People",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchName",
                table: "People",
                type: "character varying(201)",
                maxLength: 201,
                nullable: false,
                defaultValue: "");

            // Document
            migrationBuilder.AddColumn<string>(
                name: "DocumentType",
                table: "People",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentIssuingCountry",
                table: "People",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                table: "People",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumberNormalized",
                table: "People",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Address
            migrationBuilder.AddColumn<string>(
                name: "AddressStreet",
                table: "People",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressNumber",
                table: "People",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressFloor",
                table: "People",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressApartment",
                table: "People",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressPostalCode",
                table: "People",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressCity",
                table: "People",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressStateProvince",
                table: "People",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressCountryCode",
                table: "People",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            // ProfileImage
            migrationBuilder.AddColumn<string>(
                name: "ProfileImageKey",
                table: "People",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImageContentType",
                table: "People",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProfileImageSizeBytes",
                table: "People",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProfileImageUploadedAtUtc",
                table: "People",
                type: "timestamp with time zone",
                nullable: true);

            // Concurrency token xmin
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "People",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            // 7. Backfill de datos
            migrationBuilder.Sql(@"
UPDATE ""People""
SET ""Status"" = CASE WHEN ""IsActive"" = false THEN 'Inactive' ELSE 'Active' END,
    ""DocumentType"" = CASE WHEN ""Dni"" IS NOT NULL AND trim(""Dni"") <> '' THEN 'Dni' ELSE NULL END,
    ""DocumentIssuingCountry"" = CASE WHEN ""Dni"" IS NOT NULL AND trim(""Dni"") <> '' THEN 'AR' ELSE NULL END,
    ""DocumentNumber"" = CASE WHEN ""Dni"" IS NOT NULL AND trim(""Dni"") <> '' THEN trim(""Dni"") ELSE NULL END,
    ""DocumentNumberNormalized"" = CASE WHEN ""Dni"" IS NOT NULL AND trim(""Dni"") <> '' THEN ltrim(regexp_replace(""Dni"", '\D', '', 'g'), '0') ELSE NULL END,
    ""Email"" = NULLIF(lower(trim(""Email"")), ''),
    ""LastName"" = CASE WHEN ""LastName"" IS NULL OR trim(""LastName"") = '' THEN 'Sin apellido' ELSE trim(""LastName"") END,
    ""FirstName"" = trim(""FirstName"");

UPDATE ""People""
SET ""SearchName"" = lower(regexp_replace(unaccent(trim(""FirstName"") || ' ' || trim(""LastName"")), '\s+', ' ', 'g'));
");

            // 8. Eliminar columna antigua Dni
            migrationBuilder.DropColumn(
                name: "Dni",
                table: "People");

            // 9. Crear nuevos índices y restricciones
            migrationBuilder.CreateIndex(
                name: "IX_People_Document_Unique",
                table: "People",
                columns: new[] { "DocumentType", "DocumentIssuingCountry", "DocumentNumberNormalized" },
                unique: true,
                filter: "\"DocumentNumberNormalized\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_People_Email_Unique",
                table: "People",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_People_LastName_FirstName",
                table: "People",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_People_SearchName_Trgm",
                table: "People",
                column: "SearchName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_People_Document_Consistency",
                table: "People",
                sql: "(\"DocumentType\" IS NULL AND \"DocumentIssuingCountry\" IS NULL AND \"DocumentNumber\" IS NULL AND \"DocumentNumberNormalized\" IS NULL) OR (\"DocumentType\" IS NOT NULL AND \"DocumentIssuingCountry\" IS NOT NULL AND \"DocumentNumber\" IS NOT NULL AND \"DocumentNumberNormalized\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_People_Document_Consistency",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_Document_Unique",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_Email_Unique",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_LastName_FirstName",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_SearchName_Trgm",
                table: "People");

            // Restaurar columna Dni
            migrationBuilder.AddColumn<string>(
                name: "Dni",
                table: "People",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Restaurar DNI desde DocumentNumber donde tipo sea Dni
            migrationBuilder.Sql(@"
UPDATE ""People""
SET ""Dni"" = ""DocumentNumber""
WHERE ""DocumentType"" = 'Dni';
");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "People",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "PrimaryPhone",
                table: "People",
                newName: "PhoneNumber");

            migrationBuilder.RenameColumn(
                name: "ExternalAvatarUrl",
                table: "People",
                newName: "PhotoUrl");

            migrationBuilder.DropColumn(
                name: "AddressApartment",
                table: "People");

            migrationBuilder.DropColumn(
                name: "AddressCity",
                table: "People");

            migrationBuilder.DropColumn(
                name: "AddressCountryCode",
                table: "People");

            migrationBuilder.DropColumn(
                name: "AddressFloor",
                table: "People");

            migrationBuilder.DropColumn(
                name: "AddressNumber",
                table: "People");

            migrationBuilder.DropColumn(
                name: "AddressPostalCode",
                table: "People");

            migrationBuilder.DropColumn(
                name: "AddressStateProvince",
                table: "People");

            migrationBuilder.DropColumn(
                name: "AddressStreet",
                table: "People");

            migrationBuilder.DropColumn(
                name: "DocumentIssuingCountry",
                table: "People");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                table: "People");

            migrationBuilder.DropColumn(
                name: "DocumentNumberNormalized",
                table: "People");

            migrationBuilder.DropColumn(
                name: "DocumentType",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "People");

            migrationBuilder.DropColumn(
                name: "ProfileImageContentType",
                table: "People");

            migrationBuilder.DropColumn(
                name: "ProfileImageKey",
                table: "People");

            migrationBuilder.DropColumn(
                name: "ProfileImageSizeBytes",
                table: "People");

            migrationBuilder.DropColumn(
                name: "ProfileImageUploadedAtUtc",
                table: "People");

            migrationBuilder.DropColumn(
                name: "SearchName",
                table: "People");

            migrationBuilder.DropColumn(
                name: "SecondaryPhone",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "People");

            migrationBuilder.DropColumn(
                name: "StatusChangedAtUtc",
                table: "People");

            migrationBuilder.DropColumn(
                name: "StatusChangedByUserId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "People");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "People");

            migrationBuilder.CreateIndex(
                name: "IX_People_Dni",
                table: "People",
                column: "Dni",
                unique: true,
                filter: "\"Dni\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_People_Email",
                table: "People",
                column: "Email",
                unique: true);
        }
    }
}
