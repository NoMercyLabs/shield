using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddPackageUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackageUpdates",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    InventoryItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    Ecosystem = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    CurrentVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: false
                    ),
                    LatestVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 80,
                        nullable: false
                    ),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsBreakingMajor = table.Column<bool>(type: "INTEGER", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AppliedPullRequestUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageUpdates", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PackageUpdates_SourceId_AppliedAt",
                table: "PackageUpdates",
                columns: new[] { "SourceId", "AppliedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PackageUpdates_SourceId_Ecosystem_Name",
                table: "PackageUpdates",
                columns: new[] { "SourceId", "Ecosystem", "Name" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PackageUpdates");
        }
    }
}
