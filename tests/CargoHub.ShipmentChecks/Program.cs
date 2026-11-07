using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CargoHub.Api.Controllers;
using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

var adminConnection = Environment.GetEnvironmentVariable("CARGOHUB_TEST_ADMIN_CONNECTION")
    ?? throw new InvalidOperationException("Set CARGOHUB_TEST_ADMIN_CONNECTION to a dedicated test PostgreSQL instance with CREATEDB rights.");
var repository = FindRepository();
var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
var database = "shipments_" + Guid.NewGuid().ToString("N");
var connection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database, Pooling = false }.ConnectionString;
var passed = new List<string>();
var python = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
python.ArgumentList.Add(Path.Combine(repository, "tests/CargoHub.ShipmentChecks/python_oracle.py"));
python.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
using var oracleProcess = Process.Start(python)!;
var oracleJson = await oracleProcess.StandardOutput.ReadToEndAsync();
var oracleError = await oracleProcess.StandardError.ReadToEndAsync();
await oracleProcess.WaitForExitAsync();
Ensure(oracleProcess.ExitCode == 0, "Python oracle failed: " + oracleError);
var oracle = JsonNode.Parse(oracleJson)!;
await using var admin = new NpgsqlConnection(adminConnection);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin).ExecuteNonQueryAsync();
await using var api = new ApiProcess(connection, Path.Combine(repository, "api-csharp"));
try
{
    await using (var db = Context())
    {
        Ensure(!db.Database.HasPendingModelChanges(), "Model must match snapshot.");
        var migrations = db.Database.GetMigrations().ToArray();
        await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO shipment (id,reference,shipment_date,shipment_type,shipment_status,carrier_name,shipping_method,payment_type)
              VALUES (999,'Upgrade','2026-03-15','Standard','Scheduled','','','');
            """);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'Europe/Amsterdam'");
        await db.Database.MigrateAsync();
        var upgraded = await db.Shipments.AsNoTracking().SingleAsync(x => x.Id == 999);
        Ensure(upgraded.ShipmentDate == new DateTimeOffset(2026,3,15,0,0,0,TimeSpan.Zero), "Date upgrade must use UTC midnight.");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM shipment WHERE id=999; SET TIME ZONE 'UTC'");
        Pass("Timestamp migration preserves existing dates as UTC midnight even in another session time zone");
        db.Warehouses.Add(new Warehouse { Id=1, Name="Test" });
        db.Locations.AddRange(new Location { Id=1, WarehouseId=1 },new Location { Id=2, WarehouseId=1 });
        db.Set<Client>().Add(new Client { Id=1 });
        db.Set<Supplier>().Add(new Supplier { Id=1 });
        db.ItemGroups.Add(new ItemGroup { Id=1 }); db.ItemLines.Add(new ItemLine { Id=1 }); db.ItemTypes.Add(new ItemType { Id=1 });
        db.Items.AddRange(Enumerable.Range(1,4).Select(id => new Item { Id=id, SupplierId=1, ItemGroupId=1, ItemLineId=1, ItemTypeId=1 }));
        db.Set<Order>().AddRange(Enumerable.Range(1,2).Select(id => new Order { Id=id, ClientId=1, ShipToClientId=1, BillToClientId=1, WarehouseId=1,
            OrderStatus="Pending", OrderDate=new DateOnly(2026,11,2), RequestDate=new DateOnly(2026,11,3) }));
        db.Inventories.AddRange(oracle["initial"]!["inventory"]!.Deserialize<List<Inventory>>(jsonOptions)!);
        db.Shipments.AddRange(oracle["initial"]!["shipments"]!.Deserialize<List<Shipment>>(jsonOptions)!);
        db.ShipmentItems.AddRange(oracle["initial"]!["lines"]!.Deserialize<List<ShipmentItem>>(jsonOptions)!);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE shipment ALTER COLUMN id RESTART WITH 3; ALTER TABLE shipment_item ALTER COLUMN id RESTART WITH 3");
    }
    await api.StartAsync();
    await ExpectSnapshot(oracle["start"]!, "Initial Python read responses and stock");
    foreach (var operation in oracle["operations"]!.AsArray())
    {
        var method = new HttpMethod(operation!["method"]!.GetValue<string>());
        var response = await api.SendAsync(method, operation["path"]!.GetValue<string>(), operation["body"]?.ToJsonString());
        Empty(response, method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK);
        await ExpectSnapshot(operation["expected"]!, operation["name"]!.GetValue<string>());
    }
    await api.RestartAsync();
    await ExpectSnapshot(oracle["operations"]!.AsArray().Last()!["expected"]!, "Data survives actual API process restart");
    var missing = await api.SendAsync(HttpMethod.Get, "/api/v1/shipments/999999");
    Ensure(missing.Status==HttpStatusCode.OK && missing.Json.ValueKind==JsonValueKind.Null,"Missing detail must remain 200/null.");
    foreach (var part in new[]{"items","orders"})
    {
        var result = await api.SendAsync(HttpMethod.Get,"/api/v1/shipments/999999/"+part);
        Ensure(result.Status==HttpStatusCode.OK && result.Json.GetArrayLength()==0,"Missing subresource must remain [].");
    }
    Pass("Missing GET detail/items/orders preserve 200/null/[] responses");
    Empty(await api.SendAsync(HttpMethod.Put,"/api/v1/shipments/999999",oracle["initial"]!["shipments"]![0]!.ToJsonString()), HttpStatusCode.OK);
    Empty(await api.SendAsync(HttpMethod.Put,"/api/v1/shipments/999999/orders","[1]"),HttpStatusCode.OK);
    Empty(await api.SendAsync(HttpMethod.Delete,"/api/v1/shipments/999999"),HttpStatusCode.OK);
    Pass("Missing header PUT/order PUT/DELETE preserve legacy 200 no-op behavior");
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/shipments/999999/items","[{\"item_id\":1,\"amount\":1}]"),HttpStatusCode.BadRequest);
    Pass("Unknown shipment cannot acquire orphan item rows");

    foreach (var body in new[]{"{","null","{}","[{\"item_id\":1,\"amount\":0}]","[{\"item_id\":1,\"amount\":-1}]","[{\"item_id\":1,\"amount\":1.5}]","[{\"item_id\":999,\"amount\":1}]","[null]"})
        Status(await api.SendAsync(HttpMethod.Put,"/api/v1/shipments/1/items",body),HttpStatusCode.BadRequest);
    Pass("Malformed, null, wrong-shape, invalid-amount, unknown-item and null-line requests return 400");
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/shipments/1/orders","[999]"),HttpStatusCode.BadRequest);
    Empty(await api.SendAsync(HttpMethod.Put,"/api/v1/shipments/1/orders","[1,999]"),HttpStatusCode.OK);
    Pass("First order ID is validated and used; later IDs are ignored as in Python");
    var header=oracle["initial"]!["shipments"]![0]!.DeepClone();
    header["shipment_status"]="Unknown";
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/shipments/1",header.ToJsonString()),HttpStatusCode.BadRequest);
    header["shipment_status"]="Scheduled";header["order_id"]=999;
    Status(await api.SendAsync(HttpMethod.Post,"/api/v1/shipments",header.ToJsonString()),HttpStatusCode.BadRequest);
    header["order_id"]=1;
    Status(await api.SendAsync(HttpMethod.Post,"/api/v1/shipments",header.ToJsonString()),HttpStatusCode.Conflict);
    Pass("Invalid status/order and duplicate shipment ID preserve existing data and return 400/409");

    header["id"]=0;header["shipment_date"]="2026-11-02T12:34:56+02:00";header["items"]=new JsonArray();
    Empty(await api.SendAsync(HttpMethod.Post,"/api/v1/shipments",header.ToJsonString()),HttpStatusCode.Created);
    await using(var db=Context())
    {
        var generated=await db.Shipments.OrderByDescending(x=>x.Id).FirstAsync();
        Ensure(generated.Id>2 && generated.ShipmentDate==new DateTimeOffset(2026,11,2,10,34,56,TimeSpan.Zero),"Generated ID or timestamp normalization failed.");
        Ensure(generated.CreatedAt!=DateTimeOffset.Parse("2026-11-02T10:00:00Z"),"POST must set server timestamps.");
    }
    Pass("POST generates usable IDs, normalizes offset timestamps and creates server timestamps");
    header["shipment_date"]="2026-11-02T12:34:56";
    Status(await api.SendAsync(HttpMethod.Post,"/api/v1/shipments",header.ToJsonString()),HttpStatusCode.BadRequest);
    header["shipment_date"]="2026-11-03";
    Empty(await api.SendAsync(HttpMethod.Post,"/api/v1/shipments",header.ToJsonString()),HttpStatusCode.Created);
    Pass("Explicit offsets are required for timestamps; date-only requests remain accepted as UTC midnight");

    var orderedBefore=await Ordered();
    await Task.WhenAll(
        ExpectWrite("/api/v1/shipments/1/items","[{\"item_id\":1,\"amount\":2}]"),
        ExpectWrite("/api/v1/shipments/2/items","[{\"item_id\":1,\"amount\":3}]"));
    Ensure(await Ordered()==orderedBefore+5,"Concurrent shipments lost an ordered-stock delta.");
    Pass("Different shipments updating the same inventory keep both ordered-stock deltas");
    orderedBefore=await Ordered();
    await Task.WhenAll(
        ExpectWrite("/api/v1/shipments/1/items","[{\"item_id\":1,\"amount\":6}]"),
        ExpectWrite("/api/v1/shipments/1/items","[{\"item_id\":1,\"amount\":7}]"));
    await using(var db=Context())
    {
        var finalAmount=await db.ShipmentItems.Where(x=>x.ShipmentId==1).Select(x=>x.Amount).SingleAsync();
        Ensure(await Ordered()==orderedBefore+finalAmount-2,"Concurrent replacement of one shipment did not serialize.");
    }
    Pass("Concurrent replacements of the same shipment serialize without duplicate lines or lost updates");

    // Force a database error AFTER the inventory delta/header update but BEFORE new lines are stored.
    await using(var db=Context())
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION shipment_test_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
              IF NEW.amount=13 THEN RAISE EXCEPTION 'synthetic shipment test failure'; END IF;
              RETURN NEW; END $$;
            CREATE TRIGGER shipment_test_failure BEFORE INSERT ON shipment_item FOR EACH ROW EXECUTE FUNCTION shipment_test_failure();
            """);
    var beforeFailure=await Snapshot();
    header=JsonNode.Parse((await api.SendAsync(HttpMethod.Get,"/api/v1/shipments/1")).Body)!;
    header["reference"]="Must roll back";header["items"]=JsonNode.Parse("[{\"item_id\":1,\"amount\":13}]");
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/shipments/1",header.ToJsonString()),HttpStatusCode.InternalServerError);
    Ensure(JsonNode.DeepEquals(await Snapshot(),beforeFailure),"A failed multi-part shipment update must roll back header, timestamps, items and all inventory.");
    Pass("Injected late database error rolls back shipment header, replacement lines and ordered inventory together");
    await using(var db=Context())await db.Database.ExecuteSqlRawAsync("DROP TRIGGER shipment_test_failure ON shipment_item; DROP FUNCTION shipment_test_failure()");

    header["id"]=999;header["reference"]="Path ID wins";header["items"]=null;
    var created=(await api.SendAsync(HttpMethod.Get,"/api/v1/shipments/1")).Json.GetProperty("created_at").GetDateTimeOffset();
    Empty(await api.SendAsync(HttpMethod.Put,"/api/v1/shipments/1",header.ToJsonString()),HttpStatusCode.OK);
    var after=(await api.SendAsync(HttpMethod.Get,"/api/v1/shipments/1")).Json;
    Ensure(after.GetProperty("id").GetInt32()==1 && after.GetProperty("created_at").GetDateTimeOffset()==created,"PUT must preserve path ID and creation time.");
    Pass("PUT retains path ID and original creation time even with a mismatching body ID");

    var swagger=await api.SendAsync(HttpMethod.Get,"/swagger/v1/swagger.json");
    Status(swagger,HttpStatusCode.OK);
    foreach(var path in new[]{"/api/v1/shipments","/api/v1/shipments/{id}","/api/v1/shipments/{id}/items","/api/v1/shipments/{id}/orders"})
        Ensure(swagger.Json.GetProperty("paths").TryGetProperty(path,out _),"Shipment route missing from OpenAPI: "+path);
    Pass("All shipment route families are present in the real generated OpenAPI document");
    Status(await api.SendAsync(HttpMethod.Patch,"/api/v1/shipments/1","{}"),HttpStatusCode.MethodNotAllowed);
    Status(await api.SendAsync(HttpMethod.Get,"/api/v1/shipments/1/unknown"),HttpStatusCode.NotFound);
    Pass("Unsupported method and unknown subroute return 405/404");
    await using(var db=Context())
    {
        try { await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().ToArray()[^2]); throw new Exception("Lossy date rollback must be refused."); }
        catch(PostgresException exception) when(exception.SqlState=="P0001") { }
        Ensure((await db.Database.GetAppliedMigrationsAsync()).Count()==db.Database.GetMigrations().Count(),"Refused rollback changed migration history.");
    }
    Pass("Rollback refuses to discard newly stored shipment times and preserves migration history");
    var stockBeforeDelete = await Ordered();
    var remaining = (await api.SendAsync(HttpMethod.Get,"/api/v1/shipments")).Json;
    foreach(var record in remaining.EnumerateArray())
        Empty(await api.SendAsync(HttpMethod.Delete,"/api/v1/shipments/"+record.GetProperty("id").GetInt32()),HttpStatusCode.OK);
    var emptyCollection = await api.SendAsync(HttpMethod.Get,"/api/v1/shipments");
    Ensure(emptyCollection.Status==HttpStatusCode.OK && emptyCollection.Json.GetArrayLength()==0,"Empty collection must return [].");
    await using(var db=Context())Ensure(!await db.ShipmentItems.AnyAsync(),"Deleting shipments left orphan lines.");
    Ensure(await Ordered()==stockBeforeDelete,"Delete must preserve Python's unchanged ordered-stock behavior.");
    Pass("Deleting all shipments leaves an empty array, removes their item rows and preserves ordered inventory");
    await api.RestartAsync();
    Ensure((await api.SendAsync(HttpMethod.Get,"/api/v1/shipments")).Json.GetArrayLength()==0,"Deleted shipments reappeared after restart.");
    Pass("Deleted shipment headers and lines stay deleted after API restart");
    Console.WriteLine($"PASS: {passed.Count} shipment acceptance checks.");
}
finally
{
    await api.StopAsync();
    await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)",admin).ExecuteNonQueryAsync();
}

