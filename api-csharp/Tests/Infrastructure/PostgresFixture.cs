using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CargoHub.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Geeft de tests één eigen PostgreSQL-database met actuele migraties en kleine testdata.
/// Alleen de zelf aangemaakte database wordt na afloop verwijderd.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string databaseName = "cargohub_ci_" + Guid.NewGuid().ToString("N");
    private string adminConnection = string.Empty;
    private bool databaseCreated;

    public string ConnectionString { get; private set; } = string.Empty;
    public ApiFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public CargoHubDbContext CreateContext() => new(
        new DbContextOptionsBuilder<CargoHubDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        adminConnection = Environment.GetEnvironmentVariable("CARGOHUB_TEST_ADMIN_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set CARGOHUB_TEST_ADMIN_CONNECTION to a dedicated PostgreSQL test server with CREATEDB rights. See Tests/README.md.");
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnection)
        {
            Database = databaseName,
            Pooling = false
        }.ConnectionString;

        try
        {
            await ExecuteAdminAsync($"CREATE DATABASE \"{databaseName}\"");
            databaseCreated = true;
            await using var db = CreateContext();
            await db.Database.MigrateAsync();
            db.Warehouses.Add(new Warehouse { Id = 1, Code = "CI-WH", Name = "CI warehouse" });
            db.Locations.AddRange(
                new Location { Id = 1, WarehouseId = 1, Code = "SOURCE" },
                new Location { Id = 2, WarehouseId = 1, Code = "DESTINATION" });
            db.Suppliers.Add(new Supplier { Id = 1, Code = "CI-SUP" });
            db.ItemGroups.Add(new ItemGroup { Id = 1, Name = "CI group" });
            db.ItemLines.Add(new ItemLine { Id = 1, Name = "CI line" });
            db.ItemTypes.Add(new ItemType { Id = 1, Name = "CI type" });
            db.Items.Add(new Item
            {
                Id = 1, Code = "CI-ITEM", SupplierId = 1,
                ItemGroupId = 1, ItemLineId = 1, ItemTypeId = 1
            });
            await db.SaveChangesAsync();
            Factory = new ApiFactory(ConnectionString);
            Client = Factory.CreateClient();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null)
            await Factory.DisposeAsync();
        if (databaseCreated)
        {
            await ExecuteAdminAsync($"DROP DATABASE \"{databaseName}\" WITH (FORCE)");
            databaseCreated = false;
        }
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Deelt de tijdelijke database en voorkomt gelijktijdige wijzigingen tussen testklassen.
/// Tests gebruiken daarnaast eigen IDs en zijn niet afhankelijk van uitvoervolgorde.
/// </summary>
[CollectionDefinition("PostgreSQL API", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
