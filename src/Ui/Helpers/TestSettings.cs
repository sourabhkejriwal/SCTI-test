using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace TechnicalTest.Ui.Helpers;

/// <summary>
/// Strongly typed view of appsettings.json (+ appsettings.{TEST_ENV}.json + environment variables).
/// Every value the UI tests depend on lives in configuration; nothing environment-specific is hardcoded.
/// </summary>
public sealed class TestSettings
{
    public required string EnvironmentName { get; init; }
    public required string BaseUrl { get; init; }

    public required int Timeout { get; init; }
    public required int NavigationTimeout { get; init; }
    public required int ExpectTimeout { get; init; }

    public required string Browser { get; init; }
    public string? BrowserChannel { get; init; }
    public required bool Headless { get; init; }
    public required int SlowMo { get; init; }
    public required int ViewportWidth { get; init; }
    public required int ViewportHeight { get; init; }
    public required string Locale { get; init; }
    public required string TimezoneId { get; init; }

    public required string DateFormat { get; init; }
    public required string CurrencySymbol { get; init; }

    public required ArtifactSettings Artifacts { get; init; }
    public required RouteSettings Routes { get; init; }
    public required ApiEndpointSettings ApiEndpoints { get; init; }

    /// <summary>Today's date in the site's time zone, so relative travel dates match what the site considers "today".</summary>
    public DateOnly LocalToday()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimezoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
    }

    public string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public string FormatCurrency(decimal amount) =>
        CurrencySymbol + amount.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>Glob used to intercept and record the site's own API traffic, e.g. https://insure.scti.co.nz/api/**.</summary>
    public string ApiCaptureGlob => $"{BaseUrl.TrimEnd('/')}/{ApiEndpoints.BasePath.Trim('/')}/**";

    public static TestSettings Load(IConfiguration config) => new()
    {
        EnvironmentName = Required(config, "EnvironmentName"),
        BaseUrl = Required(config, "BaseUrl"),
        Timeout = Int(config, "Timeout"),
        NavigationTimeout = Int(config, "NavigationTimeout"),
        ExpectTimeout = Int(config, "ExpectTimeout"),
        Browser = Required(config, "Browser"),
        BrowserChannel = config["BrowserChannel"],
        Headless = Bool(config, "Headless"),
        SlowMo = Int(config, "SlowMo"),
        ViewportWidth = Int(config, "ViewportWidth"),
        ViewportHeight = Int(config, "ViewportHeight"),
        Locale = Required(config, "Locale"),
        TimezoneId = Required(config, "TimezoneId"),
        DateFormat = Required(config, "DateFormat"),
        CurrencySymbol = Required(config, "CurrencySymbol"),
        Artifacts = new ArtifactSettings
        {
            Directory = Required(config, "Artifacts:Directory"),
            TraceOnFailure = Bool(config, "Artifacts:TraceOnFailure"),
            ScreenshotOnFailure = Bool(config, "Artifacts:ScreenshotOnFailure")
        },
        Routes = new RouteSettings
        {
            PlanOptions = Required(config, "Routes:PlanOptions"),
            TravellerDetails = Required(config, "Routes:TravellerDetails")
        },
        ApiEndpoints = new ApiEndpointSettings
        {
            BasePath = Required(config, "ApiEndpoints:BasePath"),
            CreateQuote = ApiPath(config, "ApiEndpoints:CreateQuote"),
            UpdateExtras = ApiPath(config, "ApiEndpoints:UpdateExtras"),
            SelectQuote = ApiPath(config, "ApiEndpoints:SelectQuote")
        }
    };

    /// <summary>Endpoints are configured relative to ApiEndpoints:BasePath, e.g. "Quotes/create" becomes "/api/Quotes/create".</summary>
    private static string ApiPath(IConfiguration config, string key) =>
        $"/{Required(config, "ApiEndpoints:BasePath").Trim('/')}/{Required(config, key).TrimStart('/')}";

    private static string Required(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Missing required setting '{key}'. Add it to appsettings.json, appsettings.{{TEST_ENV}}.json or an environment variable ('TEST_{key.Replace(":", "__")}').");

    private static int Int(IConfiguration config, string key) =>
        int.Parse(Required(config, key), CultureInfo.InvariantCulture);

    private static bool Bool(IConfiguration config, string key) =>
        bool.Parse(Required(config, key));
}

public sealed class ArtifactSettings
{
    public required string Directory { get; init; }
    public required bool TraceOnFailure { get; init; }
    public required bool ScreenshotOnFailure { get; init; }
}

public sealed class RouteSettings
{
    public required string PlanOptions { get; init; }
    public required string TravellerDetails { get; init; }
}

/// <summary>The endpoint properties hold full paths (base path included), ready for URL matching.</summary>
public sealed class ApiEndpointSettings
{
    public required string BasePath { get; init; }
    public required string CreateQuote { get; init; }
    public required string UpdateExtras { get; init; }
    public required string SelectQuote { get; init; }
}
