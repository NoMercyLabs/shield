using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddInventoryItemManifestPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable — existing rows get null. The next scan repopulates ManifestPath for
            // every item it writes; there is no backfill needed or attempted here.
            migrationBuilder.AddColumn<string>(
                name: "ManifestPath",
                table: "InventoryItems",
                type: "TEXT",
                maxLength: 1000,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ManifestPath", table: "InventoryItems");
        }
    }
}
