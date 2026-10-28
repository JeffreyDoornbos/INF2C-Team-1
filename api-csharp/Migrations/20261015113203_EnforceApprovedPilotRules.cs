using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CargoHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnforceApprovedPilotRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transfer_item_transfer_id",
                table: "transfer_item");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_item_transfer_id_item_id",
                table: "transfer_item",
                columns: new[] { "transfer_id", "item_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_allocation_within_stock",
                table: "inventory",
                sql: "quantity_allocated <= quantity_on_hand");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transfer_item_transfer_id_item_id",
                table: "transfer_item");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_allocation_within_stock",
                table: "inventory");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_item_transfer_id",
                table: "transfer_item",
                column: "transfer_id");
        }
    }
}
