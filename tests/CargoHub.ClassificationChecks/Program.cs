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

// Match the existing schema verifier: only use a dedicated local test instance.
var adminConnection = Environment.GetEnvironmentVariable("CARGOHUB_TEST_ADMIN_CONNECTION")
    ?? throw new InvalidOperationException("Set CARGOHUB_TEST_ADMIN_CONNECTION to a dedicated PostgreSQL test instance with CREATEDB permission.");
var repository = FindRepository();
var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
var database = "ch029_" + Guid.NewGuid().ToString("N");
var connection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database, Pooling = false }.ConnectionString;
var classifications = new[]
{
    (Resource: "item_groups", Table: "item_group", ForeignKey: "item_group_id"),
    (Resource: "item_lines", Table: "item_line", ForeignKey: "item_line_id"),
    (Resource: "item_types", Table: "item_type", ForeignKey: "item_type_id"),
};
var passed = 0;
await using var admin = new NpgsqlConnection(adminConnection);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin).ExecuteNonQueryAsync();
await using var api = new ApiProcess(connection, Path.Combine(repository, "api-csharp"));
try
{
    await using (var db = CreateContext())
    {
        Ensure(!db.Database.HasPendingModelChanges(), "DbSets must not change the EF Core schema.");
        await db.Database.MigrateAsync();
        db.Warehouses.AddRange(ReadFixture<Warehouse>("warehouse"));
        db.Locations.AddRange(ReadFixture<Location>("location"));
        db.Set<Supplier>().AddRange(ReadFixture<Supplier>("supplier"));
        db.ItemGroups.AddRange(ReadFixture<ItemGroup>("item_group"));
        db.ItemLines.AddRange(ReadFixture<ItemLine>("item_line"));
        db.ItemTypes.AddRange(ReadFixture<ItemType>("item_type"));
        var items = JsonNode.Parse(File.ReadAllText(Path.Combine(repository, "data", "item.json")))!.AsArray();
        foreach (var item in items)
            item!["commodity_code"] = item["commodity_code"]!.ToString();
        db.Items.AddRange(items.Deserialize<List<Item>>(jsonOptions)!);
        db.Inventories.AddRange(ReadFixture<Inventory>("inventory"));
        await db.SaveChangesAsync();
    }
    await using (var sql = new NpgsqlConnection(connection))
    {
        await sql.OpenAsync();
        foreach (var table in new[] { "warehouse", "location", "supplier", "item_group", "item_line", "item_type", "item" })
            await new NpgsqlCommand($"SELECT setval(pg_get_serial_sequence('\"{table}\"', 'id'), (SELECT MAX(id) FROM \"{table}\"), true)", sql).ExecuteNonQueryAsync();
    }
    Pass("Existing migrations and unmodified JSON fixtures loaded; no new migration required");
    await api.StartAsync();
    var sourceItems = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "data", "item.json"))).RootElement;

    foreach (var classification in classifications)
    {
        var path = "/api/v1/" + classification.Resource;
        var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "data", classification.Table + ".json"))).RootElement;
        var first = source[0];
        var firstId = first.GetProperty("id").GetInt32();
        const int createdId = 900001;
        const int missingId = 900099;

        var list = await api.SendAsync(HttpMethod.Get, path);
        ExpectStatus(list, HttpStatusCode.OK);
        var records = list.Json;
        Ensure(records.GetArrayLength() == source.GetArrayLength(), "Initial list count differs from JSON fixture.");
        var expected = source.EnumerateArray().OrderBy(x => x.GetProperty("id").GetInt32()).ToArray();
        for (var i = 0; i < expected.Length; i++)
            ExpectClassification(records[i], expected[i]);
        Pass($"{classification.Resource}: GET list matches all original JSON records and five snake_case fields");

        var detail = await api.SendAsync(HttpMethod.Get, $"{path}/{firstId}");
        ExpectStatus(detail, HttpStatusCode.OK);
        ExpectClassification(detail.Json, first);
        Pass($"{classification.Resource}: GET existing ID preserves values and timestamps");

        var missing = await api.SendAsync(HttpMethod.Get, $"{path}/{missingId}");
        ExpectStatus(missing, HttpStatusCode.OK);
        Ensure(missing.Json.ValueKind == JsonValueKind.Null && missing.ContentType == "application/json", "Missing ID must return JSON null with HTTP 200, not 204.");
        Pass($"{classification.Resource}: GET missing ID returns 200 with JSON null");

        var itemIds = sourceItems.EnumerateArray().Where(item => item.GetProperty(classification.ForeignKey).GetInt32() == firstId)
            .Select(item => item.GetProperty("id").GetInt32()).Order().ToArray();
        Ensure(itemIds.Length > 0, "Fixture must contain references for the classification.");
        var membership = await api.SendAsync(HttpMethod.Get, $"{path}/{firstId}/items");
        ExpectStatus(membership, HttpStatusCode.OK);
        Ensure(membership.Json.EnumerateArray().Select(id => id.GetInt32()).Order().SequenceEqual(itemIds), "Nested route must return item IDs, not item objects.");
        Pass($"{classification.Resource}: Artur's existing /items route returns the matching integer IDs without route ambiguity");

        var missingMembers = await api.SendAsync(HttpMethod.Get, $"{path}/{missingId}/items");
        ExpectStatus(missingMembers, HttpStatusCode.OK);
        Ensure(missingMembers.Json.GetArrayLength() == 0, "Unknown classification must have no item IDs.");
        Pass($"{classification.Resource}: unknown /items route returns an empty JSON array");

        var invalidId = await api.SendAsync(HttpMethod.Get, path + "/not-an-integer");
        ExpectStatus(invalidId, HttpStatusCode.NotFound);
        Pass($"{classification.Resource}: invalid integer route returns 404, consistent with Artur's route constraints");

        var body = JsonSerializer.Serialize(new
        {
            id = createdId, name = "CH-029 \"Vers\"", description = "Classificatie met accenten: café en groente.",
            created_at = "1970-01-01T00:00:00Z", updated_at = "1970-01-01T00:00:00Z"
        });
        var post = await api.SendAsync(HttpMethod.Post, path, body);
        ExpectEmpty(post, HttpStatusCode.Created);
        Pass($"{classification.Resource}: POST returns the original empty 201 response");
        var created = await api.SendAsync(HttpMethod.Get, $"{path}/{createdId}");
        ExpectStatus(created, HttpStatusCode.OK);
        ExpectFields(created.Json);
        Ensure(created.Json.GetProperty("id").GetInt32() == createdId, "Explicit source ID was not preserved.");
        Ensure(created.Json.GetProperty("name").GetString() == "CH-029 \"Vers\"" && created.Json.GetProperty("description").GetString() == "Classificatie met accenten: café en groente.", "POST values changed.");
        var createdAt = created.Json.GetProperty("created_at").GetDateTimeOffset();
        Ensure(createdAt > DateTimeOffset.UtcNow.AddMinutes(-5) && created.Json.GetProperty("updated_at").GetDateTimeOffset() == createdAt, "POST must generate both timestamps on the server.");
        Pass($"{classification.Resource}: POST persists all fields with server-generated UTC timestamps");

        await using (var check = new NpgsqlConnection(connection))
        {
            await check.OpenAsync();
            await using var command = new NpgsqlCommand($"SELECT name FROM \"{classification.Table}\" WHERE id = @id", check);
            command.Parameters.AddWithValue("id", createdId);
            Ensure(Equals(await command.ExecuteScalarAsync(), "CH-029 \"Vers\""), "Independent database read did not find POST data.");
        }
        Pass($"{classification.Resource}: independent PostgreSQL connection confirms persisted POST data");

        var duplicate = await api.SendAsync(HttpMethod.Post, path, body);
        ExpectStatus(duplicate, HttpStatusCode.Conflict);
        var afterDuplicate = await api.SendAsync(HttpMethod.Get, $"{path}/{createdId}");
        ExpectClassification(afterDuplicate.Json, created.Json);
        Pass($"{classification.Resource}: duplicate ID returns 409 and preserves the original record");

        var automatic = await api.SendAsync(HttpMethod.Post, path, "{\"name\":\"CH-029 automatic\",\"description\":\"Database-generated ID\"}");
        ExpectEmpty(automatic, HttpStatusCode.Created);
        var withAutomatic = await api.SendAsync(HttpMethod.Get, path);
        var autoRecord = withAutomatic.Json.EnumerateArray().Single(row => row.GetProperty("name").GetString() == "CH-029 automatic");
        var autoId = autoRecord.GetProperty("id").GetInt32();
        Ensure(autoId > expected.Max(row => row.GetProperty("id").GetInt32()) && autoId != createdId, "Generated ID collides with fixture IDs.");
        Pass($"{classification.Resource}: POST without an ID generates a usable database ID");

        var updated = await api.SendAsync(HttpMethod.Put, $"{path}/{createdId}", JsonSerializer.Serialize(new
        {
            id = firstId, name = "CH-029 updated", description = "Nieuwe omschrijving",
            created_at = "1970-01-01T00:00:00Z", updated_at = "1970-01-01T00:00:00Z"
        }));
        ExpectEmpty(updated, HttpStatusCode.OK);
        var afterUpdate = await api.SendAsync(HttpMethod.Get, $"{path}/{createdId}");
        Ensure(afterUpdate.Json.GetProperty("id").GetInt32() == createdId && afterUpdate.Json.GetProperty("name").GetString() == "CH-029 updated" && afterUpdate.Json.GetProperty("description").GetString() == "Nieuwe omschrijving", "PUT must use the path ID and update editable fields.");
        Ensure(afterUpdate.Json.GetProperty("created_at").GetDateTimeOffset() == createdAt && afterUpdate.Json.GetProperty("updated_at").GetDateTimeOffset() >= createdAt, "PUT must preserve created_at and refresh updated_at.");
        var unchanged = await api.SendAsync(HttpMethod.Get, $"{path}/{firstId}");
        ExpectClassification(unchanged.Json, first);
        Pass($"{classification.Resource}: PUT returns empty 200, updates fields and preserves path ID/created_at");

        await api.RestartAsync();
        var afterRestart = await api.SendAsync(HttpMethod.Get, $"{path}/{createdId}");
        ExpectStatus(afterRestart, HttpStatusCode.OK);
        ExpectClassification(afterRestart.Json, afterUpdate.Json);
        Pass($"{classification.Resource}: values survive an actual API process restart");

        var missingUpdate = await api.SendAsync(HttpMethod.Put, $"{path}/{missingId}", "{\"name\":\"Not inserted\",\"description\":\"Missing ID\"}");
        ExpectEmpty(missingUpdate, HttpStatusCode.OK);
        var stillMissing = await api.SendAsync(HttpMethod.Get, $"{path}/{missingId}");
        Ensure(stillMissing.Json.ValueKind == JsonValueKind.Null, "PUT to missing ID must not insert a record.");
        Pass($"{classification.Resource}: PUT missing ID preserves empty 200/no-op behavior");

        foreach (var invalid in new[] { "{", "[]", "null", "{\"name\":null,\"description\":\"Invalid\"}" })
        {
            var invalidPost = await api.SendAsync(HttpMethod.Post, path, invalid);
            ExpectStatus(invalidPost, HttpStatusCode.BadRequest);
        }
        Pass($"{classification.Resource}: malformed JSON, array, null body and null required field return 400");
        var invalidPut = await api.SendAsync(HttpMethod.Put, $"{path}/{createdId}", "{");
        ExpectStatus(invalidPut, HttpStatusCode.BadRequest);
        var afterInvalidPut = await api.SendAsync(HttpMethod.Get, $"{path}/{createdId}");
        ExpectClassification(afterInvalidPut.Json, afterRestart.Json);
        Pass($"{classification.Resource}: malformed PUT returns 400 without modifying data");

        var inUse = await api.SendAsync(HttpMethod.Delete, $"{path}/{firstId}");
        ExpectStatus(inUse, HttpStatusCode.Conflict);
        var retained = await api.SendAsync(HttpMethod.Get, $"{path}/{firstId}");
        ExpectClassification(retained.Json, first);
        var retainedItems = await api.SendAsync(HttpMethod.Get, $"{path}/{firstId}/items");
        Ensure(retainedItems.Json.EnumerateArray().Select(item => item.GetInt32()).Order().SequenceEqual(itemIds), "Rejected deletion must preserve item relationships.");
        Pass($"{classification.Resource}: referenced classification returns 409 without losing records or item links");

        foreach (var id in new[] { createdId, autoId })
        {
            ExpectEmpty(await api.SendAsync(HttpMethod.Delete, $"{path}/{id}"), HttpStatusCode.OK);
            var deleted = await api.SendAsync(HttpMethod.Get, $"{path}/{id}");
            ExpectStatus(deleted, HttpStatusCode.OK);
            Ensure(deleted.Json.ValueKind == JsonValueKind.Null, "Deleted record must not reappear.");
        }
        Pass($"{classification.Resource}: DELETE returns empty 200 and permanently removes unreferenced records");
        ExpectEmpty(await api.SendAsync(HttpMethod.Delete, $"{path}/{createdId}"), HttpStatusCode.OK);
        Pass($"{classification.Resource}: DELETE missing ID remains an empty 200/no-op");
        ExpectStatus(await api.SendAsync(HttpMethod.Patch, $"{path}/{firstId}", "{}"), HttpStatusCode.MethodNotAllowed);
        ExpectStatus(await api.SendAsync(HttpMethod.Get, $"{path}/{firstId}/unknown"), HttpStatusCode.NotFound);
        Pass($"{classification.Resource}: unsupported method/subroute return 405/404");
        var restored = await api.SendAsync(HttpMethod.Get, path);
        Ensure(restored.Json.GetArrayLength() == expected.Length, "CRUD tests must restore the fixture record count.");
        Pass($"{classification.Resource}: complete CRUD cycle preserves the original dataset");
    }

    foreach (var (resource, file) in new[] { ("warehouses", "warehouse"), ("locations", "location"), ("items", "item"), ("inventories", "inventory") })
    {
        var response = await api.SendAsync(HttpMethod.Get, "/api/v1/" + resource);
        ExpectStatus(response, HttpStatusCode.OK);
        var count = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "data", file + ".json"))).RootElement.GetArrayLength();
        Ensure(response.Json.GetArrayLength() == count, "Existing controller list count changed.");
        Pass($"Artur regression: GET /api/v1/{resource} returns its original fixture count ({count})");
    }
    foreach (var classification in classifications)
    {
        var response = await api.SendAsync(HttpMethod.Get, $"/api/v1/items?{classification.ForeignKey.Replace("_id", "")}=1");
        ExpectStatus(response, HttpStatusCode.OK);
        var expected = sourceItems.EnumerateArray().Where(item => item.GetProperty(classification.ForeignKey).GetInt32() == 1).Select(item => item.GetProperty("id").GetInt32()).Order();
        Ensure(response.Json.EnumerateArray().Select(item => item.GetProperty("id").GetInt32()).Order().SequenceEqual(expected), "Existing item filter changed.");
        Pass($"Artur regression: item filter for {classification.Resource} still matches source JSON");
    }
    Console.WriteLine($"PASS: {passed} checks, real HTTP + PostgreSQL; temporary database will be removed.");
}
finally
{
    await api.StopAsync();
    NpgsqlConnection.ClearAllPools();
    await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
}

