using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using TechnicalTest.Ui.Helpers;
using TechnicalTest.Ui.Pages.Models;

namespace TechnicalTest.Ui.Pages;

/// <summary>
/// Home page quote form: journey type, destinations, travel dates and travellers.
/// The main form's &lt;label for&gt; attributes don't point at the inputs, so the form fields are found by
/// role + accessible name; inside each picker dialog the inputs are properly labelled and use GetByLabel.
/// </summary>
public sealed class InsurancePage : BasePage
{
    private static readonly Regex AdultCountLabel = new(@"^Adult \(Aged");
    private static readonly Regex DependentCountLabel = new(@"^Dependent children");
    private static readonly Regex NonDependentCountLabel = new(@"^Non-dependent children \(Aged");

    public InsurancePage(IPage page, TestSettings settings) : base(page, settings)
    {
    }

    public ILocator JourneyTab(string journeyType) =>
        Page.GetByRole(AriaRole.Tab, new() { Name = journeyType, Exact = true });

    public ILocator DestinationsField => Page.GetByRole(AriaRole.Textbox, new() { Name = "Enter destination(s)" });

    public ILocator TravelDatesField => Page.GetByRole(AriaRole.Textbox, new() { Name = "Select travel dates" });

    public ILocator TravellersField => Page.GetByRole(AriaRole.Textbox, new() { Name = "Add travellers" });

    public ILocator GetAQuoteButton => Page.GetByRole(AriaRole.Button, new() { Name = "Get a quote", Exact = true });

    private ILocator DestinationsDialog => Page.GetByRole(AriaRole.Dialog, new() { Name = "Select destinations" });

    private ILocator DatePickerDialog => Page.GetByRole(AriaRole.Dialog, new() { Name = "Date picker" });

    private ILocator TravellersDialog => Page.GetByRole(AriaRole.Dialog, new() { Name = "Add number of travellers" });

    public async Task NavigateAsync()
    {
        await Page.GotoAsync(Settings.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await DestinationsField.WaitForAsync();
    }

    public Task SelectJourneyTypeAsync(string journeyType) => JourneyTab(journeyType).ClickAsync();

    /// <summary>Picks destinations from the dialog's destination buttons (search results are plain text with no role).</summary>
    public async Task AddDestinationsAsync(IEnumerable<string> destinations)
    {
        await DestinationsField.ClickAsync();
        await DestinationsDialog.WaitForAsync();

        foreach (var destination in destinations)
        {
            await DestinationsDialog.GetByRole(AriaRole.Button, new() { Name = destination, Exact = true }).ClickAsync();
        }

        await ConfirmDialogAsync(DestinationsDialog);
    }

    public async Task SetTravelDatesAsync(DateOnly departure, DateOnly returnDate)
    {
        await TravelDatesField.ClickAsync();
        await DatePickerDialog.GetByLabel("Departure date").FillAsync(Settings.FormatDate(departure));
        await DatePickerDialog.GetByLabel("Return date").FillAsync(Settings.FormatDate(returnDate));
        await ConfirmDialogAsync(DatePickerDialog);
    }

    public async Task SetTravellersAsync(TravellerParty party)
    {
        await TravellersField.ClickAsync();
        var dialog = TravellersDialog;

        await dialog.GetByLabel(AdultCountLabel).FillAsync(Count(party.AdultAges));
        for (var i = 0; i < party.AdultAges.Count; i++)
        {
            await dialog.GetByLabel(new Regex($@"^Adult {i + 1}\s+age")).FillAsync(Age(party.AdultAges[i]));
        }

        await dialog.GetByLabel(DependentCountLabel).FillAsync(Count(party.DependentChildAges));
        await dialog.GetByLabel(NonDependentCountLabel).FillAsync(Count(party.NonDependentChildAges));

        // Dependent and non-dependent age fields share the label "Child N age". The dependent section is
        // rendered before the non-dependent one, so the first match is the dependent child, the last is
        // the non-dependent child (when only one exists, both resolve to the same field).
        for (var i = 0; i < party.DependentChildAges.Count; i++)
        {
            await ChildAgeField(dialog, i + 1).First.FillAsync(Age(party.DependentChildAges[i]));
        }

        for (var i = 0; i < party.NonDependentChildAges.Count; i++)
        {
            await ChildAgeField(dialog, i + 1).Last.FillAsync(Age(party.NonDependentChildAges[i]));
        }

        await ConfirmDialogAsync(dialog);
    }

    /// <summary>Submits the quote form and returns the /api/Quotes/create response, then waits for plan options.</summary>
    public async Task<IResponse> GetQuoteAsync()
    {
        var response = await RunAndWaitForApiAsync(Settings.ApiEndpoints.CreateQuote, "POST", () => GetAQuoteButton.ClickAsync());
        await Page.WaitForURLAsync(UrlFor(Settings.Routes.PlanOptions));
        return response;
    }

    public string ExpectedDatesText(DateOnly departure, DateOnly returnDate) =>
        $"{Settings.FormatDate(departure)} - {Settings.FormatDate(returnDate)}";

    public static string ExpectedTravellersText(TravellerParty party) =>
        party.Total == 1 ? "1 Traveller" : $"{party.Total} Travellers";

    private static ILocator ChildAgeField(ILocator dialog, int childNumber) =>
        dialog.GetByLabel(new Regex($@"^Child {childNumber}\s+age"));

    private static string Count(IReadOnlyCollection<int> ages) => ages.Count.ToString(CultureInfo.InvariantCulture);

    private static string Age(int age) => age.ToString(CultureInfo.InvariantCulture);
}
