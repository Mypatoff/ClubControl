using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubControl.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPcAuthTokenHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthTokenHash",
                table: "Pcs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthTokenHash",
                table: "Pcs");
        }
    }
}
