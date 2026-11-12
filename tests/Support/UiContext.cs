using Microsoft.Playwright;
using TechnicalTest.Configuration;
using TechnicalTest.Ui.Helpers;
using TechnicalTest.Ui.Pages.Models;
using TechnicalTest.Ui.Pages;

namespace TechnicalTest.Tests.Support;

/// <summary>
/// Per-scenario state shared between hooks and step definitions through Reqnroll context injection.
/// One instance per scenario, so scenarios stay isolated and parallel-safe.
/// </summary>
public sealed class UiContext
{
    private InsurancePage? _insurancePage;
    private PlanOptionsPage? _planOptionsPage;
    private TravellerDetailsPage? _travellerDetailsPage;

    public TestSettings Settings { get; } = TestSettings.Load(ConfigurationHelper.Configuration);

    public IPlaywright? Playwright { get; set; }
    public IBrowser? Browser { get; set; }
    public IBrowserContext? Context { get; set; }
    public NetworkRecorder Network { get; } = new();

    public IPage Page { get; set; } = null!;

    public InsurancePage InsurancePage => _insurancePage ??= new InsurancePage(Page, Settings);
    public PlanOptionsPage PlanOptionsPage => _planOptionsPage ??= new PlanOptionsPage(Page, Settings);
    public TravellerDetailsPage TravellerDetailsPage => _travellerDetailsPage ??= new TravellerDetailsPage(Page, Settings);

    public JourneyState Journey { get; } = new();
}

/// <summary>What the scenario has entered so far, and the API calls it needs to assert on.</summary>
public sealed class JourneyState
{
    public List<string> Destinations { get; set; } = new();
    public DateOnly DepartureDate { get; set; }
    public DateOnly ReturnDate { get; set; }
    public TravellerParty Party { get; set; } = new(Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());

    public string Discount { get; set; } = "";
    public string Excess { get; set; } = "";
    public string CancellationCover { get; set; } = "";
    public string ExtraCover { get; set; } = "";

    public ApiCall? CreateQuoteCall { get; set; }
    public ApiCall? LatestExtrasCall { get; set; }
    public ApiCall? SelectQuoteCall { get; set; }
    public PolicyholderDetails? Policyholder { get; set; }

    /// <summary>Premium of the selected plan after customisation, used across later steps.</summary>
    public decimal SelectedPremium { get; set; }
}
