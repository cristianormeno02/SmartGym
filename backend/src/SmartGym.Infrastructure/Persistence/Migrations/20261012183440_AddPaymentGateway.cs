using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckoutUrl",
                table: "Payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                table: "Payments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "PaymentRefunds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalRefundId",
                table: "PaymentRefunds",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "PaymentRefunds",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "PaymentRefunds",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            // Las devoluciones existentes son todas de pagos manuales: quedaron efectivas al registrarse.
            migrationBuilder.Sql(@"
UPDATE ""PaymentRefunds""
SET ""CompletedAtUtc"" = ""CreatedAtUtc""
WHERE ""CompletedAtUtc"" IS NULL;
");

            migrationBuilder.CreateTable(
                name: "PaymentDiscrepancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InternalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    InternalCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    InternalStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ExternalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ExternalCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ExternalStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ExternalTransactionId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentDiscrepancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentDiscrepancies_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ExternalEventId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ExternalTransactionId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentWebhookEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentWebhookEvents_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Payments_OpenCheckout",
                table: "Payments",
                column: "MembershipId",
                unique: true,
                filter: "\"Provider\" <> 1 AND \"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Payments_Provider_ExternalTx",
                table: "Payments",
                columns: new[] { "Provider", "ExternalTransactionId" },
                unique: true,
                filter: "\"Provider\" <> 1 AND \"ExternalTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_CompletedAtUtc",
                table: "PaymentRefunds",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentDiscrepancies_Status",
                table: "PaymentDiscrepancies",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentDiscrepancies_Open_Payment",
                table: "PaymentDiscrepancies",
                columns: new[] { "PaymentId", "Type" },
                unique: true,
                filter: "\"Status\" = 1 AND \"PaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentDiscrepancies_Open_Unknown",
                table: "PaymentDiscrepancies",
                columns: new[] { "Provider", "ExternalTransactionId", "Type" },
                unique: true,
                filter: "\"Status\" = 1 AND \"PaymentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_PaymentId",
                table: "PaymentWebhookEvents",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentWebhookEvents_Provider_ExternalEventId",
                table: "PaymentWebhookEvents",
                columns: new[] { "Provider", "ExternalEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentDiscrepancies");

            migrationBuilder.DropTable(
                name: "PaymentWebhookEvents");

            migrationBuilder.DropIndex(
                name: "UX_Payments_OpenCheckout",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "UX_Payments_Provider_ExternalTx",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRefunds_CompletedAtUtc",
                table: "PaymentRefunds");

            migrationBuilder.DropColumn(
                name: "CheckoutUrl",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "PaymentRefunds");

            migrationBuilder.DropColumn(
                name: "ExternalRefundId",
                table: "PaymentRefunds");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "PaymentRefunds");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PaymentRefunds");
        }
    }
}
