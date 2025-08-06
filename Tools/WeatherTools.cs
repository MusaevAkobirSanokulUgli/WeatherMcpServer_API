using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WeatherMcpServer.Tools;

public class WeatherTools
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<WeatherTools> _logger;

    public WeatherTools(HttpClient http, IConfiguration config, ILogger<WeatherTools> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["OPENWEATHER_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENWEATHER_API_KEY") ?? string.Empty;

        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("OPENWEATHER_API_KEY is not set. Weather calls will fail without it.");
        }
    }

    private async Task<(double lat, double lon, string displayName)?> GeocodeAsync(string city, string? countryCode)
    {
        try
        {
            var q = string.IsNullOrWhiteSpace(countryCode) ? city : $"{city},{countryCode}";
            var url = $"http://api.openweathermap.org/geo/1.0/direct?q={Uri.EscapeDataString(q)}&limit=1&appid={_apiKey}";

            var arr = await _http.GetFromJsonAsync<JsonElement[]>(url);

            if (arr is null || arr.Length == 0) return null;

            var first = arr[0];
            double lat = first.GetProperty("lat").GetDouble();
            double lon = first.GetProperty("lon").GetDouble();
            string name = first.GetProperty("name").GetString() ?? city;
            string country = first.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "";
            var display = string.IsNullOrEmpty(country) ? name : $"{name}, {country}";

            return (lat, lon, display);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Geocoding failed for {city}", city);
            return null;
        }
    }

    [McpServerTool]
    [Description("Gets current weather for the specified city.")]
    public async Task<string> GetCurrentWeather(
        [Description("The city name to get weather for")] string city,
        [Description("Optional: Country code (e.g., 'US', 'KR')")] string? countryCode = null)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return "API key not configured. Please set OPENWEATHER_API_KEY env variable.";

        var geo = await GeocodeAsync(city, countryCode);
        if (geo is null) return $"Could not find location: {city}";

        try
        {
            var url = $"https://api.openweathermap.org/data/2.5/weather?lat={geo.Value.lat}&lon={geo.Value.lon}&units=metric&appid={_apiKey}";

            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return $"Error fetching weather data: {response.ReasonPhrase}";

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var root = doc.RootElement;
            var temp = root.GetProperty("main").GetProperty("temp").GetDouble();
            var feels = root.GetProperty("main").GetProperty("feels_like").GetDouble();
            var humidity = root.GetProperty("main").GetProperty("humidity").GetInt32();
            var desc = root.GetProperty("weather")[0].GetProperty("description").GetString() ?? "";

            return $"Current weather in {geo.Value.displayName}: {desc}, {temp}°C (feels like {feels}°C), humidity {humidity}%";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCurrentWeather failed for {city}", city);
            return $"Internal error while getting weather for {city}";
        }
    }

    [McpServerTool]
    [Description("Gets 3-day weather forecast for the specified city.")]
    public async Task<string> GetWeatherForecast(
        [Description("The city name to get forecast for")] string city,
        [Description("Optional: Country code (e.g., 'US', 'KR')")] string? countryCode = null)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return "API key not configured. Please set OPENWEATHER_API_KEY env variable.";

        var geo = await GeocodeAsync(city, countryCode);
        if (geo is null) return $"Could not find location: {city}";

        try
        {
            var url = $"https://api.openweathermap.org/data/2.5/onecall?lat={geo.Value.lat}&lon={geo.Value.lon}&exclude=minutely,hourly,current&units=metric&appid={_apiKey}";

            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return $"Error fetching forecast: {response.ReasonPhrase}";

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            if (!doc.RootElement.TryGetProperty("daily", out var daily))
                return "No forecast data.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"3-day forecast for {geo.Value.displayName}:");
            for (int i = 0; i < Math.Min(3, daily.GetArrayLength()); i++)
            {
                var day = daily[i];
                var dt = DateTimeOffset.FromUnixTimeSeconds(day.GetProperty("dt").GetInt64()).DateTime.Date;
                var tempDay = day.GetProperty("temp").GetProperty("day").GetDouble();
                var tempNight = day.GetProperty("temp").GetProperty("night").GetDouble();
                var desc = day.GetProperty("weather")[0].GetProperty("description").GetString() ?? "";
                sb.AppendLine($"{dt:yyyy-MM-dd}: {desc}, day {tempDay}°C, night {tempNight}°C");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetWeatherForecast failed for {city}", city);
            return $"Internal error while getting forecast for {city}";
        }
    }

    [McpServerTool]
    [Description("Gets weather alerts for the specified city (if any).")]
    public async Task<string> GetWeatherAlerts(
        [Description("The city name to get alerts for")] string city,
        [Description("Optional: Country code (e.g., 'US', 'KR')")] string? countryCode = null)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return "API key not configured. Please set OPENWEATHER_API_KEY env variable.";

        var geo = await GeocodeAsync(city, countryCode);
        if (geo is null) return $"Could not find location: {city}";

        try
        {
            var url = $"https://api.openweathermap.org/data/2.5/onecall?lat={geo.Value.lat}&lon={geo.Value.lon}&exclude=minutely,hourly,current,daily&appid={_apiKey}";

            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return $"Error fetching alerts: {response.ReasonPhrase}";

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            if (doc.RootElement.TryGetProperty("alerts", out var alerts) &&
                alerts.ValueKind == JsonValueKind.Array &&
                alerts.GetArrayLength() > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Weather alerts for {geo.Value.displayName}:");
                foreach (var a in alerts.EnumerateArray())
                {
                    var ev = a.GetProperty("event").GetString() ?? "Alert";
                    var start = DateTimeOffset.FromUnixTimeSeconds(a.GetProperty("start").GetInt64()).DateTime;
                    var end = DateTimeOffset.FromUnixTimeSeconds(a.GetProperty("end").GetInt64()).DateTime;
                    var desc = a.GetProperty("description").GetString() ?? "";
                    sb.AppendLine($"{ev} from {start} to {end}: {desc}");
                }
                return sb.ToString();
            }
            else
            {
                return $"No weather alerts for {geo.Value.displayName}.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetWeatherAlerts failed for {city}", city);
            return $"Internal error while getting alerts for {city}";
        }
    }
}