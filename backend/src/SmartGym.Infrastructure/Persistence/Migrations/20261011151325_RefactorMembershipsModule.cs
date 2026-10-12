using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefactorMembershipsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MembershipCreditMovements_Memberships_MembershipId",
                table: "MembershipCreditMovements");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_StudentId",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_StudentId_Status",
                table: "Memberships");

            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAtUtc",
                table: "Memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AppliesToAllActivities",
                table: "Memberships",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Memberships",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "Memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Memberships",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "ARS");

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "Memberships",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DurationDaysSnapshot",
                table: "Memberships",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredNotificationSentAtUtc",
                table: "Memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiringNotificationSentAtUtc",
                table: "Memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExtraCreditsGranted",
                table: "Memberships",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Memberships",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InitialCredits",
                table: "Memberships",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "InitialCreditsLoaded",
                table: "Memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsUnlimited",
                table: "Memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Memberships",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalPrice",
                table: "Memberships",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionId",
                table: "Memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionRedemptionId",
                table: "Memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchasePrice",
                table: "Memberships",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "PurchasedAtUtc",
                table: "Memberships",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "RenewedFromMembershipId",
                table: "Memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MembershipAllowedActivities",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipAllowedActivities", x => new { x.MembershipId, x.ActivityId });
                    table.ForeignKey(
                        name: "FK_MembershipAllowedActivities_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MembershipAllowedActivities_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MembershipStatusChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MembershipStatusChanges_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MembershipStatusChanges_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MembershipSuspensions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReactivatedAtDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExtendExpirationDate = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ExtensionDaysApplied = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SuspendedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SuspendedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReactivatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReactivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActiveSuspension = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipSuspensions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MembershipSuspensions_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MembershipSuspensions_Users_ReactivatedByUserId",
                        column: x => x.ReactivatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MembershipSuspensions_Users_SuspendedByUserId",
                        column: x => x.SuspendedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Migración de datos de membresías existentes (Migration Plan en design.md)
            migrationBuilder.Sql(@"
UPDATE ""Memberships"" m
SET
    ""PurchasedAtUtc"" = m.""CreatedAtUtc"",
    ""ActivatedAtUtc"" = m.""CreatedAtUtc"",
    ""DurationDaysSnapshot"" = GREATEST(1, (m.""EndDate"" - m.""StartDate"") + 1),
    ""InitialCredits"" = CASE WHEN mp.""IsUnlimited"" THEN 0 ELSE GREATEST(0, m.""TotalCredits"") END,
    ""TotalCredits"" = CASE WHEN mp.""IsUnlimited"" THEN 0 ELSE GREATEST(0, m.""TotalCredits"") END,
    ""AvailableCredits"" = CASE WHEN mp.""IsUnlimited"" THEN 0 ELSE GREATEST(0, m.""AvailableCredits"") END,
    ""ExtraCreditsGranted"" = 0,
    ""InitialCreditsLoaded"" = TRUE,
    ""OriginalPrice"" = GREATEST(0, mp.""Price""),
    ""DiscountAmount"" = 0,
    ""PurchasePrice"" = GREATEST(0, mp.""Price""),
    ""Currency"" = 'ARS',
    ""IsUnlimited"" = mp.""IsUnlimited"",
    ""AppliesToAllActivities"" = mp.""AppliesToAllActivities"",
    ""Notes"" = CASE
        WHEN m.""Notes"" IS NULL OR btrim(m.""Notes"") = '' THEN 'Precio estimado desde el plan vigente al migrar'
        ELSE m.""Notes"" || ' | Precio estimado desde el plan vigente al migrar'
    END
FROM ""MembershipPlans"" mp
WHERE m.""MembershipPlanId"" = mp.""Id"";

INSERT INTO ""MembershipAllowedActivities"" (""MembershipId"", ""ActivityId"")
SELECT m.""Id"", mpa.""ActivityId""
FROM ""Memberships"" m
INNER JOIN ""MembershipPlanActivities"" mpa ON mpa.""MembershipPlanId"" = m.""MembershipPlanId""
WHERE m.""AppliesToAllActivities"" = FALSE
ON CONFLICT DO NOTHING;

-- Remapear Status = 5 (Depleted) a Status = 1 (Active)
UPDATE ""Memberships""
SET ""Status"" = 1
WHERE ""Status"" = 5;

-- Insertar movimiento Expiration (Type = 9) por el remanente de las que ya están vencidas (EndDate < hoy local)
INSERT INTO ""MembershipCreditMovements"" (
    ""Id"",
    ""MembershipId"",
    ""StudentId"",
    ""ClassSessionId"",
    ""Type"",
    ""Amount"",
    ""BalanceAfter"",
    ""Reason"",
    ""PerformedByUserId"",
    ""CreatedAtUtc"",
    ""UpdatedAtUtc"",
    ""IsActive""
)
SELECT
    gen_random_uuid(),
    m.""Id"",
    m.""StudentId"",
    NULL,
    9,
    -m.""AvailableCredits"",
    0,
    'Expiración de créditos por vencimiento previo a migración',
    NULL,
    now(),
    NULL,
    TRUE
FROM ""Memberships"" m
WHERE m.""Status"" IN (1, 3)
  AND m.""EndDate"" < (now() AT TIME ZONE 'America/Argentina/San_Juan')::date
  AND m.""IsUnlimited"" = FALSE
  AND m.""AvailableCredits"" > 0;

-- Cerrar las ya vencidas (Status = 2 Expired, AvailableCredits = 0 y marcas de notificación enviadas)
UPDATE ""Memberships""
SET
    ""Status"" = 2,
    ""AvailableCredits"" = 0,
    ""ExpiredNotificationSentAtUtc"" = now(),
    ""ExpiringNotificationSentAtUtc"" = now(),
    ""UpdatedAtUtc"" = COALESCE(""UpdatedAtUtc"", now())
WHERE ""Status"" IN (1, 2, 3)
  AND ""EndDate"" < (now() AT TIME ZONE 'America/Argentina/San_Juan')::date;

-- Marcar aviso enviado para las Active que vencen dentro de los próximos 7 días
UPDATE ""Memberships""
SET ""ExpiringNotificationSentAtUtc"" = now()
WHERE ""Status"" = 1
  AND ""EndDate"" >= (now() AT TIME ZONE 'America/Argentina/San_Juan')::date
  AND ""EndDate"" <= ((now() AT TIME ZONE 'America/Argentina/San_Juan')::date + 7);

-- Poblar CancelledAtUtc y CancellationReason en las canceladas (Status = 4)
UPDATE ""Memberships""
SET
    ""CancelledAtUtc"" = COALESCE(""CancelledAtUtc"", ""UpdatedAtUtc"", ""CreatedAtUtc""),
    ""CancellationReason"" = COALESCE(NULLIF(btrim(""CancellationReason""), ''), 'Cancelación previa a migración')
WHERE ""Status"" = 4;

-- Insertar fila inicial en MembershipStatusChanges por cada membresía existente
INSERT INTO ""MembershipStatusChanges"" (
    ""Id"",
    ""MembershipId"",
    ""PreviousStatus"",
    ""NewStatus"",
    ""Reason"",
    ""Notes"",
    ""ChangedByUserId"",
    ""ChangedAtUtc""
)
SELECT
    gen_random_uuid(),
    m.""Id"",
    NULL,
    m.""Status"",
    'Estado migrado',
    NULL,
    NULL,
    COALESCE(m.""UpdatedAtUtc"", m.""CreatedAtUtc"")
FROM ""Memberships"" m;
");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_CancelledByUserId",
                table: "Memberships",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_CreatedByUserId",
                table: "Memberships",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_PromotionId",
                table: "Memberships",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_PromotionRedemptionId",
                table: "Memberships",
                column: "PromotionRedemptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_PurchasedAtUtc",
                table: "Memberships",
                column: "PurchasedAtUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_RenewedFromMembershipId",
                table: "Memberships",
                column: "RenewedFromMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_Status_EndDate",
                table: "Memberships",
                columns: new[] { "Status", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_Student_Status_EndDate",
                table: "Memberships",
                columns: new[] { "StudentId", "Status", "EndDate" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "UX_Memberships_Student_IdempotencyKey",
                table: "Memberships",
                columns: new[] { "StudentId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Memberships_Cancellation",
                table: "Memberships",
                sql: "((\"Status\" = 4) = (\"CancelledAtUtc\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Memberships_Credits",
                table: "Memberships",
                sql: "\"InitialCredits\" >= 0 AND \"ExtraCreditsGranted\" >= 0 AND \"TotalCredits\" >= 0 AND \"AvailableCredits\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Memberships_Dates",
                table: "Memberships",
                sql: "\"EndDate\" >= \"StartDate\" AND \"DurationDaysSnapshot\" >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Memberships_Prices",
                table: "Memberships",
                sql: "\"OriginalPrice\" >= 0 AND \"DiscountAmount\" >= 0 AND \"PurchasePrice\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Memberships_Status",
                table: "Memberships",
                sql: "\"Status\" IN (0, 1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipAllowedActivities_ActivityId",
                table: "MembershipAllowedActivities",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipStatusChanges_ChangedByUserId",
                table: "MembershipStatusChanges",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipStatusChanges_MembershipId_ChangedAtUtc",
                table: "MembershipStatusChanges",
                columns: new[] { "MembershipId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipSuspensions_MembershipId_IsActiveSuspension",
                table: "MembershipSuspensions",
                columns: new[] { "MembershipId", "IsActiveSuspension" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipSuspensions_ReactivatedByUserId",
                table: "MembershipSuspensions",
                column: "ReactivatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipSuspensions_SuspendedByUserId",
                table: "MembershipSuspensions",
                column: "SuspendedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipCreditMovements_Memberships_MembershipId",
                table: "MembershipCreditMovements",
                column: "MembershipId",
                principalTable: "Memberships",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_Memberships_RenewedFromMembershipId",
                table: "Memberships",
                column: "RenewedFromMembershipId",
                principalTable: "Memberships",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_PromotionRedemptions_PromotionRedemptionId",
                table: "Memberships",
                column: "PromotionRedemptionId",
                principalTable: "PromotionRedemptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_Promotions_PromotionId",
                table: "Memberships",
                column: "PromotionId",
                principalTable: "Promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_Users_CancelledByUserId",
                table: "Memberships",
                column: "CancelledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_Users_CreatedByUserId",
                table: "Memberships",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MembershipCreditMovements_Memberships_MembershipId",
                table: "MembershipCreditMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_Memberships_RenewedFromMembershipId",
                table: "Memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_PromotionRedemptions_PromotionRedemptionId",
                table: "Memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_Promotions_PromotionId",
                table: "Memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_Users_CancelledByUserId",
                table: "Memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_Users_CreatedByUserId",
                table: "Memberships");

            migrationBuilder.DropTable(
                name: "MembershipAllowedActivities");

            migrationBuilder.DropTable(
                name: "MembershipStatusChanges");

            migrationBuilder.DropTable(
                name: "MembershipSuspensions");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_CancelledByUserId",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_CreatedByUserId",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_PromotionId",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_PromotionRedemptionId",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_PurchasedAtUtc",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_RenewedFromMembershipId",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_Status_EndDate",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_Student_Status_EndDate",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "UX_Memberships_Student_IdempotencyKey",
                table: "Memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Memberships_Cancellation",
                table: "Memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Memberships_Credits",
                table: "Memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Memberships_Dates",
                table: "Memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Memberships_Prices",
                table: "Memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Memberships_Status",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "ActivatedAtUtc",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "AppliesToAllActivities",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "DurationDaysSnapshot",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "ExpiredNotificationSentAtUtc",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "ExpiringNotificationSentAtUtc",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "ExtraCreditsGranted",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "InitialCredits",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "InitialCreditsLoaded",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "IsUnlimited",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "OriginalPrice",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "PromotionId",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "PromotionRedemptionId",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "PurchasePrice",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "PurchasedAtUtc",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "RenewedFromMembershipId",
                table: "Memberships");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_StudentId",
                table: "Memberships",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_StudentId_Status",
                table: "Memberships",
                columns: new[] { "StudentId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipCreditMovements_Memberships_MembershipId",
                table: "MembershipCreditMovements",
                column: "MembershipId",
                principalTable: "Memberships",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
