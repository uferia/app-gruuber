using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gruuber.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryOrderColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "RideId",
                table: "orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "CancellationNote",
                table: "orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByRole",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryFee",
                table: "orders",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<double>(
                name: "DeliveryLat",
                table: "orders",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DeliveryLng",
                table: "orders",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "CardMock");

            migrationBuilder.CreateIndex(
                name: "IX_orders_RestaurantId_Status",
                table: "orders",
                columns: new[] { "RestaurantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_RestaurantId_Status",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CancellationNote",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CancelledByRole",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryFee",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryLat",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryLng",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "orders");

            migrationBuilder.AlterColumn<Guid>(
                name: "RideId",
                table: "orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
