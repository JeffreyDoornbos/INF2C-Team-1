using System.Text;
using CargoHub.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

if (args.Length == 2 && args[0] == "--erd")
{
    using var design = new CargoHubDbContextFactory().CreateDbContext([]);
    var model = design.GetService<IDesignTimeModel>().Model;
    var erd = new StringBuilder("# CargoHub ERD — migratiebasis\n\nGegenereerd uit het EF Core-model. Niet handmatig aanpassen; zie CH-012-schema.md.\n\n```mermaid\nerDiagram\n");
    foreach (var entity in model.GetEntityTypes().OrderBy(e => e.GetTableName()))
    {
        erd.AppendLine($"    {entity.GetTableName()} {{");
        foreach (var property in entity.GetProperties())
        {
            var key = property.IsPrimaryKey() && property.IsForeignKey() ? " PK,FK" : property.IsPrimaryKey() ? " PK" : property.IsForeignKey() ? " FK" : "";
            var type = property.ClrType == typeof(DateOnly) ? "date" : property.ClrType == typeof(int?) ? "int" : property.ClrType == typeof(int) ? "int" : property.ClrType == typeof(decimal) ? "decimal" : property.ClrType == typeof(DateTimeOffset) ? "timestamptz" : "text";
            erd.AppendLine($"        {type} {property.GetColumnName()}{key}");
        }
        erd.AppendLine("    }");
        foreach (var fk in entity.GetForeignKeys())
            erd.AppendLine($"    {fk.PrincipalEntityType.GetTableName()} {(fk.IsRequired ? "||" : "|o")}--o{{ {entity.GetTableName()} : \"{string.Join(",", fk.Properties.Select(p => p.GetColumnName()))} / RESTRICT\"");
    }
    erd.AppendLine("```\n\nAlle kolommen zijn verplicht behalve `order.shipping_notes` en `shipment.order_id`. `inventory` heeft één samengestelde primary key: `(item_id, location_id)`.\n\n## Check constraints\n");
    foreach (var entity in model.GetEntityTypes().OrderBy(e => e.GetTableName()))
        foreach (var check in entity.GetCheckConstraints())
            erd.AppendLine($"- `{entity.GetTableName()}.{check.Name}`: `{check.Sql}`");
    erd.AppendLine("\n## Unieke combinaties\n");
    foreach (var entity in model.GetEntityTypes().OrderBy(e => e.GetTableName()))
        foreach (var index in entity.GetIndexes().Where(i => i.IsUnique))
            erd.AppendLine($"- `{entity.GetTableName()}`: `({string.Join(", ", index.Properties.Select(p => p.GetColumnName()))})`");
    erd.AppendLine("\n## Standaardwaarden\n\n- Enkelvoudige `id`-kolommen: PostgreSQL identity by default.\n- `created_at` en `updated_at`: `CURRENT_TIMESTAMP` bij invoegen; updates wijzigen `updated_at` niet automatisch.\n- `transfer_status`: `Scheduled`.\n\nDe schemaregels volgen het gebruikersakkoord op CH-010 van 15 oktober 2026. Statusovergangen, onveranderlijke verwerkte transfers en transactieveilige commitverwerking worden in CH-015 geïmplementeerd. De bestaande resources, inclusief klanten, orders en zendingen, zijn opgenomen. API-sleutelbeheer blijft bij CH-020; geheimen worden niet door deze migratie geïmporteerd.");
    await File.WriteAllTextAsync(args[1], erd.ToString());
    return;
}

var connection = Environment.GetEnvironmentVariable("CARGOHUB_TEST_ADMIN_CONNECTION")
    ?? throw new InvalidOperationException("Set CARGOHUB_TEST_ADMIN_CONNECTION to a dedicated local PostgreSQL instance with CREATEDB permission.");
