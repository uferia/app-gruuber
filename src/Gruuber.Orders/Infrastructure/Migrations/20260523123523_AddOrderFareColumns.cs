using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gruuber.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderFareColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BaseFare",
                table: "orders",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalFare",
                table: "orders",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SurgeMultiplier",
                table: "orders",
                type: "numeric(6,2)",
                nullable: false,
                defaultValue: 1.0m);

            migrationBuilder.AddColumn<string>(
                name: "SurgeReason",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseFare",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "FinalFare",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "SurgeMultiplier",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "SurgeReason",
                table: "orders");
        }
    }
}
