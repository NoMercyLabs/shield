using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddIntegrationTokenLinkedUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntegrationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AccessTokenEncrypted = table.Column<string>(type: "TEXT", nullable: false),
                    RefreshTokenEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AccountLogin = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    AccountId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Extra = table.Column<string>(type: "TEXT", nullable: true),
                    LinkedUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationTokens", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationTokens_Provider_LinkedUserId",
                table: "IntegrationTokens",
                columns: ["Provider", "LinkedUserId"]
            );

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationTokens_Provider_Subject",
                table: "IntegrationTokens",
                columns: ["Provider", "Subject"]
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IntegrationTokens");
        }
    }
}
