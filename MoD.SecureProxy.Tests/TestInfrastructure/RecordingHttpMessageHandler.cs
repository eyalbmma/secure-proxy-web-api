using System.Collections.Concurrent;
using System.Net;

namespace MoD.SecureProxy.Tests.TestInfrastructure;

public sealed record RecordedUpstreamRequest(
    string Method,
    string Url,
    string Body,
    IReadOnlyDictionary<string, string[]> Headers);

public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<RecordedUpstreamRequest> _requests = new();
    private readonly TaskCompletionSource<bool> _requestRecorded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<RecordedUpstreamRequest> Requests => _requests.ToArray();
    public Task WaitForRequestAsync => _requestRecorded.Task;

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Responder { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var headers = request.Headers
            .ToDictionary(
                header => header.Key,
                header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        _requests.Enqueue(new RecordedUpstreamRequest(
            request.Method.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            body,
            headers));
        _requestRecorded.TrySetResult(true);

        if (Responder is not null)
        {
            return await Responder(request, cancellationToken);
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("mock upstream response")
        };
    }
}

public sealed class RecordingHttpClientFactory(RecordingHttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
}
