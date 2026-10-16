using System.Text.Json;
using EnphaseLocal;
using EnphaseLocal.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace EnphaseLocal.Tests;

public sealed class EnphaseLocalTests
{
    private const string EnvoyAddress = "http://envoy.example:8080";
    private const string ProductionPath = "/production.json";

    [Theory]
    [InlineData(500, 650, -150)] // Import: household load exceeds production.
    [InlineData(800, 300, 500)]  // Export: production exceeds household load.
    [InlineData(400, 400, 0)]    // Balanced: production equals household load.
    public async Task GetNetPowerProductionAsync_ReturnsExactNetForImportExportAndBalance(
        double productionWatts,
        double consumptionWatts,
        double expectedNetWatts)
    {
        using var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, EnvoyAddress + ProductionPath)
            .Respond("application/json", CreatePayload(productionWatts, consumptionWatts));
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var netPower = await service.GetNetPowerProductionAsync();

        Assert.Equal(expectedNetWatts, netPower);
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetNetPowerProductionAsync_UsesLatestUpstreamReadingsOnEachCall()
    {
        using var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, EnvoyAddress + ProductionPath)
            .Respond("application/json", CreatePayload(1000, 900));
        handler.Expect(HttpMethod.Get, EnvoyAddress + ProductionPath)
            .Respond("application/json", CreatePayload(1000, 600));
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var firstNetPower = await service.GetNetPowerProductionAsync();
        var secondNetPower = await service.GetNetPowerProductionAsync();

        Assert.Equal(100, firstNetPower);
        Assert.Equal(400, secondNetPower);
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetProductionDataAsync_UsesConfiguredRequestAndMapsJsonPropertyNamesCaseInsensitively()
    {
        const string payload = """
            {
              "PrOdUcTiOn": [
                { "TyPe": "inverters", "ActiveCount": 1, "ReadingTime": 123, "WnOw": 1250, "WhLifetime": 10000, "MeasurementType": "production" }
              ],
              "cOnSuMpTiOn": [
                { "Type": "eim", "ActiveCount": 1, "MeasurementType": "total-consumption", "rEaDiNgTiMe": 123, "WnOw": 250, "WhLifetime": 5000 }
              ],
              "StOrAgE": []
            }
            """;
        using var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, EnvoyAddress + ProductionPath)
            .WithHeaders("Authorization", "Bearer dummy-token")
            .Respond("application/json", payload);
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var data = await service.GetProductionDataAsync();
        var netPower = service.CalculateNetPowerProduction();

        Assert.Equal(1250, data.Production[0].WNow);
        Assert.Equal(250, data.Consumption[0].WNow);
        Assert.Equal(1000, netPower);
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetNetPowerProductionAsync_PrefersIdentifiedEimMeasurementsOverOtherMeters()
    {
        var payload = JsonSerializer.Serialize(new
        {
            production = new[]
            {
                new { type = "inverters", activeCount = 1, readingTime = 123, wNow = 1000, whLifetime = 5000, measurementType = "production" },
                new { type = "eim", activeCount = 1, readingTime = 123, wNow = 2000, whLifetime = 10000, measurementType = "production" }
            },
            consumption = new[]
            {
                new { type = "utility", activeCount = 1, measurementType = "total-consumption", readingTime = 123, wNow = 600, whLifetime = 2500 },
                new { type = "eim", activeCount = 1, measurementType = "total-consumption", readingTime = 123, wNow = 500, whLifetime = 2000 }
            },
            storage = Array.Empty<object>()
        });
        using var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, EnvoyAddress + ProductionPath)
            .Respond("application/json", payload);
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var netPower = await service.GetNetPowerProductionAsync();

        Assert.Equal(1500, netPower);
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public void CalculateNetPowerProduction_BeforeFetchingData_Throws()
    {
        using var handler = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient);

        var exception = Assert.Throws<InvalidOperationException>(() => service.CalculateNetPowerProduction());

        Assert.Equal("Production data is not available.", exception.Message);
    }

    private static EnphaseService CreateService(HttpClient httpClient) => new(
        httpClient,
        Options.Create(new EnphaseOptions
        {
            BaseAddress = EnvoyAddress,
            BearerToken = "dummy-token"
        }),
        NullLogger<EnphaseService>.Instance);

    private static string CreatePayload(double productionWatts, double consumptionWatts) =>
        JsonSerializer.Serialize(new
        {
            production = new[]
            {
                new
                {
                    type = "inverters",
                    activeCount = 1,
                    readingTime = 123,
                    wNow = productionWatts,
                    whLifetime = 10000,
                    measurementType = "production"
                }
            },
            consumption = new[]
            {
                new
                {
                    type = "eim",
                    activeCount = 1,
                    measurementType = "total-consumption",
                    readingTime = 123,
                    wNow = consumptionWatts,
                    whLifetime = 5000
                }
            },
            storage = Array.Empty<object>()
        });
}
