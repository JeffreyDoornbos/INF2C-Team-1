using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CargoHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: false),
                    zip_code = table.Column<string>(type: "text", nullable: false),
                    province = table.Column<string>(type: "text", nullable: false),
                    country = table.Column<string>(type: "text", nullable: false),
                    contact_name = table.Column<string>(type: "text", nullable: false),
                    contact_phone = table.Column<string>(type: "text", nullable: false),
                    contact_email = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_group",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_group", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_line",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_line", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_type",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_type", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: false),
                    zip_code = table.Column<string>(type: "text", nullable: false),
                    province = table.Column<string>(type: "text", nullable: false),
                    country = table.Column<string>(type: "text", nullable: false),
                    contact_name = table.Column<string>(type: "text", nullable: false),
                    phone_number = table.Column<string>(type: "text", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "warehouse",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: false),
                    zip_code = table.Column<string>(type: "text", nullable: false),
                    province = table.Column<string>(type: "text", nullable: false),
                    country = table.Column<string>(type: "text", nullable: false),
                    contact_name = table.Column<string>(type: "text", nullable: false),
                    contact_phone = table.Column<string>(type: "text", nullable: false),
                    contact_email = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    barcode = table.Column<string>(type: "text", nullable: false),
                    model_number = table.Column<string>(type: "text", nullable: false),
                    commodity_code = table.Column<string>(type: "text", nullable: false),
                    unit_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    item_line_id = table.Column<int>(type: "integer", nullable: false),
                    item_group_id = table.Column<int>(type: "integer", nullable: false),
                    item_type_id = table.Column<int>(type: "integer", nullable: false),
                    min_purchase_qty = table.Column<int>(type: "integer", nullable: false),
                    case_size = table.Column<int>(type: "integer", nullable: false),
                    packaging_type = table.Column<string>(type: "text", nullable: false),
                    order_multiple = table.Column<int>(type: "integer", nullable: false),
                    supplier_id = table.Column<int>(type: "integer", nullable: false),
                    supplier_sku = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_item_item_group_item_group_id",
                        column: x => x.item_group_id,
                        principalTable: "item_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_item_line_item_line_id",
                        column: x => x.item_line_id,
                        principalTable: "item_line",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_item_type_item_type_id",
                        column: x => x.item_type_id,
                        principalTable: "item_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "supplier",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "location",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    warehouse_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_location", x => x.id);
                    table.ForeignKey(
                        name: "FK_location_warehouse_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    order_date = table.Column<DateOnly>(type: "date", nullable: false),
                    request_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: false),
                    customer_po_number = table.Column<string>(type: "text", nullable: false),
                    order_status = table.Column<string>(type: "text", nullable: false),
                    shipping_notes = table.Column<string>(type: "text", nullable: true),
                    warehouse_id = table.Column<int>(type: "integer", nullable: false),
                    ship_to_client_id = table.Column<int>(type: "integer", nullable: false),
                    bill_to_client_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order", x => x.id);
                    table.CheckConstraint("ck_order_status", "order_status IN ('Pending', 'Shipped')");
                    table.ForeignKey(
                        name: "FK_order_client_bill_to_client_id",
                        column: x => x.bill_to_client_id,
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_client_client_id",
                        column: x => x.client_id,
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_client_ship_to_client_id",
                        column: x => x.ship_to_client_id,
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_warehouse_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory",
                columns: table => new
                {
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    location_id = table.Column<int>(type: "integer", nullable: false),
                    quantity_on_hand = table.Column<int>(type: "integer", nullable: false),
                    quantity_expected = table.Column<int>(type: "integer", nullable: false),
                    quantity_ordered = table.Column<int>(type: "integer", nullable: false),
                    quantity_allocated = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory", x => new { x.item_id, x.location_id });
                    table.CheckConstraint("ck_inventory_nonnegative", "quantity_on_hand >= 0 AND quantity_expected >= 0 AND quantity_ordered >= 0 AND quantity_allocated >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_item_item_id",
                        column: x => x.item_id,
                        principalTable: "item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_location_location_id",
                        column: x => x.location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reference = table.Column<string>(type: "text", nullable: false),
                    from_location_id = table.Column<int>(type: "integer", nullable: false),
                    to_location_id = table.Column<int>(type: "integer", nullable: false),
                    transfer_status = table.Column<string>(type: "text", nullable: false, defaultValue: "Scheduled"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer", x => x.id);
                    table.CheckConstraint("ck_transfer_different_locations", "from_location_id <> to_location_id");
                    table.CheckConstraint("ck_transfer_status", "transfer_status IN ('Scheduled', 'Processed')");
                    table.ForeignKey(
                        name: "FK_transfer_location_from_location_id",
                        column: x => x.from_location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_location_to_location_id",
                        column: x => x.to_location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_item",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item", x => x.id);
                    table.CheckConstraint("ck_order_item_nonnegative_price", "unit_price >= 0");
                    table.CheckConstraint("ck_order_item_positive_amount", "amount > 0");
                    table.ForeignKey(
                        name: "FK_order_item_item_item_id",
                        column: x => x.item_id,
                        principalTable: "item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_item_order_order_id",
                        column: x => x.order_id,
                        principalTable: "order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reference = table.Column<string>(type: "text", nullable: false),
                    order_id = table.Column<int>(type: "integer", nullable: true),
                    shipment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    shipment_type = table.Column<string>(type: "text", nullable: false),
                    shipment_status = table.Column<string>(type: "text", nullable: false),
                    carrier_name = table.Column<string>(type: "text", nullable: false),
                    shipping_method = table.Column<string>(type: "text", nullable: false),
                    payment_type = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment", x => x.id);
                    table.CheckConstraint("ck_shipment_status", "shipment_status IN ('Scheduled', 'Transit', 'Delivered')");
                    table.ForeignKey(
                        name: "FK_shipment_order_order_id",
                        column: x => x.order_id,
                        principalTable: "order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_item",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_id = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_item", x => x.id);
                    table.CheckConstraint("ck_transfer_item_positive_amount", "amount > 0");
                    table.ForeignKey(
                        name: "FK_transfer_item_item_item_id",
                        column: x => x.item_id,
                        principalTable: "item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transfer_item_transfer_transfer_id",
                        column: x => x.transfer_id,
                        principalTable: "transfer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_item",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    shipment_id = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_item", x => x.id);
                    table.CheckConstraint("ck_shipment_item_positive_amount", "amount > 0");
                    table.ForeignKey(
                        name: "FK_shipment_item_item_item_id",
                        column: x => x.item_id,
                        principalTable: "item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipment_item_shipment_shipment_id",
                        column: x => x.shipment_id,
                        principalTable: "shipment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_location_id",
                table: "inventory",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_item_item_group_id",
                table: "item",
                column: "item_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_item_item_line_id",
                table: "item",
                column: "item_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_item_item_type_id",
                table: "item",
                column: "item_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_item_supplier_id",
                table: "item",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_location_warehouse_id",
                table: "location",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_bill_to_client_id",
                table: "order",
                column: "bill_to_client_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_client_id",
                table: "order",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_ship_to_client_id",
                table: "order",
                column: "ship_to_client_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_warehouse_id",
                table: "order",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_item_id",
                table: "order_item",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_order_id",
                table: "order_item",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_order_id",
                table: "shipment",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_item_item_id",
                table: "shipment_item",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_item_shipment_id",
                table: "shipment_item",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_from_location_id",
                table: "transfer",
                column: "from_location_id");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_to_location_id",
                table: "transfer",
                column: "to_location_id");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_item_item_id",
                table: "transfer_item",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_item_transfer_id",
                table: "transfer_item",
                column: "transfer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory");

            migrationBuilder.DropTable(
                name: "order_item");

            migrationBuilder.DropTable(
                name: "shipment_item");

            migrationBuilder.DropTable(
                name: "transfer_item");

            migrationBuilder.DropTable(
                name: "shipment");

            migrationBuilder.DropTable(
                name: "item");

            migrationBuilder.DropTable(
                name: "transfer");

            migrationBuilder.DropTable(
                name: "order");

            migrationBuilder.DropTable(
                name: "item_group");

            migrationBuilder.DropTable(
                name: "item_line");

            migrationBuilder.DropTable(
                name: "item_type");

            migrationBuilder.DropTable(
                name: "supplier");

            migrationBuilder.DropTable(
                name: "location");

            migrationBuilder.DropTable(
                name: "client");

            migrationBuilder.DropTable(
                name: "warehouse");
        }
    }
}
