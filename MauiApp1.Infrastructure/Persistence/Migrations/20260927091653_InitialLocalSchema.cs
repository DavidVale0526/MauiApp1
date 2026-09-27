using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MauiApp1.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLocalSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceIdentity",
                columns: table => new
                {
                    SingletonKey = table.Column<int>(type: "INTEGER", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceIdentity", x => x.SingletonKey);
                    table.CheckConstraint("CK_DeviceIdentity_SingletonKey", "\"SingletonKey\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationType = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    ConfirmedAtUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.OperationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceIdentity_DeviceId",
                table: "DeviceIdentity",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_DeviceId",
                table: "OutboxMessages",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_CreatedAtUtc",
                table: "OutboxMessages",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceIdentity");

            migrationBuilder.DropTable(
                name: "OutboxMessages");
        }
    }
}
