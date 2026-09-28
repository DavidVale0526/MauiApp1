using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations
{
    public partial class InventoryMovementSnapshots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductNameSnapshot",
                table: "InventoryMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UnitSnapshot",
                table: "InventoryMovements",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE InventoryMovements
                SET ProductNameSnapshot = COALESCE((SELECT Name FROM Products WHERE Products.ProductId = InventoryMovements.ProductId), ''),
                    UnitSnapshot = COALESCE((SELECT Unit FROM Products WHERE Products.ProductId = InventoryMovements.ProductId), '')
                """);

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ProductNameSnapshot", table: "InventoryMovements");
            migrationBuilder.DropColumn(name: "UnitSnapshot", table: "InventoryMovements");
        }
    }
}
