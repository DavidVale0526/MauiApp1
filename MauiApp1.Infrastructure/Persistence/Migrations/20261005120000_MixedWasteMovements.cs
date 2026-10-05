using MauiApp1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LocalDbContext))]
[Migration("20261005120000_MixedWasteMovements")]
public sealed class MixedWasteMovements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQLite does not implement ALTER COLUMN. Rebuild the ledger table so
        // mixed-waste lines can have a null ProductId while preserving history.
        migrationBuilder.Sql("""
            CREATE TABLE "ef_temp_InventoryMovements" (
                "MovementId" TEXT NOT NULL CONSTRAINT "PK_InventoryMovements" PRIMARY KEY,
                "OperationId" TEXT NOT NULL,
                "LineNumber" INTEGER NOT NULL,
                "ProductId" TEXT NULL,
                "ProductNameSnapshot" TEXT NOT NULL DEFAULT '',
                "UnitSnapshot" TEXT NOT NULL DEFAULT '',
                "Type" INTEGER NOT NULL,
                "QuantityMilliUnits" INTEGER NOT NULL,
                "Reason" TEXT NULL,
                "OccurredAtUtc" INTEGER NOT NULL,
                "ActorUserId" TEXT NOT NULL,
                CONSTRAINT "CK_InventoryMovements_LineNumber_NonNegative" CHECK ("LineNumber" >= 0),
                CONSTRAINT "CK_InventoryMovements_Quantity_Positive" CHECK ("QuantityMilliUnits" > 0),
                CONSTRAINT "FK_InventoryMovements_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("ProductId") ON DELETE RESTRICT,
                CONSTRAINT "FK_InventoryMovements_UserAccounts_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "UserAccounts" ("UserId") ON DELETE RESTRICT
            );
            INSERT INTO "ef_temp_InventoryMovements"
                ("MovementId", "OperationId", "LineNumber", "ProductId", "ProductNameSnapshot", "UnitSnapshot", "Type", "QuantityMilliUnits", "Reason", "OccurredAtUtc", "ActorUserId")
            SELECT "MovementId", "OperationId", "LineNumber", "ProductId", "ProductNameSnapshot", "UnitSnapshot", "Type", "QuantityMilliUnits", "Reason", "OccurredAtUtc", "ActorUserId"
            FROM "InventoryMovements";
            DROP TABLE "InventoryMovements";
            ALTER TABLE "ef_temp_InventoryMovements" RENAME TO "InventoryMovements";
            CREATE INDEX "IX_InventoryMovements_ActorUserId" ON "InventoryMovements" ("ActorUserId");
            CREATE INDEX "IX_InventoryMovements_OperationId" ON "InventoryMovements" ("OperationId");
            CREATE UNIQUE INDEX "IX_InventoryMovements_OperationId_LineNumber" ON "InventoryMovements" ("OperationId", "LineNumber");
            CREATE INDEX "IX_InventoryMovements_ProductId_OccurredAtUtc" ON "InventoryMovements" ("ProductId", "OccurredAtUtc");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM "InventoryMovements" WHERE "ProductId" IS NULL;
            CREATE TABLE "ef_temp_InventoryMovements" (
                "MovementId" TEXT NOT NULL CONSTRAINT "PK_InventoryMovements" PRIMARY KEY,
                "OperationId" TEXT NOT NULL,
                "LineNumber" INTEGER NOT NULL,
                "ProductId" TEXT NOT NULL,
                "ProductNameSnapshot" TEXT NOT NULL DEFAULT '',
                "UnitSnapshot" TEXT NOT NULL DEFAULT '',
                "Type" INTEGER NOT NULL,
                "QuantityMilliUnits" INTEGER NOT NULL,
                "Reason" TEXT NULL,
                "OccurredAtUtc" INTEGER NOT NULL,
                "ActorUserId" TEXT NOT NULL,
                CONSTRAINT "CK_InventoryMovements_LineNumber_NonNegative" CHECK ("LineNumber" >= 0),
                CONSTRAINT "CK_InventoryMovements_Quantity_Positive" CHECK ("QuantityMilliUnits" > 0),
                CONSTRAINT "FK_InventoryMovements_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("ProductId") ON DELETE RESTRICT,
                CONSTRAINT "FK_InventoryMovements_UserAccounts_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "UserAccounts" ("UserId") ON DELETE RESTRICT
            );
            INSERT INTO "ef_temp_InventoryMovements"
                ("MovementId", "OperationId", "LineNumber", "ProductId", "ProductNameSnapshot", "UnitSnapshot", "Type", "QuantityMilliUnits", "Reason", "OccurredAtUtc", "ActorUserId")
            SELECT "MovementId", "OperationId", "LineNumber", "ProductId", "ProductNameSnapshot", "UnitSnapshot", "Type", "QuantityMilliUnits", "Reason", "OccurredAtUtc", "ActorUserId"
            FROM "InventoryMovements";
            DROP TABLE "InventoryMovements";
            ALTER TABLE "ef_temp_InventoryMovements" RENAME TO "InventoryMovements";
            CREATE INDEX "IX_InventoryMovements_ActorUserId" ON "InventoryMovements" ("ActorUserId");
            CREATE INDEX "IX_InventoryMovements_OperationId" ON "InventoryMovements" ("OperationId");
            CREATE UNIQUE INDEX "IX_InventoryMovements_OperationId_LineNumber" ON "InventoryMovements" ("OperationId", "LineNumber");
            CREATE INDEX "IX_InventoryMovements_ProductId_OccurredAtUtc" ON "InventoryMovements" ("ProductId", "OccurredAtUtc");
            """);
    }
}
