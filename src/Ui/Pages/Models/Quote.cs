namespace TechnicalTest.Ui.Pages.Models;

/// <summary>Body returned by /api/Quotes/create, /api/Quotes/extras and /api/Quotes/select.</summary>
public sealed class QuoteResponse
{
    public string QuoteId { get; set; } = "";
    public string PolicyNumber { get; set; } = "";
    public DateTime StartDateOfJourney { get; set; }
    public DateTime EndDate { get; set; }
    public List<string> Destinations { get; set; } = new();
    public List<QuotePremium> Premiums { get; set; } = new();
    public List<QuoteTraveller> Travellers { get; set; } = new();
    public bool IsSuccess { get; set; }

    public QuotePremium SelectedPremium =>
        Premiums.SingleOrDefault(p => p.IsSelected)
        ?? throw new InvalidOperationException($"Quote {QuoteId} has no selected premium.");

    public QuotePremium PremiumFor(string policyTypeCode) =>
        Premiums.SingleOrDefault(p => p.PolicyTypeCode == policyTypeCode)
        ?? throw new InvalidOperationException($"Quote {QuoteId} has no '{policyTypeCode}' premium.");
}

public sealed class QuotePremium
{
    public string PolicyTypeCode { get; set; } = "";
    public bool IsSelected { get; set; }
    public string? SelectedExcess { get; set; }
    public string? SelectedCancellationCover { get; set; }
    public bool HasSkiCover { get; set; }
    public bool HasMopedCover { get; set; }
    public bool HasCruiseCover { get; set; }
    public bool IsSchsMember { get; set; }
    public decimal TotalPremium { get; set; }
}

public sealed class QuoteTraveller
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public int Age { get; set; }
}

/// <summary>Body posted to /api/Quotes/create.</summary>
public sealed class CreateQuoteRequest
{
    public string ProductCode { get; set; } = "";
    public List<string> Destinations { get; set; } = new();
    public List<int> AdultsAge { get; set; } = new();
    public List<int> DependentChildAges { get; set; } = new();
    public List<int> NonDependentChildAges { get; set; } = new();
}

/// <summary>Codes and labels the site uses for the plans, traveller types and extra covers.</summary>
public static class QuoteCodes
{
    public const string SingleTripPolicy = "ST";

    public const string Adult = "Adult";
    public const string DependentChild = "DependentChild";
    public const string NonDependentChild = "NonDependentChild";

    public const string NoExtraCover = "None";
    public const string MopedCover = "Moped or motorbike";
    public const string SkiCover = "Skiing or snowboarding";
    public const string CruiseCover = "Cruise";
}