CargoHubDbContext CreateContext() => new(new DbContextOptionsBuilder<CargoHubDbContext>().UseNpgsql(connection).Options);
List<T> ReadFixture<T>(string name) => JsonSerializer.Deserialize<List<T>>(File.ReadAllText(Path.Combine(repository, "data", name + ".json")), jsonOptions)!;
void Pass(string message) { passed++; Console.WriteLine($"PASS {passed}: {message}"); }
static void Ensure(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void ExpectStatus(HttpResult response, HttpStatusCode expected) => Ensure(response.Status == expected, $"Expected HTTP {(int)expected}, got {(int)response.Status}: {response.Body}");
static void ExpectEmpty(HttpResult response, HttpStatusCode expected)
{
    ExpectStatus(response, expected);
    Ensure(response.Body.Length == 0, $"HTTP {(int)expected} must have an empty body.");
}
static void ExpectFields(JsonElement record)
{
    var expected = new[] { "created_at", "description", "id", "name", "updated_at" };
    Ensure(record.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(expected), "Classification JSON field names or shape changed.");
}
static void ExpectClassification(JsonElement actual, JsonElement expected)
{
    ExpectFields(actual);
    foreach (var name in new[] { "id", "name", "description" })
        Ensure(JsonElement.DeepEquals(actual.GetProperty(name), expected.GetProperty(name)), "Classification field differs: " + name);
    foreach (var name in new[] { "created_at", "updated_at" })
        Ensure(actual.GetProperty(name).GetDateTimeOffset() == expected.GetProperty(name).GetDateTimeOffset(), "Timestamp instant differs: " + name);
}
static string FindRepository()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "api-csharp", "CargoHub.Api.csproj"))) return directory.FullName;
    throw new DirectoryNotFoundException("Run this verifier from a repository checkout containing data/ and api-csharp/.");
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
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "CargoHub.ClassificationChecks.runtimeconfig.json"), "--depsfile", Path.Combine(AppContext.BaseDirectory, "CargoHub.ClassificationChecks.deps.json"), typeof(ItemGroupsController).Assembly.Location, "--urls", "http://127.0.0.1:0" })
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
