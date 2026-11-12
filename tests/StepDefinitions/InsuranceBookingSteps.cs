using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Reqnroll;
using Reqnroll.Assist;
using TechnicalTest.Tests.Support;
using TechnicalTest.Ui.Pages.Models;
using TechnicalTest.Ui.Pages;
using static Microsoft.Playwright.Assertions;

namespace TechnicalTest.Tests.StepDefinitions;

/// <summary>
/// Steps coordinate intent and assertions; locators and mechanics live in the page objects.
/// Each checkpoint asserts three layers: the URL, the UI, and the API call behind the step.
/// </summary>
[Binding]
public sealed class InsuranceBookingSteps
{
    /// <summary>Keeps generated dates of birth well clear of a birthday falling during the trip.</summary>
    private const int DateOfBirthBufferDays = 60;

    private readonly UiContext _ui;

    public InsuranceBookingSteps(UiContext ui)
    {
        _ui = ui;
    }

    private JourneyState Journey => _ui.Journey;
    private InsurancePage Home => _ui.InsurancePage;
    private PlanOptionsPage PlanOptions => _ui.PlanOptionsPage;
    private TravellerDetailsPage TravellerDetails => _ui.TravellerDetailsPage;

    // ---------- Quote form ----------

    [Given(@"I am on the SCTI travel insurance home page")]
    public async Task GivenIAmOnTheHomePage()
    {
        await Home.NavigateAsync();

        await Expect(_ui.Page).ToHaveURLAsync(Home.HomeUrl);
        await Expect(_ui.Page).ToHaveTitleAsync(new Regex("SCTI"));
        await Expect(Home.DestinationsField).ToBeVisibleAsync();
    }

    [When(@"I choose the ""(.*)"" cover")]
    public async Task WhenIChooseTheCover(string journeyType)
    {
        await Home.SelectJourneyTypeAsync(journeyType);

        await Expect(Home.JourneyTab(journeyType)).ToHaveAttributeAsync("aria-selected", "true");
    }

    [When(@"I add the destinations ""(.*)""")]
    public async Task WhenIAddTheDestinations(string destinations)
    {
        Journey.Destinations = SplitList(destinations);

        await Home.AddDestinationsAsync(Journey.Destinations);

        await Expect(Home.DestinationsField).ToHaveValueAsync(string.Join(", ", Journey.Destinations));
    }

    [When(@"I add travel dates departing in (\d+) days for a (\d+) day trip")]
    public async Task WhenIAddTravelDates(int departsInDays, int tripDays)
    {
        Journey.DepartureDate = _ui.Settings.LocalToday().AddDays(departsInDays);
        Journey.ReturnDate = Journey.DepartureDate.AddDays(tripDays);

        await Home.SetTravelDatesAsync(Journey.DepartureDate, Journey.ReturnDate);

        await Expect(Home.TravelDatesField).ToHaveValueAsync(Home.ExpectedDatesText(Journey.DepartureDate, Journey.ReturnDate));
    }

    [When(@"I add 1 adult aged (\d+), 1 dependent child aged (\d+) and 1 non-dependent child aged (\d+)")]
    public async Task WhenIAddTravellers(int adultAge, int dependentAge, int nonDependentAge)
    {
        Journey.Party = new TravellerParty(new[] { adultAge }, new[] { dependentAge }, new[] { nonDependentAge });

        await Home.SetTravellersAsync(Journey.Party);

        await Expect(Home.TravellersField).ToHaveValueAsync(InsurancePage.ExpectedTravellersText(Journey.Party));
    }

    [When(@"I get plan options")]
    public async Task WhenIGetPlanOptions()
    {
        var response = await Home.GetQuoteAsync();
        Journey.CreateQuoteCall = _ui.Network.For(response);
    }

    [Then(@"plan options should be quoted for destination codes ""(.*)""")]
    public async Task ThenPlanOptionsShouldBeQuoted(string destinationCodes)
    {
        var codes = SplitList(destinationCodes);
        var call = Journey.CreateQuoteCall!;

        // URL
        await Expect(_ui.Page).ToHaveURLAsync(PlanOptions.UrlFor(_ui.Settings.Routes.PlanOptions));

        // API: what the browser sent...
        call.Status.Should().Be((int)HttpStatusCode.OK, "POST {0} should succeed", call.Url);
        var request = call.ReadRequest<CreateQuoteRequest>();
        request.Destinations.Should().Equal(codes);
        request.AdultsAge.Should().Equal(Journey.Party.AdultAges);
        request.DependentChildAges.Should().Equal(Journey.Party.DependentChildAges);
        request.NonDependentChildAges.Should().Equal(Journey.Party.NonDependentChildAges);

        // ...and what the quote service returned
        var quote = call.ReadResponse<QuoteResponse>();
        AssertQuoteMatchesTrip(quote, codes);
        var singleTrip = quote.PremiumFor(QuoteCodes.SingleTripPolicy);
        singleTrip.TotalPremium.Should().BePositive();

        // UI: plans are offered and the one return journey price matches the API
        await Expect(PlanOptions.SelectPlanButtons.First).ToBeVisibleAsync();
        await Expect(PlanOptions.Amount(singleTrip.TotalPremium)).ToBeVisibleAsync();
    }

