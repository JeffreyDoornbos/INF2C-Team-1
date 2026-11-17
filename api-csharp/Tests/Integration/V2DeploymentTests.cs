using System.Net;
using System.Text.Json;
using CargoHub.Api.IntegrationTests.Infrastructure;

namespace CargoHub.Api.IntegrationTests.Integration;

/// <summary>Controleert dezelfde V2-routing en Swagger-instellingen als op de Docker-server.</summary>
[Collection("PostgreSQL API")]
public sealed class V2DeploymentTests(PostgresFixture fixture)
{
    /// <summary>Controleert dat de C#-host onder V2 reageert en zelf geen V1-controllerroute meer aanbiedt.</summary>
    [Fact]
    public async Task V2Host_UsesVersionedRoutesAndKeepsLegacyPrefixFree()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString, apiVersion: "v2", swaggerPrefix: "api/v2/swagger");
        using var client = factory.CreateClient();
        using var current = await client.GetAsync("/api/v2/warehouses");
        using var legacy = await client.GetAsync("/api/v1/warehouses");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, legacy.StatusCode);
    }

    /// <summary>Controleert dat de publieke Swagger-URL en alle gedocumenteerde controllerpaden V2 gebruiken.</summary>
    [Fact]
    public async Task V2Swagger_UsesReachableDocumentAndVersionedPaths()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString, apiVersion: "v2", swaggerPrefix: "api/v2/swagger");
        using var client = factory.CreateClient();
        using var ui = await client.GetAsync("/api/v2/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Contains("swagger-ui", await ui.Content.ReadAsStringAsync());
        using var initializer = await client.GetAsync("/api/v2/swagger/index.js");
        Assert.Equal(HttpStatusCode.OK, initializer.StatusCode);
        Assert.Contains("v2/swagger.json", await initializer.Content.ReadAsStringAsync());
        using var response = await client.GetAsync("/api/v2/swagger/v2/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("v2", document.RootElement.GetProperty("info").GetProperty("version").GetString());
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().ToList();
        Assert.Contains(paths, path => path.Name == "/api/v2/warehouses");
        Assert.DoesNotContain(paths, path => path.Name.StartsWith("/api/v1/", StringComparison.Ordinal));
    }

    /// <summary>Controleert dat de gereedheidscheck met PostgreSQL slaagt en de basis-URL V2 teruggeeft.</summary>
    [Fact]
    public async Task V2HealthAndBaseUrl_ReportDatabaseReadinessAndVersion()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString, apiVersion: "v2", swaggerPrefix: "api/v2/swagger");
        using var client = factory.CreateClient();
        using var ready = await client.GetAsync("/health/ready");
        using var root = await client.GetAsync("/api/v2");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        using var result = JsonDocument.Parse(await root.Content.ReadAsStringAsync());
        Assert.Equal("v2", result.RootElement.GetProperty("version").GetString());
        Assert.Equal("/api/v2/swagger/index.html", result.RootElement.GetProperty("swagger").GetString());
    }
}
