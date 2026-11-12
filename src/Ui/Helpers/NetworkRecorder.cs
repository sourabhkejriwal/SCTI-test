using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;

namespace TechnicalTest.Ui.Helpers;

/// <summary>One call to the site's own API, with request and response bodies captured.</summary>
public sealed record ApiCall(string Method, string Url, int Status, string? RequestBody, string ResponseBody)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public T ReadResponse<T>() =>
        JsonSerializer.Deserialize<T>(ResponseBody, JsonOptions)
        ?? throw new InvalidOperationException($"{Method} {Url} returned an empty body.");

    public T ReadRequest<T>() =>
        JsonSerializer.Deserialize<T>(RequestBody ?? throw new InvalidOperationException($"{Method} {Url} had no request body."), JsonOptions)
        ?? throw new InvalidOperationException($"{Method} {Url} request body could not be parsed.");

    public override string ToString() => $"{Method} {Url} -> {Status}";
}

/// <summary>
/// Captures network traffic for a browser context:
/// every response (method, URL, status) for logging, and the site's own API calls with full bodies.
/// API bodies are captured through a route so they stay readable even when the page navigates
/// straight after the call (e.g. "Get a quote" posts, then loads the plan options page).
/// </summary>
/// <remarks>
/// Trade-off: a routed call is sent by Playwright on the page's behalf (same cookies and headers) and the
/// result is handed back to the page, so the site's API traffic does not go through the browser's own
/// network stack. Everything outside the API glob is only observed, never intercepted.
/// </remarks>
public sealed class NetworkRecorder
{
    private readonly ConcurrentQueue<string> _allResponses = new();
    private readonly ConcurrentQueue<ApiCall> _apiCalls = new();
    private readonly ConcurrentDictionary<IRequest, ApiCall> _apiCallsByRequest = new();

    public IReadOnlyCollection<string> AllResponses => _allResponses.ToArray();

    public IReadOnlyCollection<ApiCall> ApiCalls => _apiCalls.ToArray();

    public async Task AttachAsync(IBrowserContext context, string apiGlob)
    {
        context.Response += (_, response) =>
            _allResponses.Enqueue($"{response.Request.Method} {response.Url} -> {response.Status}");
        context.RequestFailed += (_, request) =>
            _allResponses.Enqueue($"{request.Method} {request.Url} -> FAILED ({request.Failure})");

        await context.RouteAsync(apiGlob, async route =>
        {
            IAPIResponse fetched;
            try
            {
                fetched = await route.FetchAsync();
            }
            catch (PlaywrightException)
            {
                // Network failure: surface it to the page as a failed request (logged via RequestFailed).
                // The request is not re-sent, so a non-idempotent call such as the quote POST cannot be duplicated.
                await route.AbortAsync();
                return;
            }

            var body = await fetched.TextAsync();
            var call = new ApiCall(route.Request.Method, route.Request.Url, fetched.Status, route.Request.PostData, body);
            _apiCallsByRequest[route.Request] = call;
            _apiCalls.Enqueue(call);

            await route.FulfillAsync(new RouteFulfillOptions { Response = fetched, Body = body });
        });
    }

    /// <summary>Returns the recorded API call (with bodies) behind a response the test waited for.</summary>
    public ApiCall For(IResponse response) =>
        _apiCallsByRequest.TryGetValue(response.Request, out var call)
            ? call
            : throw new InvalidOperationException($"No recorded API call for {response.Request.Method} {response.Url}. Is it covered by the API capture glob?");
}
