using Microsoft.AspNetCore.Mvc;

namespace NidarosRTT.API.Controllers;

[ApiController]
[Route("api/info")]
public class InfoController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { name = "NidarosRTT", version = "0.1.0" });
}
