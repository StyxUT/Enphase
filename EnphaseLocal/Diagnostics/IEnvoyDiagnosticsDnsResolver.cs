using System.Net;

namespace EnphaseLocal.Diagnostics;

public interface IEnvoyDiagnosticsDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemEnvoyDiagnosticsDnsResolver : IEnvoyDiagnosticsDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}
