using System.Net;
using System.Text;

namespace EnphaseLocal.Tests;

/// <summary>
/// Controlled primary handler for pipeline tests: replays a fixed sequence of
/// upstream responses, then repeats the fallback response, and records every
/// request it receives so retry behavior can be asserted exactly. Each send
/// produces a fresh response instance because the typed-client retry pipeline
/// disposes the prior response on every retry.
/// </summary>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    /// <summary>Describes the upstream response to replay for one attempt.</summary>
    public sealed record Step(
        HttpStatusCode StatusCode = HttpStatusCode.OK,
        string Body = "",
        string ContentType = "application/json");

    private readonly Step _fallback;
    private readonly Queue<Step> _sequence;
    private readonly object _gate = new();

    public ScriptedHttpMessageHandler(Step fallback, params Step[] sequence)
    {
        _fallback = fallback;
        _sequence = new Queue<Step>(sequence);
    }

    public int RequestCount { get; private set; }

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Step step;
        lock (_gate)
        {
            step = _sequence.Count > 0 ? _sequence.Dequeue() : _fallback;
            RequestCount += 1;
            LastRequest = request;
        }

        return Task.FromResult(new HttpResponseMessage(step.StatusCode)
        {
            Content = new StringContent(step.Body, Encoding.UTF8, step.ContentType)
        });
    }
}
