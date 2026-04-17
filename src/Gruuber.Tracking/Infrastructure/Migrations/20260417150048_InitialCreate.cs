using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gruuber.Tracking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ride_views",
                columns: table => new
                {
                    RideId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Lat = table.Column<double>(type: "double precision", nullable: false),
                    Lng = table.Column<double>(type: "double precision", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ride_views", x => x.RideId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ride_views_DriverId",
                table: "ride_views",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_ride_views_RegionId",
                table: "ride_views",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_ride_views_Status",
                table: "ride_views",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ride_views");
        }
    }
}
