using Microsoft.Extensions.Configuration;

namespace TechnicalTest.Ui.Helpers;

public static class ConfigurationHelper
{
    private static IConfiguration? _configuration;

    public static IConfiguration Configuration
    {
        get
        {
            if (_configuration is not null)
            {
                return _configuration;
            }

            var environment = Environment.GetEnvironmentVariable("TEST_ENV");
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

            if (!string.IsNullOrWhiteSpace(environment))
            {
                builder.AddJsonFile(
                    $"appsettings.{environment}.json",
                    optional: true,
                    reloadOnChange: false);
            }

            _configuration = builder.Build();
            return _configuration;
        }
    }
}
