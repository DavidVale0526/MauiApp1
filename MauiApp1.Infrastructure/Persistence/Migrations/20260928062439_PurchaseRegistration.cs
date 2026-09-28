using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Purchases",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Purchases", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_Purchases_UserAccounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseLines",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityMilliUnits = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseLines", x => new { x.OperationId, x.LineNumber });
                    table.CheckConstraint("CK_PurchaseLines_LineNumber_NonNegative", "\"LineNumber\" >= 0");
                    table.CheckConstraint("CK_PurchaseLines_Quantity_Positive", "\"QuantityMilliUnits\" > 0");
                    table.ForeignKey(
                        name: "FK_PurchaseLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseLines_Purchases_OperationId",
                        column: x => x.OperationId,
                        principalTable: "Purchases",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseLines_ProductId",
                table: "PurchaseLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_ActorUserId",
                table: "Purchases",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_OccurredAtUtc",
                table: "Purchases",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseLines");

            migrationBuilder.DropTable(
                name: "Purchases");
        }
    }
}
