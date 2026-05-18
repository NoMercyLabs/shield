using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Feeds
{
    /// <inheritdoc />
    public partial class AddAdvisoryKevEpss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsKev",
                table: "Advisories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "KevAddedAt",
                table: "Advisories",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "KevDueDate",
                table: "Advisories",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.AddColumn<double>(
                name: "EpssScore",
                table: "Advisories",
                type: "REAL",
                nullable: true
            );

            migrationBuilder.AddColumn<double>(
                name: "EpssPercentile",
                table: "Advisories",
                type: "REAL",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsKev", table: "Advisories");
            migrationBuilder.DropColumn(name: "KevAddedAt", table: "Advisories");
            migrationBuilder.DropColumn(name: "KevDueDate", table: "Advisories");
            migrationBuilder.DropColumn(name: "EpssScore", table: "Advisories");
            migrationBuilder.DropColumn(name: "EpssPercentile", table: "Advisories");
        }
    }
}
