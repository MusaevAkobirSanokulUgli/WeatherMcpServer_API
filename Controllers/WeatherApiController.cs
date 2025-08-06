using Microsoft.AspNetCore.Mvc;
using WeatherMcpServer.Tools;

namespace WeatherMcpServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WeatherApiController : ControllerBase
{
    private readonly WeatherTools _weatherTools;

    public WeatherApiController(WeatherTools weatherTools)
    {
        _weatherTools = weatherTools;
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent([FromQuery] string city, [FromQuery] string? countryCode = null)
    {
        var result = await _weatherTools.GetCurrentWeather(city, countryCode);
        return Ok(result);
    }

    [HttpGet("forecast")]
    public async Task<IActionResult> GetForecast([FromQuery] string city, [FromQuery] string? countryCode = null)
    {
        var result = await _weatherTools.GetWeatherForecast(city, countryCode);
        return Ok(result);
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts([FromQuery] string city, [FromQuery] string? countryCode = null)
    {
        var result = await _weatherTools.GetWeatherAlerts(city, countryCode);
        return Ok(result);
    }
}