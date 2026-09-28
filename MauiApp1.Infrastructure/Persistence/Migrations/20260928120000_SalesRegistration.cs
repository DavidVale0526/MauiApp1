using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations
{
    public partial class SalesRegistration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TotalMinorUnits = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.OperationId);
                    table.CheckConstraint("CK_Sales_Total_NonNegative", "\"TotalMinorUnits\" >= 0");
                    table.ForeignKey(
                        name: "FK_Sales_UserAccounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleLines",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    UnitSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    QuantityMilliUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    SalePriceMinorUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    LineTotalMinorUnits = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLines", x => new { x.OperationId, x.LineNumber });
                    table.CheckConstraint("CK_SaleLines_LineNumber_NonNegative", "\"LineNumber\" >= 0");
                    table.CheckConstraint("CK_SaleLines_Price_NonNegative", "\"SalePriceMinorUnits\" >= 0");
                    table.CheckConstraint("CK_SaleLines_Quantity_Positive", "\"QuantityMilliUnits\" > 0");
                    table.CheckConstraint("CK_SaleLines_Total_NonNegative", "\"LineTotalMinorUnits\" >= 0");
                    table.ForeignKey(
                        name: "FK_SaleLines_Sales_OperationId",
                        column: x => x.OperationId,
                        principalTable: "Sales",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ActorUserId",
                table: "Sales",
                column: "ActorUserId");
            migrationBuilder.CreateIndex(
                name: "IX_Sales_OccurredAtUtc",
                table: "Sales",
                column: "OccurredAtUtc");
            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_ProductId",
                table: "SaleLines",
                column: "ProductId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SaleLines");
            migrationBuilder.DropTable(name: "Sales");
        }
    }
}
