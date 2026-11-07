using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubControl.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPcTariffForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Until now nothing stopped a PC from pointing at a tariff that was gone. SQLite would refuse the new foreign key
            // on such a row, so first make that PC "new" again (no number/tariff): the admin picks a tariff for it in Settings.
            migrationBuilder.Sql(
                "UPDATE Pcs SET TariffId = NULL WHERE TariffId IS NOT NULL AND TariffId NOT IN (SELECT Id FROM Tariffs);");

            migrationBuilder.CreateIndex(
                name: "IX_Pcs_TariffId",
                table: "Pcs",
                column: "TariffId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pcs_Tariffs_TariffId",
                table: "Pcs",
                column: "TariffId",
                principalTable: "Tariffs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pcs_Tariffs_TariffId",
                table: "Pcs");

            migrationBuilder.DropIndex(
                name: "IX_Pcs_TariffId",
                table: "Pcs");
        }
    }
}
