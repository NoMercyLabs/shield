using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Feeds
{
    /// <inheritdoc />
    public partial class WidenAdvisoryUniqueKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
