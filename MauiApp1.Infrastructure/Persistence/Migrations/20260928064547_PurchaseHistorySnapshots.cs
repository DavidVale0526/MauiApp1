using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseHistorySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductNameSnapshot",
                table: "PurchaseLines",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UnitSnapshot",
                table: "PurchaseLines",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE PurchaseLines
                SET ProductNameSnapshot = COALESCE((SELECT Name FROM Products WHERE Products.ProductId = PurchaseLines.ProductId), ''),
                    UnitSnapshot = COALESCE((SELECT Unit FROM Products WHERE Products.ProductId = PurchaseLines.ProductId), '')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductNameSnapshot",
                table: "PurchaseLines");

            migrationBuilder.DropColumn(
                name: "UnitSnapshot",
                table: "PurchaseLines");
        }
    }
}
