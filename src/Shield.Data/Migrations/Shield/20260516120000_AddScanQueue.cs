using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddScanQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScanQueueEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnqueuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2000,
                        nullable: true
                    ),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanQueueEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ScanQueueEntries_CompletedAt_StartedAt_EnqueuedAt",
                table: "ScanQueueEntries",
                columns: ["CompletedAt", "StartedAt", "EnqueuedAt"]
            );

            migrationBuilder.CreateIndex(
                name: "IX_ScanQueueEntries_SourceId",
                table: "ScanQueueEntries",
                column: "SourceId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ScanQueueEntries");
        }
    }
}
