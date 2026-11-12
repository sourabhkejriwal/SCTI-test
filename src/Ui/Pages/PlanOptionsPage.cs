using Microsoft.Playwright;
using TechnicalTest.Ui.Helpers;
using TechnicalTest.Ui.Pages.Models;

namespace TechnicalTest.Ui.Pages;

/// <summary>
/// Plan options step (?step=plan-options): choose a plan, then customise discount, excess,
/// cancellation cover and extra cover. Each customisation re-prices the quote via PUT /api/Quotes/extras.
/// </summary>
/// <remarks>
/// Options are chosen and read through the shared radio-group helpers on <see cref="BasePage"/>.
/// </remarks>
public sealed class PlanOptionsPage : BasePage
{
    public const string DiscountGroup = "schsDiscount";
    public const string ExcessGroup = "excess";
    public const string CancellationCoverGroup = "cancellationCover";

    public PlanOptionsPage(IPage page, TestSettings settings) : base(page, settings)
    {
    }

    public ILocator SelectPlanButtons =>
        Page.GetByRole(AriaRole.Button, new() { Name = "Select and customise plan", Exact = true });

    public ILocator PlanSelectedLabel => Page.GetByText("Plan selected", new() { Exact = true });

    public ILocator NextTravellerDetailsButton =>
        Page.GetByRole(AriaRole.Button, new() { Name = "Next: Traveller details", Exact = true });

    public ILocator ExtraCoverCheckbox(string coverName) =>
        Page.GetByLabel(coverName, new() { Exact = true });

    /// <summary>
    /// The plan cards have no accessible grouping, so the plan is chosen by position.
    /// The first card is the One return journey (International Comprehensive Single Trip) plan;
    /// the step definitions confirm this through the API (selected premium = single trip).
    /// </summary>
    public async Task SelectOneReturnJourneyPlanAsync()
    {
        await SelectPlanButtons.First.ClickAsync();
        await RadioGroup(DiscountGroup).WaitForAsync();
    }

    public Task<IResponse?> ChooseDiscountAsync(string answer) => ChooseOptionAsync(DiscountGroup, answer);

    public Task<IResponse?> ChooseExcessAsync(string excess) => ChooseOptionAsync(ExcessGroup, excess);

    public Task<IResponse?> ChooseCancellationCoverAsync(string cover) => ChooseOptionAsync(CancellationCoverGroup, cover);

    /// <summary>Ticks an extra cover. Returns null when nothing changes ("None" or already ticked).</summary>
    public async Task<IResponse?> AddExtraCoverAsync(string coverName)
    {
        if (coverName.Equals(QuoteCodes.NoExtraCover, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var checkbox = ExtraCoverCheckbox(coverName);
        if (await checkbox.IsCheckedAsync())
        {
            return null;
        }

        return await RunAndWaitForApiAsync(Settings.ApiEndpoints.UpdateExtras, "PUT", () => checkbox.CheckAsync());
    }

    /// <summary>Clicks "Next: Traveller details" and returns the PUT /api/Quotes/select response.</summary>
    public async Task<IResponse> ContinueToTravellerDetailsAsync()
    {
        var response = await RunAndWaitForApiAsync(
            Settings.ApiEndpoints.SelectQuote, "PUT", () => NextTravellerDetailsButton.ClickAsync());
        await Page.WaitForURLAsync(UrlFor(Settings.Routes.TravellerDetails));
        return response;
    }

    /// <summary>Selects an option; returns null when it was already selected (the site makes no API call then).</summary>
    private async Task<IResponse?> ChooseOptionAsync(string groupName, string optionLabel)
    {
        if (await Option(groupName, optionLabel).IsCheckedAsync())
        {
            return null;
        }

        return await RunAndWaitForApiAsync(
            Settings.ApiEndpoints.UpdateExtras, "PUT", () => ClickOptionAsync(groupName, optionLabel));
    }
}
