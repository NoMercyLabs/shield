using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddSourceAcl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupMemberships",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMemberships", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "SourceAccesses",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GrantedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceAccesses", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "SourceGroups",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2000,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceGroups", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GroupMemberships_GroupId_UserId",
                table: "GroupMemberships",
                columns: ["GroupId", "UserId"],
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SourceAccesses_SourceId_GroupId",
                table: "SourceAccesses",
                columns: ["SourceId", "GroupId"]
            );

            migrationBuilder.CreateIndex(
                name: "IX_SourceAccesses_SourceId_UserId",
                table: "SourceAccesses",
                columns: ["SourceId", "UserId"]
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GroupMemberships");

            migrationBuilder.DropTable(name: "SourceAccesses");

            migrationBuilder.DropTable(name: "SourceGroups");
        }
    }
}
