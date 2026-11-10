using System.Net;
using System.Text.Json;
using CargoHub.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.IntegrationTests.Integration;

/// <summary>Controleert of de API met het actuele databaseschema kan starten en leesrequests verwerkt.</summary>
[Collection("PostgreSQL API")]
public sealed class ApiSmokeTests(PostgresFixture fixture)
{
    /// <summary>
    /// Controleert voor elk hoofdonderdeel dat de echte HTTP-route 200 en een JSON-lijst teruggeeft.
    /// Hiermee detecteert CI onder meer kapotte routes, dependency injection en databasequeries.
    /// </summary>
    [Theory]
    [InlineData("warehouses")]
    [InlineData("locations")]
    [InlineData("items")]
    [InlineData("inventories")]
    [InlineData("clients")]
    [InlineData("suppliers")]
    [InlineData("orders")]
    [InlineData("shipments")]
    [InlineData("transfers")]
    [InlineData("item_groups")]
    [InlineData("item_lines")]
    [InlineData("item_types")]
    public async Task GetCollection_Returns200AndJsonArray(string resource)
    {
        using var response = await fixture.Client.GetAsync($"/api/v1/{resource}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
    }

    /// <summary>
    /// Controleert dat magazijngegevens via HTTP uit PostgreSQL komen en snake_case veldnamen behouden.
    /// </summary>
    [Fact]
    public async Task GetWarehouse_ReturnsSeededDataWithSnakeCaseFields()
    {
        using var response = await fixture.Client.GetAsync("/api/v1/warehouses/1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CI warehouse", json.RootElement.GetProperty("name").GetString());
        Assert.True(json.RootElement.TryGetProperty("created_at", out _));
        Assert.True(json.RootElement.TryGetProperty("contact_email", out _));
        Assert.False(json.RootElement.TryGetProperty("CreatedAt", out _));
    }

    /// <summary>
    /// Controleert dat onbekende routes en de legacy-voorraadroute met één ID 404 geven.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/warehouses/999999")]
    [InlineData("/api/v1/inventories/1")]
    [InlineData("/api/v1/does-not-exist")]
    public async Task MissingResource_Returns404(string path)
    {
        using var response = await fixture.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Controleert dat alle migraties zijn toegepast en het EF-model overeenkomt met de migratiesnapshot.
    /// De fixture bouwt de database vanaf nul, zodat een onvolledige migratie de test laat falen.
    /// </summary>
    [Fact]
    public async Task Database_AllMigrationsAppliedAndModelMatchesSnapshot()
    {
        await using var db = fixture.CreateContext();
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    /// <summary>
    /// Controleert dat Swagger de migratieroutes bevat en opgegeven operation IDs uniek zijn.
    /// Een operationId is optioneel in OpenAPI; ontbrekende IDs zijn daarom geen testfout.
    /// </summary>
    [Fact]
    public async Task Swagger_ContainsMigrationRoutesAndUniqueDeclaredOperationIds()
    {
        using var response = await fixture.Client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        foreach (var resource in new[] { "item_groups", "item_lines", "item_types" })
        {
            Assert.True(paths.GetProperty($"/api/v1/{resource}").TryGetProperty("post", out _));
            Assert.True(paths.GetProperty($"/api/v1/{resource}/{{id}}").TryGetProperty("put", out _));
        }
        Assert.True(paths.GetProperty("/api/v1/transfers/{id}/commit").TryGetProperty("put", out _));
        var httpMethods = new HashSet<string> { "get", "post", "put", "patch", "delete", "head", "options", "trace" };
        var operations = paths.EnumerateObject().SelectMany(path => path.Value.EnumerateObject())
            .Where(method => httpMethods.Contains(method.Name)).ToList();
        var operationIds = operations.Where(method => method.Value.TryGetProperty("operationId", out _))
            .Select(method => method.Value.GetProperty("operationId").GetString()).ToList();
        Assert.NotEmpty(operationIds);
        Assert.All(operationIds, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(operationIds.Count, operationIds.Distinct().Count());
    }

    /// <summary>Controleert dat Swagger met de productie-instelling uitgeschakeld niet bereikbaar is.</summary>
    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Swagger_WhenDisabledInProduction_Returns404(string path)
    {
        await using var factory = new ApiFactory(fixture.ConnectionString, swaggerEnabled: false);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
