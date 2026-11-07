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
using Npgsql;

var adminConnection = Environment.GetEnvironmentVariable("CARGOHUB_TEST_ADMIN_CONNECTION")
    ?? throw new InvalidOperationException("Set CARGOHUB_TEST_ADMIN_CONNECTION to a dedicated test PostgreSQL instance with CREATEDB rights.");
var repository = FindRepository();
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
var database = "transfers_" + Guid.NewGuid().ToString("N");
var connection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database, Pooling = false }.ConnectionString;
var count=0;
var python = new ProcessStartInfo("python3") { RedirectStandardOutput=true, RedirectStandardError=true, UseShellExecute=false };
python.ArgumentList.Add(Path.Combine(repository,"tests/CargoHub.TransferChecks/python_oracle.py"));
python.Environment["PYTHONDONTWRITEBYTECODE"]="1";
using var oracleProcess=Process.Start(python)!;
var oracleText=await oracleProcess.StandardOutput.ReadToEndAsync();
var oracleError=await oracleProcess.StandardError.ReadToEndAsync();await oracleProcess.WaitForExitAsync();
Ensure(oracleProcess.ExitCode==0,"Python oracle failed: "+oracleError);
var oracle=JsonNode.Parse(oracleText)!;
await using var admin=new NpgsqlConnection(adminConnection);await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{database}\"",admin).ExecuteNonQueryAsync();
await using var api=new ApiProcess(connection,Path.Combine(repository,"api-csharp"));
try
{
    await using(var db=Context())
    {
        Ensure(!db.Database.HasPendingModelChanges(),"Transfer DbSets must not change the EF schema.");
        await db.Database.MigrateAsync();
        db.Warehouses.Add(new Warehouse{Id=1});
        db.Locations.AddRange(Enumerable.Range(1,3).Select(id=>new Location{Id=id,WarehouseId=1}));
        db.Suppliers.Add(new Supplier{Id=1});db.ItemGroups.Add(new ItemGroup{Id=1});
        db.ItemLines.Add(new ItemLine{Id=1});db.ItemTypes.Add(new ItemType{Id=1});
        db.Items.AddRange(Enumerable.Range(1,4).Select(id=>new Item{Id=id,SupplierId=1,ItemGroupId=1,ItemLineId=1,ItemTypeId=1}));
        db.Transfers.AddRange(oracle["initial"]!["transfers"]!.Deserialize<List<Transfer>>(options)!);
        db.TransferItems.AddRange(oracle["initial"]!["lines"]!.Deserialize<List<TransferItem>>(options)!);
        db.Inventories.AddRange(oracle["initial"]!["inventory"]!.Deserialize<List<Inventory>>(options)!);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE transfer ALTER COLUMN id RESTART WITH 10000; ALTER TABLE transfer_item ALTER COLUMN id RESTART WITH 4");
    }
    Pass("Existing migrations build the isolated database and model matches snapshot");
    await api.StartAsync();
    await ExpectSnapshot(oracle["start"]!,"Initial transfer list/detail/items responses");
    foreach(var operation in oracle["operations"]!.AsArray())
    {
        var method=new HttpMethod(operation!["method"]!.GetValue<string>());
        var response=await api.SendAsync(method,operation["path"]!.GetValue<string>(),operation["body"]?.ToJsonString());
        Empty(response,method==HttpMethod.Post?HttpStatusCode.Created:HttpStatusCode.OK);
        await ExpectSnapshot(operation["expected"]!,operation["name"]!.GetValue<string>());
    }
    await api.WaitNotification("Processed batch transfer with id:1");
    var processingLogs=api.Notifications.Count(x=>x.Contains("Processed batch transfer with id:1"));
    var before=await Snapshot();
    Empty(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/1/commit"),HttpStatusCode.OK);
    Ensure(JsonNode.DeepEquals(before,await Snapshot()),"Repeated commit changed data or timestamps.");
    Ensure(api.Notifications.Count(x=>x.Contains("Processed batch transfer with id:1"))==processingLogs,"Repeated commit logged a second processing event.");
    Pass("Repeated Processed commit is an empty 200 without another stock movement, timestamp change or processing event");
    await api.RestartAsync();
    await ExpectSnapshot(oracle["operations"]!.AsArray().Last()!["expected"]!,"Committed transfers, items and stock survive an API process restart");
    var missing=await api.SendAsync(HttpMethod.Get,"/api/v1/transfers/999999");
    Ensure(missing.Status==HttpStatusCode.OK && missing.Json.ValueKind==JsonValueKind.Null,"Unknown detail must return 200/null.");
    var missingItems=await api.SendAsync(HttpMethod.Get,"/api/v1/transfers/999999/items");
    Ensure(missingItems.Status==HttpStatusCode.OK && missingItems.Json.GetArrayLength()==0,"Unknown items must return [].");
    Empty(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/999999",Header(999999).ToJsonString()),HttpStatusCode.OK);
    Empty(await api.SendAsync(HttpMethod.Delete,"/api/v1/transfers/999999"),HttpStatusCode.OK);
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/999999/commit"),HttpStatusCode.NotFound);
    Pass("Unknown GET/PUT/DELETE preserve legacy null/[]/no-op behavior; unknown commit returns 404");
    before=await Snapshot();
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/1",Header(1).ToJsonString()),HttpStatusCode.Conflict);
    Status(await api.SendAsync(HttpMethod.Delete,"/api/v1/transfers/1"),HttpStatusCode.Conflict);
    Ensure(JsonNode.DeepEquals(before,await Snapshot()),"Processed history was modified or deleted.");
    Pass("Processed transfer headers and item history cannot be changed or deleted");
    var manual=Header(2);manual["transfer_status"]="Processed";
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/2",manual.ToJsonString()),HttpStatusCode.Conflict);
    Pass("Ordinary PUT cannot bypass commit by changing the status to Processed");

    foreach(var raw in new[]{"{","[]","null","{}"})
        Status(await api.SendAsync(HttpMethod.Post,"/api/v1/transfers",raw),HttpStatusCode.BadRequest);
    Pass("Malformed, null, array and missing-field POST bodies return 400");
    foreach(var items in new[]{"[]","null","[null]","[{\"item_id\":1,\"amount\":0}]","[{\"item_id\":1,\"amount\":-1}]","[{\"item_id\":1,\"amount\":1.5}]","[{\"item_id\":999,\"amount\":1}]","[{\"item_id\":1,\"amount\":1},{\"item_id\":1,\"amount\":2}]"})
    {
        var invalid=Header(900);invalid["items"]=JsonNode.Parse(items);
        Status(await api.SendAsync(HttpMethod.Post,"/api/v1/transfers",invalid.ToJsonString()),HttpStatusCode.BadRequest);
    }
    Pass("Empty, null, duplicate, unknown, non-positive and fractional transfer lines are rejected without partial headers");
    var same=Header(900);same["to_location_id"]=1;
    Status(await api.SendAsync(HttpMethod.Post,"/api/v1/transfers",same.ToJsonString()),HttpStatusCode.BadRequest);
    same["to_location_id"]=999;
    Status(await api.SendAsync(HttpMethod.Post,"/api/v1/transfers",same.ToJsonString()),HttpStatusCode.BadRequest);
    Status(await api.SendAsync(HttpMethod.Post,"/api/v1/transfers",Header(2).ToJsonString()),HttpStatusCode.Conflict);
    Pass("Equal/missing locations and duplicate transfer IDs return 400/409");
    await using(var db=Context())Ensure(!await db.Transfers.AnyAsync(x=>x.Id==900),"Validation stored a partial header.");

    var routeHeader=Header(999);routeHeader["reference"]="Path ID wins";routeHeader["items"]=null;
    var created=(await api.SendAsync(HttpMethod.Get,"/api/v1/transfers/2")).Json.GetProperty("created_at").GetDateTimeOffset();
    Empty(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/2",routeHeader.ToJsonString()),HttpStatusCode.OK);
    var changed=(await api.SendAsync(HttpMethod.Get,"/api/v1/transfers/2")).Json;
    Ensure(changed.GetProperty("id").GetInt32()==2 && changed.GetProperty("created_at").GetDateTimeOffset()==created && changed.GetProperty("items").GetArrayLength()==1,"Header update must preserve ID, created_at and unspecified lines.");
    Pass("Header PUT keeps route ID, creation time and null/omitted item rows");

    var automatic=Header(0);automatic["created_at"]="1970-01-01T00:00:00Z";automatic["updated_at"]="1970-01-01T00:00:00Z";
    Empty(await api.SendAsync(HttpMethod.Post,"/api/v1/transfers",automatic.ToJsonString()),HttpStatusCode.Created);
    await using(var db=Context())
    {
        var generated=await db.Transfers.OrderByDescending(x=>x.Id).FirstAsync();
        Ensure(generated.Id==10000 && generated.TransferStatus=="Scheduled" && generated.CreatedAt.Year!=1970,"Generated IDs/timestamps/status failed.");
    }
    Pass("POST without ID generates a usable identity and server timestamps");

    await Stock(3,1,10,8);await Add(101,3,3);
    before=await Snapshot();
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/101/commit"),HttpStatusCode.Conflict);
    Ensure(JsonNode.DeepEquals(before,await Snapshot()),"Insufficient available stock caused changes.");
    Pass("Available-stock check uses on_hand minus allocated; insufficient stock changes nothing");
    var lower=Header(101,3,2);Empty(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/101",lower.ToJsonString()),HttpStatusCode.OK);
    Empty(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/101/commit"),HttpStatusCode.OK);
    Ensure(await Amount(3,1)==8 && await Amount(3,2)==2,"Available-stock boundary was not respected.");
    Pass("A transfer exactly equal to available stock succeeds without moving allocated stock");
    await Add(102,4,1);before=await Snapshot();
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/102/commit"),HttpStatusCode.Conflict);
    Ensure(JsonNode.DeepEquals(before,await Snapshot()),"Missing source inventory created stock.");
    Pass("Missing source inventory refuses commit instead of creating unbacked destination stock");
    await Stock(4,1,20);await Stock(4,2,int.MaxValue);await Add(103,4,1);before=await Snapshot();
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/103/commit"),HttpStatusCode.Conflict);
    Ensure(JsonNode.DeepEquals(before,await Snapshot()),"Destination overflow partially updated inventory.");
    Pass("Destination integer overflow is rejected with no partial stock movement");

    // Force a failure after header creation, and after old item rows were removed on PUT.
    await Sql("""
        CREATE FUNCTION transfer_test_line_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          IF NEW.amount=13 THEN RAISE EXCEPTION 'synthetic transfer line failure'; END IF; RETURN NEW; END $$;
        CREATE TRIGGER transfer_test_line_failure BEFORE INSERT ON transfer_item FOR EACH ROW EXECUTE FUNCTION transfer_test_line_failure();
        """);
    before=await Snapshot();
    Status(await api.SendAsync(HttpMethod.Post,"/api/v1/transfers",Header(104,1,13).ToJsonString()),HttpStatusCode.InternalServerError);
    Ensure(JsonNode.DeepEquals(before,await Snapshot()),"Failed POST left header or lines.");
    Pass("Late transfer-item insertion error rolls back a newly inserted transfer header");
    var badUpdate=Header(2,1,13);badUpdate["reference"]="Must roll back";
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/2",badUpdate.ToJsonString()),HttpStatusCode.InternalServerError);
    Ensure(JsonNode.DeepEquals(before,await Snapshot()),"Failed item replacement changed header, timestamps or old lines.");
    Pass("Failed PUT restores header, timestamps and original item rows after replacement began");
    await Sql("DROP TRIGGER transfer_test_line_failure ON transfer_item; DROP FUNCTION transfer_test_line_failure()");
    await Add(105,1,1);
    await Sql("""
        CREATE FUNCTION transfer_test_stock_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          IF NEW.location_id=2 THEN RAISE EXCEPTION 'synthetic destination update failure'; END IF; RETURN NEW; END $$;
        CREATE TRIGGER transfer_test_stock_failure BEFORE UPDATE ON inventory FOR EACH ROW EXECUTE FUNCTION transfer_test_stock_failure();
        """);
    before=await Snapshot();
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/105/commit"),HttpStatusCode.InternalServerError);
    Ensure(JsonNode.DeepEquals(before,await Snapshot()),"Failed commit changed stock, status, timestamps or item history.");
    Ensure(!api.Notifications.Any(x=>x.Contains("Processed batch transfer with id:105")),"Failed commit sent a processing event.");
    Pass("Destination update failure rolls back both inventories and transfer status, without a processing event");
    await Sql("DROP TRIGGER transfer_test_stock_failure ON inventory; DROP FUNCTION transfer_test_stock_failure()");
    Empty(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/105/commit"),HttpStatusCode.OK);
    Pass("Retry after complete rollback processes the transfer successfully");

    await Add(201,1,7);var sourceBefore=await Amount(1,1);var destinationBefore=await Amount(1,2);
    var repeated=await Task.WhenAll(api.SendAsync(HttpMethod.Put,"/api/v1/transfers/201/commit"),api.SendAsync(HttpMethod.Put,"/api/v1/transfers/201/commit"));
    foreach(var r in repeated)Empty(r,HttpStatusCode.OK);
    Ensure(await Amount(1,1)==sourceBefore-7 && await Amount(1,2)==destinationBefore+7,"Concurrent same-transfer commits booked more than once.");
    await api.WaitNotification("Processed batch transfer with id:201");
    Ensure(api.Notifications.Count(x=>x.Contains("Processed batch transfer with id:201"))==1,"Concurrent commit logged more than once.");
    Pass("Concurrent commits of the same transfer both return 200 and book/log only once");

    await Sql("DELETE FROM inventory WHERE item_id=4");await Stock(4,1,10);await Add(202,4,7);await Add(203,4,7);
    var competitors=await Task.WhenAll(api.SendAsync(HttpMethod.Put,"/api/v1/transfers/202/commit"),api.SendAsync(HttpMethod.Put,"/api/v1/transfers/203/commit"));
    Ensure(competitors.Count(x=>x.Status==HttpStatusCode.OK)==1 && competitors.Count(x=>x.Status==HttpStatusCode.Conflict)==1,"Competing transfers must produce one success and one stock conflict.");
    Ensure(await Amount(4,1)==3 && await Amount(4,2)==7,"Competing transfers lost stock or overbooked.");
    Pass("Different transfers cannot jointly exceed source stock: one succeeds and one stays Scheduled");

    await Sql("DELETE FROM inventory WHERE item_id=3");await Stock(3,1,10);await Add(204,3,3,1,3);await Add(205,3,4,1,3);
    var destinations=await Task.WhenAll(api.SendAsync(HttpMethod.Put,"/api/v1/transfers/204/commit"),api.SendAsync(HttpMethod.Put,"/api/v1/transfers/205/commit"));
    foreach(var r in destinations)Empty(r,HttpStatusCode.OK);
    Ensure(await Amount(3,1)==3 && await Amount(3,3)==7,"Concurrent creation of destination inventory lost an increment.");
    await using(var db=Context())Ensure(await db.Inventories.CountAsync(x=>x.ItemId==3 && x.LocationId==3)==1,"Duplicate destination rows.");
    Pass("Concurrent transfers create one shared missing destination row and preserve both increments");
    await Add(206,3,1,1,3);await Add(207,3,1,3,1);
    var reverse=await Task.WhenAll(api.SendAsync(HttpMethod.Put,"/api/v1/transfers/206/commit"),api.SendAsync(HttpMethod.Put,"/api/v1/transfers/207/commit"));
    foreach(var r in reverse)Empty(r,HttpStatusCode.OK);
    Ensure(await Amount(3,1)==3 && await Amount(3,3)==7,"Opposite-direction transfers lost stock.");
    Pass("Opposite-direction transfers complete without deadlock or lost stock");

    var multiple=Header(208,1,1);
    multiple["items"]=JsonNode.Parse("[{\"item_id\":1,\"amount\":1},{\"item_id\":4,\"amount\":4}]");
    Empty(await api.SendAsync(HttpMethod.Post,"/api/v1/transfers",multiple.ToJsonString()),HttpStatusCode.Created);
    before=await Snapshot();
    Status(await api.SendAsync(HttpMethod.Put,"/api/v1/transfers/208/commit"),HttpStatusCode.Conflict);
    Ensure(JsonNode.DeepEquals(before,await Snapshot()),"A shortage on the second article partially moved the first article.");
    Pass("A shortage in a later transfer item leaves every earlier item, destination and status unchanged");

    await Add(209,1,1);sourceBefore=await Amount(1,1);destinationBefore=await Amount(1,2);
    var race=await Task.WhenAll(api.SendAsync(HttpMethod.Put,"/api/v1/transfers/209/commit"),api.SendAsync(HttpMethod.Delete,"/api/v1/transfers/209"));
    if(race[0].Status==HttpStatusCode.OK)
    {
        Status(race[1],HttpStatusCode.Conflict);
        Ensure(await Amount(1,1)==sourceBefore-1 && await Amount(1,2)==destinationBefore+1,"Winning commit lost its stock movement.");
    }
    else
    {
        Status(race[0],HttpStatusCode.NotFound);Empty(race[1],HttpStatusCode.OK);
        Ensure(await Amount(1,1)==sourceBefore && await Amount(1,2)==destinationBefore,"Deleted Scheduled transfer moved stock.");
    }
    Pass("Commit versus DELETE serializes: either Processed history survives or deletion prevents all stock movement");

    var swagger=await api.SendAsync(HttpMethod.Get,"/swagger/v1/swagger.json");Status(swagger,HttpStatusCode.OK);
    foreach(var path in new[]{"/api/v1/transfers","/api/v1/transfers/{id}","/api/v1/transfers/{id}/items","/api/v1/transfers/{id}/commit"})
        Ensure(swagger.Json.GetProperty("paths").TryGetProperty(path,out _),"Missing OpenAPI path: "+path);
    Status(await api.SendAsync(HttpMethod.Patch,"/api/v1/transfers/2","{}"),HttpStatusCode.MethodNotAllowed);
    Status(await api.SendAsync(HttpMethod.Get,"/api/v1/transfers/2/unknown"),HttpStatusCode.NotFound);
    Pass("All transfer route families appear in OpenAPI; unsupported methods/subroutes return 405/404");
    before=await Snapshot();await api.RestartAsync();
    Ensure(JsonNode.DeepEquals(before,await Snapshot()),"Final state changed after process restart.");
    Pass("Final concurrent and processed state survives another actual process restart");
    await using(var db=Context())
    {
        Ensure(!await db.TransferItems.AnyAsync(x=>!db.Transfers.Any(t=>t.Id==x.TransferId)),"Orphan transfer items.");
        Ensure(!await db.Inventories.AnyAsync(x=>x.QuantityOnHand<0 || x.QuantityAllocated>x.QuantityOnHand),"Invalid final inventory.");
    }
    Pass("Final database has no orphan transfer items or invalid stock allocations");
    Console.WriteLine($"PASS: {count} transfer acceptance checks.");
}
finally
{
    await api.StopAsync();await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)",admin).ExecuteNonQueryAsync();
}

CargoHubDbContext Context()=>new(new DbContextOptionsBuilder<CargoHubDbContext>().UseNpgsql(connection).Options);
void Pass(string text){count++;Console.WriteLine("PASS: "+text);}
static void Ensure(bool condition,string text){if(!condition)throw new Exception(text);}
static void Status(HttpResult r,HttpStatusCode status)=>Ensure(r.Status==status,$"Expected {(int)status}; got {(int)r.Status}: {r.Body}");
static void Empty(HttpResult r,HttpStatusCode status){Status(r,status);Ensure(r.Body.Length==0,"Legacy success response must have an empty body.");}
JsonNode Header(int id,int itemId=1,int amount=1,int from=1,int to=2)=>JsonSerializer.SerializeToNode(new
    {id,reference="Transfer test "+id,from_location_id=from,to_location_id=to,transfer_status="Scheduled",items=new[]{new{item_id=itemId,amount}}})!;
async Task Add(int id,int itemId,int amount,int from=1,int to=2)=>Empty(await api.SendAsync(HttpMethod.Post,"/api/v1/transfers",Header(id,itemId,amount,from,to).ToJsonString()),HttpStatusCode.Created);
async Task Sql(string sql){await using var db=Context();await db.Database.ExecuteSqlRawAsync(sql);}
async Task Stock(int item,int location,int quantity,int allocated=0)
{
    await using var db=Context();
    db.Inventories.Add(new Inventory{ItemId=item,LocationId=location,QuantityOnHand=quantity,QuantityAllocated=allocated,CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});
    await db.SaveChangesAsync();
}
async Task<int> Amount(int item,int location){await using var db=Context();return await db.Inventories.Where(x=>x.ItemId==item && x.LocationId==location).Select(x=>x.QuantityOnHand).SingleAsync();}
async Task<JsonNode> Snapshot()
{
    var response=await api.SendAsync(HttpMethod.Get,"/api/v1/transfers");Status(response,HttpStatusCode.OK);
    await using var db=Context();var stock=await db.Inventories.AsNoTracking().OrderBy(x=>x.ItemId).ThenBy(x=>x.LocationId).ToListAsync();
    return new JsonObject{["transfers"]=JsonNode.Parse(response.Body),["inventory"]=JsonSerializer.SerializeToNode(stock,options)};
}
async Task ExpectSnapshot(JsonNode expected,string description)
{
    Equal(await Snapshot(),expected,description);
    foreach(var transfer in expected["transfers"]!.AsArray())
    {
        var id=transfer!["id"]!.GetValue<int>();
        var detail=await api.SendAsync(HttpMethod.Get,$"/api/v1/transfers/{id}");Status(detail,HttpStatusCode.OK);
        Equal(JsonNode.Parse(detail.Body)!,transfer,"Transfer detail must match Python field names and values");
        var items=await api.SendAsync(HttpMethod.Get,$"/api/v1/transfers/{id}/items");Status(items,HttpStatusCode.OK);
        Equal(JsonNode.Parse(items.Body)!,transfer["items"]!,"Item response must contain item_id and amount only");
    }
    Pass(description+" — HTTP and PostgreSQL match original Python models/handler");
}
static void Equal(JsonNode actual,JsonNode expected,string text)
{
    JsonNode? Normalize(JsonNode? n)
    {
        if(n is JsonObject o)return new JsonObject(o.Where(kv=>kv.Key is not "created_at" and not "updated_at").OrderBy(kv=>kv.Key).Select(kv=>new KeyValuePair<string,JsonNode?>(kv.Key,Normalize(kv.Value))));
        if(n is JsonArray a)return new JsonArray(a.Select(Normalize).ToArray());
        return n?.DeepClone();
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
    public ConcurrentQueue<string> Notifications { get; } = new();
    public async Task WaitNotification(string message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!Notifications.Any(x => x.Contains(message))) await Task.Delay(10, timeout.Token);
    }
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
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "CargoHub.TransferChecks.runtimeconfig.json"), "--depsfile", Path.Combine(AppContext.BaseDirectory, "CargoHub.TransferChecks.deps.json"), typeof(TransfersController).Assembly.Location, "--urls", "http://127.0.0.1:0" })
            info.ArgumentList.Add(argument);
        info.Environment["ConnectionStrings__CargoHub"] = connection;
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["Logging__LogLevel__Default"] = "Warning";
        info.Environment["Logging__LogLevel__CargoHub.Api.Services.TransferService"] = "Information";
        info.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        void ReadLog(object sender, DataReceivedEventArgs args)
        {
            if (args.Data is null) return;
            logs.Enqueue(args.Data);
            if (args.Data.Contains("Scheduled batch transfer ") || args.Data.Contains("Processed batch transfer with id:"))
                Notifications.Enqueue(args.Data);
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
