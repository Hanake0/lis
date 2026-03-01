using Microsoft.AspNetCore.Mvc;

namespace Lis.Api;

[ApiController]
[Route("health")]
public class HealthController :ControllerBase {
	[HttpGet]
	[ProducesResponseType(StatusCodes.Status200OK)]
	public IActionResult GetHealth() => this.Ok("ok");
}
