using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Feeds
{
    /// <inheritdoc />
    public partial class AddFeedSyncRateLimitResetAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RateLimitResetAt",
                table: "FeedSyncStates",
                type: "TEXT",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RateLimitResetAt", table: "FeedSyncStates");
        }
    }
}
