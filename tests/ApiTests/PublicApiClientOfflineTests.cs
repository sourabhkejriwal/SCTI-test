using System.Net;
using System.Text;
using System.Text.Json;
using TechnicalTest.Api.Clients;
using TechnicalTest.Api.Models;
using NUnit.Framework;

namespace TechnicalTest.Tests.ApiTests;

[TestFixture]
[Category("API")]
public class PublicApiClientOfflineTests
{
    private RecordingHttpMessageHandler _handler = null!;
    private HttpClient _httpClient = null!;
    private PublicApiClient _apiClient = null!;

    [TearDown]
    public void TearDown()
    {
        _httpClient?.Dispose();
        _handler?.Dispose();
    }

    [Test]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new PublicApiClient(null!));

        Assert.That(exception!.ParamName, Is.EqualTo("httpClient"));
    }

    [Test]
    public void Constructor_WithoutBaseAddress_ThrowsArgumentException()
    {
        using var httpClient = new HttpClient();

        var exception = Assert.Throws<ArgumentException>(() => new PublicApiClient(httpClient));

        Assert.That(exception!.ParamName, Is.EqualTo("httpClient"));
    }

    [Test]
    public async Task GetPostsAsync_WithSuccessfulResponse_ReturnsMappedPostsAndSendsGetRequest()
    {
        ArrangeJsonResponse(HttpStatusCode.OK, """
            [
              { "userId": 1, "id": 1, "title": "First title", "body": "First body" },
              { "userId": 2, "id": 2, "title": "Second title", "body": "Second body" }
            ]
            """);

        var posts = await _apiClient.GetPostsAsync();
        var request = SingleRequest();

        Assert.That(posts, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(posts[0].UserId, Is.EqualTo(1));
            Assert.That(posts[0].Id, Is.EqualTo(1));
            Assert.That(posts[0].Title, Is.EqualTo("First title"));
            Assert.That(posts[0].Body, Is.EqualTo("First body"));
            Assert.That(posts[1].UserId, Is.EqualTo(2));
            Assert.That(posts[1].Id, Is.EqualTo(2));
            Assert.That(posts[1].Title, Is.EqualTo("Second title"));
            Assert.That(posts[1].Body, Is.EqualTo("Second body"));
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/posts"));
            Assert.That(request.Body, Is.Null);
        });
    }

    [Test]
    public void GetPostsAsync_WithServerError_ThrowsHttpRequestException()
    {
        ArrangeJsonResponse(HttpStatusCode.InternalServerError, "{}");

        var exception = Assert.ThrowsAsync<HttpRequestException>(async () => await _apiClient.GetPostsAsync());

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
    }

    [Test]
    public void GetPostsAsync_WithMalformedJson_ThrowsJsonException()
    {
        ArrangeJsonResponse(HttpStatusCode.OK, "[{ \"id\": ");

        Assert.ThrowsAsync<JsonException>(async () => await _apiClient.GetPostsAsync());
    }

    [Test]
    public void GetPostsAsync_WhenHandlerThrows_PropagatesException()
    {
        ArrangeHandler(_ => throw new HttpRequestException("Simulated network failure."));

        var exception = Assert.ThrowsAsync<HttpRequestException>(async () => await _apiClient.GetPostsAsync());

        Assert.That(exception!.Message, Is.EqualTo("Simulated network failure."));
    }

    [Test]
    public async Task GetPostAsync_WithValidId_ReturnsMappedPostAndSendsExpectedRequest()
    {
        ArrangeJsonResponse(
            HttpStatusCode.OK,
            """{ "userId": 4, "id": 25, "title": "Mapped title", "body": "Mapped body" }""");

        var post = await _apiClient.GetPostAsync(25);
        var request = SingleRequest();

        Assert.That(post, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(post!.UserId, Is.EqualTo(4));
            Assert.That(post.Id, Is.EqualTo(25));
            Assert.That(post.Title, Is.EqualTo("Mapped title"));
            Assert.That(post.Body, Is.EqualTo("Mapped body"));
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/posts/25"));
        });
    }

    [Test]
    public async Task GetPostAsync_WhenEndpointReturnsNotFound_ReturnsNull()
    {
        ArrangeJsonResponse(HttpStatusCode.NotFound, "{}");

        var post = await _apiClient.GetPostAsync(999_999);

        Assert.That(post, Is.Null);
        Assert.That(SingleRequest().RequestUri.AbsolutePath, Is.EqualTo("/posts/999999"));
    }

    [Test]
    public void GetPostAsync_WithServerError_ThrowsInsteadOfReturningNull()
    {
        ArrangeJsonResponse(HttpStatusCode.InternalServerError, "{}");

        var exception = Assert.ThrowsAsync<HttpRequestException>(
            async () => await _apiClient.GetPostAsync(25));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
    }

    [Test]
    public void GetPostAsync_WithNullJsonResponse_ThrowsJsonException()
    {
        ArrangeJsonResponse(HttpStatusCode.OK, "null");

        var exception = Assert.ThrowsAsync<JsonException>(async () => await _apiClient.GetPostAsync(25));

        Assert.That(exception!.Message, Does.Contain("null JSON value"));
    }

    [Test]
    public async Task CreatePostAsync_WithSuccessfulResponse_SendsJsonAndReturnsCreatedPost()
    {
        ArrangeJsonResponse(
            HttpStatusCode.Created,
            """{ "userId": 7, "id": 101, "title": "New title", "body": "New body" }""");
        var newPost = new Post
        {
            UserId = 7,
            Title = "New title",
            Body = "New body"
        };

        var created = await _apiClient.CreatePostAsync(newPost);
        var request = SingleRequest();
        using var requestJson = JsonDocument.Parse(request.Body!);
        var root = requestJson.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/posts"));
            Assert.That(request.ContentType, Is.EqualTo("application/json"));
            Assert.That(root.GetProperty("userId").GetInt32(), Is.EqualTo(7));
            Assert.That(root.GetProperty("title").GetString(), Is.EqualTo("New title"));
            Assert.That(root.GetProperty("body").GetString(), Is.EqualTo("New body"));
            Assert.That(root.TryGetProperty("UserId", out _), Is.False, "The request must use web JSON casing.");
            Assert.That(created.UserId, Is.EqualTo(7));
            Assert.That(created.Id, Is.EqualTo(101));
            Assert.That(created.Title, Is.EqualTo("New title"));
            Assert.That(created.Body, Is.EqualTo("New body"));
        });
    }

    [Test]
    public void CreatePostAsync_WithBadRequest_ThrowsHttpRequestException()
    {
        ArrangeJsonResponse(HttpStatusCode.BadRequest, "{}");
        var newPost = new Post { UserId = 1, Title = "Title", Body = "Body" };

        var exception = Assert.ThrowsAsync<HttpRequestException>(
            async () => await _apiClient.CreatePostAsync(newPost));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void CreatePostAsync_WithNullPost_ThrowsArgumentNullExceptionWithoutSendingRequest()
    {
        ArrangeJsonResponse(HttpStatusCode.Created, "{}");

        var exception = Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _apiClient.CreatePostAsync(null!));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.ParamName, Is.EqualTo("newPost"));
            Assert.That(_handler.Requests, Is.Empty);
        });
    }

    private void ArrangeJsonResponse(HttpStatusCode statusCode, string json)
    {
        ArrangeHandler(
            _ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private void ArrangeHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _handler = new RecordingHttpMessageHandler(responseFactory);
        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        _apiClient = new PublicApiClient(_httpClient);
    }

    private CapturedRequest SingleRequest()
    {
        Assert.That(_handler.Requests, Has.Count.EqualTo(1));
        return _handler.Requests.Single();
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri RequestUri,
        string? ContentType,
        string? Body);

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Content?.Headers.ContentType?.MediaType,
                body));

            return _responseFactory(request);
        }
    }
}
