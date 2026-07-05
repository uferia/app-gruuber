using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gruuber.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPaymentColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "RideId",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "payments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "CardMock");

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_OrderId",
                table: "payments",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_OrderId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "payments");

            migrationBuilder.AlterColumn<Guid>(
                name: "RideId",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
