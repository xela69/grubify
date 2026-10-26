namespace GrubifyApi.Chaos;

// Central place for on-demand fault injection. Every toggle is read from an
// environment variable and defaults OFF, so the app runs clean unless a chaos
// script explicitly enables a fault. Env vars are set per container revision via
// `az containerapp update --set-env-vars`, which the break/reset scripts drive.
public static class ChaosConfig
{
    public static bool MemoryLeakEnabled => GetBool("CHAOS_MEMORY_LEAK");

    // Upper bound for the simulated leak. Keeps the fault observable (memory climbs)
    // without letting it reach the .NET GC heap hard limit (~75% of the container
    // memory limit), where every allocation starts throwing OutOfMemoryException.
    public const int DefaultMemoryLeakMaxMb = 512;
    public static int MemoryLeakMaxMb =>
        GetInt("CHAOS_MEMORY_LEAK_MAX_MB") is var v && v > 0 ? v : DefaultMemoryLeakMaxMb;
    public static bool PaymentFailEnabled => GetBool("CHAOS_PAYMENT_FAIL");
    public static bool CpuSpinEnabled => GetBool("CHAOS_CPU_SPIN");
    public static int LatencyMs => GetInt("CHAOS_LATENCY_MS");
    public static double ErrorRate => GetDouble("CHAOS_ERROR_RATE");

    private static bool GetBool(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(v) &&
               (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1");
    }

    private static int GetInt(string name)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : 0;

    private static double GetDouble(string name)
        => double.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v is > 0 and <= 1 ? v : 0;
}
