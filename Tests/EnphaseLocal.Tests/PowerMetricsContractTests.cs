using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnphaseLocal;
using EnphaseLocal.Models.DTO;
using EnphaseLocal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace EnphaseLocal.Tests;

public sealed class PowerMetricsContractTests
{
    [Theory]
    [InlineData(6745, 935, 7680, -935)]
    [InlineData(6725, -2597, 4128, 2597)]
    [InlineData(0, 1800, 1800, -1800)]
    [InlineData(1800, 0, 1800, 0)]
    [InlineData(6811, -3594.54, 3216.46, 3594.54)]
    [InlineData(5881.192, -4175.004, 1706.19, 4175.0)]
    public async Task PowerMetrics_UsesSignedGridMeasurementRegardlessOfArrayOrder(
        double production, double gridImport, double consumption, double netProduction)
    {
        foreach (var netFirst in new[] { true, false })
        {
            // The installed Envoy duplicates net-consumption into total-consumption.
            var net = new { type = "EIM", measurementType = "NET-CONSUMPTION", wNow = gridImport };
            var total = new { type = "eim", measurementType = "total-consumption", wNow = gridImport };
            var payload = JsonSerializer.Serialize(new
            {
                production = new[]
                {
                    new { type = "eim", measurementType = "other", wNow = 9999.0 },
                    new { type = "inverters", measurementType = "production", wNow = 8888.0 },
                    new { type = "EIM", measurementType = "PRODUCTION", wNow = production }
                },
                consumption = netFirst ? new[] { net, total } : new[] { total, net },
                storage = Array.Empty<object>()
            });
            using var serviceClient = CreateServiceClient(payload);
            var service = CreateService(serviceClient);
            using var factory = CreateFactory(service);
            using var client = factory.CreateClient();

            var metrics = await client.GetFromJsonAsync<PowerMetricsDto>("/powermetrics");

            Assert.Equal(new PowerMetricsDto(netProduction, Math.Round(production, 2), consumption), metrics);
            Assert.Equal(netProduction, await service.GetNetPowerProductionAsync());
            var html = await client.GetStringAsync("/netpowerproduction");
            Assert.Contains($"{Math.Round(netProduction)} <span class=\"unit\">W</span>", html);
        }
    }

    [Theory]
    [InlineData("total-consumption", 500, 500.0)]
    [InlineData("total-consumption", -500, null)]
    [InlineData("other", 500, null)]
    public async Task PowerMetrics_TotalOnlyRequiresAnIdentifiedNonnegativeHouseholdLoad(
        string measurementType, double reading, double? expectedNet)
    {
        var payload = JsonSerializer.Serialize(new
        {
            production = new[] { new { type = "inverters", wNow = 1000.0 } },
            consumption = new[] { new { type = "eim", measurementType, wNow = reading } },
            storage = Array.Empty<object>()
        });
        using var serviceClient = CreateServiceClient(payload);
        using var factory = CreateFactory(CreateService(serviceClient));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/powermetrics");

        if (expectedNet.HasValue)
        {
            response.EnsureSuccessStatusCode();
            var metrics = await response.Content.ReadFromJsonAsync<PowerMetricsDto>();
            Assert.Equal(new PowerMetricsDto(expectedNet.Value, 1000, reading), metrics);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }
    }

    [Fact]
    public async Task PowerMetrics_RejectsGridReadingThatWouldImplyNegativeHouseholdConsumption()
    {
        const string payload = """
            {"production":[{"type":"eim","measurementType":"production","wNow":1000}],
             "consumption":[{"type":"eim","measurementType":"net-consumption","wNow":-2000}],
             "storage":[]}
            """;
        using var serviceClient = CreateServiceClient(payload);
        using var factory = CreateFactory(CreateService(serviceClient));
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/powermetrics", "/netpowerproduction" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }
    }

    private static HttpClient CreateServiceClient(string payload)
    {
        var handler = new MockHttpMessageHandler();
        handler.When("http://localhost/production.json").Respond("application/json", payload);
        return new HttpClient(handler);
    }

    private static EnphaseService CreateService(HttpClient client) => new(client,
        Options.Create(new EnphaseOptions { BaseAddress = "http://localhost", BearerToken = "test" }),
        NullLogger<EnphaseService>.Instance);

    private static WebApplicationFactory<Program> CreateFactory(IEnphaseService service) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "EnphaseLocal")));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEnphaseService>();
                services.AddSingleton(service);
            });
        });
}
