using System.Text.Json;
using CargoHub.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register controllers and enforce snake_case JSON formatting to match Python API responses
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    });

// Configure PostgreSQL connection string for Entity Framework Core
builder.Services.AddDbContext<CargoHubDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("CargoHub")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__CargoHub.")));

var app = builder.Build();

// Map route endpoints to their respective controllers
app.MapControllers();

app.Run();