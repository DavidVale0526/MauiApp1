using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryMovementLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    MovementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityMilliUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    OccurredAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.MovementId);
                    table.CheckConstraint("CK_InventoryMovements_LineNumber_NonNegative", "\"LineNumber\" >= 0");
                    table.CheckConstraint("CK_InventoryMovements_Quantity_Positive", "\"QuantityMilliUnits\" > 0");
                    table.ForeignKey(
                        name: "FK_InventoryMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_UserAccounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ActorUserId",
                table: "InventoryMovements",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_OperationId",
                table: "InventoryMovements",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_OperationId_LineNumber",
                table: "InventoryMovements",
                columns: new[] { "OperationId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ProductId_OccurredAtUtc",
                table: "InventoryMovements",
                columns: new[] { "ProductId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryMovements");
        }
    }
}
