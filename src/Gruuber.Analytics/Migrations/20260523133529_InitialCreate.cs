using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gruuber.Analytics.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_stats_daily",
                columns: table => new
                {
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    StatDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalRides = table.Column<int>(type: "integer", nullable: false),
                    TotalPoolRides = table.Column<int>(type: "integer", nullable: false),
                    TotalOrders = table.Column<int>(type: "integer", nullable: false),
                    GrossPlatformRevenue = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    ActiveDrivers = table.Column<int>(type: "integer", nullable: false),
                    ActiveRestaurants = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_stats_daily", x => new { x.RegionId, x.StatDate });
                });

            migrationBuilder.CreateTable(
                name: "analytics_export_jobs",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Format = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DownloadUrl = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analytics_export_jobs", x => x.JobId);
                });

            migrationBuilder.CreateTable(
                name: "driver_stats_daily",
                columns: table => new
                {
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    TripsCompleted = table.Column<int>(type: "integer", nullable: false),
                    TripsCancelled = table.Column<int>(type: "integer", nullable: false),
                    PoolTrips = table.Column<int>(type: "integer", nullable: false),
                    GrossEarnings = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    BonusEarnings = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    PayoutAmount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    AvgRating = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                    AcceptanceRate = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                    OnlineMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_stats_daily", x => new { x.DriverId, x.StatDate });
                });

            migrationBuilder.CreateTable(
                name: "menu_item_stats_daily",
                columns: table => new
                {
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemName = table.Column<string>(type: "text", nullable: false),
                    StatDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UnitsSold = table.Column<int>(type: "integer", nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(12,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_item_stats_daily", x => new { x.RestaurantId, x.ItemName, x.StatDate });
                });

            migrationBuilder.CreateTable(
                name: "processed_analytics_events",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_analytics_events", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "restaurant_stats_daily",
                columns: table => new
                {
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    OrdersReceived = table.Column<int>(type: "integer", nullable: false),
                    OrdersCompleted = table.Column<int>(type: "integer", nullable: false),
                    OrdersCancelled = table.Column<int>(type: "integer", nullable: false),
                    GrossRevenue = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    AvgPrepTimeSecs = table.Column<int>(type: "integer", nullable: false),
                    AvgRating = table.Column<decimal>(type: "numeric(3,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_stats_daily", x => new { x.RestaurantId, x.StatDate });
                });

            migrationBuilder.CreateIndex(
                name: "IX_analytics_export_jobs_OwnerId_Status",
                table: "analytics_export_jobs",
                columns: new[] { "OwnerId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_stats_daily");

            migrationBuilder.DropTable(
                name: "analytics_export_jobs");

            migrationBuilder.DropTable(
                name: "driver_stats_daily");

            migrationBuilder.DropTable(
                name: "menu_item_stats_daily");

            migrationBuilder.DropTable(
                name: "processed_analytics_events");

            migrationBuilder.DropTable(
                name: "restaurant_stats_daily");
        }
    }
}
