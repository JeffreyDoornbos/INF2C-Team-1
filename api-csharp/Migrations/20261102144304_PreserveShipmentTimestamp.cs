using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace CargoHub.Api.Migrations;

public partial class PreserveShipmentTimestamp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing dates become UTC midnight, independent of the server time zone.
        // A previously discarded time cannot be reconstructed without the JSON source.
        migrationBuilder.Sql("ALTER TABLE public.shipment ALTER COLUMN shipment_date TYPE timestamp with time zone USING shipment_date::timestamp AT TIME ZONE 'UTC';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Refuse a rollback that would silently discard newly stored time-of-day.
        migrationBuilder.Sql("""
            DO $guard$ BEGIN
                IF EXISTS (SELECT 1 FROM public.shipment WHERE (shipment_date AT TIME ZONE 'UTC')::time <> time '00:00:00') THEN
                    RAISE EXCEPTION 'Rollback would discard shipment times; export and explicitly resolve these records first.';
                END IF;
            END $guard$;
            ALTER TABLE public.shipment ALTER COLUMN shipment_date TYPE date USING (shipment_date AT TIME ZONE 'UTC')::date;
            """);
    }
}
