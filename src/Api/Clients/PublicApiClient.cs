using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TechnicalTest.Api.Models;

namespace TechnicalTest.Api.Clients;

public class PublicApiClient : IPublicApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public PublicApiClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException("HttpClient.BaseAddress must be configured.", nameof(httpClient));
        }

        _http = httpClient;
    }

    public async Task<List<Post>> GetPostsAsync()
    {
        using var response = await _http.GetAsync("posts");
        response.EnsureSuccessStatusCode();
        return await ReadResponseAsync<List<Post>>(response);
    }

    public async Task<Post?> GetPostAsync(int id)
    {
        using var response = await _http.GetAsync($"posts/{id}");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await ReadResponseAsync<Post>(response);
    }

    public async Task<Post> CreatePostAsync(Post newPost)
    {
        ArgumentNullException.ThrowIfNull(newPost);

        using var response = await _http.PostAsJsonAsync("posts", newPost, SerializerOptions);
        response.EnsureSuccessStatusCode();

        return await ReadResponseAsync<Post>(response);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(SerializerOptions);
        return value ?? throw new JsonException("The API response contained a null JSON value.");
    }
}
