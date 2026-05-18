using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Feeds
{
    /// <inheritdoc />
    public partial class RestoreWidenedAdvisoryUniqueKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SyncPendingChanges reverted the widened unique index back to (Feed, ExternalId)
            // because the EntityType configuration drifted. The OSV path fans one vuln id
            // into one Advisory row per affected package, so the narrow shape rejects every
            // fan-out advisory after the first with a UNIQUE constraint violation. Restore
            // the (Feed, ExternalId, Ecosystem, PackageName) shape originally added in
            // 20260516062150_WidenAdvisoryUniqueKey.
            migrationBuilder.DropIndex(name: "IX_Advisories_Feed_ExternalId", table: "Advisories");

            migrationBuilder.CreateIndex(
                name: "IX_Advisories_Feed_ExternalId_Ecosystem_PackageName",
                table: "Advisories",
                columns: ["Feed", "ExternalId", "Ecosystem", "PackageName"],
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Advisories_Feed_ExternalId_Ecosystem_PackageName",
                table: "Advisories"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Advisories_Feed_ExternalId",
                table: "Advisories",
                columns: ["Feed", "ExternalId"],
                unique: true
            );
        }
    }
}
