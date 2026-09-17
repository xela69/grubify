using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using GrubifyApi.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Azure SQL persistence — connection string injected via ConnectionStrings__DefaultConnection.
builder.Services.AddDbContext<GrubifyDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Observability via the Azure Monitor OpenTelemetry Distro. The classic
// Microsoft.ApplicationInsights.AspNetCore SDK does not auto-collect on .NET 9
// (TelemetryClient is never registered), so this distro is the supported path.
// The target App Insights has DisableLocalAuth=true, so telemetry must be
// ingested with a Microsoft Entra credential (the app's user-assigned managed
// identity, selected via the AZURE_CLIENT_ID env var) rather than the
// connection string's instrumentation key. UseAzureMonitor() still reads the
// endpoint from APPLICATIONINSIGHTS_CONNECTION_STRING.
var aiConnString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(aiConnString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
    {
        options.Credential = new DefaultAzureCredential();
    });
}

// Configure forwarded headers for Azure Container Apps
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Add CORS — reads AllowedOrigins from config/env vars (e.g. AllowedOrigins__0)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() 
                ?? Array.Empty<string>();
            
            // Always include localhost for development
            var origins = new HashSet<string>(allowedOrigins)
            {
                "http://localhost:3000",
                "https://localhost:3000"
            };
            
            // Remove empty entries
            origins.RemoveWhere(string.IsNullOrWhiteSpace);
            
            if (origins.Count > 0)
            {
                policy.WithOrigins(origins.ToArray())
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            }
            else
            {
                // Fallback: allow any origin without credentials
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            }
        });
});

var app = builder.Build();

// Create schema and seed the catalog on first startup. Wrapped so a SQL outage
// degrades (catalog endpoints error) rather than crash-looping the container.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GrubifyDbContext>();
    try
    {
        db.Database.EnsureCreated();
        if (!db.Restaurants.Any())
        {
            db.Restaurants.AddRange(GrubifyApi.Controllers.RestaurantsController.Seed);
            db.FoodItems.AddRange(GrubifyApi.Controllers.FoodItemsController.Seed);
            db.SaveChanges();
            Console.WriteLine($"[seed] Seeded {db.Restaurants.Count()} restaurants, {db.FoodItems.Count()} food items.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[seed] SQL init skipped/failed: {ex.Message}");
    }
}

// Use forwarded headers for Azure Container Apps
app.UseForwardedHeaders();

// Chaos fault injection (no-op unless CHAOS_* env vars are set).
app.UseMiddleware<GrubifyApi.Middleware.ChaosMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Remove UseHttpsRedirection for Azure Container Apps - ACA handles TLS termination
// app.UseHttpsRedirection();

// Use CORS
app.UseCors("AllowReactApp");

app.UseAuthorization();

app.MapControllers();

app.Run();
