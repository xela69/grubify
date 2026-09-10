using Microsoft.AspNetCore.Mvc;

namespace GrubifyApi.Controllers;

// Liveness/readiness endpoints for Container Apps probes. Deliberately outside the
// /api route and untouched by chaos so the platform can always assess health.
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Live() =>
        Ok(new { status = "healthy", check = "live", timestamp = DateTime.UtcNow });

    [HttpGet("ready")]
    public IActionResult Ready() =>
        Ok(new { status = "ready", check = "ready", timestamp = DateTime.UtcNow });
}
