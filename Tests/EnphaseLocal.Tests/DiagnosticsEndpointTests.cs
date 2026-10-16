using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using EnphaseLocal;
using EnphaseLocal.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnphaseLocal.Tests;

public sealed class DiagnosticsEndpointTests
{
    [Fact]
    public async Task InvalidBaseAddress_ReturnsDiagnosticShapeWithoutCreatingHttpClient()
    {
        using var handler = new DelegateHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("HTTP must not be called for an invalid BaseAddress."));
        using var httpClientFactory = new TrackingHttpClientFactory(handler);
        var dnsResolver = new TrackingDnsResolver((_, _) =>
            throw new InvalidOperationException("DNS must not be called for an invalid BaseAddress."));
        using var factory = CreateFactory(
            new EnphaseOptions { BaseAddress = "not-an-absolute-uri", BearerToken = "your-bearer-token-here" },
            httpClientFactory,
            dnsResolver);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/envoydiagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        AssertDiagnosticResponseShape(root);
        Assert.Equal("not-an-absolute-uri", root.GetProperty("baseAddress").GetString());
        Assert.False(root.GetProperty("baseAddressValid").GetBoolean());
        Assert.False(root.GetProperty("tokenConfigured").GetBoolean());
        Assert.Empty(root.GetProperty("dnsAddresses").EnumerateArray());
        Assert.Null(root.GetProperty("dnsError").GetString());
        Assert.Null(root.GetProperty("upstreamStatusCode").GetString());
        Assert.Null(root.GetProperty("upstreamReasonPhrase").GetString());
        Assert.Equal("Enphase:BaseAddress is not a valid absolute URI.", root.GetProperty("upstreamError").GetString());
        Assert.Equal(0, httpClientFactory.CreateCount);
        Assert.Equal(0, dnsResolver.CallCount);
    }

    [Fact]
    public async Task ConfiguredToken_ReportsSuccessfulDnsAndUpstreamRequest()
    {
        const string bearerToken = "dummy-diagnostics-token";
        using var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("http://envoy.example:8080/production.json", request.RequestUri?.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(bearerToken, request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var httpClientFactory = new TrackingHttpClientFactory(handler);
        var dnsResolver = new TrackingDnsResolver((host, _) =>
        {
            Assert.Equal("envoy.example", host);
            return Task.FromResult(new[] { IPAddress.Parse("192.0.2.10") });
        });
        using var factory = CreateFactory(
            new EnphaseOptions { BaseAddress = "http://envoy.example:8080", BearerToken = bearerToken },
            httpClientFactory,
            dnsResolver);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/envoydiagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        AssertDiagnosticResponseShape(root);
        Assert.True(root.GetProperty("baseAddressValid").GetBoolean());
        Assert.True(root.GetProperty("tokenConfigured").GetBoolean());
        Assert.Equal("192.0.2.10", root.GetProperty("dnsAddresses")[0].GetString());
        Assert.Null(root.GetProperty("dnsError").GetString());
        Assert.Equal((int)HttpStatusCode.OK, root.GetProperty("upstreamStatusCode").GetInt32());
        Assert.Equal("OK", root.GetProperty("upstreamReasonPhrase").GetString());
        Assert.Null(root.GetProperty("upstreamError").GetString());
        Assert.DoesNotContain(bearerToken, body, StringComparison.Ordinal);
        Assert.Equal(1, dnsResolver.CallCount);
        Assert.Equal(1, httpClientFactory.CreateCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("your-bearer-token-here")]
    public async Task MissingBlankOrPlaceholderToken_IsNotSentOrReportedAsConfigured(string? bearerToken)
    {
        using var handler = new DelegateHttpMessageHandler((request, _) =>
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var httpClientFactory = new TrackingHttpClientFactory(handler);
        var dnsResolver = new TrackingDnsResolver((_, _) =>
            Task.FromResult(new[] { IPAddress.Parse("192.0.2.10") }));
        using var factory = CreateFactory(
            new EnphaseOptions { BaseAddress = "http://envoy.example", BearerToken = bearerToken! },
            httpClientFactory,
            dnsResolver);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/envoydiagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("tokenConfigured").GetBoolean());
        Assert.DoesNotContain("Authorization", body, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            Assert.DoesNotContain(bearerToken, body, StringComparison.Ordinal);
        }
        Assert.Equal(1, httpClientFactory.CreateCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Service Unavailable")]
    public async Task UpstreamStatusAndReasonPhrase_AreReported(
        HttpStatusCode upstreamStatus,
        string reasonPhrase)
    {
        using var handler = new DelegateHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(upstreamStatus) { ReasonPhrase = reasonPhrase }));
        using var httpClientFactory = new TrackingHttpClientFactory(handler);
        var dnsResolver = new TrackingDnsResolver((_, _) =>
            Task.FromResult(new[] { IPAddress.Parse("192.0.2.10") }));
        using var factory = CreateFactory(
            new EnphaseOptions { BaseAddress = "http://envoy.example", BearerToken = "dummy-token" },
            httpClientFactory,
            dnsResolver);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/envoydiagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal((int)upstreamStatus, document.RootElement.GetProperty("upstreamStatusCode").GetInt32());
        Assert.Equal(reasonPhrase, document.RootElement.GetProperty("upstreamReasonPhrase").GetString());
        Assert.Null(document.RootElement.GetProperty("upstreamError").GetString());
        Assert.DoesNotContain("dummy-token", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransportFailure_DoesNotExposeBearerTokenInResponseOrLogs()
    {
        const string bearerToken = "dummy-secret-token";
        using var handler = new DelegateHttpMessageHandler((_, _) =>
            throw new HttpRequestException($"Connection failed while using {bearerToken}."));
        using var loggerProvider = new CapturingLoggerProvider();
        using var httpClientFactory = new TrackingHttpClientFactory(handler);
        var dnsResolver = new TrackingDnsResolver((_, _) =>
            Task.FromResult(new[] { IPAddress.Parse("192.0.2.10") }));
        using var factory = CreateFactory(
            new EnphaseOptions { BaseAddress = "http://envoy.example", BearerToken = bearerToken },
            httpClientFactory,
            dnsResolver,
            loggerProvider);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/envoydiagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("Connection failed while using [REDACTED].", document.RootElement.GetProperty("upstreamError").GetString());
        Assert.DoesNotContain(bearerToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(bearerToken, string.Join(Environment.NewLine, loggerProvider.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DnsFailure_IsReportedAndDoesNotExposeBearerToken()
    {
        const string bearerToken = "dummy-dns-secret";
        using var handler = new DelegateHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var loggerProvider = new CapturingLoggerProvider();
        using var httpClientFactory = new TrackingHttpClientFactory(handler);
        var dnsResolver = new TrackingDnsResolver((_, _) =>
            throw new HttpRequestException($"DNS lookup failed for {bearerToken}."));
        using var factory = CreateFactory(
            new EnphaseOptions { BaseAddress = "http://envoy.example", BearerToken = bearerToken },
            httpClientFactory,
            dnsResolver,
            loggerProvider);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/envoydiagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("DNS lookup failed for [REDACTED].", document.RootElement.GetProperty("dnsError").GetString());
        Assert.Equal((int)HttpStatusCode.OK, document.RootElement.GetProperty("upstreamStatusCode").GetInt32());
        Assert.DoesNotContain(bearerToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(bearerToken, string.Join(Environment.NewLine, loggerProvider.Entries), StringComparison.Ordinal);
        Assert.Equal(1, httpClientFactory.CreateCount);
    }

    [Fact]
    public async Task UpstreamTimeout_IsReportedAsAnUpstreamError()
    {
        using var handler = new DelegateHttpMessageHandler((_, _) =>
            throw new TaskCanceledException("The Envoy diagnostics request timed out."));
        using var httpClientFactory = new TrackingHttpClientFactory(handler);
        var dnsResolver = new TrackingDnsResolver((_, _) =>
            Task.FromResult(new[] { IPAddress.Parse("192.0.2.10") }));
        using var factory = CreateFactory(
            new EnphaseOptions { BaseAddress = "http://envoy.example", BearerToken = string.Empty },
            httpClientFactory,
            dnsResolver);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/envoydiagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Null(document.RootElement.GetProperty("upstreamStatusCode").GetString());
        Assert.Equal("The Envoy diagnostics request timed out.", document.RootElement.GetProperty("upstreamError").GetString());
        Assert.Null(document.RootElement.GetProperty("upstreamReasonPhrase").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        EnphaseOptions options,
        TrackingHttpClientFactory httpClientFactory,
        TrackingDnsResolver dnsResolver,
        ILoggerProvider? loggerProvider = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseContentRoot(Path.Combine(FindRepositoryRoot(), "EnphaseLocal"));
            if (loggerProvider is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
            }
            builder.ConfigureServices(services =>
            {
                services.Configure<EnphaseOptions>(configured =>
                {
                    configured.BaseAddress = options.BaseAddress;
                    configured.BearerToken = options.BearerToken;
                });
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(httpClientFactory);
                services.RemoveAll<IEnvoyDiagnosticsDnsResolver>();
                services.AddSingleton<IEnvoyDiagnosticsDnsResolver>(dnsResolver);
            });
        });
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EnphaseLocal", "EnphaseLocal.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Enphase repository root.");
    }

    private static void AssertDiagnosticResponseShape(JsonElement root)
    {
        var expectedProperties = new[]
        {
            "baseAddress",
            "baseAddressValid",
            "tokenConfigured",
            "dnsAddresses",
            "dnsError",
            "upstreamStatusCode",
            "upstreamReasonPhrase",
            "upstreamError"
        };
        var actualProperties = root.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal(expectedProperties.OrderBy(name => name, StringComparer.Ordinal),
            actualProperties.OrderBy(name => name, StringComparer.Ordinal));
    }

    private sealed class TrackingHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
    {
        private readonly List<HttpClient> _clients = [];

        public int CreateCount { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreateCount++;
            Assert.Equal("EnvoyDiagnostics", name);
            var client = new HttpClient(handler, disposeHandler: false);
            _clients.Add(client);
            return client;
        }

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }
        }
    }

    private sealed class DelegateHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class TrackingDnsResolver(
        Func<string, CancellationToken, Task<IPAddress[]>> resolve) : IEnvoyDiagnosticsDnsResolver
    {
        public int CallCount { get; private set; }

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            CallCount++;
            return resolve(host, cancellationToken);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IEnumerable<string> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.Enqueue(formatter(state, exception));
                if (exception is not null)
                {
                    entries.Enqueue(exception.ToString());
                }
            }
        }
    }
}
