using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gruuber.Rides.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRidePickupCoords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PickupLat",
                table: "rides",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PickupLng",
                table: "rides",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PickupLat",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "PickupLng",
                table: "rides");
        }
    }
}
