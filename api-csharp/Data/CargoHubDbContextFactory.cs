using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CargoHub.Api.Data;

public sealed class CargoHubDbContextFactory : IDesignTimeDbContextFactory<CargoHubDbContext>
{
    public CargoHubDbContext CreateDbContext(string[] args)
    {
        // Generating a migration needs a provider, not a running database or a secret.
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__CargoHub")
            ?? "Host=localhost;Database=cargohub;Username=cargohub";
        return new CargoHubDbContext(new DbContextOptionsBuilder<CargoHubDbContext>()
            .UseNpgsql(connection).Options);
    }
}
