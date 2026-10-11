using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClassSessionsRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Extensión btree_gist requerida para exclusiones gist (Decision 12)
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_People_InstructorId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_People_OriginalInstructorId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_People_SubstituteInstructorId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_RecurringSchedules_RecurringScheduleId",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_ActivityId",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_Date",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_Date_Status",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_InstructorId",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_RecurringScheduleId_Date",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_RoomId",
                table: "ClassSessions");

            // Mantener temporalmente las columnas legacy Date, StartTime, EndTime para la migración de datos (Decision 12).
            // Se eliminarán más adelante tras poblar StartsAtUtc, EndsAtUtc y mapear el staff.

            migrationBuilder.RenameColumn(
                name: "SubstituteInstructorId",
                table: "ClassSessions",
                newName: "SuspendedByUserId");

            migrationBuilder.RenameColumn(
                name: "OriginalInstructorId",
                table: "ClassSessions",
                newName: "ScheduledSecretaryId");

            migrationBuilder.RenameColumn(
                name: "MaxCapacity",
                table: "ClassSessions",
                newName: "Capacity");

            migrationBuilder.RenameColumn(
                name: "InstructorId",
                table: "ClassSessions",
                newName: "ScheduledInstructorId");

            migrationBuilder.RenameIndex(
                name: "IX_ClassSessions_SubstituteInstructorId",
                table: "ClassSessions",
                newName: "IX_ClassSessions_SuspendedByUserId");

            migrationBuilder.RenameIndex(
                name: "IX_ClassSessions_OriginalInstructorId",
                table: "ClassSessions",
                newName: "IX_ClassSessions_ScheduledSecretaryId");

            migrationBuilder.AddColumn<Guid>(
                name: "SecretaryId",
                table: "RecurringSchedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CancellationReason",
                table: "ClassSessions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationObservations",
                table: "ClassSessions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "ClassSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "ClassSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EffectiveInstructorId",
                table: "ClassSessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "EffectiveSecretaryId",
                table: "ClassSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndsAtUtc",
                table: "ClassSessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsDetachedFromSchedule",
                table: "ClassSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ClassSessions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RescheduledFromSessionId",
                table: "ClassSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RescheduledToSessionId",
                table: "ClassSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAtUtc",
                table: "ClassSessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "SuspensionObservations",
                table: "ClassSessions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionReason",
                table: "ClassSessions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ClassSessions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "ClassSessionStaffChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PreviousPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassSessionStaffChanges", x => x.Id);
                    table.CheckConstraint("CK_ClassSessionStaffChanges_Role", "\"Role\" IN ('Instructor', 'Secretary')");
                    table.ForeignKey(
                        name: "FK_ClassSessionStaffChanges_ClassSessions_ClassSessionId",
                        column: x => x.ClassSessionId,
                        principalTable: "ClassSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassSessionStaffChanges_People_NewPersonId",
                        column: x => x.NewPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassSessionStaffChanges_People_PreviousPersonId",
                        column: x => x.PreviousPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassSessionStaffChanges_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedules_SecretaryId",
                table: "RecurringSchedules",
                column: "SecretaryId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_ActivityId_StartsAtUtc",
                table: "ClassSessions",
                columns: new[] { "ActivityId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_CancelledByUserId",
                table: "ClassSessions",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_EffectiveInstructorId_StartsAtUtc",
                table: "ClassSessions",
                columns: new[] { "EffectiveInstructorId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_EffectiveSecretaryId",
                table: "ClassSessions",
                column: "EffectiveSecretaryId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_RescheduledFromSessionId",
                table: "ClassSessions",
                column: "RescheduledFromSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_RescheduledToSessionId",
                table: "ClassSessions",
                column: "RescheduledToSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_RoomId_StartsAtUtc",
                table: "ClassSessions",
                columns: new[] { "RoomId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_ScheduledInstructorId_StartsAtUtc",
                table: "ClassSessions",
                columns: new[] { "ScheduledInstructorId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_StartsAtUtc",
                table: "ClassSessions",
                column: "StartsAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_Status_StartsAtUtc",
                table: "ClassSessions",
                columns: new[] { "Status", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ClassSessions_Schedule_StartsAt",
                table: "ClassSessions",
                columns: new[] { "RecurringScheduleId", "StartsAtUtc" },
                unique: true,
                filter: "\"RecurringScheduleId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassSessions_Cancellation_Other",
                table: "ClassSessions",
                sql: "(\"CancellationReason\" IS NULL OR \"CancellationReason\" <> 'Other' OR (\"CancellationObservations\" IS NOT NULL AND length(trim(\"CancellationObservations\")) > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassSessions_Cancelled",
                table: "ClassSessions",
                sql: "((\"Status\" = 5) = (\"CancelledAtUtc\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassSessions_Capacity",
                table: "ClassSessions",
                sql: "\"Capacity\" > 0 AND \"ReservedCount\" >= 0 AND \"ReservedCount\" <= \"Capacity\" AND \"AttendedCount\" >= 0 AND \"AttendedCount\" <= \"ReservedCount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassSessions_Dates",
                table: "ClassSessions",
                sql: "\"EndsAtUtc\" > \"StartsAtUtc\" AND \"EndsAtUtc\" - \"StartsAtUtc\" <= INTERVAL '24 hours'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassSessions_Rescheduled",
                table: "ClassSessions",
                sql: "((\"Status\" = 7) = (\"RescheduledToSessionId\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassSessions_Status",
                table: "ClassSessions",
                sql: "\"Status\" IN (1, 4, 5, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassSessions_Suspended",
                table: "ClassSessions",
                sql: "((\"Status\" = 4) = (\"SuspendedAtUtc\" IS NOT NULL AND \"SuspensionReason\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassSessions_Suspension_Other",
                table: "ClassSessions",
                sql: "(\"SuspensionReason\" IS NULL OR \"SuspensionReason\" <> 'Other' OR (\"SuspensionObservations\" IS NOT NULL AND length(trim(\"SuspensionObservations\")) > 0))");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessionStaffChanges_ChangedByUserId",
                table: "ClassSessionStaffChanges",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessionStaffChanges_ClassSessionId",
                table: "ClassSessionStaffChanges",
                column: "ClassSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessionStaffChanges_NewPersonId",
                table: "ClassSessionStaffChanges",
                column: "NewPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessionStaffChanges_PreviousPersonId",
                table: "ClassSessionStaffChanges",
                column: "PreviousPersonId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_ClassSessions_RescheduledFromSessionId",
                table: "ClassSessions",
                column: "RescheduledFromSessionId",
                principalTable: "ClassSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_ClassSessions_RescheduledToSessionId",
                table: "ClassSessions",
                column: "RescheduledToSessionId",
                principalTable: "ClassSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_People_EffectiveInstructorId",
                table: "ClassSessions",
                column: "EffectiveInstructorId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_People_EffectiveSecretaryId",
                table: "ClassSessions",
                column: "EffectiveSecretaryId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_People_ScheduledInstructorId",
                table: "ClassSessions",
                column: "ScheduledInstructorId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_People_ScheduledSecretaryId",
                table: "ClassSessions",
                column: "ScheduledSecretaryId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_RecurringSchedules_RecurringScheduleId",
                table: "ClassSessions",
                column: "RecurringScheduleId",
                principalTable: "RecurringSchedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_Users_CancelledByUserId",
                table: "ClassSessions",
                column: "CancelledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_Users_SuspendedByUserId",
                table: "ClassSessions",
                column: "SuspendedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringSchedules_People_SecretaryId",
                table: "RecurringSchedules",
                column: "SecretaryId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ==========================================
            // Migración de datos según Decision 12
            // Zona horaria fija: 'America/Argentina/San_Juan'
            // ==========================================

            // Paso 2: Conversión AT TIME ZONE para StartsAtUtc y EndsAtUtc
            migrationBuilder.Sql(@"
UPDATE ""ClassSessions""
SET ""StartsAtUtc"" = (""Date"" + ""StartTime"") AT TIME ZONE 'America/Argentina/San_Juan',
    ""EndsAtUtc"" = CASE 
        WHEN ""EndTime"" <= ""StartTime"" THEN (""Date"" + INTERVAL '1 day' + ""EndTime"") AT TIME ZONE 'America/Argentina/San_Juan'
        ELSE (""Date"" + ""EndTime"") AT TIME ZONE 'America/Argentina/San_Juan'
    END
WHERE ""Date"" IS NOT NULL;
");

            // Paso 3: Staff mapping y creación de historial de reemplazo
            migrationBuilder.Sql(@"
UPDATE ""ClassSessions""
SET ""EffectiveInstructorId"" = ""ScheduledInstructorId""
WHERE ""EffectiveInstructorId"" = '00000000-0000-0000-0000-000000000000'::uuid;

-- Insertar en ClassSessionStaffChanges por cada sesión que tenía sustituto registrado (en SuspendedByUserId antes de rename)
INSERT INTO ""ClassSessionStaffChanges"" (
    ""Id"", ""ClassSessionId"", ""Role"", ""PreviousPersonId"", ""NewPersonId"",
    ""Reason"", ""ChangedByUserId"", ""ChangedAtUtc"", ""CreatedAtUtc"", ""UpdatedAtUtc"", ""IsActive""
)
SELECT
    gen_random_uuid(),
    cs.""Id"",
    'Instructor',
    cs.""ScheduledInstructorId"",
    cs.""SuspendedByUserId"",
    'Migrado desde SubstituteInstructorId',
    NULL,
    COALESCE(cs.""UpdatedAtUtc"", cs.""CreatedAtUtc""),
    NOW(),
    NOW(),
    TRUE
FROM ""ClassSessions"" cs
WHERE cs.""SuspendedByUserId"" IS NOT NULL 
  AND cs.""Status"" NOT IN (4, 5); -- Solo si el campo representaba el sustituto, no una suspensión legacy
");

            // Paso 5: Remapeo de estados legacy 1, 2, 3 -> 6 (Open)
            migrationBuilder.Sql(@"
UPDATE ""ClassSessions""
SET ""Status"" = 6
WHERE ""Status"" IN (1, 2, 3);
");

            // Paso 6: Conversión de motivos tipificados
            migrationBuilder.Sql(@"
-- Para sesiones suspendidas (Status = 4)
UPDATE ""ClassSessions""
SET ""SuspensionReason"" = 'Other',
    ""SuspensionObservations"" = COALESCE(""CancellationReason"", 'Suspensión migrada sin observaciones')
WHERE ""Status"" = 4 AND ""SuspensionReason"" IS NULL;

-- Para sesiones canceladas (Status = 5)
UPDATE ""ClassSessions""
SET ""CancellationReason"" = 'Other',
    ""CancellationObservations"" = COALESCE(""CancellationReason"", 'Cancelación migrada sin observaciones')
WHERE ""Status"" = 5 AND ""CancellationReason"" IS NOT NULL;
");

            // Paso 8: Verificación previa con RAISE EXCEPTION
            migrationBuilder.Sql(@"
DO $$
DECLARE
    overlap_count INT;
    overlap_details TEXT;
BEGIN
    SELECT count(*), string_agg(format('Sala: %s / %s (inicio: %s)', s1.""Id"", s2.""Id"", s1.""StartsAtUtc""), '; ')
    INTO overlap_count, overlap_details
    FROM ""ClassSessions"" s1
    JOIN ""ClassSessions"" s2 ON s1.""RoomId"" = s2.""RoomId"" 
                             AND s1.""Id"" < s2.""Id""
                             AND s1.""IsActive"" = TRUE AND s2.""IsActive"" = TRUE
                             AND s1.""Status"" IN (1, 6) AND s2.""Status"" IN (1, 6)
                             AND tstzrange(s1.""StartsAtUtc"", s1.""EndsAtUtc"") && tstzrange(s2.""StartsAtUtc"", s2.""EndsAtUtc"");

    IF overlap_count > 0 THEN
        RAISE EXCEPTION 'Abortando migración ClassSessionsRefactor: se detectaron % solapamientos de sala: %', overlap_count, overlap_details;
    END IF;

    SELECT count(*), string_agg(format('Instructor: %s / %s (inicio: %s)', s1.""Id"", s2.""Id"", s1.""StartsAtUtc""), '; ')
    INTO overlap_count, overlap_details
    FROM ""ClassSessions"" s1
    JOIN ""ClassSessions"" s2 ON s1.""EffectiveInstructorId"" = s2.""EffectiveInstructorId"" 
                             AND s1.""Id"" < s2.""Id""
                             AND s1.""IsActive"" = TRUE AND s2.""IsActive"" = TRUE
                             AND s1.""Status"" IN (1, 6) AND s2.""Status"" IN (1, 6)
                             AND tstzrange(s1.""StartsAtUtc"", s1.""EndsAtUtc"") && tstzrange(s2.""StartsAtUtc"", s2.""EndsAtUtc"");

    IF overlap_count > 0 THEN
        RAISE EXCEPTION 'Abortando migración ClassSessionsRefactor: se detectaron % solapamientos de instructor: %', overlap_count, overlap_details;
    END IF;
END $$;
");

            // Paso 9: Restricciones de exclusión y limpieza de columnas legacy
            migrationBuilder.Sql(@"
ALTER TABLE ""ClassSessions""
ADD CONSTRAINT ""EX_ClassSessions_Room_NoOverlap""
EXCLUDE USING gist (
    ""RoomId"" WITH =,
    tstzrange(""StartsAtUtc"", ""EndsAtUtc"") WITH &&
) WHERE (""IsActive"" = TRUE AND ""Status"" IN (1, 6));

ALTER TABLE ""ClassSessions""
ADD CONSTRAINT ""EX_ClassSessions_Instructor_NoOverlap""
EXCLUDE USING gist (
    ""EffectiveInstructorId"" WITH =,
    tstzrange(""StartsAtUtc"", ""EndsAtUtc"") WITH &&
) WHERE (""IsActive"" = TRUE AND ""Status"" IN (1, 6));
");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "ClassSessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""ClassSessions"" DROP CONSTRAINT IF EXISTS ""EX_ClassSessions_Room_NoOverlap"";
ALTER TABLE ""ClassSessions"" DROP CONSTRAINT IF EXISTS ""EX_ClassSessions_Instructor_NoOverlap"";
");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_ClassSessions_RescheduledFromSessionId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_ClassSessions_RescheduledToSessionId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_People_EffectiveInstructorId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_People_EffectiveSecretaryId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_People_ScheduledInstructorId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_People_ScheduledSecretaryId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_RecurringSchedules_RecurringScheduleId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_Users_CancelledByUserId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_Users_SuspendedByUserId",
                table: "ClassSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringSchedules_People_SecretaryId",
                table: "RecurringSchedules");

            migrationBuilder.DropTable(
                name: "ClassSessionStaffChanges");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSchedules_SecretaryId",
                table: "RecurringSchedules");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_ActivityId_StartsAtUtc",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_CancelledByUserId",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_EffectiveInstructorId_StartsAtUtc",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_EffectiveSecretaryId",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_RescheduledFromSessionId",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_RescheduledToSessionId",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_RoomId_StartsAtUtc",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_ScheduledInstructorId_StartsAtUtc",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_StartsAtUtc",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_Status_StartsAtUtc",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "UX_ClassSessions_Schedule_StartsAt",
                table: "ClassSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassSessions_Cancellation_Other",
                table: "ClassSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassSessions_Cancelled",
                table: "ClassSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassSessions_Capacity",
                table: "ClassSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassSessions_Dates",
                table: "ClassSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassSessions_Rescheduled",
                table: "ClassSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassSessions_Status",
                table: "ClassSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassSessions_Suspended",
                table: "ClassSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassSessions_Suspension_Other",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "SecretaryId",
                table: "RecurringSchedules");

            migrationBuilder.DropColumn(
                name: "CancellationObservations",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "EffectiveInstructorId",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "EffectiveSecretaryId",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "EndsAtUtc",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "IsDetachedFromSchedule",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "RescheduledFromSessionId",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "RescheduledToSessionId",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "StartsAtUtc",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "SuspensionObservations",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "SuspensionReason",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ClassSessions");

            migrationBuilder.RenameColumn(
                name: "SuspendedByUserId",
                table: "ClassSessions",
                newName: "SubstituteInstructorId");

            migrationBuilder.RenameColumn(
                name: "ScheduledSecretaryId",
                table: "ClassSessions",
                newName: "OriginalInstructorId");

            migrationBuilder.RenameColumn(
                name: "ScheduledInstructorId",
                table: "ClassSessions",
                newName: "InstructorId");

            migrationBuilder.RenameColumn(
                name: "Capacity",
                table: "ClassSessions",
                newName: "MaxCapacity");

            migrationBuilder.RenameIndex(
                name: "IX_ClassSessions_SuspendedByUserId",
                table: "ClassSessions",
                newName: "IX_ClassSessions_SubstituteInstructorId");

            migrationBuilder.RenameIndex(
                name: "IX_ClassSessions_ScheduledSecretaryId",
                table: "ClassSessions",
                newName: "IX_ClassSessions_OriginalInstructorId");

            migrationBuilder.AlterColumn<string>(
                name: "CancellationReason",
                table: "ClassSessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Date",
                table: "ClassSessions",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                table: "ClassSessions",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "ClassSessions",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_ActivityId",
                table: "ClassSessions",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_Date",
                table: "ClassSessions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_Date_Status",
                table: "ClassSessions",
                columns: new[] { "Date", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_InstructorId",
                table: "ClassSessions",
                column: "InstructorId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_RecurringScheduleId_Date",
                table: "ClassSessions",
                columns: new[] { "RecurringScheduleId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_RoomId",
                table: "ClassSessions",
                column: "RoomId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_People_InstructorId",
                table: "ClassSessions",
                column: "InstructorId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_People_OriginalInstructorId",
                table: "ClassSessions",
                column: "OriginalInstructorId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_People_SubstituteInstructorId",
                table: "ClassSessions",
                column: "SubstituteInstructorId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_RecurringSchedules_RecurringScheduleId",
                table: "ClassSessions",
                column: "RecurringScheduleId",
                principalTable: "RecurringSchedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
