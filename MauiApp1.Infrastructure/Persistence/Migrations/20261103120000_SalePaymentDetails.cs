using MauiApp1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LocalDbContext))]
[Migration("20261103120000_SalePaymentDetails")]
public partial class SalePaymentDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SalePayments",
            columns: table => new
            {
                PaymentId = table.Column<Guid>(type: "TEXT", nullable: false),
                OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                Method = table.Column<int>(type: "INTEGER", nullable: false),
                AmountMinorUnits = table.Column<long>(type: "INTEGER", nullable: false),
                CashReceivedMinorUnits = table.Column<long>(type: "INTEGER", nullable: false),
                ChangeMinorUnits = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalePayments", item => item.PaymentId);
                table.CheckConstraint("CK_SalePayments_Method_Valid", "\"Method\" BETWEEN 1 AND 3");
                table.CheckConstraint("CK_SalePayments_Amount_Positive", "\"AmountMinorUnits\" > 0");
                table.CheckConstraint("CK_SalePayments_Cash_NonNegative", "\"CashReceivedMinorUnits\" >= 0");
                table.CheckConstraint("CK_SalePayments_Change_NonNegative", "\"ChangeMinorUnits\" >= 0");
                table.CheckConstraint("CK_SalePayments_Method_Amounts",
                    "(\"Method\" = 1 AND \"CashReceivedMinorUnits\" >= \"AmountMinorUnits\" AND \"ChangeMinorUnits\" = \"CashReceivedMinorUnits\" - \"AmountMinorUnits\") OR (\"Method\" <> 1 AND \"CashReceivedMinorUnits\" = 0 AND \"ChangeMinorUnits\" = 0)");
                table.ForeignKey("FK_SalePayments_Sales_OperationId", item => item.OperationId,
                    "Sales", "OperationId", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SalePayments_OperationId_Method",
            table: "SalePayments",
            columns: new[] { "OperationId", "Method" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "SalePayments");
}
