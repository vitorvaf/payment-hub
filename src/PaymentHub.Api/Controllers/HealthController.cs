using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentHub.Infrastructure.Postgres;

namespace PaymentHub.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly PaymentHubDbContext _db;

    public HealthController(PaymentHubDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult Live() => Ok(new { status = "ok", time = DateTime.UtcNow });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? Ok(new { status = "ready" })
                : StatusCode(503, new { status = "degraded", reason = "database_unreachable" });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { status = "degraded", reason = ex.GetType().Name });
        }
    }
}
