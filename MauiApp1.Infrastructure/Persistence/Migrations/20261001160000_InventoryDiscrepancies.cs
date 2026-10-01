using MauiApp1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LocalDbContext))]
[Migration("20261001160000_InventoryDiscrepancies")]
public sealed class InventoryDiscrepancies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LastError",
            table: "OutboxMessages",
            type: "TEXT",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "DeviceSyncStates",
            columns: table => new
            {
                DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                LastSequence = table.Column<long>(type: "INTEGER", nullable: false),
                LastSuccessfulSyncAtUtc = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_DeviceSyncStates", x => x.DeviceId));

        migrationBuilder.CreateTable(
            name: "DeviceSyncConflicts",
            columns: table => new
            {
                ConflictId = table.Column<Guid>(type: "TEXT", nullable: false),
                CoordinatorSequence = table.Column<long>(type: "INTEGER", nullable: false),
                OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                OperationType = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                Message = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                IsResolved = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DeviceSyncConflicts", x => x.ConflictId));
        migrationBuilder.CreateIndex(name: "IX_DeviceSyncConflicts_OperationId", table: "DeviceSyncConflicts", column: "OperationId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_DeviceSyncConflicts_IsResolved_CoordinatorSequence", table: "DeviceSyncConflicts", columns: new[] { "IsResolved", "CoordinatorSequence" });

        migrationBuilder.CreateTable(
            name: "InventoryDiscrepancies",
            columns: table => new
            {
                DiscrepancyId = table.Column<Guid>(type: "TEXT", nullable: false),
                OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                ShortageMilliUnits = table.Column<long>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                IsResolved = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventoryDiscrepancies", x => x.DiscrepancyId);
                table.CheckConstraint("CK_InventoryDiscrepancies_Shortage_Positive", "\"ShortageMilliUnits\" > 0");
                table.ForeignKey("FK_InventoryDiscrepancies_Products_ProductId", x => x.ProductId,
                    "Products", "ProductId", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InventoryDiscrepancies_UserAccounts_ActorUserId", x => x.ActorUserId,
                    "UserAccounts", "UserId", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryDiscrepancies_ProductId_IsResolved_CreatedAtUtc",
            table: "InventoryDiscrepancies",
            columns: new[] { "ProductId", "IsResolved", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_InventoryDiscrepancies_OperationId_ProductId",
            table: "InventoryDiscrepancies",
            columns: new[] { "OperationId", "ProductId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_InventoryDiscrepancies_ActorUserId",
            table: "InventoryDiscrepancies",
            column: "ActorUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "InventoryDiscrepancies");
        migrationBuilder.DropTable(name: "DeviceSyncStates");
        migrationBuilder.DropTable(name: "DeviceSyncConflicts");
        migrationBuilder.DropColumn(name: "LastError", table: "OutboxMessages");
    }
}
