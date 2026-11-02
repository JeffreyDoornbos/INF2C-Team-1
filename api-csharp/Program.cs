using System.Text.Json;
using CargoHub.Api.Data;
using CargoHub.Api.Documentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

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

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CargoHub API",
        Version = "v1",
        Description = "Interactieve documentatie van de huidige C#-API. Kies een endpoint, klik op Try it out en daarna Execute. " +
            "De voorbeelden zijn fictief. Schrijfacties wijzigen de aangesloten database. " +
            "Authenticatie is nog niet geïmplementeerd: er is momenteel geen API-sleutel nodig. " +
            "Voor een korte demonstratie: maak een artikelgroep met ID 900001, haal deze op, wijzig de naam en verwijder de groep weer. " +
            "Gebruik daarvoor een aparte demodatabase."
    });
    options.TagActionsBy(description => [DemoOperationFilter.TagFor(description.RelativePath!)]);
    options.OperationFilter<DemoOperationFilter>();
    options.DocumentFilter<DemoOperationFilter>();
});

var app = builder.Build();

// Enable locally by default; a deployed demo can opt in with Swagger__Enabled=true.
if (app.Configuration.GetValue<bool?>("Swagger:Enabled") ?? app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("v1/swagger.json", "CargoHub API v1");
        options.DisplayRequestDuration();
        options.EnableFilter();
    });
}

// Map route endpoints to their respective controllers
app.MapControllers();

app.Run();
