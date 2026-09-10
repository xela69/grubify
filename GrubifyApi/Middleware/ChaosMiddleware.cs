using GrubifyApi.Chaos;

namespace GrubifyApi.Middleware;

// Applies request-level faults (latency, random 500s, CPU burn) when the matching
// chaos toggles are enabled. A complete no-op when everything is off. Health probes
// are always skipped so the platform can still tell the container is alive.
public class ChaosMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly Random Rng = new();

    public ChaosMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var latency = ChaosConfig.LatencyMs;
        if (latency > 0)
        {
            await Task.Delay(latency);
        }

        if (ChaosConfig.CpuSpinEnabled)
        {
            BurnCpu(TimeSpan.FromMilliseconds(200));
        }

        var errorRate = ChaosConfig.ErrorRate;
        if (errorRate > 0 && Rng.NextDouble() < errorRate)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Injected chaos error",
                code = "CHAOS_ERROR",
                timestamp = DateTime.UtcNow
            });
            return;
        }

        await _next(context);
    }

    private static void BurnCpu(TimeSpan duration)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < duration)
        {
            _ = Math.Sqrt(Rng.NextDouble() * 12345.678);
        }
    }
}
