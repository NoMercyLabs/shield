using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceGroupIdsCsv = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1024,
                        nullable: false
                    ),
                    Token = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResendCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invites", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Invites_AcceptedAt_RevokedAt_ExpiresAt",
                table: "Invites",
                columns: ["AcceptedAt", "RevokedAt", "ExpiresAt"]
            );

            migrationBuilder.CreateIndex(
                name: "IX_Invites_Token",
                table: "Invites",
                column: "Token",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Invites");
        }
    }
}
