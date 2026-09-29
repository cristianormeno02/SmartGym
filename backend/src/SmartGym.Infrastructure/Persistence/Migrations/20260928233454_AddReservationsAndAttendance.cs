using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationsAndAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreditMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsLateCancellation = table.Column<bool>(type: "boolean", nullable: false),
                    RefundCreditMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttendedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckedInByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttendanceSource = table.Column<int>(type: "integer", nullable: true),
                    WaitListPosition = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reservations_ClassSessions_ClassSessionId",
                        column: x => x.ClassSessionId,
                        principalTable: "ClassSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_MembershipCreditMovements_CreditMovementId",
                        column: x => x.CreditMovementId,
                        principalTable: "MembershipCreditMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Reservations_MembershipCreditMovements_RefundCreditMovement~",
                        column: x => x.RefundCreditMovementId,
                        principalTable: "MembershipCreditMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Reservations_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Reservations_People_StudentId",
                        column: x => x.StudentId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ClassSessionId",
                table: "Reservations",
                column: "ClassSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ClassSessionId_Status",
                table: "Reservations",
                columns: new[] { "ClassSessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ClassSessionId_StudentId",
                table: "Reservations",
                columns: new[] { "ClassSessionId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_CreditMovementId",
                table: "Reservations",
                column: "CreditMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_MembershipId",
                table: "Reservations",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_RefundCreditMovementId",
                table: "Reservations",
                column: "RefundCreditMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_StudentId",
                table: "Reservations",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reservations");
        }
    }
}