CargoHubDbContext Context()=>new(new DbContextOptionsBuilder<CargoHubDbContext>().UseNpgsql(connection).Options);
void Pass(string text){passed.Add(text);Console.WriteLine("PASS: "+text);}
static void Ensure(bool condition,string text){if(!condition)throw new Exception(text);}
static void Status(HttpResult r,HttpStatusCode code)=>Ensure(r.Status==code,$"Expected {(int)code}; got {(int)r.Status}: {r.Body}");
static void Empty(HttpResult r,HttpStatusCode code){Status(r,code);Ensure(r.Body.Length==0,"Legacy success body must be empty.");}
async Task ExpectWrite(string path,string body)=>Empty(await api.SendAsync(HttpMethod.Put,path,body),HttpStatusCode.OK);
async Task<int> Ordered(){await using var db=Context();return await db.Inventories.Where(x=>x.ItemId==1 && x.LocationId==2).Select(x=>x.QuantityOrdered).SingleAsync();}
async Task<JsonNode> Snapshot()
{
    var shipments=await api.SendAsync(HttpMethod.Get,"/api/v1/shipments");Status(shipments,HttpStatusCode.OK);
    await using var db=Context();
    var stock=await db.Inventories.AsNoTracking().OrderBy(x=>x.ItemId).ThenBy(x=>x.LocationId).ToListAsync();
    return new JsonObject{["shipments"]=JsonNode.Parse(shipments.Body),["inventory"]=JsonSerializer.SerializeToNode(stock,jsonOptions)};
}
async Task ExpectSnapshot(JsonNode expected,string description)
{
    Equal(await Snapshot(),expected,description);
    foreach(var shipment in expected["shipments"]!.AsArray())
    {
        var id=shipment!["id"]!.GetValue<int>();
        var detail=await api.SendAsync(HttpMethod.Get,$"/api/v1/shipments/{id}");Status(detail,HttpStatusCode.OK);
        Equal(JsonNode.Parse(detail.Body)!,shipment,"Detail must include the exact legacy fields and items");
        var items=await api.SendAsync(HttpMethod.Get,$"/api/v1/shipments/{id}/items");Status(items,HttpStatusCode.OK);
        Equal(JsonNode.Parse(items.Body)!,shipment["items"]!,"Items route must contain item_id and amount only");
        var orders=await api.SendAsync(HttpMethod.Get,$"/api/v1/shipments/{id}/orders");Status(orders,HttpStatusCode.OK);
        var expectedOrders=shipment["order_id"] is null?new JsonArray():new JsonArray(shipment["order_id"]!.DeepClone());
        Equal(JsonNode.Parse(orders.Body)!,expectedOrders,"Orders route must preserve [] or [order_id]");
        if (shipment["order_id"] is not null)
        {
            var linked = await api.SendAsync(HttpMethod.Get,"/api/v1/orders/"+shipment["order_id"]!.GetValue<int>());
            Status(linked,HttpStatusCode.OK);
            Ensure(linked.Json.ValueKind==JsonValueKind.Object && linked.Json.GetProperty("id").GetInt32()==shipment["order_id"]!.GetValue<int>(),"Shipment link must resolve through the newly merged orders API.");
        }
    }
    Pass(description+" — HTTP responses and independent PostgreSQL stock match Python models");
}
static void Equal(JsonNode actual,JsonNode expected,string text)
{
    JsonNode? Normalize(JsonNode? node,string? key=null)
    {
        if(node is JsonObject o)return new JsonObject(o.Where(kv=>kv.Key is not "created_at" and not "updated_at").OrderBy(kv=>kv.Key).Select(kv=>new KeyValuePair<string,JsonNode?>(kv.Key,Normalize(kv.Value,kv.Key))));
        if(node is JsonArray a)return new JsonArray(a.Select(x=>Normalize(x)).ToArray());
        if(key=="shipment_date" && node is not null)return JsonValue.Create(DateTimeOffset.Parse(node.GetValue<string>()).ToUnixTimeMilliseconds());
        return node?.DeepClone();
    }
    Ensure(JsonNode.DeepEquals(Normalize(actual),Normalize(expected)),text+"\nActual: "+actual.ToJsonString()+"\nExpected: "+expected.ToJsonString());
}
static string FindRepository()
{
    for(var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent)
        if(File.Exists(Path.Combine(d.FullName,"api-csharp","CargoHub.Api.csproj")))return d.FullName;
    throw new DirectoryNotFoundException("Repository not found.");
}

