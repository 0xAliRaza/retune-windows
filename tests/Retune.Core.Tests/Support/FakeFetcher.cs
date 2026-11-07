using System.Text;
using Retune.Core.Net;

namespace Retune.Core.Tests.Support;

/// <summary>Scripted HTTP responses, consumed in order; records every requested URL.</summary>
internal sealed class FakeFetcher : IHttpFetcher
{
    private readonly Queue<Func<HttpBody>> _script = new();
    private readonly Func<string, HttpBody>? _router;

    public FakeFetcher()
    {
    }

    /// <summary>Answers by URL instead of a fixed order (for parallel requests).</summary>
    public FakeFetcher(Func<string, HttpBody> router)
    {
        _router = router;
    }

    public List<string> Requests { get; } = [];

    public FakeFetcher Then(int status, string body = "")
    {
        _script.Enqueue(() => new HttpBody(status, Encoding.UTF8.GetBytes(body)));
        return this;
    }

    public FakeFetcher ThenFail(FetchFailure failure)
    {
        _script.Enqueue(() => throw new FetchException(failure, $"simulated {failure}"));
        return this;
    }

    public Task<HttpBody> GetAsync(string url, long maxBytes, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(url);
        }

        if (_router is not null)
        {
            return Task.FromResult(_router(url));
        }

        return _script.Count == 0
            ? throw new InvalidOperationException("No scripted response left.")
            : Task.FromResult(_script.Dequeue()());
    }

    public static HttpBody Ok(string body) => new(200, Encoding.UTF8.GetBytes(body));
}
