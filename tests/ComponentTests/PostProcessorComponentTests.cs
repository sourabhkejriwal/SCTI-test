using TechnicalTest.Api.Clients;
using TechnicalTest.Api.Models;
using TechnicalTest.Api.Processors;
using NUnit.Framework;

namespace TechnicalTest.Tests.ComponentTests;

[TestFixture]
[Category("Component")]
public class PostProcessorComponentTests
{
    [Test]
    public async Task GetValidPostsAsync_WithValidPosts_ReturnsEveryPostInSourceOrder()
    {
        var posts = new List<Post>
        {
            ValidPost(id: 3, userId: 1),
            ValidPost(id: 1, userId: 2),
            ValidPost(id: 2, userId: 3)
        };
        var processor = CreateProcessor(posts);

        var result = await processor.GetValidPostsAsync();

        Assert.That(result.Select(post => post.Id), Is.EqualTo(new[] { 3, 1, 2 }));
    }

    [Test]
    public async Task GetValidPostsAsync_AppliesAllPostBusinessRules()
    {
        var posts = new List<Post>
        {
            ValidPost(id: 1, userId: 1),
            new() { Id = 2, UserId = 0, Title = "Title", Body = "Body" },
            new() { Id = 3, UserId = -1, Title = "Title", Body = "Body" },
            new() { Id = 4, UserId = 1, Title = "   ", Body = "Body" },
            new() { Id = 5, UserId = 1, Title = "Title", Body = "\t" }
        };
        var processor = CreateProcessor(posts);

        var result = await processor.GetValidPostsAsync();

        Assert.That(result.Select(post => post.Id), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public async Task GetValidPostsAsync_WithPartiallyPopulatedPosts_FiltersIncompleteData()
    {
        var posts = new List<Post>
        {
            new() { Id = 1, UserId = 1 },
            new() { Id = 2, UserId = 1, Title = "Title" },
            new() { Id = 3, UserId = 1, Body = "Body" },
            new() { Id = 4, UserId = 1, Title = null!, Body = "Body" },
            new() { Id = 5, UserId = 1, Title = "Title", Body = null! },
            ValidPost(id: 6, userId: 1)
        };
        var processor = CreateProcessor(posts);

        var result = await processor.GetValidPostsAsync();

        Assert.That(result.Select(post => post.Id), Is.EqualTo(new[] { 6 }));
    }

    [Test]
    public async Task GetValidPostsAsync_WithAllInvalidPosts_ReturnsEmptyList()
    {
        var posts = new List<Post>
        {
            new() { Id = 1, UserId = 0, Title = "Title", Body = "Body" },
            new() { Id = 2, UserId = 1, Title = "", Body = "Body" },
            new() { Id = 3, UserId = 1, Title = "Title", Body = "" }
        };
        var processor = CreateProcessor(posts);

        var result = await processor.GetValidPostsAsync();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetValidPostsAsync_WithMixedData_ReturnsOnlyValidPosts()
    {
        var posts = new List<Post>
        {
            ValidPost(id: 10, userId: 1),
            new() { Id = 20, UserId = 0, Title = "Invalid", Body = "Body" },
            ValidPost(id: 30, userId: 2),
            new() { Id = 40, UserId = 3, Title = "Title", Body = "" },
            ValidPost(id: 50, userId: 4)
        };
        var processor = CreateProcessor(posts);

        var result = await processor.GetValidPostsAsync();

        Assert.That(result.Select(post => post.Id), Is.EqualTo(new[] { 10, 30, 50 }));
    }

    [Test]
    public async Task GetValidPostsAsync_WithNullEntry_SkipsItAndReturnsRemainingPosts()
    {
        var posts = new List<Post>
        {
            ValidPost(id: 1, userId: 1),
            null!,
            ValidPost(id: 2, userId: 2)
        };
        var processor = CreateProcessor(posts);

        var result = await processor.GetValidPostsAsync();

        Assert.That(result.Select(post => post.Id), Is.EqualTo(new[] { 1, 2 }));
    }

    private static Post ValidPost(int id, int userId)
    {
        return new Post
        {
            Id = id,
            UserId = userId,
            Title = $"Title {id}",
            Body = $"Body {id}"
        };
    }

    private static PostProcessor CreateProcessor(IEnumerable<Post> posts)
    {
        return new PostProcessor(new InMemoryPublicApiClient(posts));
    }

    private sealed class InMemoryPublicApiClient : IPublicApiClient
    {
        private readonly List<Post> _posts;

        public InMemoryPublicApiClient(IEnumerable<Post> posts)
        {
            _posts = posts.ToList();
        }

        public Task<List<Post>> GetPostsAsync()
        {
            return Task.FromResult(_posts.ToList());
        }

        public Task<Post?> GetPostAsync(int id)
        {
            throw new NotSupportedException();
        }

        public Task<Post> CreatePostAsync(Post newPost)
        {
            throw new NotSupportedException();
        }
    }
}
