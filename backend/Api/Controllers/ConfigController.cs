using Api.Config;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Api.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController(IOptions<RecalcConfig> config) : ControllerBase
{
    [HttpGet]
    public ActionResult<RecalcConfig> Get() => Ok(config.Value);
}
