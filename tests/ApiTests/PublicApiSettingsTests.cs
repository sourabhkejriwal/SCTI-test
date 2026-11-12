using Microsoft.Extensions.Configuration;
using TechnicalTest.Api.Configuration;
using NUnit.Framework;

namespace TechnicalTest.Tests.ApiTests;

[TestFixture]
[Category("API")]
public class PublicApiSettingsTests
{
    [Test]
    public void Load_WithOnlyApiSettings_ReturnsNormalisedSettings()
    {
        var config = Configuration(
            apiBaseUrl: "https://api.example.test/v1",
            timeout: "1500");

        var settings = PublicApiSettings.Load(config);

        Assert.Multiple(() =>
        {
            Assert.That(settings.BaseUri, Is.EqualTo(new Uri("https://api.example.test/v1/")));
            Assert.That(settings.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-a-url")]
    [TestCase("ftp://api.example.test")]
    public void Load_WithMissingOrInvalidBaseUrl_ThrowsClearConfigurationError(string? apiBaseUrl)
    {
        var config = Configuration(apiBaseUrl, "1500");

        var exception = Assert.Throws<InvalidOperationException>(() => PublicApiSettings.Load(config));

        Assert.That(exception!.Message, Does.Contain("ApiBaseUrl"));
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("not-a-number")]
    public void Load_WithInvalidTimeout_ThrowsClearConfigurationError(string timeout)
    {
        var config = Configuration("https://api.example.test", timeout);

        var exception = Assert.Throws<InvalidOperationException>(() => PublicApiSettings.Load(config));

        Assert.That(exception!.Message, Does.Contain("Timeout"));
    }

    private static IConfiguration Configuration(string? apiBaseUrl, string? timeout)
    {
        var values = new Dictionary<string, string?>
        {
            ["ApiBaseUrl"] = apiBaseUrl,
            ["Timeout"] = timeout
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
