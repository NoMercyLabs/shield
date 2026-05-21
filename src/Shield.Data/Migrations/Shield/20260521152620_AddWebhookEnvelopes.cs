using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddWebhookEnvelopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookEnvelopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EndpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DeliveryId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    HeadersJson = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    SignatureValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEnvelopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookEnvelopes_WebhookEndpoints_EndpointId",
                        column: x => x.EndpointId,
                        principalTable: "WebhookEndpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEnvelopes_EndpointId_ReceivedAt",
                table: "WebhookEnvelopes",
                columns: new[] { "EndpointId", "ReceivedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEnvelopes_ReceivedAt",
                table: "WebhookEnvelopes",
                column: "ReceivedAt"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WebhookEnvelopes");
        }
    }
}
