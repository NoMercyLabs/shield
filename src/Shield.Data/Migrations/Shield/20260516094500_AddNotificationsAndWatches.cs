using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddNotificationsAndWatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    RelatedType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    RelatedId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "PackageWatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ecosystem = table.Column<int>(type: "INTEGER", nullable: false),
                    PackageName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 400,
                        nullable: false
                    ),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageWatches", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "SavedFilters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    QueryJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedFilters", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications",
                column: "CreatedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ReadAt_CreatedAt",
                table: "Notifications",
                columns: ["UserId", "ReadAt", "CreatedAt"]
            );

            migrationBuilder.CreateIndex(
                name: "IX_PackageWatches_UserId_Ecosystem_PackageName",
                table: "PackageWatches",
                columns: ["UserId", "Ecosystem", "PackageName"],
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SavedFilters_UserId_Kind_CreatedAt",
                table: "SavedFilters",
                columns: ["UserId", "Kind", "CreatedAt"]
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Notifications");

            migrationBuilder.DropTable(name: "PackageWatches");

            migrationBuilder.DropTable(name: "SavedFilters");
        }
    }
}
