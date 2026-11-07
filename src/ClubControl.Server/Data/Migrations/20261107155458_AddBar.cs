using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClubControl.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BarCupPriceUzs",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BarProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PriceUzs = table.Column<int>(type: "INTEGER", nullable: false),
                    Stock = table.Column<int>(type: "INTEGER", nullable: false),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BarProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BarSales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BarProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductName = table.Column<string>(type: "TEXT", nullable: false),
                    UnitPriceUzs = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Cups = table.Column<int>(type: "INTEGER", nullable: false),
                    CupPriceUzs = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalUzs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VoidedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SessionExtraId = table.Column<int>(type: "INTEGER", nullable: true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: true),
                    PcId = table.Column<int>(type: "INTEGER", nullable: true),
                    PcLabel = table.Column<string>(type: "TEXT", nullable: false),
                    IsUnpaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnpaidComment = table.Column<string>(type: "TEXT", nullable: false),
                    PaidUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BarSales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BarSales_BarProducts_BarProductId",
                        column: x => x.BarProductId,
                        principalTable: "BarProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BarSales_SessionExtras_SessionExtraId",
                        column: x => x.SessionExtraId,
                        principalTable: "SessionExtras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BarStockMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BarProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Delta = table.Column<int>(type: "INTEGER", nullable: false),
                    StockAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    BarSaleId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BarStockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BarStockMovements_BarProducts_BarProductId",
                        column: x => x.BarProductId,
                        principalTable: "BarProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BarStockMovements_BarSales_BarSaleId",
                        column: x => x.BarSaleId,
                        principalTable: "BarSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BarSales_BarProductId",
                table: "BarSales",
                column: "BarProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BarSales_CreatedUtc",
                table: "BarSales",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BarSales_IsUnpaid_PaidUtc",
                table: "BarSales",
                columns: new[] { "IsUnpaid", "PaidUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BarSales_SessionExtraId",
                table: "BarSales",
                column: "SessionExtraId");

            migrationBuilder.CreateIndex(
                name: "IX_BarStockMovements_BarProductId_CreatedUtc",
                table: "BarStockMovements",
                columns: new[] { "BarProductId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BarStockMovements_BarSaleId",
                table: "BarStockMovements",
                column: "BarSaleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BarStockMovements");

            migrationBuilder.DropTable(
                name: "BarSales");

            migrationBuilder.DropTable(
                name: "BarProducts");

            migrationBuilder.DropColumn(
                name: "BarCupPriceUzs",
                table: "Settings");
        }
    }
}
