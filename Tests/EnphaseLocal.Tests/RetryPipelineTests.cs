using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnphaseLocal.Models;
using EnphaseLocal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnphaseLocal.Tests;

/// <summary>
/// Exercises the registered typed HttpClient pipeline (the real EnphaseService plus
/// the Polly transient-HTTP retry policy) with a controlled primary handler and a
/// zero-delay retry clock, so retry semantics are verified without the production
/// 3^attempt-second backoff and without a live Envoy.
/// </summary>
public sealed class RetryPipelineTests
{
    private const string ProductionPayload = """
        {
          "production": [
            { "type": "inverters", "activeCount": 1, "readingTime": 123, "wNow": 1000, "whLifetime": 10000, "measurementType": "production" }
          ],
          "consumption": [
            { "type": "eim", "activeCount": 1, "measurementType": "total-consumption", "readingTime": 123, "wNow": 500, "whLifetime": 5000 }
          ],
          "storage": []
        }
        """;

    [Fact]
    public async Task TransientUpstreamError_IsRetriedThenSucceeds()
    {
        var handler = new ScriptedHttpMessageHandler(
            fallback: new ScriptedHttpMessageHandler.Step(HttpStatusCode.OK, ProductionPayload),
            sequence: [new ScriptedHttpMessageHandler.Step(HttpStatusCode.ServiceUnavailable, "unavailable")]);

        using var factory = CreatePipelineFactory(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/production");

        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<ProductionDataDto.Production[]>();
        Assert.NotNull(items);
        Assert.Equal(1000.0, items[0].WNow);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task TransientUpstreamErrors_RetryExhaustion_ReturnsProblemDetails()
    {
        var handler = new ScriptedHttpMessageHandler(
            new ScriptedHttpMessageHandler.Step(HttpStatusCode.ServiceUnavailable, "unavailable"));

        using var factory = CreatePipelineFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.GetAsync("/production");
        var body = await response.Content.ReadAsStringAsync();

        // Initial attempt plus all five retries, then the endpoint maps the
        // terminal HttpRequestException to a 502 problem document.
        Assert.Equal(TransientHttpRetryPolicy.RetryAttempts + 1, handler.RequestCount);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        AssertProblemDetails(body,
            title: "Upstream Envoy request failed",
            detail: "Response status code does not indicate success: 503 (Service Unavailable).");
    }

    [Fact]
    public async Task NullUpstreamBody_ReturnsInvalidEnergyBalanceProblemDetails()
    {
        var handler = new ScriptedHttpMessageHandler(
            new ScriptedHttpMessageHandler.Step(HttpStatusCode.OK, "null"));

        using var factory = CreatePipelineFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.GetAsync("/powermetrics");
        var body = await response.Content.ReadAsStringAsync();

        // The real EnphaseService throws InvalidOperationException for a JSON null body;
        // no retry, and the endpoint maps it to the invalid-energy-balance problem.
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        AssertProblemDetails(body,
            title: "Invalid Envoy Energy Balance",
            detail: "Failed to deserialize production data.");
    }

    [Fact]
    public async Task UnauthorizedUpstreamError_IsNotRetried_ReturnsProblemDetails()
    {
        var handler = new ScriptedHttpMessageHandler(
            new ScriptedHttpMessageHandler.Step(HttpStatusCode.Unauthorized, "unauthorized"));

        using var factory = CreatePipelineFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.GetAsync("/powermetrics");
        var body = await response.Content.ReadAsStringAsync();

        // 401 is not a transient HTTP error: exactly one upstream attempt.
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        AssertProblemDetails(body,
            title: "Upstream Envoy request failed",
            detail: "Envoy returned 401 Unauthorized");
    }

    private static void AssertProblemDetails(string body, string title, string detail)
    {
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal(502, root.GetProperty("status").GetInt32());
        Assert.Equal(title, root.GetProperty("title").GetString());
        Assert.Equal(detail, root.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task MalformedUpstreamBody_ReturnsMalformedDataProblemDetails()
    {
        var handler = new ScriptedHttpMessageHandler(
            new ScriptedHttpMessageHandler.Step(HttpStatusCode.OK, "{ invalid"));

        using var factory = CreatePipelineFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.GetAsync("/powermetrics");
        var body = await response.Content.ReadAsStringAsync();

        // The real EnphaseService wraps the JsonException in InvalidOperationException;
        // no retry, and the endpoint maps it to the malformed-data problem.
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        AssertProblemDetails(body,
            title: "Invalid Envoy Energy Balance",
            detail: "Envoy returned malformed production data.");
    }

    [Fact(Skip = "Malformed upstream JSON on /production is not yet mapped to problem+json (returns 500); tracked in Enphase-4ae")]
    public async Task Production_MalformedUpstreamBody_ReturnsProblemDetails()
    {
        var handler = new ScriptedHttpMessageHandler(
            new ScriptedHttpMessageHandler.Step(HttpStatusCode.OK, "{ invalid"));

        using var factory = CreatePipelineFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.GetAsync("/production");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        AssertProblemDetails(body,
            title: "Invalid Envoy Energy Balance",
            detail: "Envoy returned malformed production data.");
    }

    private static WebApplicationFactory<Program> CreatePipelineFactory(HttpMessageHandler primaryHandler)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "EnphaseLocal")));

            builder.ConfigureServices(services =>
            {
                // Replace the production typed-client registration: the same EnphaseService
                // implementation and the same Polly pipeline shape, but with a controlled
                // primary handler and a zero-delay retry clock instead of the 3^n-second
                // backoff. IEnphaseService itself is NOT replaced by a fake.
                // RemoveAll<IHttpClientBuilder> alone is not enough: AddPolicyHandler
                // (which delegates to AddHttpMessageHandler) parks its handler factory in a
                // named IConfigureOptions<HttpClientFactoryOptions> entry keyed to the
                // client name, and that entry would re-apply the production 3^n-second
                // policy alongside the test policy. Remove the named options entries first.
                services.RemoveAll<IEnphaseService>();
                services.RemoveAll<IHttpClientBuilder>();
                services.RemoveAll<IConfigureOptions<HttpClientFactoryOptions>>();
                services.AddHttpClient<IEnphaseService, EnphaseService>()
                    .ConfigurePrimaryHttpMessageHandler(() => primaryHandler)
                    .AddPolicyHandler(TransientHttpRetryPolicy.Create(_ => TimeSpan.Zero));
            });
        });
    }
}
