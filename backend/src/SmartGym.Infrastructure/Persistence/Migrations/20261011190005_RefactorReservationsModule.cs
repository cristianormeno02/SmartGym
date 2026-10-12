using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefactorReservationsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_MembershipCreditMovements_CreditMovementId",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_MembershipCreditMovements_RefundCreditMovement~",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Memberships_MembershipId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_ClassSessionId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_ClassSessionId_StudentId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_StudentId",
                table: "Reservations");

            migrationBuilder.RenameIndex(
                name: "IX_Reservations_ClassSessionId_Status",
                table: "Reservations",
                newName: "IX_Reservations_Session_Status");

            migrationBuilder.AlterColumn<bool>(
                name: "IsLateCancellation",
                table: "Reservations",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddColumn<int>(
                name: "AccessType",
                table: "Reservations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AffectedBySessionChange",
                table: "Reservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CancellationSource",
                table: "Reservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Reservations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPolicyOverride",
                table: "Reservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Reservations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "Reservations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyOverrideReason",
                table: "Reservations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionRedemptionId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedPromotionCode",
                table: "Reservations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RescheduledToSessionId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SingleClassTicketMovementId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WaitlistEnteredAtUtc",
                table: "Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WaitlistOfferExpiresAtUtc",
                table: "Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WaitlistPromotedAtUtc",
                table: "Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReservationId",
                table: "PromotionRedemptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActivityId",
                table: "MembershipCreditMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpiresOn",
                table: "MembershipCreditMovements",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReservationId",
                table: "MembershipCreditMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReservationStatusChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Trigger = table.Column<int>(type: "integer", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservationStatusChanges_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReservationStatusChanges_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Migración de datos de reservas existentes (Migration Plan en design.md)
            migrationBuilder.Sql(@"
-- 1. Fusionar Status = 2 en Status = 1 y poblar ConfirmedAtUtc
UPDATE ""Reservations""
SET
    ""Status"" = 1,
    ""ConfirmedAtUtc"" = COALESCE(""ConfirmedAtUtc"", ""ReservedAtUtc"")
WHERE ""Status"" IN (1, 2);

-- Poblar WaitlistEnteredAtUtc para reservas en lista de espera (Status = 6) y asegurar posición válida
UPDATE ""Reservations""
SET
    ""WaitlistEnteredAtUtc"" = COALESCE(""WaitlistEnteredAtUtc"", ""ReservedAtUtc""),
    ""WaitListPosition"" = COALESCE(GREATEST(1, ""WaitListPosition""), 1)
WHERE ""Status"" = 6;

UPDATE ""Reservations""
SET ""WaitListPosition"" = NULL
WHERE ""Status"" <> 6 AND ""WaitListPosition"" IS NOT NULL;

-- 2. Origin = 3 (Legacy)
UPDATE ""Reservations""
SET ""Origin"" = 3;

-- 3. AccessType: con MembershipId -> 2 si ilimitada, 1 si no; sin MembershipId -> 4 (SingleClassTicket) moviendo CreditMovementId a SingleClassTicketMovementId
UPDATE ""Reservations"" r
SET ""AccessType"" = CASE WHEN m.""IsUnlimited"" THEN 2 ELSE 1 END
FROM ""Memberships"" m
WHERE r.""MembershipId"" = m.""Id"";

UPDATE ""Reservations""
SET
    ""AccessType"" = 4,
    ""SingleClassTicketMovementId"" = ""CreditMovementId"",
    ""CreditMovementId"" = NULL
WHERE ""MembershipId"" IS NULL;

-- Fallback por si alguna fila quedó con AccessType = 0
UPDATE ""Reservations""
SET ""AccessType"" = 1
WHERE ""AccessType"" = 0;

-- 4. Pases reutilizados: conservar el vínculo en la de ReservedAtUtc más antigua dentro de las filas cubiertas por UX_Reservations_Held_AccessMovement
WITH ranked_shared_tickets AS (
    SELECT
        ""Id"",
        ROW_NUMBER() OVER (
            PARTITION BY ""SingleClassTicketMovementId""
            ORDER BY ""ReservedAtUtc"" ASC, ""Id"" ASC
        ) AS rn
    FROM ""Reservations""
    WHERE ""SingleClassTicketMovementId"" IS NOT NULL
      AND ""IsActive"" = TRUE
      AND (""Status"" IN (1, 4, 5, 7) OR (""Status"" = 3 AND ""IsLateCancellation"" = TRUE))
)
UPDATE ""Reservations"" r
SET
    ""SingleClassTicketMovementId"" = NULL,
    ""Notes"" = CASE
        WHEN r.""Notes"" IS NULL OR btrim(r.""Notes"") = '' THEN 'Pase compartido con otra reserva antes de la migración'
        ELSE r.""Notes"" || ' | Pase compartido con otra reserva antes de la migración'
    END
FROM ranked_shared_tickets d
WHERE r.""Id"" = d.""Id"" AND d.rn > 1;

-- 5. Canceladas sin motivo o sin fecha de cancelación
UPDATE ""Reservations""
SET
    ""CancelledAtUtc"" = COALESCE(""CancelledAtUtc"", ""UpdatedAtUtc"", ""ReservedAtUtc""),
    ""CancellationReason"" = COALESCE(NULLIF(btrim(""CancellationReason""), ''), 'Cancelación previa a migración')
WHERE ""Status"" = 3;

-- Limpiar CancelledAtUtc / CancellationReason en no canceladas para cumplir CK_Reservations_Cancellation
UPDATE ""Reservations""
SET
    ""CancelledAtUtc"" = NULL,
    ""CancellationReason"" = NULL
WHERE ""Status"" <> 3 AND (""CancelledAtUtc"" IS NOT NULL OR ""CancellationReason"" IS NOT NULL);

-- 6. MembershipCreditMovements.ReservationId para movimientos tipo 2 (débito) y 3 (devolución)
UPDATE ""MembershipCreditMovements"" cm
SET ""ReservationId"" = sub.""ReservationId""
FROM (
    SELECT DISTINCT ON (r.""CreditMovementId"")
        r.""CreditMovementId"" AS ""MovementId"",
        r.""Id"" AS ""ReservationId""
    FROM ""Reservations"" r
    WHERE r.""CreditMovementId"" IS NOT NULL
    ORDER BY r.""CreditMovementId"", r.""ReservedAtUtc"" ASC, r.""Id"" ASC
) sub
WHERE cm.""Id"" = sub.""MovementId"" AND cm.""Type"" = 2;

UPDATE ""MembershipCreditMovements"" cm
SET ""ReservationId"" = sub.""ReservationId""
FROM (
    SELECT DISTINCT ON (r.""RefundCreditMovementId"")
        r.""RefundCreditMovementId"" AS ""MovementId"",
        r.""Id"" AS ""ReservationId""
    FROM ""Reservations"" r
    WHERE r.""RefundCreditMovementId"" IS NOT NULL
    ORDER BY r.""RefundCreditMovementId"", r.""ReservedAtUtc"" ASC, r.""Id"" ASC
) sub
WHERE cm.""Id"" = sub.""MovementId"" AND cm.""Type"" = 3;

-- 7. Fila inicial en ReservationStatusChanges por cada reserva existente
INSERT INTO ""ReservationStatusChanges"" (
    ""Id"",
    ""ReservationId"",
    ""PreviousStatus"",
    ""NewStatus"",
    ""Reason"",
    ""ChangedByUserId"",
    ""Trigger"",
    ""ChangedAtUtc""
)
SELECT
    gen_random_uuid(),
    r.""Id"",
    NULL,
    r.""Status"",
    'Estado migrado',
    NULL,
    8,
    COALESCE(r.""UpdatedAtUtc"", r.""ReservedAtUtc"", r.""CreatedAtUtc"")
FROM ""Reservations"" r;

-- 8. Quitar valores por defecto temporales de Origin y AccessType
ALTER TABLE ""Reservations"" ALTER COLUMN ""Origin"" DROP DEFAULT;
ALTER TABLE ""Reservations"" ALTER COLUMN ""AccessType"" DROP DEFAULT;
");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_CancelledByUserId",
                table: "Reservations",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_CheckedInByUserId",
                table: "Reservations",
                column: "CheckedInByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_CreatedByUserId",
                table: "Reservations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PaymentId",
                table: "Reservations",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PendingOffers",
                table: "Reservations",
                column: "WaitlistOfferExpiresAtUtc",
                filter: "\"Status\" = 7 AND \"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_RescheduledToSessionId",
                table: "Reservations",
                column: "RescheduledToSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Session_Waitlist",
                table: "Reservations",
                columns: new[] { "ClassSessionId", "WaitListPosition" },
                filter: "\"Status\" = 6 AND \"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Student_Status_ReservedAt",
                table: "Reservations",
                columns: new[] { "StudentId", "Status", "ReservedAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_Active_Session_Student",
                table: "Reservations",
                columns: new[] { "ClassSessionId", "StudentId" },
                unique: true,
                filter: "\"Status\" IN (1, 6, 7) AND \"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_Held_AccessMovement",
                table: "Reservations",
                column: "SingleClassTicketMovementId",
                unique: true,
                filter: "\"SingleClassTicketMovementId\" IS NOT NULL AND \"IsActive\" AND (\"Status\" IN (1, 4, 5, 7) OR (\"Status\" = 3 AND \"IsLateCancellation\"))");

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_PromotionRedemption",
                table: "Reservations",
                column: "PromotionRedemptionId",
                unique: true,
                filter: "\"PromotionRedemptionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_Student_IdempotencyKey",
                table: "Reservations",
                columns: new[] { "StudentId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_AccessType",
                table: "Reservations",
                sql: "\"AccessType\" IN (1, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_Cancellation",
                table: "Reservations",
                sql: "((\"Status\" = 3) = (\"CancelledAtUtc\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_Origin",
                table: "Reservations",
                sql: "\"Origin\" IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_PendingOffer",
                table: "Reservations",
                sql: "((\"Status\" = 7) = (\"WaitlistOfferExpiresAtUtc\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_PolicyOverride",
                table: "Reservations",
                sql: "(NOT \"IsPolicyOverride\" OR \"PolicyOverrideReason\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_Status",
                table: "Reservations",
                sql: "\"Status\" IN (1, 3, 4, 5, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_WaitlistPosition",
                table: "Reservations",
                sql: "((\"Status\" = 6) = (\"WaitListPosition\" IS NOT NULL AND \"WaitListPosition\" >= 1))");

            migrationBuilder.CreateIndex(
                name: "UX_PromotionRedemptions_Reservation",
                table: "PromotionRedemptions",
                column: "ReservationId",
                unique: true,
                filter: "\"ReservationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipCreditMovements_ActivityId",
                table: "MembershipCreditMovements",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "UX_CreditMovements_Reservation_Type",
                table: "MembershipCreditMovements",
                columns: new[] { "ReservationId", "Type" },
                unique: true,
                filter: "\"ReservationId\" IS NOT NULL AND \"Type\" IN (2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationStatusChanges_ChangedByUserId",
                table: "ReservationStatusChanges",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationStatusChanges_ReservationId_ChangedAtUtc",
                table: "ReservationStatusChanges",
                columns: new[] { "ReservationId", "ChangedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipCreditMovements_Activities_ActivityId",
                table: "MembershipCreditMovements",
                column: "ActivityId",
                principalTable: "Activities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipCreditMovements_Reservations_ReservationId",
                table: "MembershipCreditMovements",
                column: "ReservationId",
                principalTable: "Reservations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PromotionRedemptions_Reservations_ReservationId",
                table: "PromotionRedemptions",
                column: "ReservationId",
                principalTable: "Reservations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_ClassSessions_RescheduledToSessionId",
                table: "Reservations",
                column: "RescheduledToSessionId",
                principalTable: "ClassSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_MembershipCreditMovements_CreditMovementId",
                table: "Reservations",
                column: "CreditMovementId",
                principalTable: "MembershipCreditMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_MembershipCreditMovements_RefundCreditMovement~",
                table: "Reservations",
                column: "RefundCreditMovementId",
                principalTable: "MembershipCreditMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_MembershipCreditMovements_SingleClassTicketMov~",
                table: "Reservations",
                column: "SingleClassTicketMovementId",
                principalTable: "MembershipCreditMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Memberships_MembershipId",
                table: "Reservations",
                column: "MembershipId",
                principalTable: "Memberships",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Payments_PaymentId",
                table: "Reservations",
                column: "PaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_PromotionRedemptions_PromotionRedemptionId",
                table: "Reservations",
                column: "PromotionRedemptionId",
                principalTable: "PromotionRedemptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Users_CancelledByUserId",
                table: "Reservations",
                column: "CancelledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Users_CheckedInByUserId",
                table: "Reservations",
                column: "CheckedInByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Users_CreatedByUserId",
                table: "Reservations",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MembershipCreditMovements_Activities_ActivityId",
                table: "MembershipCreditMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipCreditMovements_Reservations_ReservationId",
                table: "MembershipCreditMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_PromotionRedemptions_Reservations_ReservationId",
                table: "PromotionRedemptions");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_ClassSessions_RescheduledToSessionId",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_MembershipCreditMovements_CreditMovementId",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_MembershipCreditMovements_RefundCreditMovement~",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_MembershipCreditMovements_SingleClassTicketMov~",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Memberships_MembershipId",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Payments_PaymentId",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_PromotionRedemptions_PromotionRedemptionId",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Users_CancelledByUserId",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Users_CheckedInByUserId",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Users_CreatedByUserId",
                table: "Reservations");

            migrationBuilder.DropTable(
                name: "ReservationStatusChanges");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_CancelledByUserId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_CheckedInByUserId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_CreatedByUserId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_PaymentId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_PendingOffers",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_RescheduledToSessionId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_Session_Waitlist",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_Student_Status_ReservedAt",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "UX_Reservations_Active_Session_Student",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "UX_Reservations_Held_AccessMovement",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "UX_Reservations_PromotionRedemption",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "UX_Reservations_Student_IdempotencyKey",
                table: "Reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_AccessType",
                table: "Reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_Cancellation",
                table: "Reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_Origin",
                table: "Reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_PendingOffer",
                table: "Reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_PolicyOverride",
                table: "Reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_Status",
                table: "Reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_WaitlistPosition",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "UX_PromotionRedemptions_Reservation",
                table: "PromotionRedemptions");

            migrationBuilder.DropIndex(
                name: "IX_MembershipCreditMovements_ActivityId",
                table: "MembershipCreditMovements");

            migrationBuilder.DropIndex(
                name: "UX_CreditMovements_Reservation_Type",
                table: "MembershipCreditMovements");

            migrationBuilder.DropColumn(
                name: "AccessType",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "AffectedBySessionChange",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CancellationSource",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "IsPolicyOverride",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "PolicyOverrideReason",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "PromotionRedemptionId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RequestedPromotionCode",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RescheduledToSessionId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "SingleClassTicketMovementId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "WaitlistEnteredAtUtc",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "WaitlistOfferExpiresAtUtc",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "WaitlistPromotedAtUtc",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "PromotionRedemptions");

            migrationBuilder.DropColumn(
                name: "ActivityId",
                table: "MembershipCreditMovements");

            migrationBuilder.DropColumn(
                name: "ExpiresOn",
                table: "MembershipCreditMovements");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "MembershipCreditMovements");

            migrationBuilder.RenameIndex(
                name: "IX_Reservations_Session_Status",
                table: "Reservations",
                newName: "IX_Reservations_ClassSessionId_Status");

            migrationBuilder.AlterColumn<bool>(
                name: "IsLateCancellation",
                table: "Reservations",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ClassSessionId",
                table: "Reservations",
                column: "ClassSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ClassSessionId_StudentId",
                table: "Reservations",
                columns: new[] { "ClassSessionId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_StudentId",
                table: "Reservations",
                column: "StudentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_MembershipCreditMovements_CreditMovementId",
                table: "Reservations",
                column: "CreditMovementId",
                principalTable: "MembershipCreditMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_MembershipCreditMovements_RefundCreditMovement~",
                table: "Reservations",
                column: "RefundCreditMovementId",
                principalTable: "MembershipCreditMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Memberships_MembershipId",
                table: "Reservations",
                column: "MembershipId",
                principalTable: "Memberships",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
