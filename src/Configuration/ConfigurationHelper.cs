using Microsoft.Extensions.Configuration;

namespace TechnicalTest.Configuration;

/// <summary>
/// Loads appsettings.json, then appsettings.{TEST_ENV}.json, then TEST_-prefixed environment variables
/// (e.g. <c>TEST_Headless=false</c> or <c>TEST_ApiBaseUrl=https://example.test</c>), later sources winning.
/// A TEST_ENV with no matching file (exact case) fails the run instead of silently using the defaults.
/// Thread-safe so all test layers share one immutable configuration.
/// </summary>
public static class ConfigurationHelper
{
    private static readonly Lazy<IConfiguration> LazyConfiguration = new(Build);

    public static IConfiguration Configuration => LazyConfiguration.Value;

    private static IConfiguration Build()
    {
        var environment = Environment.GetEnvironmentVariable("TEST_ENV");
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

        if (!string.IsNullOrWhiteSpace(environment))
        {
            builder.AddJsonFile(EnvironmentFile(environment.Trim()), optional: false, reloadOnChange: false);
        }

        // Prefixed so unrelated machine variables (e.g. BROWSER) can't override test settings.
        builder.AddEnvironmentVariables(prefix: "TEST_");

        return builder.Build();
    }

    /// <summary>
    /// Resolves appsettings.{environment}.json by exact, case-sensitive name so a typo such as "dev" behaves
    /// the same on Windows, macOS and Linux build agents.
    /// </summary>
    private static string EnvironmentFile(string environment)
    {
        const string prefix = "appsettings.";
        const string suffix = ".json";

        var available = Directory.GetFiles(AppContext.BaseDirectory, prefix + "*" + suffix)
            .Select(path => Path.GetFileName(path))
            .Where(name => name.Length > prefix.Length + suffix.Length)
            .Select(name => name[prefix.Length..^suffix.Length])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return available.Contains(environment, StringComparer.Ordinal)
            ? prefix + environment + suffix
            : throw new InvalidOperationException(
                $"TEST_ENV is '{environment}' but there is no {prefix}{environment}{suffix} (names are case-sensitive). " +
                $"Available environments: {string.Join(", ", available)}.");
    }
}
