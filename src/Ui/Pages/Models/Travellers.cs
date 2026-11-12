namespace TechnicalTest.Ui.Pages.Models;

/// <summary>Who is travelling, by type and age, as entered on the quote form.</summary>
public sealed record TravellerParty(
    IReadOnlyList<int> AdultAges,
    IReadOnlyList<int> DependentChildAges,
    IReadOnlyList<int> NonDependentChildAges)
{
    public int Total => AdultAges.Count + DependentChildAges.Count + NonDependentChildAges.Count;
}

/// <summary>Main policyholder details (populated from the feature's vertical table).</summary>
public sealed class PolicyholderDetails
{
    public string Title { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string MobilePhone { get; set; } = "";
    public string EmailAddress { get; set; } = "";
    public string StreetAddress { get; set; } = "";
    public string AddressSuggestion { get; set; } = "";
    public string Suburb { get; set; } = "";
    public string City { get; set; } = "";
    public string Postcode { get; set; } = "";
}

/// <summary>An additional (non-policyholder) traveller on the traveller details form.</summary>
public sealed record AdditionalTraveller(string FirstName, string LastName, DateOnly DateOfBirth);
