using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gruuber.Rides.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSurgeTimeRuleTimezone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "surge_time_rules",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "surge_time_rules");
        }
    }
}
