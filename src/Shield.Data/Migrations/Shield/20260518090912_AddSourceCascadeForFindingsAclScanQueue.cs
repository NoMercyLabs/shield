using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddSourceCascadeForFindingsAclScanQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clean orphan rows from prior deletes that didn't cascade. Source/112 surfaced
            // this — Findings, SourceAccesses, and pending ScanQueueEntries kept references
            // to long-gone source ids. Adding the FK constraint over orphans would fail the
            // migration at the SQLite rebuild step, so wipe them first.
            migrationBuilder.Sql(
                "DELETE FROM Findings WHERE SourceId NOT IN (SELECT Id FROM Sources);"
            );
            migrationBuilder.Sql(
                "DELETE FROM ScanQueueEntries WHERE SourceId NOT IN (SELECT Id FROM Sources);"
            );
            migrationBuilder.Sql(
                "DELETE FROM SourceAccesses WHERE SourceId NOT IN (SELECT Id FROM Sources);"
            );
            // Also prune the cross-table downstream: an orphan Finding's InventoryItem row
            // may have already been removed by the snapshot cascade, but the InventoryItem
            // rows themselves are tied to InventorySnapshot (which DOES cascade), so we
            // catch any stragglers via that path too.
            migrationBuilder.Sql(
                "DELETE FROM InventorySnapshots WHERE SourceId NOT IN (SELECT Id FROM Sources);"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Findings_Sources_SourceId",
                table: "Findings",
                column: "SourceId",
                principalTable: "Sources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_ScanQueueEntries_Sources_SourceId",
                table: "ScanQueueEntries",
                column: "SourceId",
                principalTable: "Sources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );

            migrationBuilder.AddForeignKey(
                name: "FK_SourceAccesses_Sources_SourceId",
                table: "SourceAccesses",
                column: "SourceId",
                principalTable: "Sources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Findings_Sources_SourceId",
                table: "Findings"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_ScanQueueEntries_Sources_SourceId",
                table: "ScanQueueEntries"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_SourceAccesses_Sources_SourceId",
                table: "SourceAccesses"
            );
        }
    }
}
