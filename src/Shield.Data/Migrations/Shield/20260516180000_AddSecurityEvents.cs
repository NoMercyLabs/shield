using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shield.Data;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    [DbContext(typeof(ShieldDbContext))]
    [Migration("20260516180000_AddSecurityEvents")]
    public partial class AddSecurityEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecurityEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Jail = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RemoteIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DetailsJson = table.Column<string>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityEvents", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "IpReputations",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ip = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EventCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastJail = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastBannedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastUnbannedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CurrentlyBanned = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpReputations", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_At_Source_Severity",
                table: "SecurityEvents",
                columns: ["At", "Source", "Severity"]
            );

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_RemoteIp",
                table: "SecurityEvents",
                column: "RemoteIp"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_Host",
                table: "SecurityEvents",
                column: "Host"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IpReputations_Ip",
                table: "IpReputations",
                column: "Ip",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_IpReputations_CurrentlyBanned_LastSeenAt",
                table: "IpReputations",
                columns: ["CurrentlyBanned", "LastSeenAt"]
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SecurityEvents");
            migrationBuilder.DropTable(name: "IpReputations");
        }
    }
}
