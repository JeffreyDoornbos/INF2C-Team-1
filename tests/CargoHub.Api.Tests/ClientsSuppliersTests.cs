
using CargoHub.Api.Controllers;
using CargoHub.Api.Data;
using CargoHub.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoHub.Api.Tests;

public class ClientsSuppliersTests
{
    private static CargoHubDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<CargoHubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new CargoHubDbContext(options);
    }

    private static Client NewClient(int id = 0) => new()
    {
        Id = id,
        Name = "Test Client",
        Address = "Teststraat 1",
        City = "Rotterdam",
        ZipCode = "3011AA",
        Province = "Zuid-Holland",
        Country = "Nederland",
        ContactName = "Testpersoon",
        ContactPhone = "0101234567",
        ContactEmail = "test@example.com"
    };

    private static Supplier NewSupplier(int id = 0) => new()
    {
        Id = id,
        Code = "SUP-001",
        Name = "Test Supplier",
        Address = "Leveranciersstraat 1",
        City = "Rotterdam",
        ZipCode = "3011AA",
        Province = "Zuid-Holland",
        Country = "Nederland",
        ContactName = "Contactpersoon",
        PhoneNumber = "0101234567",
        Reference = "REF-001"
    };

    [Fact]
    public async Task Clients_GetAll_EmptyDatabase_ReturnsEmptyList()
    {
        await using var db = CreateDatabase();
        var controller = new ClientsController(db);

        var result = Assert.IsType<OkObjectResult>(
            await controller.GetClients(CancellationToken.None));

        var clients = Assert.IsAssignableFrom<IEnumerable<Client>>(result.Value);
        Assert.Empty(clients);
    }

    [Fact]
    public async Task Suppliers_GetAll_EmptyDatabase_ReturnsEmptyList()
    {
        await using var db = CreateDatabase();
        var controller = new SuppliersController(db);

        var result = Assert.IsType<OkObjectResult>(
            await controller.GetSuppliers(CancellationToken.None));

        var suppliers = Assert.IsAssignableFrom<IEnumerable<Supplier>>(result.Value);
        Assert.Empty(suppliers);
    }

    [Fact]
    public async Task Clients_Post_CreatesRecordAndReturns201()
    {
        await using var db = CreateDatabase();
        var controller = new ClientsController(db);

        var result = await controller.AddClient(NewClient(), CancellationToken.None);

        Assert.Equal(201, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Single(await db.Clients.ToListAsync());
        Assert.True((await db.Clients.SingleAsync()).CreatedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task Suppliers_Post_CreatesRecordAndReturns201()
    {
        await using var db = CreateDatabase();
        var controller = new SuppliersController(db);

        var result = await controller.AddSupplier(NewSupplier(), CancellationToken.None);

        Assert.Equal(201, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Single(await db.Suppliers.ToListAsync());
        Assert.True((await db.Suppliers.SingleAsync()).CreatedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task Clients_GetById_ReturnsExistingClient()
    {
        await using var db = CreateDatabase();
        db.Clients.Add(NewClient(1));
        await db.SaveChangesAsync();

        var controller = new ClientsController(db);
        var result = Assert.IsType<JsonResult>(
            await controller.GetClient(1, CancellationToken.None));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("Test Client", Assert.IsType<Client>(result.Value).Name);
    }

    [Fact]
    public async Task Suppliers_GetById_ReturnsExistingSupplier()
    {
        await using var db = CreateDatabase();
        db.Suppliers.Add(NewSupplier(1));
        await db.SaveChangesAsync();

        var controller = new SuppliersController(db);
        var result = Assert.IsType<JsonResult>(
            await controller.GetSupplier(1, CancellationToken.None));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("Test Supplier", Assert.IsType<Supplier>(result.Value).Name);
    }

    [Fact]
    public async Task Clients_GetUnknownId_Returns200WithNull()
    {
        await using var db = CreateDatabase();
        var controller = new ClientsController(db);

        var result = Assert.IsType<JsonResult>(
            await controller.GetClient(99999, CancellationToken.None));

        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Suppliers_GetUnknownId_Returns200WithNull()
    {
        await using var db = CreateDatabase();
        var controller = new SuppliersController(db);

        var result = Assert.IsType<JsonResult>(
            await controller.GetSupplier(99999, CancellationToken.None));

        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Clients_Put_UpdatesRecord()
    {
        await using var db = CreateDatabase();
        db.Clients.Add(NewClient(1));
        await db.SaveChangesAsync();

        var controller = new ClientsController(db);
        var updated = NewClient(1);
        updated.Name = "Updated Client";

        var result = await controller.UpdateClient(
            1, updated, CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Equal("Updated Client", (await db.Clients.SingleAsync()).Name);
    }

    [Fact]
    public async Task Suppliers_Put_UpdatesRecord()
    {
        await using var db = CreateDatabase();
        db.Suppliers.Add(NewSupplier(1));
        await db.SaveChangesAsync();

        var controller = new SuppliersController(db);
        var updated = NewSupplier(1);
        updated.Name = "Updated Supplier";

        var result = await controller.UpdateSupplier(
            1, updated, CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Equal("Updated Supplier", (await db.Suppliers.SingleAsync()).Name);
    }

    [Fact]
    public async Task Clients_PutUnknownId_Returns200WithoutCreatingRecord()
    {
        await using var db = CreateDatabase();
        var controller = new ClientsController(db);

        var result = await controller.UpdateClient(
            99999, NewClient(), CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Empty(await db.Clients.ToListAsync());
    }

    [Fact]
    public async Task Suppliers_PutUnknownId_Returns200WithoutCreatingRecord()
    {
        await using var db = CreateDatabase();
        var controller = new SuppliersController(db);

        var result = await controller.UpdateSupplier(
            99999, NewSupplier(), CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Empty(await db.Suppliers.ToListAsync());
    }

    [Fact]
    public async Task Clients_Delete_RemovesRecord()
    {
        await using var db = CreateDatabase();
        db.Clients.Add(NewClient(1));
        await db.SaveChangesAsync();

        var controller = new ClientsController(db);
        var result = await controller.RemoveClient(1, CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Empty(await db.Clients.ToListAsync());
    }

    [Fact]
    public async Task Suppliers_Delete_RemovesRecord()
    {
        await using var db = CreateDatabase();
        db.Suppliers.Add(NewSupplier(1));
        await db.SaveChangesAsync();

        var controller = new SuppliersController(db);
        var result = await controller.RemoveSupplier(1, CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Empty(await db.Suppliers.ToListAsync());
    }

    [Fact]
    public async Task Clients_DeleteUnknownId_Returns200()
    {
        await using var db = CreateDatabase();
        var controller = new ClientsController(db);

        var result = await controller.RemoveClient(99999, CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Suppliers_DeleteUnknownId_Returns200()
    {
        await using var db = CreateDatabase();
        var controller = new SuppliersController(db);

        var result = await controller.RemoveSupplier(99999, CancellationToken.None);

        Assert.IsType<OkResult>(result);
    }
}
