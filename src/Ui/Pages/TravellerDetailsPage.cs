using Microsoft.Playwright;
using TechnicalTest.Ui.Helpers;
using TechnicalTest.Ui.Pages.Models;

namespace TechnicalTest.Ui.Pages;

/// <summary>
/// Traveller details step (?step=traveller-details): eligibility questions, privacy declaration and the
/// traveller form. The journey under test stops on this page (no "Next: High-value items", no payment).
/// </summary>
/// <remarks>
/// Every traveller block repeats the same field labels ("First name", "Date of birth", ...) with no accessible
/// grouping, so fields are addressed by label plus traveller index. The index follows the quote's traveller order:
/// 0 = main policyholder (adult), then dependent children, then non-dependent children.
/// </remarks>
public sealed class TravellerDetailsPage : BasePage
{
    public const string ResidencyGroup = "CitizenOrLivingInNZ";
    public const string JourneyStartedGroup = "JourneyStarted NZ";
    public const string FraudGroup = "Fraud NZ";

    public const string AdultTypeLabel = "Adult (Aged 18 to 118)";
    public const string DependentChildTypeLabel = "Dependent child (20 and under)";
    public const string NonDependentChildTypeLabel = "Non-dependent child (17 and under)";

    public TravellerDetailsPage(IPage page, TestSettings settings) : base(page, settings)
    {
    }

    public ILocator EligibilityHeading => Page.GetByText("Traveller Eligibility", new() { Exact = true });

    public ILocator PolicyAgreementCheckbox =>
        Page.GetByLabel("I confirm that I have read, understood, and agree to the travel insurance policy");

    public ILocator PrivacyAuthorisationCheckbox =>
        Page.GetByLabel("I authorise SCTI to collect, use, and share personal information");

    public ILocator TravellersHeading => Page.GetByText("Travellers", new() { Exact = true });

    public ILocator TravellerHeading(string text) => Page.GetByText(text, new() { Exact = true });

    public ILocator TitleField => Page.GetByLabel("Title", new() { Exact = true });
    public ILocator FirstNameField(int travellerIndex) => Page.GetByLabel("First name").Nth(travellerIndex);
    public ILocator LastNameField(int travellerIndex) => Page.GetByLabel("Last name").Nth(travellerIndex);
    public ILocator DateOfBirthField(int travellerIndex) => Page.GetByLabel("Date of birth").Nth(travellerIndex);
    public ILocator MobilePhoneField => Page.GetByLabel("Mobile phone");
    public ILocator EmailAddressField => Page.GetByLabel("Email address");
    public ILocator StreetAddressField => Page.GetByLabel("Street address");
    public ILocator SuburbField => Page.GetByLabel("Suburb");
    public ILocator CityField => Page.GetByLabel("City");
    public ILocator PostcodeField => Page.GetByLabel("Postcode");

    /// <summary>"What type of traveller is this?" option for an additional traveller (traveller index 1, 2, ...).</summary>
    public ILocator TravellerTypeOption(int travellerIndex, string typeLabel) =>
        Page.GetByLabel(typeLabel).Nth(travellerIndex - 1);

    public ILocator DateOfBirthMismatchError =>
        Page.GetByText("Entered age and date of birth do not match.");

    public ILocator DateOfBirthChangedDialog => Page.GetByText("Date of birth changed", new() { Exact = true });

    public ILocator NextHighValueItemsButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = "Next: High-value items", Exact = true });

    public async Task AnswerEligibilityAsync(string residency, string journeyStarted, string fraud)
    {
        await ClickOptionAsync(ResidencyGroup, residency);
        await ClickOptionAsync(JourneyStartedGroup, journeyStarted);
        await ClickOptionAsync(FraudGroup, fraud);
    }

    public async Task AcceptPrivacyDeclarationAsync()
    {
        await PolicyAgreementCheckbox.CheckAsync();
        await PrivacyAuthorisationCheckbox.CheckAsync();
        await TravellersHeading.WaitForAsync();
    }

    /// <summary>
    /// Fills the main policyholder. The street address uses autocomplete: the text is typed key by key and the
    /// suggestion is selected. The scenario verifies the resulting suburb, city and postcode values.
    /// </summary>
    public async Task FillMainPolicyholderAsync(PolicyholderDetails details, DateOnly dateOfBirth)
    {
        await TitleField.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = details.Title, Exact = true }).ClickAsync();

        await FirstNameField(0).FillAsync(details.FirstName);
        await LastNameField(0).FillAsync(details.LastName);
        await FillDateOfBirthAsync(0, dateOfBirth);
        await MobilePhoneField.FillAsync(details.MobilePhone);
        await EmailAddressField.FillAsync(details.EmailAddress);

        await StreetAddressField.PressSequentiallyAsync(details.StreetAddress, new() { Delay = 50 });
        await Page.GetByText(details.AddressSuggestion, new() { Exact = true }).ClickAsync();
    }

    public async Task FillAdditionalTravellerAsync(int travellerIndex, AdditionalTraveller traveller)
    {
        await FirstNameField(travellerIndex).FillAsync(traveller.FirstName);
        await LastNameField(travellerIndex).FillAsync(traveller.LastName);
        await FillDateOfBirthAsync(travellerIndex, traveller.DateOfBirth);
    }

    /// <summary>
    /// The date of birth is validated against the age given on the quote when the field loses focus.
    /// The field is masked: fill it once while empty (re-filling a completed value is ignored by the mask).
    /// </summary>
    private async Task FillDateOfBirthAsync(int travellerIndex, DateOnly dateOfBirth)
    {
        var field = DateOfBirthField(travellerIndex);
        await field.FillAsync(Settings.FormatDate(dateOfBirth));
        await field.BlurAsync();
    }
}
