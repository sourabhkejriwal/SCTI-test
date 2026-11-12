namespace TechnicalTest.Tests.Support;

internal static class JsonPlaceholderTestData
{
    internal const int ExpectedPostCount = 100;

    // JSONPlaceholder simulates POST creation with ID 101 without persisting it, so the same ID
    // remains the first missing resource for subsequent GET requests.
    internal const int FirstIdAfterStoredPosts = ExpectedPostCount + 1;
}
