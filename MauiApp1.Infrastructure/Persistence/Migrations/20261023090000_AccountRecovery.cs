using MauiApp1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LocalDbContext))]
[Migration("20261023090000_AccountRecovery")]
public sealed class AccountRecovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "RecoveryCodeHash",
            table: "UserAccounts",
            type: "BLOB",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "RecoveryCodeIterations",
            table: "UserAccounts",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<byte[]>(
            name: "RecoveryCodeSalt",
            table: "UserAccounts",
            type: "BLOB",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "RecoveryCodeHash", table: "UserAccounts");
        migrationBuilder.DropColumn(name: "RecoveryCodeIterations", table: "UserAccounts");
        migrationBuilder.DropColumn(name: "RecoveryCodeSalt", table: "UserAccounts");
    }
}
