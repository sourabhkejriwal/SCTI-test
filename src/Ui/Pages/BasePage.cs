using System.Text.RegularExpressions;
using Microsoft.Playwright;
using TechnicalTest.Ui.Helpers;

namespace TechnicalTest.Ui.Pages;

/// <summary>
/// Shared page-object plumbing. Locators in derived pages use user-facing queries only
/// (GetByLabel first, then GetByRole, then GetByText); no CSS or XPath selectors.
/// </summary>
public abstract class BasePage
{
    protected readonly IPage Page;
    protected readonly TestSettings Settings;

    protected BasePage(IPage page, TestSettings settings)
    {
        Page = page;
        Settings = settings;
    }

    /// <summary>Matches a URL on the configured site that contains the given route fragment.</summary>
    public Regex UrlFor(string route) =>
        new("^" + Regex.Escape(Settings.BaseUrl.TrimEnd('/')) + ".*" + Regex.Escape(route), RegexOptions.IgnoreCase);

    public Regex HomeUrl => new("^" + Regex.Escape(Settings.BaseUrl.TrimEnd('/')) + "/?$", RegexOptions.IgnoreCase);

    /// <summary>A displayed money amount, formatted the way the site shows it.</summary>
    public ILocator Amount(decimal amount) =>
        Page.GetByText(Settings.FormatCurrency(amount), new() { Exact = true }).First;

    public ILocator RadioGroup(string groupName) =>
        Page.GetByRole(AriaRole.Radiogroup, new() { Name = groupName, Exact = true });

    /// <summary>The radio input for an option, used to read its checked state.</summary>
    public ILocator Option(string groupName, string optionLabel) =>
        RadioGroup(groupName).GetByLabel(optionLabel, new() { Exact = true });

    /// <summary>
    /// The site's radio inputs are visually hidden inside segmented-button labels, so an option is chosen
    /// by clicking its visible label text inside the named radio group.
    /// </summary>
    protected Task ClickOptionAsync(string groupName, string optionLabel) =>
        RadioGroup(groupName).GetByText(optionLabel, new() { Exact = true }).ClickAsync();

    /// <summary>Runs an action and waits for the API response it triggers (endpoint match is a URL substring).</summary>
    protected Task<IResponse> RunAndWaitForApiAsync(string endpoint, string? method, Func<Task> action) =>
        Page.RunAndWaitForResponseAsync(
            action,
            response => response.Url.Contains(endpoint, StringComparison.OrdinalIgnoreCase)
                        && (method is null || response.Request.Method.Equals(method, StringComparison.OrdinalIgnoreCase)),
            new PageRunAndWaitForResponseOptions { Timeout = Settings.Timeout });

    protected async Task ConfirmDialogAsync(ILocator dialog)
    {
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
    }
}
