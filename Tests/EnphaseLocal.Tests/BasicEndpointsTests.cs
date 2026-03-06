using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnphaseLocal.Models;
using EnphaseLocal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnphaseLocal.Tests;

public sealed class BasicEndpointsTests : IClassFixture<EnphaseLocalApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BasicEndpointsTests(EnphaseLocalApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Healthcheck_ReturnsNoContent()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthcheck");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Production_ReturnsJson()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/production");

        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<ProductionDataDto.Production[]>();
        Assert.NotNull(items);
        Assert.NotEmpty(items);
        Assert.Equal(1000.0, items[0].WNow);
    }

    [Fact]
    public async Task Consumption_ReturnsJson()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/consumption");

        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<ProductionDataDto.Consumption[]>();
        Assert.NotNull(items);
        Assert.NotEmpty(items);
        Assert.Equal(500.0, items[0].WNow);
    }

    [Fact]
    public async Task Production_WhenEnvoyUnauthorized_ReturnsBadGatewayProblemDetails()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                var contentRoot = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..",
                    "EnphaseLocal"));
                builder.UseContentRoot(contentRoot);

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IEnphaseService>();
                    services.AddSingleton<IEnphaseService>(new UnauthorizedEnphaseService());
                });
            });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.GetAsync("/production");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType.MediaType);
    }

    private sealed class UnauthorizedEnphaseService : IEnphaseService
    {
        public Task<ProductionDataDto.Root> GetProductionDataAsync() =>
            throw new HttpRequestException("Unauthorized", inner: null, statusCode: HttpStatusCode.Unauthorized);

        public Task<double> GetNetPowerProductionAsync() =>
            throw new HttpRequestException("Unauthorized", inner: null, statusCode: HttpStatusCode.Unauthorized);

        public double CalculateNetPowerProduction() =>
            throw new InvalidOperationException("Not used");
    }
}
