using Polly;
using Polly.Extensions.Http;

namespace EnphaseLocal;

/// <summary>
/// Builds the transient-HTTP retry policy used by the Enphase typed HttpClient.
/// The per-attempt delay is injectable so tests can prove retry semantics
/// without the production 3^attempt-second backoff.
/// </summary>
public static class TransientHttpRetryPolicy
{
    public const int RetryAttempts = 5;

    /// <summary>Production backoff: 3, 9, 27, 81, 243 seconds.</summary>
    public static TimeSpan DelayForAttempt(int attempt) => TimeSpan.FromSeconds(Math.Pow(3, attempt));

    public static AsyncPolicy<HttpResponseMessage> Create(Func<int, TimeSpan> delayForAttempt)
        => HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(RetryAttempts, attempt => delayForAttempt(attempt));
}
