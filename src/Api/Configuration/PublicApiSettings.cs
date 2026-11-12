using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace TechnicalTest.Api.Configuration;

/// <summary>Configuration required to call the public JSONPlaceholder API.</summary>
public sealed class PublicApiSettings
{
    public required Uri BaseUri { get; init; }
    public required TimeSpan Timeout { get; init; }

    public static PublicApiSettings Load(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new PublicApiSettings
        {
            BaseUri = ParseBaseUri(config),
            Timeout = ParseTimeout(config)
        };
    }

    private static Uri ParseBaseUri(IConfiguration config)
    {
        var value = Required(config, "ApiBaseUrl").Trim();

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "Setting 'ApiBaseUrl' must be an absolute HTTP or HTTPS URL without a query or fragment.");
        }

        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static TimeSpan ParseTimeout(IConfiguration config)
    {
        var value = Required(config, "Timeout");

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) ||
            milliseconds <= 0)
        {
            throw new InvalidOperationException("Setting 'Timeout' must be a positive integer number of milliseconds.");
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static string Required(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Missing required setting '{key}'. Add it to appsettings.json, appsettings.{{TEST_ENV}}.json " +
                $"or an environment variable ('TEST_{key.Replace(":", "__")}').");
}