    // ---------- Plan options ----------

    [When(@"I select and customise the one return journey plan")]
    public async Task WhenISelectAndCustomiseThePlan()
    {
        await PlanOptions.SelectOneReturnJourneyPlanAsync();

        await Expect(PlanOptions.PlanSelectedLabel).ToBeVisibleAsync();
        await Expect(PlanOptions.RadioGroup(PlanOptionsPage.ExcessGroup)).ToBeVisibleAsync();
    }

    [When(@"I choose discount ""(.*)"", excess ""(.*)"", cancellation cover ""(.*)"" and extra cover ""(.*)""")]
    public async Task WhenIChooseCustomisations(string discount, string excess, string cancellationCover, string extraCover)
    {
        Journey.Discount = discount;
        Journey.Excess = excess;
        Journey.CancellationCover = cancellationCover;
        Journey.ExtraCover = extraCover;

        var responses = new[]
        {
            await PlanOptions.ChooseDiscountAsync(discount),
            await PlanOptions.ChooseExcessAsync(excess),
            await PlanOptions.ChooseCancellationCoverAsync(cancellationCover),
            await PlanOptions.AddExtraCoverAsync(extraCover)
        };

        // Every change re-prices the quote; the last call reflects all choices. The discount question has
        // no default, so at least that call is always made.
        var latest = responses.LastOrDefault(r => r is not null)
                     ?? throw new InvalidOperationException("No customisation triggered a re-price (PUT extras) call.");
        Journey.LatestExtrasCall = _ui.Network.For(latest);
    }

    [Then(@"the selected plan premium should reflect my customisation")]
    public async Task ThenThePremiumShouldReflectMyCustomisation()
    {
        var call = Journey.LatestExtrasCall!;

        // API
        call.Status.Should().Be((int)HttpStatusCode.OK, "PUT {0} should succeed", call.Url);
        var quote = call.ReadResponse<QuoteResponse>();
        quote.IsSuccess.Should().BeTrue();
        var selected = quote.SelectedPremium;
        selected.PolicyTypeCode.Should().Be(QuoteCodes.SingleTripPolicy, "the one return journey plan was selected");
        selected.IsSchsMember.Should().Be(IsYes(Journey.Discount));
        selected.SelectedExcess.Should().Be(Journey.Excess);
        selected.SelectedCancellationCover.Should().Be(Journey.CancellationCover);
        selected.HasCruiseCover.Should().Be(Journey.ExtraCover == QuoteCodes.CruiseCover);
        selected.HasMopedCover.Should().Be(Journey.ExtraCover == QuoteCodes.MopedCover);
        selected.HasSkiCover.Should().Be(Journey.ExtraCover == QuoteCodes.SkiCover);
        Journey.SelectedPremium = selected.TotalPremium;

        // UI: choices are shown as selected and the re-priced total is displayed
        await Expect(PlanOptions.Option(PlanOptionsPage.DiscountGroup, Journey.Discount)).ToBeCheckedAsync();
        await Expect(PlanOptions.Option(PlanOptionsPage.ExcessGroup, Journey.Excess)).ToBeCheckedAsync();
        await Expect(PlanOptions.Option(PlanOptionsPage.CancellationCoverGroup, Journey.CancellationCover)).ToBeCheckedAsync();
        if (Journey.ExtraCover != QuoteCodes.NoExtraCover)
        {
            await Expect(PlanOptions.ExtraCoverCheckbox(Journey.ExtraCover)).ToBeCheckedAsync();
        }
        await Expect(PlanOptions.Amount(Journey.SelectedPremium)).ToBeVisibleAsync();

        // URL: customisation happens in place
        await Expect(_ui.Page).ToHaveURLAsync(PlanOptions.UrlFor(_ui.Settings.Routes.PlanOptions));
    }

    [When(@"I continue to traveller details")]
    public async Task WhenIContinueToTravellerDetails()
    {
        var response = await PlanOptions.ContinueToTravellerDetailsAsync();
        Journey.SelectQuoteCall = _ui.Network.For(response);
    }

    // ---------- Traveller details ----------