var database = "ch012_" + Guid.NewGuid().ToString("N");
var cs = new NpgsqlConnectionStringBuilder(connection) { Database = database, Pooling = false };
await using var admin = new NpgsqlConnection(connection);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin).ExecuteNonQueryAsync();
var passed = 0;
try
{
    await using var db = new CargoHubDbContext(new DbContextOptionsBuilder<CargoHubDbContext>().UseNpgsql(cs.ConnectionString).Options);
    if (db.Database.HasPendingModelChanges()) throw new Exception("Model and migration snapshot differ.");
    await db.Database.MigrateAsync();
    await db.Database.MigrateAsync(); // Running the migration twice must be harmless.
    Pass("Empty database migrated; repeat migration is harmless; snapshot matches model");
    await db.Database.ExecuteSqlRawAsync("""
        INSERT INTO warehouse (id,code,name,address,city,zip_code,province,country,contact_name,contact_phone,contact_email)
          VALUES (1,'W','Test','','','','','','','','');
        INSERT INTO location (id,warehouse_id,code,name) VALUES (1,1,'A','A'),(2,1,'B','B');
        INSERT INTO supplier (id,code,name,address,city,zip_code,province,country,contact_name,phone_number,reference)
          VALUES (1,'S','Test','','','','','','','','');
        INSERT INTO item_line (id,name,description) VALUES (1,'Test','');
        INSERT INTO item_group (id,name,description) VALUES (1,'Test','');
        INSERT INTO item_type (id,name,description) VALUES (1,'Test','');
        INSERT INTO item (id,code,description,barcode,model_number,commodity_code,unit_weight,item_line_id,item_group_id,item_type_id,min_purchase_qty,case_size,packaging_type,order_multiple,supplier_id,supplier_sku)
          VALUES (1,'I','','','','',1,1,1,1,1,1,'',1,1,'');
        INSERT INTO inventory (item_id,location_id,quantity_on_hand,quantity_expected,quantity_ordered,quantity_allocated)
          VALUES (1,1,100,0,0,0),(1,2,20,0,0,0);
        INSERT INTO transfer (id,reference,from_location_id,to_location_id) VALUES (1,'T',1,2);
        INSERT INTO transfer_item (id,transfer_id,item_id,amount) VALUES (1,1,1,10);
        INSERT INTO client (id,name,address,city,zip_code,province,country,contact_name,contact_phone,contact_email)
          VALUES (1,'Test','','','','','','','','');
        INSERT INTO "order" (id,client_id,order_date,request_date,reference,customer_po_number,order_status,warehouse_id,ship_to_client_id,bill_to_client_id)
          VALUES (1,1,'2026-10-15','2026-10-16','O','','Pending',1,1,1);
        INSERT INTO shipment (id,reference,order_id,shipment_date,shipment_type,shipment_status,carrier_name,shipping_method,payment_type)
          VALUES (1,'S',1,'2026-10-16','Standard','Scheduled','','',''),(2,'S2',1,'2026-10-16','Standard','Transit','','',''),(3,'S3',NULL,'2026-10-16','Standard','Delivered','','','');
        INSERT INTO order_item (id,order_id,item_id,amount,unit_price) VALUES (1,1,1,10,2.50);
        INSERT INTO shipment_item (id,shipment_id,item_id,amount) VALUES (1,1,1,10);
        """);
    Pass("Valid related records, two locations per item and optional/multiple shipments per order accepted");
    await using var sql = new NpgsqlConnection(cs.ConnectionString);
    await sql.OpenAsync();
    foreach (var table in new[] {"warehouse","location","supplier","item_line","item_group","item_type","item","inventory","transfer","transfer_item","client","order","shipment","order_item","shipment_item"})
        await Reject($"INSERT INTO \"{table}\" SELECT * FROM \"{table}\" LIMIT 1", "23505", $"Duplicate primary key: {table}");
    foreach (var (table, column) in new[] {
        ("location","warehouse_id"),("item","supplier_id"),("item","item_line_id"),("item","item_group_id"),("item","item_type_id"),
        ("inventory","item_id"),("inventory","location_id"),("transfer","from_location_id"),("transfer","to_location_id"),
        ("transfer_item","transfer_id"),("transfer_item","item_id"),("order","client_id"),("order","ship_to_client_id"),("order","bill_to_client_id"),("order","warehouse_id"),("shipment","order_id"),("order_item","order_id"),("order_item","item_id"),("shipment_item","shipment_id"),("shipment_item","item_id")})
        await Reject($"UPDATE \"{table}\" SET {column}=999999 WHERE ctid=(SELECT ctid FROM \"{table}\" LIMIT 1)", "23503", $"Missing referenced record: {table}.{column}");
    foreach (var table in new[] {"warehouse","location","supplier","item_line","item_group","item_type","item","transfer","client","order","shipment"})
        await Reject($"DELETE FROM \"{table}\" WHERE id=1", "23503", $"Referenced parent cannot be deleted: {table}");
    foreach (var column in new[] {"quantity_on_hand","quantity_expected","quantity_ordered","quantity_allocated"})
        await Reject($"UPDATE inventory SET {column}=-1", "23514", $"Negative {column}");
    foreach (var amount in new[] {0,-1})
        await Reject($"UPDATE transfer_item SET amount={amount}", "23514", $"Invalid transfer amount {amount}");
    await Reject("UPDATE transfer SET to_location_id=from_location_id", "23514", "Same source and destination");
    await Reject("UPDATE transfer SET transfer_status='Unknown'", "23514", "Unknown status");
    await Reject("UPDATE \"order\" SET order_status='Unknown'", "23514", "Unknown order status");
    await Reject("UPDATE shipment SET shipment_status='Unknown'", "23514", "Unknown shipment status");
    await Reject("UPDATE order_item SET amount=0", "23514", "Invalid order amount");
    await Reject("UPDATE shipment_item SET amount=0", "23514", "Invalid shipment amount");
    await Reject("UPDATE order_item SET unit_price=-1", "23514", "Negative unit price");
    await Reject("UPDATE inventory SET item_id=NULL", "23502", "Null key");
    await Reject("UPDATE inventory SET quantity_allocated=quantity_on_hand+1", "23514", "Allocation cannot exceed on-hand stock");
    await Reject("INSERT INTO transfer_item (id,transfer_id,item_id,amount) VALUES (99,1,1,5)", "23505", "Duplicate item within one transfer");
    await new NpgsqlCommand("UPDATE inventory SET quantity_allocated=quantity_on_hand WHERE item_id=1 AND location_id=1", sql).ExecuteNonQueryAsync();
    Pass("Allocation equal to on-hand stock is valid");
    await Reject("UPDATE inventory SET quantity_on_hand=99 WHERE item_id=1 AND location_id=1", "23514", "Cannot reduce stock below existing allocation");
    await new NpgsqlCommand("INSERT INTO transfer (id,reference,from_location_id,to_location_id) VALUES (2,'T2',1,2); INSERT INTO transfer_item (id,transfer_id,item_id,amount) VALUES (2,2,1,5)", sql).ExecuteNonQueryAsync();
    Pass("Same item in a different transfer is valid");
    var status = await new NpgsqlCommand("SELECT transfer_status FROM transfer WHERE id=1",sql).ExecuteScalarAsync();
    if (!Equals(status,"Scheduled")) throw new Exception("Unexpected default status");
    await new NpgsqlCommand("UPDATE transfer SET transfer_status='Processed' WHERE id=1",sql).ExecuteNonQueryAsync();
    Pass("Scheduled default and Processed status accepted (not a transfer-service test)");
    await sql.CloseAsync();
    var baseline = db.Database.GetMigrations().First();
    await db.GetService<IMigrator>().MigrateAsync(baseline);
    await db.Database.ExecuteSqlRawAsync("UPDATE inventory SET quantity_allocated=101 WHERE item_id=1 AND location_id=1");
    try
    {
        await db.Database.MigrateAsync();
        throw new Exception("Upgrade must reject existing overallocated stock");
    }
    catch (PostgresException exception) when (exception.SqlState == "23514")
    {
        Pass("Upgrade rejects existing overallocated stock rather than silently changing data");
    }
    await db.Database.ExecuteSqlRawAsync("UPDATE inventory SET quantity_allocated=0 WHERE item_id=1 AND location_id=1; INSERT INTO transfer_item (id,transfer_id,item_id,amount) VALUES (99,1,1,5)");
    try
    {
        await db.Database.MigrateAsync();
        throw new Exception("Upgrade must reject existing duplicate transfer items");
    }
    catch (PostgresException exception) when (exception.SqlState == "23505")
    {
        Pass("Upgrade rejects existing duplicate transfer items rather than deleting data");
    }
    await db.Database.ExecuteSqlRawAsync("DELETE FROM transfer_item WHERE id=99");
    await db.Database.MigrateAsync();
    await using (var verification = new NpgsqlConnection(cs.ConnectionString))
    {
        await verification.OpenAsync();
        var count = await new NpgsqlCommand("SELECT count(*) FROM transfer_item", verification).ExecuteScalarAsync();
        if (!Equals(count, 2L)) throw new Exception("Upgrade did not preserve valid transfer items");
    }
    Pass("Upgrade from populated baseline preserves valid transfer items");
    await db.GetService<IMigrator>().MigrateAsync("0");
    await db.Database.MigrateAsync();
    Pass("Down migration and reapplication succeed");
    Console.WriteLine($"PASS: {passed} checks. Temporary database will be removed.");

    async Task Reject(string statement, string state, string description)
    {
        try { await new NpgsqlCommand(statement, sql).ExecuteNonQueryAsync(); }
        catch (PostgresException exception) when (exception.SqlState == state) { Pass(description); return; }
        throw new Exception($"Expected SQLSTATE {state}: {description}");
    }
    void Pass(string description) { passed++; Console.WriteLine($"PASS {passed}: {description}"); }
}
finally
{
    // Only the uniquely named database created by this process is removed.
    await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
}
