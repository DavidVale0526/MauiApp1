using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SaleReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaleReversals",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    RefundMethod = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleReversals", x => x.OperationId);
                    table.CheckConstraint("CK_SaleReversals_Amount_NonNegative", "\"AmountMinorUnits\" >= 0");
                    table.CheckConstraint("CK_SaleReversals_Kind_Valid", "\"Kind\" BETWEEN 1 AND 2");
                    table.CheckConstraint("CK_SaleReversals_RefundMethod_Valid", "\"RefundMethod\" IS NULL OR \"RefundMethod\" BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_SaleReversals_Sales_SaleOperationId",
                        column: x => x.SaleOperationId,
                        principalTable: "Sales",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleReversals_UserAccounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleReversalLines",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    SaleLineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    SaleOperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    UnitSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    QuantityMilliUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    ReturnToStock = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisposalReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleReversalLines", x => new { x.OperationId, x.LineNumber });
                    table.CheckConstraint("CK_SaleReversalLines_Amount_NonNegative", "\"AmountMinorUnits\" >= 0");
                    table.CheckConstraint("CK_SaleReversalLines_Quantity_Positive", "\"QuantityMilliUnits\" > 0");
                    table.ForeignKey(
                        name: "FK_SaleReversalLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleReversalLines_SaleReversals_OperationId",
                        column: x => x.OperationId,
                        principalTable: "SaleReversals",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleReversalLines_Sales_SaleOperationId",
                        column: x => x.SaleOperationId,
                        principalTable: "Sales",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleReversalLines_OperationId_SaleLineNumber",
                table: "SaleReversalLines",
                columns: new[] { "OperationId", "SaleLineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleReversalLines_ProductId",
                table: "SaleReversalLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReversalLines_SaleOperationId_SaleLineNumber",
                table: "SaleReversalLines",
                columns: new[] { "SaleOperationId", "SaleLineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleReversals_ActorUserId",
                table: "SaleReversals",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReversals_SaleOperationId",
                table: "SaleReversals",
                column: "SaleOperationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaleReversalLines");

            migrationBuilder.DropTable(
                name: "SaleReversals");
        }
    }
}
