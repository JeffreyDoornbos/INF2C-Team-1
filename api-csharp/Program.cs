using CargoHub.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<CargoHubDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("CargoHub")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__CargoHub.")));

var app = builder.Build();

app.MapControllers();

app.Run();
