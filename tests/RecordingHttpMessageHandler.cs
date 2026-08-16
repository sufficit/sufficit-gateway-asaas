using System.Net;
using System.Text;

namespace Sufficit.Gateway.Asaas.Tests;

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public IList<RecordedHttpRequest> Requests { get; } = new List<RecordedHttpRequest>();

    public void EnqueueJson(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        IReadOnlyDictionary<string, string>? headers = null)
        => EnqueueResponse((_, _) =>
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (headers is not null)
            {
                foreach (var header in headers)
                    response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return Task.FromResult(response);
        });

    public void EnqueueResponse(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        => _responses.Enqueue(response);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var headers = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(value => value.Key, value => value.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new RecordedHttpRequest
        {
            Method = request.Method,
            Uri = request.RequestUri!,
            Headers = headers,
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken)
        });

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No fake HTTP response was configured.");
        }

        return await _responses.Dequeue()(request, cancellationToken);
    }
}
