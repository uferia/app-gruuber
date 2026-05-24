using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gruuber.Rides.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSurgePricingAndRideFare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BaseFare",
                table: "rides",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DestLat",
                table: "rides",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DestLng",
                table: "rides",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalFare",
                table: "rides",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SurgeMultiplier",
                table: "rides",
                type: "numeric(6,2)",
                nullable: false,
                defaultValue: 1.0m);

            migrationBuilder.AddColumn<string>(
                name: "SurgeReason",
                table: "rides",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "surge_config",
                columns: table => new
                {
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    RideType = table.Column<string>(type: "text", nullable: false),
                    DemandRatioThreshold = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                    Multiplier = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    MaxMultiplier = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_surge_config", x => new { x.RegionId, x.RideType, x.DemandRatioThreshold });
                });

            migrationBuilder.CreateTable(
                name: "surge_time_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    RideType = table.Column<string>(type: "text", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: true),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    Multiplier = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_surge_time_rules", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "surge_config");

            migrationBuilder.DropTable(
                name: "surge_time_rules");

            migrationBuilder.DropColumn(
                name: "BaseFare",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "DestLat",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "DestLng",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "FinalFare",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "SurgeMultiplier",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "SurgeReason",
                table: "rides");
        }
    }
}
