using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubControl.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBarShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaidShiftId",
                table: "BarSales",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShiftId",
                table: "BarSales",
                type: "INTEGER",
                nullable: true);

            // Bar sales made before this migration: give each the shift that was open at the time (the dates are
            // stored in one text format, so comparing them as text is comparing them as times). Sales older than
            // the first shift, such as imported history, stay without a shift.
            migrationBuilder.Sql(
                @"UPDATE BarSales SET ShiftId = (
                      SELECT s.Id FROM Shifts s
                      WHERE s.OpenedUtc <= BarSales.CreatedUtc AND (s.ClosedUtc IS NULL OR s.ClosedUtc > BarSales.CreatedUtc)
                      ORDER BY s.Id DESC LIMIT 1)");
            migrationBuilder.Sql(
                @"UPDATE BarSales SET PaidShiftId = (
                      SELECT s.Id FROM Shifts s
                      WHERE s.OpenedUtc <= BarSales.PaidUtc AND (s.ClosedUtc IS NULL OR s.ClosedUtc > BarSales.PaidUtc)
                      ORDER BY s.Id DESC LIMIT 1)
                  WHERE PaidUtc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BarSales_PaidShiftId",
                table: "BarSales",
                column: "PaidShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_BarSales_ShiftId",
                table: "BarSales",
                column: "ShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BarSales_PaidShiftId",
                table: "BarSales");

            migrationBuilder.DropIndex(
                name: "IX_BarSales_ShiftId",
                table: "BarSales");

            migrationBuilder.DropColumn(
                name: "PaidShiftId",
                table: "BarSales");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "BarSales");
        }
    }
}
