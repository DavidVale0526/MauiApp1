using MauiApp1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LocalDbContext))]
[Migration("20261001090000_CoordinatorUserMapping")]
public sealed class CoordinatorUserMapping : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CoordinatorUserId",
            table: "UserAccounts",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserAccounts_CoordinatorUserId",
            table: "UserAccounts",
            column: "CoordinatorUserId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_UserAccounts_CoordinatorUserId",
            table: "UserAccounts");

        migrationBuilder.DropColumn(
            name: "CoordinatorUserId",
            table: "UserAccounts");
    }
}
