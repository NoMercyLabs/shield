using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Feeds
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Advisories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Feed = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    Ecosystem = table.Column<int>(type: "INTEGER", nullable: false),
                    PackageName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 400,
                        nullable: false
                    ),
                    AffectedRangesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Cvss = table.Column<double>(type: "REAL", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ReferencesJson = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Advisories", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "FeedSyncStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Feed = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSuccessAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Cursor = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedSyncStates", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "PackageMetas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ecosystem = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MaintainersJson = table.Column<string>(type: "TEXT", nullable: false),
                    TarballSha = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Deprecated = table.Column<bool>(type: "INTEGER", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageMetas", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Advisories_Ecosystem_PackageName",
                table: "Advisories",
                columns: new[] { "Ecosystem", "PackageName" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Advisories_Feed_ExternalId",
                table: "Advisories",
                columns: new[] { "Feed", "ExternalId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeedSyncStates_Feed",
                table: "FeedSyncStates",
                column: "Feed",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_PackageMetas_Ecosystem_Name_Version",
                table: "PackageMetas",
                columns: new[] { "Ecosystem", "Name", "Version" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Advisories");

            migrationBuilder.DropTable(name: "FeedSyncStates");

            migrationBuilder.DropTable(name: "PackageMetas");
        }
    }
}
