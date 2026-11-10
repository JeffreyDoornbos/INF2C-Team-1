using CargoHub.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CargoHub.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Start de echte API in een testhost en vervangt uitsluitend de databaseconfiguratie.
/// De publieke DbContext verwijst naar dezelfde assembly als het API-startpunt.
/// </summary>
public sealed class ApiFactory(string connectionString, bool swaggerEnabled = true)
    : WebApplicationFactory<CargoHubDbContext>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Swagger:Enabled", swaggerEnabled.ToString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<CargoHubDbContext>();
            services.RemoveAll<DbContextOptions<CargoHubDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<CargoHubDbContext>>();
            services.AddDbContext<CargoHubDbContext>(options => options.UseNpgsql(connectionString));
        });
    }
}
