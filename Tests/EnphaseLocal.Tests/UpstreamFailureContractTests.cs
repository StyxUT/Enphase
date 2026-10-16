using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EnphaseLocal.Models;
using EnphaseLocal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnphaseLocal.Tests;

/// <summary>
/// Endpoint-level upstream failure contract: for every data route, an
/// HttpRequestException from the service (401, 5xx, or a connection failure
/// with no status code) is mapped to 502 application/problem+json carrying
/// status/title/detail, and never to a success payload. The registered
/// typed-client retry pipeline itself is exercised separately by
/// RetryPipelineTests.
/// </summary>
public sealed class UpstreamFailureContractTests
{
    [Theory]
    [InlineData("/production", "unauthorized")]
    [InlineData("/production", "server-error")]
    [InlineData("/production", "no-status")]
    [InlineData("/consumption", "unauthorized")]
    [InlineData("/consumption", "server-error")]
    [InlineData("/consumption", "no-status")]
    [InlineData("/powermetrics", "unauthorized")]
    [InlineData("/powermetrics", "server-error")]
    [InlineData("/powermetrics", "no-status")]
    [InlineData("/netpowerproduction", "unauthorized")]
    [InlineData("/netpowerproduction", "server-error")]
    [InlineData("/netpowerproduction", "no-status")]
    public async Task UpstreamFailure_ReturnsBadGatewayProblemDetails_WithoutSuccessPayload(string route, string failureKind)
    {
        using var factory = CreateFactory(CreateUpstreamException(failureKind));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.GetAsync(route);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal(502, root.GetProperty("status").GetInt32());
        Assert.Equal("Upstream Envoy request failed", root.GetProperty("title").GetString());
        Assert.Equal(ExpectedDetail(failureKind), root.GetProperty("detail").GetString());

        // A failure must not leak the shape of a success payload.
        Assert.DoesNotContain("\"wNow\"", body);
        Assert.DoesNotContain("\"netPowerProduction\"", body);
        Assert.DoesNotContain("<title>", body, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory(HttpRequestException upstreamException)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "EnphaseLocal")));

            builder.ConfigureServices(services =>
            {
                // Endpoint-level contract: a fake service throws exactly the
                // upstream failure under test; pipeline behavior is asserted
                // separately by RetryPipelineTests.
                services.RemoveAll<IEnphaseService>();
                services.AddSingleton<IEnphaseService>(new ThrowingEnphaseService(upstreamException));
            });
        });
    }

    /// <summary>
    /// Builds the same HttpRequestException the real pipeline produces:
    /// EnsureSuccessStatusCode's message and StatusCode for a non-2xx
    /// response, or a status-less exception for a connection failure.
    /// </summary>
    private static HttpRequestException CreateUpstreamException(string failureKind)
    {
        return failureKind switch
        {
            "unauthorized" => SuccessStatusException(HttpStatusCode.Unauthorized),
            "server-error" => SuccessStatusException(HttpStatusCode.ServiceUnavailable),
            "no-status" => new HttpRequestException("Unable to connect to Envoy."),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind), failureKind, null)
        };
    }

    private static HttpRequestException SuccessStatusException(HttpStatusCode statusCode)
    {
        var response = new HttpResponseMessage(statusCode);
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"{statusCode} did not produce a failure.");
    }

    private static string ExpectedDetail(string failureKind)
    {
        return failureKind switch
        {
            "unauthorized" => "Envoy returned 401 Unauthorized",
            "server-error" => "Response status code does not indicate success: 503 (Service Unavailable).",
            "no-status" => "Unable to connect to Envoy.",
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind), failureKind, null)
        };
    }

    private sealed class ThrowingEnphaseService : IEnphaseService
    {
        private readonly HttpRequestException _exception;

        public ThrowingEnphaseService(HttpRequestException exception) => _exception = exception;

        public Task<ProductionDataDto.Root> GetProductionDataAsync() => throw _exception;

        public Task<double> GetNetPowerProductionAsync() => throw _exception;

        public double CalculateNetPowerProduction() => throw new InvalidOperationException("Not used");
    }
}
