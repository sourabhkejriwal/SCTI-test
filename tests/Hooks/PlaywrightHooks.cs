using Microsoft.Playwright;
using NUnit.Framework;
using Reqnroll;
using TechnicalTest.Tests.Support;
using TechnicalTest.Ui.Helpers;

namespace TechnicalTest.Tests.Hooks;

/// <summary>
/// Browser lifecycle for @UI scenarios: a fresh browser context per scenario (isolated cookies/storage, parallel-safe),
/// network capture, Playwright tracing, and a trace + screenshot attached to the test result when a scenario fails.
/// All browser options come from configuration.
/// </summary>
[Binding]
public sealed class PlaywrightHooks
{
    private readonly UiContext _ui;
    private readonly ScenarioContext _scenario;
    private readonly IReqnrollOutputHelper _output;

    public PlaywrightHooks(UiContext ui, ScenarioContext scenario, IReqnrollOutputHelper output)
    {
        _ui = ui;
        _scenario = scenario;
        _output = output;
    }

    [BeforeScenario("UI")]
    public async Task StartBrowserAsync()
    {
        var settings = _ui.Settings;
        _output.WriteLine($"Environment: {settings.EnvironmentName} | Base URL: {settings.BaseUrl} | Browser: {settings.Browser} | Headless: {settings.Headless}");

        _ui.Playwright = await Playwright.CreateAsync();
        _ui.Browser = await _ui.Playwright[settings.Browser].LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = settings.Headless,
            SlowMo = settings.SlowMo,
            Channel = string.IsNullOrWhiteSpace(settings.BrowserChannel) ? null : settings.BrowserChannel
        });

        _ui.Context = await _ui.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = settings.BaseUrl,
            Locale = settings.Locale,
            TimezoneId = settings.TimezoneId,
            ViewportSize = new ViewportSize { Width = settings.ViewportWidth, Height = settings.ViewportHeight }
        });
        _ui.Context.SetDefaultTimeout(settings.Timeout);
        _ui.Context.SetDefaultNavigationTimeout(settings.NavigationTimeout);

        if (settings.Artifacts.TraceOnFailure)
        {
            await _ui.Context.Tracing.StartAsync(new TracingStartOptions
            {
                Title = _scenario.ScenarioInfo.Title,
                Screenshots = true,
                Snapshots = true,
                Sources = true
            });
        }

        await _ui.Network.AttachAsync(_ui.Context, settings.ApiCaptureGlob);
        _ui.Page = await _ui.Context.NewPageAsync();
    }

    [AfterScenario("UI")]
    public async Task StopBrowserAsync()
    {
        var settings = _ui.Settings;
        var failed = _scenario.TestError is not null;

        try
        {
            if (_ui.Context is not null)
            {
                await SaveArtifactsAsync(settings, failed);
            }

            _output.WriteLine("Captured network calls:");
            foreach (var call in _ui.Network.AllResponses)
            {
                _output.WriteLine($"  {call}");
            }
        }
        finally
        {
            if (_ui.Context is not null) await _ui.Context.CloseAsync();
            if (_ui.Browser is not null) await _ui.Browser.CloseAsync();
            _ui.Playwright?.Dispose();
        }
    }

    private async Task SaveArtifactsAsync(TestSettings settings, bool failed)
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, settings.Artifacts.Directory);
        var baseName = $"{Sanitise(_scenario.ScenarioInfo.Title)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";

        if (failed && settings.Artifacts.ScreenshotOnFailure && _ui.Page is not null)
        {
            Directory.CreateDirectory(directory);
            var screenshot = Path.Combine(directory, baseName + ".png");
            await _ui.Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshot, FullPage = true });
            TestContext.AddTestAttachment(screenshot, "Screenshot at failure");
        }

        if (settings.Artifacts.TraceOnFailure)
        {
            if (failed)
            {
                Directory.CreateDirectory(directory);
                var trace = Path.Combine(directory, baseName + ".trace.zip");
                await _ui.Context!.Tracing.StopAsync(new TracingStopOptions { Path = trace });
                TestContext.AddTestAttachment(trace, "Playwright trace (open with: pwsh playwright.ps1 show-trace <file>)");
            }
            else
            {
                await _ui.Context!.Tracing.StopAsync();
            }
        }
    }

    private static string Sanitise(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { ' ', ',', '"' }).ToHashSet();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }
}
