using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iCareX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionalCostRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM fuel_entries WHERE length("Note") > 500)
                    OR EXISTS (SELECT 1 FROM maintenance_records WHERE "Cost" <> trunc("Cost") OR "Cost" > 10000000000)
                  THEN RAISE EXCEPTION 'Review legacy notes/cost precision before migrating cost records; no automatic truncation.';
                  END IF;
                END $$;
                """);
            migrationBuilder.AddColumn<Guid>(
                name: "FuelEntryId",
                table: "odometer_readings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MaintenanceRecordId",
                table: "odometer_readings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "fuel_entries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "service_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaintenanceRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_items", x => x.Id);
                    table.CheckConstraint("ck_service_item_cost", "\"Cost\" BETWEEN 0 AND 10000000000 AND trunc(\"Cost\") = \"Cost\"");
                    table.CheckConstraint("ck_service_item_plan", "\"PlanId\" IS NULL OR \"Type\" = 'Maintenance'");
                    table.CheckConstraint("ck_service_item_type", "\"Type\" IN ('Repair', 'Maintenance')");
                    table.ForeignKey(
                        name: "FK_service_items_maintenance_plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "maintenance_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_items_maintenance_records_MaintenanceRecordId",
                        column: x => x.MaintenanceRecordId,
                        principalTable: "maintenance_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_odometer_readings_FuelEntryId",
                table: "odometer_readings",
                column: "FuelEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_odometer_readings_MaintenanceRecordId",
                table: "odometer_readings",
                column: "MaintenanceRecordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_items_MaintenanceRecordId",
                table: "service_items",
                column: "MaintenanceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_service_items_PlanId",
                table: "service_items",
                column: "PlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_odometer_readings_fuel_entries_FuelEntryId",
                table: "odometer_readings",
                column: "FuelEntryId",
                principalTable: "fuel_entries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_odometer_readings_maintenance_records_MaintenanceRecordId",
                table: "odometer_readings",
                column: "MaintenanceRecordId",
                principalTable: "maintenance_records",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
            // Preserve legacy typed records as one-item services and independent source readings.
            migrationBuilder.Sql("""
                INSERT INTO service_items ("Id", "MaintenanceRecordId", "Type", "Title", "Cost", "PlanId")
                SELECT gen_random_uuid(), "Id", 'Maintenance', "Title", "Cost", "PlanId" FROM maintenance_records;
                INSERT INTO odometer_readings
                  ("Id", "AccountId", "VehicleId", "CreatedByUserId", "DriverUserId", "Date", "Odo", "Note", "Version", "CreatedAtUtc", "UpdatedAtUtc", "FuelEntryId")
                SELECT gen_random_uuid(), "AccountId", "VehicleId", "CreatedByUserId", "DriverUserId", "Date", "Odo", "Note", "Version", "CreatedAtUtc", "UpdatedAtUtc", "Id" FROM fuel_entries;
                INSERT INTO odometer_readings
                  ("Id", "AccountId", "VehicleId", "CreatedByUserId", "DriverUserId", "Date", "Odo", "Note", "Version", "CreatedAtUtc", "UpdatedAtUtc", "MaintenanceRecordId")
                SELECT gen_random_uuid(), "AccountId", "VehicleId", "CreatedByUserId", "DriverUserId", "Date", "Odo", "Note", "Version", "CreatedAtUtc", "UpdatedAtUtc", "Id" FROM maintenance_records;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A rollback would discard item types and multi-item detail. Restore a verified backup instead.
            throw new NotSupportedException("Forward-only cost-record migration; restore a verified backup to roll back.");
        }
    }
}
