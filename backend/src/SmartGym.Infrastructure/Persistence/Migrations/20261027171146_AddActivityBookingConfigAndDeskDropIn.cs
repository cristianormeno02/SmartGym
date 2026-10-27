using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityBookingConfigAndDeskDropIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttendanceRevocationNotes",
                table: "Reservations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttendanceRevocationReason",
                table: "Reservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceRevokedAtUtc",
                table: "Reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AttendanceRevokedByUserId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDropIn",
                table: "Reservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSingleClassPass",
                table: "MembershipPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresBooking",
                table: "Activities",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_AttendanceRevokedByUserId",
                table: "Reservations",
                column: "AttendanceRevokedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Users_AttendanceRevokedByUserId",
                table: "Reservations",
                column: "AttendanceRevokedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Users_AttendanceRevokedByUserId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_AttendanceRevokedByUserId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "AttendanceRevocationNotes",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "AttendanceRevocationReason",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "AttendanceRevokedAtUtc",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "AttendanceRevokedByUserId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "IsDropIn",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "IsSingleClassPass",
                table: "MembershipPlans");

            migrationBuilder.DropColumn(
                name: "RequiresBooking",
                table: "Activities");
        }
    }
}
