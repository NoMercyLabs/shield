using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddSourceManualCooldownAndPkgAge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastManualBulkApplyAt",
                table: "Sources",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "MinPackageAgeHours",
                table: "Sources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 48
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LastManualBulkApplyAt", table: "Sources");

            migrationBuilder.DropColumn(name: "MinPackageAgeHours", table: "Sources");
        }
    }
}
