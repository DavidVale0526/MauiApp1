using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductImages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.AddColumn<byte[]>(name: "ImageData", table: "Products", type: "BLOB", nullable: true);

        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.DropColumn(name: "ImageData", table: "Products");
    }
}
