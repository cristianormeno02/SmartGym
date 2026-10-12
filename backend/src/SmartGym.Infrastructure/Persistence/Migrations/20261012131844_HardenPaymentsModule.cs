using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenPaymentsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_ClassSessions_ClassSessionId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Memberships_MembershipId",
                table: "Payments");

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAtUtc",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "ARS");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Provider",
                table: "Payments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "Payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // "xmin" es una columna de sistema de PostgreSQL: no se agrega.

            // Consulta de control previa a producción: el Refund() anterior no validaba el estado, así que un pago
            // "Refunded" pudo no haberse cobrado nunca. La migración los trata como cobrados y devueltos por el total.
            //   SELECT "Id", "Amount", "Notes" FROM "Payments" WHERE "Status" = 4;
            migrationBuilder.Sql(@"
-- 0. Datos que impedirían crear las restricciones: se corrigen a mano antes de migrar
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ""Payments"" WHERE ""MembershipId"" IS NOT NULL AND ""ClassSessionId"" IS NOT NULL) THEN
        RAISE EXCEPTION 'HardenPaymentsModule: hay pagos vinculados a una membresía y a una clase a la vez.';
    END IF;
    IF EXISTS (SELECT 1 FROM ""Payments"" WHERE ""Amount"" <= 0) THEN
        RAISE EXCEPTION 'HardenPaymentsModule: hay pagos con importe menor o igual a cero.';
    END IF;
END $$;

-- 1. Los pagos confirmados (o confirmados y luego devueltos) toman su fecha de confirmación de PaidAtUtc
UPDATE ""Payments""
SET ""ConfirmedAtUtc"" = ""PaidAtUtc""
WHERE ""Status"" IN (2, 4);

-- 2. El estado Refunded anterior era siempre una devolución total
UPDATE ""Payments""
SET ""RefundedAmount"" = ""Amount""
WHERE ""Status"" = 4;

-- 3. La moneda de un pago de membresía es la de la membresía
UPDATE ""Payments"" p
SET ""Currency"" = m.""Currency""
FROM ""Memberships"" m
WHERE p.""MembershipId"" = m.""Id"";

-- Method = 5 (antes MercadoPago) pasa a significar DigitalWallet y Provider queda en Manual (1):
-- esos pagos se confirmaron a mano y no tienen una transacción de pasarela.
");

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => x.Id);
                    table.CheckConstraint("CK_PaymentRefunds_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(@"
-- 4. Cada devolución total anterior queda representada por una fila, sin responsable conocido
INSERT INTO ""PaymentRefunds"" (""Id"", ""PaymentId"", ""Amount"", ""Currency"", ""Reason"", ""RequestedByUserId"", ""CreatedAtUtc"", ""IsActive"")
SELECT gen_random_uuid(), p.""Id"", p.""Amount"", p.""Currency"", 'Devolución registrada antes de la migración', NULL,
       COALESCE(p.""UpdatedAtUtc"", p.""PaidAtUtc""), TRUE
FROM ""Payments"" p
WHERE p.""Status"" = 4;
");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ConfirmedAtUtc",
                table: "Payments",
                column: "ConfirmedAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_Payments_Payer_IdempotencyKey",
                table: "Payments",
                columns: new[] { "PayerPersonId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Amounts",
                table: "Payments",
                sql: "\"Amount\" > 0 AND \"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"Amount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_SingleTarget",
                table: "Payments",
                sql: "\"MembershipId\" IS NULL OR \"ClassSessionId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_CreatedAtUtc",
                table: "PaymentRefunds",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentRefunds_Payment_IdempotencyKey",
                table: "PaymentRefunds",
                columns: new[] { "PaymentId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_ClassSessions_ClassSessionId",
                table: "Payments",
                column: "ClassSessionId",
                principalTable: "ClassSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Memberships_MembershipId",
                table: "Payments",
                column: "MembershipId",
                principalTable: "Memberships",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_ClassSessions_ClassSessionId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Memberships_MembershipId",
                table: "Payments");

            // Los estados nuevos no existen en el modelo anterior. El detalle de las devoluciones parciales se pierde.
            migrationBuilder.Sql(@"
UPDATE ""Payments"" SET ""Status"" = 2 WHERE ""Status"" = 6;
UPDATE ""Payments"" SET ""Status"" = 3 WHERE ""Status"" = 5;
");

            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ConfirmedAtUtc",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "UX_Payments_Payer_IdempotencyKey",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Amounts",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_SingleTarget",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ConfirmedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "Payments");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_ClassSessions_ClassSessionId",
                table: "Payments",
                column: "ClassSessionId",
                principalTable: "ClassSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Memberships_MembershipId",
                table: "Payments",
                column: "MembershipId",
                principalTable: "Memberships",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
