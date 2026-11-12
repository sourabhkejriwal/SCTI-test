using TechnicalTest.Api.Clients;
using TechnicalTest.Api.Configuration;
using TechnicalTest.Api.Models;
using TechnicalTest.Api.Processors;
using TechnicalTest.Configuration;
using static TechnicalTest.Tests.Support.JsonPlaceholderTestData;
using NUnit.Framework;

namespace TechnicalTest.Tests.IntegrationTests;

[TestFixture]
[Category("Integration")]
/// <summary>
/// Exercises the typed API client and application processing pipeline against live JSONPlaceholder;
/// the API-category fixture separately verifies the service's raw HTTP contract.
/// </summary>
public class PublicApiIntegrationTests
{
    private HttpClient _httpClient = null!;
    private PublicApiClient _apiClient = null!;
    private PostProcessor _processor = null!;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        var settings = PublicApiSettings.Load(ConfigurationHelper.Configuration);
        _httpClient = new HttpClient
        {
            BaseAddress = settings.BaseUri,
            Timeout = settings.Timeout
        };
        _apiClient = new PublicApiClient(_httpClient);
        _processor = new PostProcessor(_apiClient);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient?.Dispose();
    }

    [Test]
    public async Task GetPostsAsync_UsingRealApi_ReturnsCompleteConsistentDataset()
    {
        var posts = await _apiClient.GetPostsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(posts, Has.Count.EqualTo(ExpectedPostCount));
            Assert.That(
                posts.Select(post => post.Id),
                Is.EqualTo(Enumerable.Range(1, ExpectedPostCount)),
                "The live collection should contain every stored post exactly once and in API order.");
            Assert.That(posts, Has.All.Matches<Post>(post => post.UserId > 0));
            Assert.That(posts, Has.All.Matches<Post>(post => !string.IsNullOrWhiteSpace(post.Title)));
            Assert.That(posts, Has.All.Matches<Post>(post => !string.IsNullOrWhiteSpace(post.Body)));
        });
    }

    [Test]
    public async Task GetPostAsync_ForMultipleValidIds_MatchesPostsCollection()
    {
        var ids = new[] { 1, 50, ExpectedPostCount };
        var posts = await _apiClient.GetPostsAsync();
        var requestedPosts = await Task.WhenAll(ids.Select(_apiClient.GetPostAsync));

        Assert.Multiple(() =>
        {
            for (var index = 0; index < ids.Length; index++)
            {
                var id = ids[index];
                var expected = posts.FirstOrDefault(post => post.Id == id);
                var actual = requestedPosts[index];

                Assert.That(expected, Is.Not.Null, $"The posts collection should contain post {id}.");
                Assert.That(actual, Is.Not.Null, $"The individual endpoint should return post {id}.");

                if (expected is null || actual is null)
                {
                    continue;
                }

                AssertPostMatches(actual, expected, id);
            }
        });
    }

    [Test]
    public async Task GetValidPostsAsync_UsingRealClient_PreservesEveryValidLivePost()
    {
        var sourcePosts = await _apiClient.GetPostsAsync();
        var validPosts = await _processor.GetValidPostsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(sourcePosts, Has.Count.EqualTo(ExpectedPostCount));
            Assert.That(validPosts, Has.Count.EqualTo(ExpectedPostCount));
            Assert.That(
                validPosts.Select(PostSnapshot.From),
                Is.EqualTo(sourcePosts.Select(PostSnapshot.From)),
                "Every valid live post should survive the processing pipeline with all content unchanged.");
        });
    }

    [Test]
    public async Task CreatePostAsync_UsingRealApi_RoundTripsRequestData()
    {
        var newPost = new Post
        {
            UserId = 7,
            Title = "Integration test title",
            Body = "Integration test body"
        };

        var created = await _apiClient.CreatePostAsync(newPost);

        Assert.Multiple(() =>
        {
            Assert.That(created.Id, Is.EqualTo(FirstIdAfterStoredPosts));
            Assert.That(created.UserId, Is.EqualTo(newPost.UserId));
            Assert.That(created.Title, Is.EqualTo(newPost.Title));
            Assert.That(created.Body, Is.EqualTo(newPost.Body));
        });
    }

    [Test]
    public async Task GetPostAsync_WithMissingId_ReturnsNull()
    {
        var post = await _apiClient.GetPostAsync(FirstIdAfterStoredPosts);

        Assert.That(post, Is.Null);
    }

    private static void AssertPostMatches(Post actual, Post expected, int requestedId)
    {
        Assert.That(actual.Id, Is.EqualTo(expected.Id), $"Post {requestedId} has an inconsistent ID.");
        Assert.That(actual.UserId, Is.EqualTo(expected.UserId), $"Post {requestedId} has an inconsistent user ID.");
        Assert.That(actual.Title, Is.EqualTo(expected.Title), $"Post {requestedId} has an inconsistent title.");
        Assert.That(actual.Body, Is.EqualTo(expected.Body), $"Post {requestedId} has inconsistent body content.");
    }

    private sealed record PostSnapshot(int UserId, int Id, string Title, string Body)
    {
        internal static PostSnapshot From(Post post) =>
            new(post.UserId, post.Id, post.Title, post.Body);
    }
}
