using System.Text.RegularExpressions;
using CargoHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Data;

public sealed class CargoHubDbContext(DbContextOptions<CargoHubDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Warehouse>().ToTable("warehouse");
        model.Entity<Location>().ToTable("location");
        model.Entity<Supplier>().ToTable("supplier");
        model.Entity<ItemLine>().ToTable("item_line");
        model.Entity<ItemGroup>().ToTable("item_group");
        model.Entity<ItemType>().ToTable("item_type");
        model.Entity<Item>().ToTable("item");
        model.Entity<Inventory>().ToTable("inventory", table =>
        {
            table.HasCheckConstraint("ck_inventory_nonnegative",
                "quantity_on_hand >= 0 AND quantity_expected >= 0 AND quantity_ordered >= 0 AND quantity_allocated >= 0");
            table.HasCheckConstraint("ck_inventory_allocation_within_stock", "quantity_allocated <= quantity_on_hand");
        });
        model.Entity<Transfer>().ToTable("transfer", table =>
        {
            table.HasCheckConstraint("ck_transfer_different_locations", "from_location_id <> to_location_id");
            table.HasCheckConstraint("ck_transfer_status", "transfer_status IN ('Scheduled', 'Processed')");
        });
        model.Entity<TransferItem>().ToTable("transfer_item", table =>
            table.HasCheckConstraint("ck_transfer_item_positive_amount", "amount > 0"));

        model.Entity<Client>().ToTable("client");
        model.Entity<Order>().ToTable("order", table =>
            table.HasCheckConstraint("ck_order_status", "order_status IN ('Pending', 'Shipped')"));
        model.Entity<Shipment>().ToTable("shipment", table =>
            table.HasCheckConstraint("ck_shipment_status", "shipment_status IN ('Scheduled', 'Transit', 'Delivered')"));
        model.Entity<OrderItem>().ToTable("order_item", table =>
        {
            table.HasCheckConstraint("ck_order_item_positive_amount", "amount > 0");
            table.HasCheckConstraint("ck_order_item_nonnegative_price", "unit_price >= 0");
        });
        model.Entity<ShipmentItem>().ToTable("shipment_item", table =>
            table.HasCheckConstraint("ck_shipment_item_positive_amount", "amount > 0"));
        model.Entity<Order>().HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId);
        model.Entity<Order>().HasOne<Client>().WithMany().HasForeignKey(x => x.ShipToClientId);
        model.Entity<Order>().HasOne<Client>().WithMany().HasForeignKey(x => x.BillToClientId);
        model.Entity<Order>().HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId);
        // One shipment may reference one order; an order can have several shipments.
        model.Entity<Shipment>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<OrderItem>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<OrderItem>().HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId);
        model.Entity<OrderItem>().Property(x => x.UnitPrice).HasPrecision(18, 4);
        model.Entity<ShipmentItem>().HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId);
        model.Entity<ShipmentItem>().HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId);

        model.Entity<Inventory>().HasKey(x => new { x.ItemId, x.LocationId });
        model.Entity<Location>().HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId);
        model.Entity<Item>().HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
        model.Entity<Item>().HasOne<ItemLine>().WithMany().HasForeignKey(x => x.ItemLineId);
        model.Entity<Item>().HasOne<ItemGroup>().WithMany().HasForeignKey(x => x.ItemGroupId);
        model.Entity<Item>().HasOne<ItemType>().WithMany().HasForeignKey(x => x.ItemTypeId);
        model.Entity<Item>().Property(x => x.UnitWeight).HasPrecision(18, 6);
        model.Entity<Inventory>().HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId);
        model.Entity<Inventory>().HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId);
        model.Entity<Transfer>().HasOne<Location>().WithMany().HasForeignKey(x => x.FromLocationId);
        model.Entity<Transfer>().HasOne<Location>().WithMany().HasForeignKey(x => x.ToLocationId);
        model.Entity<Transfer>().Property(x => x.TransferStatus).HasDefaultValue("Scheduled");
        model.Entity<TransferItem>().HasIndex(x => new { x.TransferId, x.ItemId }).IsUnique();
        model.Entity<TransferItem>().HasOne<Transfer>().WithMany().HasForeignKey(x => x.TransferId);
        model.Entity<TransferItem>().HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId);

        foreach (var entity in model.Model.GetEntityTypes())
        {
            // Conservative pilot policy: never cascade-delete referenced records.
            foreach (var foreignKey in entity.GetForeignKeys())
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(Regex.Replace(property.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetDefaultValueSql("CURRENT_TIMESTAMP");
            }
        }
    }
}
