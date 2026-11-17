using System.Text.Json;
using CargoHub.Api.Data;
using CargoHub.Api.Services;
using CargoHub.Api.Documentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using CargoHub.Api.Routing;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);
var apiVersion = builder.Configuration["Api:Version"] ?? "v1";
if (!System.Text.RegularExpressions.Regex.IsMatch(apiVersion, @"^v[1-9][0-9]*$"))
    throw new InvalidOperationException("Api:Version must be v1, v2, ...");
var swaggerPrefix = (builder.Configuration["Swagger:RoutePrefix"] ?? "swagger").Trim('/');

// Register controllers and enforce snake_case JSON formatting to match Python API responses
builder.Services.AddControllers(options => options.Conventions.Add(new ApiVersionRouteConvention(apiVersion)))
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    });

// Configure PostgreSQL connection string for Entity Framework Core
builder.Services.AddDbContext<CargoHubDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("CargoHub")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__CargoHub.")));

builder.Services.AddScoped<ShipmentItemStore>();
builder.Services.AddScoped<ShipmentService>();
builder.Services.AddScoped<TransferItemStore>();
builder.Services.AddScoped<TransferService>();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(apiVersion, new OpenApiInfo
    {
        Title = "CargoHub API",
        Version = apiVersion,
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
    app.UseSwagger(options => options.RouteTemplate = $"{swaggerPrefix}/{{documentName}}/swagger.json");
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = swaggerPrefix;
        options.SwaggerEndpoint($"{apiVersion}/swagger.json", $"CargoHub API {apiVersion}");
        options.DisplayRequestDuration();
        options.EnableFilter();
    });
}

// Map route endpoints to their respective controllers
app.MapControllers();
app.MapGet($"/api/{apiVersion}", () => Results.Ok(new
{
    version = apiVersion,
    build = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    swagger = $"/{swaggerPrefix}/index.html"
}));
app.MapGet("/health/ready", async (CargoHubDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ready", version = apiVersion })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.Run();
