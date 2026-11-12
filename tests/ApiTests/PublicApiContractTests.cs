using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TechnicalTest.Api.Configuration;
using TechnicalTest.Configuration;
using static TechnicalTest.Tests.Support.JsonPlaceholderTestData;
using NUnit.Framework;

namespace TechnicalTest.Tests.ApiTests;

[TestFixture]
[Category("API")]
public class PublicApiContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private HttpClient _httpClient = null!;

    [SetUp]
    public void Setup()
    {
        var settings = PublicApiSettings.Load(ConfigurationHelper.Configuration);
        _httpClient = new HttpClient
        {
            BaseAddress = settings.BaseUri,
            Timeout = settings.Timeout
        };
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient?.Dispose();
    }

    [Test]
    public async Task GetPostsEndpoint_ReturnsOkWithExpectedCountAndSchema()
    {
        using var response = await _httpClient.GetAsync("posts");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        });

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var posts = document.RootElement;

        Assert.That(posts.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(posts.GetArrayLength(), Is.EqualTo(ExpectedPostCount));

        foreach (var post in posts.EnumerateArray())
        {
            AssertPostSchema(post);
        }
    }

    [Test]
    public async Task GetPostEndpoint_WithValidId_ReturnsOkAndExpectedPost()
    {
        const int postId = 1;

        using var response = await _httpClient.GetAsync($"posts/{postId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var post = document.RootElement;
        AssertPostSchema(post);

        Assert.That(post.GetProperty("id").GetInt32(), Is.EqualTo(postId));
    }

    [TestCase(0)]
    [TestCase(FirstIdAfterStoredPosts)]
    public async Task GetPostEndpoint_WithInvalidId_ReturnsNotFound(int id)
    {
        using var response = await _httpClient.GetAsync($"posts/{id}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreatePostEndpoint_ReturnsCreatedWithExpectedSchemaAndValues()
    {
        var newPost = new
        {
            UserId = 7,
            Title = "API contract title",
            Body = "API contract body"
        };

        using var response = await _httpClient.PostAsJsonAsync("posts", newPost, SerializerOptions);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        });

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var created = document.RootElement;
        AssertPostSchema(created);

        Assert.Multiple(() =>
        {
            Assert.That(created.GetProperty("id").GetInt32(), Is.EqualTo(FirstIdAfterStoredPosts));
            Assert.That(created.GetProperty("userId").GetInt32(), Is.EqualTo(newPost.UserId));
            Assert.That(created.GetProperty("title").GetString(), Is.EqualTo(newPost.Title));
            Assert.That(created.GetProperty("body").GetString(), Is.EqualTo(newPost.Body));
        });
    }

    private static void AssertPostSchema(JsonElement post)
    {
        Assert.That(post.ValueKind, Is.EqualTo(JsonValueKind.Object));
        AssertPositiveIntegerProperty(post, "userId");
        AssertPositiveIntegerProperty(post, "id");
        AssertNonEmptyStringProperty(post, "title");
        AssertNonEmptyStringProperty(post, "body");
    }

    private static void AssertPositiveIntegerProperty(JsonElement element, string propertyName)
    {
        Assert.That(
            element.TryGetProperty(propertyName, out var property),
            Is.True,
            $"Expected required property '{propertyName}'.");
        Assert.That(property.ValueKind, Is.EqualTo(JsonValueKind.Number), $"'{propertyName}' must be a number.");
        Assert.That(property.TryGetInt32(out var value), Is.True, $"'{propertyName}' must be a 32-bit integer.");
        Assert.That(value, Is.GreaterThan(0), $"'{propertyName}' must be positive.");
    }

    private static void AssertNonEmptyStringProperty(JsonElement element, string propertyName)
    {
        Assert.That(
            element.TryGetProperty(propertyName, out var property),
            Is.True,
            $"Expected required property '{propertyName}'.");
        Assert.That(property.ValueKind, Is.EqualTo(JsonValueKind.String), $"'{propertyName}' must be a string.");
        Assert.That(property.GetString(), Is.Not.Empty, $"'{propertyName}' must not be empty.");
    }
}