    [Then(@"the traveller details page should show the selected plan premium")]
    public async Task ThenTheTravellerDetailsPageShouldShowThePremium()
    {
        var call = Journey.SelectQuoteCall!;

        await Expect(_ui.Page).ToHaveURLAsync(TravellerDetails.UrlFor(_ui.Settings.Routes.TravellerDetails));

        call.Status.Should().Be((int)HttpStatusCode.OK, "PUT {0} should succeed", call.Url);
        var quote = call.ReadResponse<QuoteResponse>();
        quote.IsSuccess.Should().BeTrue();
        quote.SelectedPremium.PolicyTypeCode.Should().Be(QuoteCodes.SingleTripPolicy);
        quote.SelectedPremium.TotalPremium.Should().Be(Journey.SelectedPremium, "selecting the plan must not change the customised price");

        await Expect(TravellerDetails.EligibilityHeading).ToBeVisibleAsync();
        await Expect(TravellerDetails.Amount(Journey.SelectedPremium)).ToBeVisibleAsync();
    }

    [When(@"I confirm all travellers are eligible NZ residents who have not started their journey and have no fraud history")]
    public async Task WhenIConfirmEligibility()
    {
        await TravellerDetails.AnswerEligibilityAsync(residency: "Yes", journeyStarted: "No", fraud: "No");

        await Expect(TravellerDetails.Option(TravellerDetailsPage.ResidencyGroup, "Yes")).ToBeCheckedAsync();
        await Expect(TravellerDetails.Option(TravellerDetailsPage.JourneyStartedGroup, "No")).ToBeCheckedAsync();
        await Expect(TravellerDetails.Option(TravellerDetailsPage.FraudGroup, "No")).ToBeCheckedAsync();
    }

    [When(@"I accept the privacy declaration")]
    public async Task WhenIAcceptThePrivacyDeclaration()
    {
        await TravellerDetails.AcceptPrivacyDeclarationAsync();

        await Expect(TravellerDetails.PolicyAgreementCheckbox).ToBeCheckedAsync();
        await Expect(TravellerDetails.PrivacyAuthorisationCheckbox).ToBeCheckedAsync();
    }

    [Then(@"the traveller form should list the main policyholder, the dependent child and the non-dependent child")]
    public async Task ThenTheTravellerFormShouldListAllTravellers()
    {
        var party = Journey.Party;

        await Expect(TravellerDetails.TravellersHeading).ToBeVisibleAsync();
        await Expect(TravellerDetails.TravellerHeading($"Main policyholder - Adult 1, aged {party.AdultAges[0]}")).ToBeVisibleAsync();
        await Expect(TravellerDetails.TravellerHeading($"Dependent child 1, aged {party.DependentChildAges[0]}")).ToBeVisibleAsync();
        await Expect(TravellerDetails.TravellerHeading($"Non-dependent child 1, aged {party.NonDependentChildAges[0]}")).ToBeVisibleAsync();

        await Expect(TravellerDetails.TravellerTypeOption(DependentIndex, TravellerDetailsPage.DependentChildTypeLabel)).ToBeCheckedAsync();
        await Expect(TravellerDetails.TravellerTypeOption(NonDependentIndex, TravellerDetailsPage.NonDependentChildTypeLabel)).ToBeCheckedAsync();

        // API: the quote holds the same travellers, in the same order as the form
        var travellers = Journey.SelectQuoteCall!.ReadResponse<QuoteResponse>().Travellers;
        travellers.Select(t => (t.Type, t.Age)).Should().Equal(
            (QuoteCodes.Adult, party.AdultAges[0]),
            (QuoteCodes.DependentChild, party.DependentChildAges[0]),
            (QuoteCodes.NonDependentChild, party.NonDependentChildAges[0]));
    }

    [When(@"I enter the main policyholder details")]
    public async Task WhenIEnterTheMainPolicyholderDetails(Table table)
    {
        Journey.Policyholder = table.CreateInstance<PolicyholderDetails>();

        await TravellerDetails.FillMainPolicyholderAsync(
            Journey.Policyholder, DateOfBirthFor(Journey.Party.AdultAges[0]));
    }

    [When(@"I enter the dependent child details with first name ""(.*)"" and last name ""(.*)""")]
    public Task WhenIEnterTheDependentChildDetails(string firstName, string lastName) =>
        TravellerDetails.FillAdditionalTravellerAsync(
            DependentIndex,
            new AdditionalTraveller(firstName, lastName, DateOfBirthFor(Journey.Party.DependentChildAges[0])));

    [When(@"I enter the non-dependent child details with first name ""(.*)"" and last name ""(.*)""")]
    public Task WhenIEnterTheNonDependentChildDetails(string firstName, string lastName) =>
        TravellerDetails.FillAdditionalTravellerAsync(
            NonDependentIndex,
            new AdditionalTraveller(firstName, lastName, DateOfBirthFor(Journey.Party.NonDependentChildAges[0])));

