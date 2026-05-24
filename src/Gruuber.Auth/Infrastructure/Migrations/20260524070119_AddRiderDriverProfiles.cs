using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gruuber.Auth.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderDriverProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "driver_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProfilePhotoUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LicenseNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LicenseExpiry = table.Column<DateOnly>(type: "date", nullable: false),
                    DriverLicenseDocumentUrl = table.Column<string>(type: "text", nullable: false),
                    MotorVehicleRegistrationUrl = table.Column<string>(type: "text", nullable: false),
                    InsurancePolicyUrl = table.Column<string>(type: "text", nullable: false),
                    NbiClearanceUrl = table.Column<string>(type: "text", nullable: false),
                    ApprovalStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "driver_vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Make = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Color = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LicensePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VehicleType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_vehicles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rider_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProfilePhotoUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rider_profiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_driver_profiles_ApprovalStatus",
                table: "driver_profiles",
                column: "ApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_driver_profiles_UserId",
                table: "driver_profiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_driver_vehicles_DriverProfileId",
                table: "driver_vehicles",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_rider_profiles_UserId",
                table: "rider_profiles",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "driver_profiles");

            migrationBuilder.DropTable(
                name: "driver_vehicles");

            migrationBuilder.DropTable(
                name: "rider_profiles");
        }
    }
}
