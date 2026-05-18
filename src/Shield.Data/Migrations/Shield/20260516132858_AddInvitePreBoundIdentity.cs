using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shield.Data.Migrations.Shield
{
    /// <inheritdoc />
    public partial class AddInvitePreBoundIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreBoundEmail",
                table: "Invites",
                type: "TEXT",
                maxLength: 320,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PreBoundLogin",
                table: "Invites",
                type: "TEXT",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PreBoundProvider",
                table: "Invites",
                type: "TEXT",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PreBoundSubjectId",
                table: "Invites",
                type: "TEXT",
                maxLength: 128,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PreBoundEmail", table: "Invites");

            migrationBuilder.DropColumn(name: "PreBoundLogin", table: "Invites");

            migrationBuilder.DropColumn(name: "PreBoundProvider", table: "Invites");

            migrationBuilder.DropColumn(name: "PreBoundSubjectId", table: "Invites");
        }
    }
}
