using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnphaseLocal;
using EnphaseLocal.Models;
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

    [Theory]
    [InlineData("production")]
    [InlineData("consumption")]
    public void PowerMetrics_RejectsMissingIdentifiedMeasurements(string missing)
    {
        var root = CreateRoot(
            production: missing == "production" ? Array.Empty<ProductionDataDto.Production>() : new[] { Production("eim", "production", 1000) },
            consumption: missing == "consumption" ? Array.Empty<ProductionDataDto.Consumption>() : new[] { Consumption("eim", "total-consumption", 500) });

        Assert.Throws<InvalidOperationException>(() => PowerMetricsDto.FromProductionData(root));
    }

    [Theory]
    [InlineData("production", double.NaN)]
    [InlineData("production", double.PositiveInfinity)]
    [InlineData("production", double.NegativeInfinity)]
    [InlineData("grid", double.NaN)]
    [InlineData("grid", double.PositiveInfinity)]
    [InlineData("grid", double.NegativeInfinity)]
    [InlineData("total", double.NaN)]
    [InlineData("total", double.PositiveInfinity)]
    [InlineData("total", double.NegativeInfinity)]
    [InlineData("production", -1)]
    [InlineData("total", -1)]
    public void PowerMetrics_RejectsInvalidDirectDtoMeasurements(string field, double value)
    {
        var production = field == "production" ? value : 1000;
        var grid = field == "grid" ? value : (double?)null;
        var total = field == "total" ? value : 500;
        var root = CreateRoot(production: new[] { Production("eim", "production", production) },
            consumption: grid.HasValue
                ? new[] { Consumption("eim", "net-consumption", grid.Value) }
                : new[] { Consumption("eim", "total-consumption", total) });

        Assert.Throws<InvalidOperationException>(() => PowerMetricsDto.FromProductionData(root));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 500, 500, -500)]
    [InlineData(1000, -1000, 0, 1000)]
    public void PowerMetrics_AcceptsValidZeroMeasurements(double production, double grid, double consumption, double net)
    {
        var root = CreateRoot(
            production: new[] { Production("eim", "production", production) },
            consumption: new[] { Consumption("eim", "net-consumption", grid), Consumption("eim", "total-consumption", 9999) });

        Assert.Equal(new PowerMetricsDto(net, production, consumption), PowerMetricsDto.FromProductionData(root));
    }

    [Fact]
    public void PowerMetrics_RoundsBothReadingsBeforeCalculatingNet()
    {
        var root = CreateRoot(
            production: new[] { Production("eim", "production", 5881.192) },
            consumption: new[] { Consumption("eim", "net-consumption", -4175.004) });

        var result = PowerMetricsDto.FromProductionData(root);

        Assert.Equal(5881.19, result.PowerProduction);
        Assert.Equal(1706.19, result.PowerConsumption);
        Assert.Equal(Math.Round(result.PowerProduction - result.PowerConsumption, 2), result.NetPowerProduction);
    }

    [Theory]
    [InlineData("{\"production\":null,\"consumption\":[],\"storage\":[]}")]
    [InlineData("{\"production\":[],\"consumption\":null,\"storage\":[]}")]
    [InlineData("{\"production\":[],\"consumption\":[],\"storage\":[]}")]
    [InlineData("{\"production\":[{\"type\":\"eim\",\"measurementType\":\"production\",\"wNow\":-1}],\"consumption\":[{\"type\":\"eim\",\"measurementType\":\"total-consumption\",\"wNow\":500}],\"storage\":[]}")]
    [InlineData("{\"production\":[{\"type\":\"eim\",\"measurementType\":\"production\",\"wNow\":1000}],\"consumption\":[{\"type\":\"eim\",\"measurementType\":\"total-consumption\",\"wNow\":-1}],\"storage\":[]}")]
    [InlineData("{\"production\":[{\"type\":\"other\",\"measurementType\":\"production\",\"wNow\":1000}],\"consumption\":[],\"storage\":[]}")]
    [InlineData("{")]
    public async Task PowerMetrics_InvalidUpstreamMeasurementsReturnProblemDetails(string payload)
    {
        using var serviceClient = CreateServiceClient(payload);
        using var factory = CreateFactory(CreateService(serviceClient));
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/powermetrics", "/netpowerproduction" })
        {
            using var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(502, problem.RootElement.GetProperty("status").GetInt32());
            Assert.False(problem.RootElement.TryGetProperty("netPowerProduction", out _));
        }
    }

    private static ProductionDataDto.Root CreateRoot(
        IReadOnlyList<ProductionDataDto.Production> production,
        IReadOnlyList<ProductionDataDto.Consumption> consumption) =>
        new(production, consumption, Array.Empty<ProductionDataDto.Storage>());

    private static ProductionDataDto.Production Production(string type, string measurement, double watts) => new(
        Type: type, ActiveCount: 1, ReadingTime: 1, WNow: watts, WhLifetime: 0, MeasurementType: measurement,
        VarhLeadLifetime: null, VarhLagLifetime: null, VahLifetime: null, RmsCurrent: null, RmsVoltage: null,
        ReactPwr: null, ApprntPwr: null, PwrFactor: null, WhToday: null, WhLastSevenDays: null,
        VahToday: null, VarhLeadToday: null, VarhLagToday: null, Lines: null);

    private static ProductionDataDto.Consumption Consumption(string type, string measurement, double watts) => new(
        Type: type, ActiveCount: 1, MeasurementType: measurement, ReadingTime: 1, WNow: watts,
        WhLifetime: 0, VarhLeadLifetime: 0, VarhLagLifetime: 0, VahLifetime: 0, RmsCurrent: 0,
        RmsVoltage: 0, ReactPwr: 0, ApprntPwr: 0, PwrFactor: 0, WhToday: 0, WhLastSevenDays: 0,
        VahToday: 0, VarhLeadToday: 0, VarhLagToday: 0, Lines: null);

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
