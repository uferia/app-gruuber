using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gruuber.Rides.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRidePooling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PoolSlot",
                table: "rides",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PoolTripId",
                table: "rides",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PoolSlot",
                table: "ride_views",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RideType",
                table: "ride_views",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pool_region_rates",
                columns: table => new
                {
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    DiscountPct = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                    MatchTimeoutSecs = table.Column<int>(type: "integer", nullable: false),
                    MaxDetourKm = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pool_region_rates", x => x.RegionId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pool_region_rates");

            migrationBuilder.DropColumn(
                name: "PoolSlot",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "PoolTripId",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "PoolSlot",
                table: "ride_views");

            migrationBuilder.DropColumn(
                name: "RideType",
                table: "ride_views");
        }
    }
}
