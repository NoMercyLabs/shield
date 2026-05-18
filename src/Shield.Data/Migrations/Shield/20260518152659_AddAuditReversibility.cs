using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddAuditReversibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AfterJson",
                table: "AuditEntries",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "BeforeJson",
                table: "AuditEntries",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsReversible",
                table: "AuditEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedAt",
                table: "AuditEntries",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ReversedByEntryId",
                table: "AuditEntries",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ReversedByEntryId",
                table: "AuditEntries",
                column: "ReversedByEntryId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_AuditEntries_AuditEntries_ReversedByEntryId",
                table: "AuditEntries",
                column: "ReversedByEntryId",
                principalTable: "AuditEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditEntries_AuditEntries_ReversedByEntryId",
                table: "AuditEntries"
            );

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_ReversedByEntryId",
                table: "AuditEntries"
            );

            migrationBuilder.DropColumn(name: "AfterJson", table: "AuditEntries");

            migrationBuilder.DropColumn(name: "BeforeJson", table: "AuditEntries");

            migrationBuilder.DropColumn(name: "IsReversible", table: "AuditEntries");

            migrationBuilder.DropColumn(name: "ReversedAt", table: "AuditEntries");

            migrationBuilder.DropColumn(name: "ReversedByEntryId", table: "AuditEntries");
        }
    }
}
