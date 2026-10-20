using Microsoft.AspNetCore.Mvc;

namespace Invoiceflow.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get() =>
        Ok(new HealthResponse("healthy", "Invoiceflow API", "0.1.0", DateTime.UtcNow));
}

public record HealthResponse(string Status, string Service, string Version, DateTime TimeUtc);