sealed record HttpResult(HttpStatusCode Status, string Body, string? ContentType)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement.Clone();
}

// Starts the real application entry point and its existing Program.cs settings.
// No duplicate test host, in-memory database, controller mocks or test-only routes.
sealed class ApiProcess(string connection, string contentRoot) : IAsyncDisposable
{
    private Process? _process;
    private HttpClient? _client;

    public async Task StartAsync()
    {
        var ready = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logs = new ConcurrentQueue<string>();
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = contentRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "CargoHub.ShipmentChecks.runtimeconfig.json"), "--depsfile", Path.Combine(AppContext.BaseDirectory, "CargoHub.ShipmentChecks.deps.json"), typeof(ShipmentsController).Assembly.Location, "--urls", "http://127.0.0.1:0" })
            info.ArgumentList.Add(argument);
        info.Environment["ConnectionStrings__CargoHub"] = connection;
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["Logging__LogLevel__Default"] = "Warning";
        info.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        void ReadLog(object sender, DataReceivedEventArgs args)
        {
            if (args.Data is null) return;
            logs.Enqueue(args.Data);
            while (logs.Count > 30) logs.TryDequeue(out _);
            var match = Regex.Match(args.Data, @"Now listening on: (http://127\.0\.0\.1:\d+)");
            if (match.Success) ready.TrySetResult(new Uri(match.Groups[1].Value));
        }
        _process.OutputDataReceived += ReadLog;
        _process.ErrorDataReceived += ReadLog;
        _process.Exited += (_, _) => ready.TrySetException(new InvalidOperationException("API exited before startup: " + string.Join(Environment.NewLine, logs)));
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        var address = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        _client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<HttpResult> SendAsync(HttpMethod method, string path, string? json = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client!.SendAsync(request);
        return new HttpResult(response.StatusCode, await response.Content.ReadAsStringAsync(), response.Content.Headers.ContentType?.MediaType);
    }

    public async Task RestartAsync() { await StopAsync(); await StartAsync(); }

    public async Task StopAsync()
    {
        _client?.Dispose();
        _client = null;
        if (_process is not null)
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            _process.Dispose();
            _process = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