    [Then(@"all travellers should be completed without validation errors")]
    public async Task ThenAllTravellersShouldBeCompleted()
    {
        var holder = Journey.Policyholder!;
        var party = Journey.Party;

        // UI: values as entered
        await Expect(TravellerDetails.TitleField).ToHaveValueAsync(holder.Title);
        await Expect(TravellerDetails.FirstNameField(0)).ToHaveValueAsync(holder.FirstName);
        await Expect(TravellerDetails.LastNameField(0)).ToHaveValueAsync(holder.LastName);
        await Expect(TravellerDetails.DateOfBirthField(0)).ToHaveValueAsync(_ui.Settings.FormatDate(DateOfBirthFor(party.AdultAges[0])));
        await Expect(TravellerDetails.MobilePhoneField).ToHaveValueAsync(DigitsAllowingSpaces(holder.MobilePhone));
        await Expect(TravellerDetails.EmailAddressField).ToHaveValueAsync(holder.EmailAddress);
        await Expect(TravellerDetails.DateOfBirthField(DependentIndex)).ToHaveValueAsync(_ui.Settings.FormatDate(DateOfBirthFor(party.DependentChildAges[0])));
        await Expect(TravellerDetails.DateOfBirthField(NonDependentIndex)).ToHaveValueAsync(_ui.Settings.FormatDate(DateOfBirthFor(party.NonDependentChildAges[0])));

        // UI: picking the address suggestion auto-filled the rest of the address
        await Expect(TravellerDetails.SuburbField).ToHaveValueAsync(holder.Suburb);
        await Expect(TravellerDetails.CityField).ToHaveValueAsync(holder.City);
        await Expect(TravellerDetails.PostcodeField).ToHaveValueAsync(holder.Postcode);

        // UI: dates of birth agree with the quoted ages, so no mismatch error or "Date of birth changed" prompt
        await Expect(TravellerDetails.DateOfBirthMismatchError).ToHaveCountAsync(0);
        await Expect(TravellerDetails.DateOfBirthChangedDialog).ToBeHiddenAsync();

        // The form is ready to continue (the journey deliberately stops here)
        await Expect(TravellerDetails.NextHighValueItemsButton).ToBeEnabledAsync();
        await Expect(TravellerDetails.Amount(Journey.SelectedPremium)).ToBeVisibleAsync();
        await Expect(_ui.Page).ToHaveURLAsync(TravellerDetails.UrlFor(_ui.Settings.Routes.TravellerDetails));
    }

    [Then(@"no API call in the journey should have failed")]
    public void ThenNoApiCallShouldHaveFailed()
    {
        var calls = _ui.Network.ApiCalls;

        calls.Should().NotBeEmpty("the journey talks to the quote API");
        calls.Where(c => c.Status >= 400).Should().BeEmpty("every SCTI API call should succeed");
        calls.Select(c => c.Url).Should().Contain(url => url.Contains(_ui.Settings.ApiEndpoints.CreateQuote))
            .And.Contain(url => url.Contains(_ui.Settings.ApiEndpoints.UpdateExtras))
            .And.Contain(url => url.Contains(_ui.Settings.ApiEndpoints.SelectQuote));
    }

    // ---------- helpers ----------

    // Form order follows the quote: policyholder (0), dependent child (1), non-dependent child (2).
    private const int DependentIndex = 1;
    private const int NonDependentIndex = 2;

    private void AssertQuoteMatchesTrip(QuoteResponse quote, IReadOnlyList<string> destinationCodes)
    {
        quote.IsSuccess.Should().BeTrue();
        quote.QuoteId.Should().NotBeNullOrWhiteSpace();
        quote.PolicyNumber.Should().NotBeNullOrWhiteSpace();
        quote.Destinations.Should().Equal(destinationCodes);
        DateOnly.FromDateTime(quote.StartDateOfJourney).Should().Be(Journey.DepartureDate);
        DateOnly.FromDateTime(quote.EndDate).Should().Be(Journey.ReturnDate);
        quote.Travellers.Should().HaveCount(Journey.Party.Total);
    }

    private DateOnly DateOfBirthFor(int age) =>
        _ui.Settings.LocalToday().AddYears(-age).AddDays(-DateOfBirthBufferDays);

    /// <summary>The site may format phone numbers with spaces (e.g. "021 123 4567").</summary>
    private static Regex DigitsAllowingSpaces(string number) =>
        new("^" + string.Join(@"\s*", number.Where(char.IsDigit)) + "$");

    private static bool IsYes(string answer) => answer.Equals("Yes", StringComparison.OrdinalIgnoreCase);

    private static List<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
