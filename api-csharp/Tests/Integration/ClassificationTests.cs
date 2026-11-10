using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CargoHub.Api.IntegrationTests.Infrastructure;

namespace CargoHub.Api.IntegrationTests.Integration;

/// <summary>Test de artikelgroepen, artikellijnen en artikeltypen via de echte HTTP-pipeline en PostgreSQL.</summary>
[Collection("PostgreSQL API")]
public sealed class ClassificationTests(PostgresFixture fixture)
{
    /// <summary>
    /// Controleert de volledige CRUD-cyclus: POST 201, GET 200, PUT 200 en DELETE 200.
    /// Controleert server-timestamps, behoud van route-ID/created_at en opslag in een nieuwe API-host.
    /// </summary>
    [Theory]
    [InlineData("item_groups")]
    [InlineData("item_lines")]
    [InlineData("item_types")]
    public async Task Crud_PersistsChangesAndPreservesIdentity(string resource)
    {
        const int id = 900001;
        var path = $"/api/v1/{resource}";
        var before = DateTimeOffset.UtcNow;
        using var created = await fixture.Client.PostAsJsonAsync(path, new
        {
            id, name = "CI original", description = "Created by integration test",
            created_at = "2000-01-01T00:00:00Z", updated_at = "2000-01-01T00:00:00Z"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Empty(await created.Content.ReadAsStringAsync());

        var initial = await ReadAsync(fixture.Client, $"{path}/{id}");
        var createdAt = initial.GetProperty("created_at").GetDateTimeOffset();
        Assert.InRange(createdAt, before, DateTimeOffset.UtcNow);
        Assert.Equal(createdAt, initial.GetProperty("updated_at").GetDateTimeOffset());
        Assert.Equal("CI original", initial.GetProperty("name").GetString());

        using var updated = await fixture.Client.PutAsJsonAsync($"{path}/{id}", new
        {
            id = 999999, name = "CI updated", description = "Saved in PostgreSQL",
            created_at = "2099-01-01T00:00:00Z"
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        // A second host shares only PostgreSQL, not a controller or tracked EF entity.
        await using var restarted = new ApiFactory(fixture.ConnectionString);
        using var client = restarted.CreateClient();
        var persisted = await ReadAsync(client, $"{path}/{id}");
        Assert.Equal(id, persisted.GetProperty("id").GetInt32());
        Assert.Equal(createdAt, persisted.GetProperty("created_at").GetDateTimeOffset());
        Assert.True(persisted.GetProperty("updated_at").GetDateTimeOffset() >= createdAt);
        Assert.Equal("CI updated", persisted.GetProperty("name").GetString());
        Assert.Equal("Saved in PostgreSQL", persisted.GetProperty("description").GetString());

        using var deleted = await client.DeleteAsync($"{path}/{id}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var missing = await ReadAsync(fixture.Client, $"{path}/{id}");
        Assert.Equal(JsonValueKind.Null, missing.ValueKind);
    }

    /// <summary>Controleert dat dubbele IDs 409 geven en de oorspronkelijke gegevens behouden blijven.</summary>
    [Theory]
    [InlineData("item_groups")]
    [InlineData("item_lines")]
    [InlineData("item_types")]
    public async Task PostDuplicateId_Returns409AndPreservesOriginal(string resource)
    {
        var path = $"/api/v1/{resource}";
        var original = await ReadAsync(fixture.Client, path + "/1");
        using var response = await fixture.Client.PostAsJsonAsync(path, new { id = 1, name = "Overwrite" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(original.GetProperty("name").GetString(),
            (await ReadAsync(fixture.Client, path + "/1")).GetProperty("name").GetString());
    }

    /// <summary>Controleert dat een classificatie die door een artikel wordt gebruikt niet verwijderd wordt: 409.</summary>
    [Theory]
    [InlineData("item_groups")]
    [InlineData("item_lines")]
    [InlineData("item_types")]
    public async Task DeleteReferencedClassification_Returns409AndPreservesRecord(string resource)
    {
        var path = $"/api/v1/{resource}/1";
        using var response = await fixture.Client.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, (await ReadAsync(fixture.Client, path)).GetProperty("id").GetInt32());
    }

    /// <summary>Controleert dat ongeldige JSON via modelbinding 400 geeft en geen record aanmaakt.</summary>
    [Theory]
    [InlineData("item_groups")]
    [InlineData("item_lines")]
    [InlineData("item_types")]
    public async Task PostMalformedJson_Returns400WithoutCreatingRecord(string resource)
    {
        using var content = new StringContent("{\"id\":900002,", Encoding.UTF8, "application/json");
        using var response = await fixture.Client.PostAsync($"/api/v1/{resource}", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(JsonValueKind.Null,
            (await ReadAsync(fixture.Client, $"/api/v1/{resource}/900002")).ValueKind);
    }

    /// <summary>Controleert behoud van het legacy-gedrag: onbekend ID geeft GET 200/null en PUT/DELETE 200 zonder aanmaak.</summary>
    [Theory]
    [InlineData("item_groups")]
    [InlineData("item_lines")]
    [InlineData("item_types")]
    public async Task MissingId_PreservesLegacyNullAndNoOpResponses(string resource)
    {
        var path = $"/api/v1/{resource}/900003";
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(fixture.Client, path)).ValueKind);
        using var updated = await fixture.Client.PutAsJsonAsync(path, new { name = "Must not be created" });
        using var deleted = await fixture.Client.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(fixture.Client, path)).ValueKind);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
