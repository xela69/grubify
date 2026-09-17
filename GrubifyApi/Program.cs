using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.ApplicationInsights;
using GrubifyApi.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Azure SQL persistence — connection string injected via ConnectionStrings__DefaultConnection.
builder.Services.AddDbContext<GrubifyDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Application Insights — consumes APPLICATIONINSIGHTS_CONNECTION_STRING when present.
var aiConnString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    if (!string.IsNullOrWhiteSpace(aiConnString))
    {
        options.ConnectionString = aiConnString;
    }
});
Console.WriteLine($"[appinsights] configured={!string.IsNullOrWhiteSpace(aiConnString)} connStringLen={(aiConnString?.Length ?? 0)}");

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

// Manual telemetry probe: isolates whether the channel + egress + connstr work,
// independent of auto-collection (request tracking) modules.
try
{
    var tc = app.Services.GetRequiredService<TelemetryClient>();
    tc.TrackTrace("[startup-probe] manual telemetry test");
    tc.Flush();
    Console.WriteLine("[ai-manual] TrackTrace + Flush called");
}
catch (Exception ex)
{
    Console.WriteLine($"[ai-manual] FAILED {ex.GetType().Name}: {ex.Message}");
}

// One-time egress diagnostic: probe the App Insights ingestion endpoint to
// confirm from container logs whether telemetry can actually reach Azure Monitor.
_ = Task.Run(async () =>
{
    try
    {
        var endpoint = "https://dc.services.visualstudio.com/";
        if (!string.IsNullOrWhiteSpace(aiConnString))
        {
            foreach (var part in aiConnString.Split(';'))
            {
                if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                {
                    endpoint = part.Substring("IngestionEndpoint=".Length);
                    break;
                }
            }
        }
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var resp = await probe.GetAsync(endpoint);
        Console.WriteLine($"[ai-egress] endpoint={endpoint} reachable status={(int)resp.StatusCode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ai-egress] FAILED {ex.GetType().Name}: {ex.Message}");
    }
});

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
