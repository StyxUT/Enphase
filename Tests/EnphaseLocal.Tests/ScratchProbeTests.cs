using System.Net;
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

public sealed class ScratchProbeTests
{
    private static ProductionDataDto.Consumption Cons(string mt, double wNow) =>
        new("eim", 1, mt, 0, wNow, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null);

    [Theory]
    [InlineData("empty-production", """{"production":[],"consumption":[{"type":"eim","measurementType":"net-consumption","wNow":-500}],"storage":[]}""")]
    [InlineData("empty-consumption", """{"production":[{"type":"eim","measurementType":"production","wNow":1000}],"consumption":[],"storage":[]}""")]
    [InlineData("null-production", """{"production":null,"consumption":[{"type":"eim","measurementType":"net-consumption","wNow":-500}],"storage":[]}""")]
    [InlineData("null-consumption", """{"production":[{"type":"eim","measurementType":"production","wNow":1000}],"consumption":null,"storage":[]}""")]
    [InlineData("null-storage", """{"production":[{"type":"eim","measurementType":"production","wNow":1000}],"consumption":[{"type":"eim","measurementType":"net-consumption","wNow":-500}],"storage":null}""")]
    [InlineData("malformed-json", """{"production":[""")]
    [InlineData("production-not-array", """{"production":42,"consumption":[],"storage":[]}""")]
    public async Task Probe_EndpointStatus(string name, string payload)
    {
        var handler = new MockHttpMessageHandler();
        handler.When("http://localhost/production.json").Respond("application/json", payload);
        var svcClient = new HttpClient(handler);
        var service = new EnphaseService(svcClient,
            Options.Create(new EnphaseOptions { BaseAddress = "http://localhost", BearerToken = "test" }),
            NullLogger<EnphaseService>.Instance);
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "EnphaseLocal")));
            builder.ConfigureServices(services => { services.RemoveAll<IEnphaseService>(); services.AddSingleton(service); });
        });
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/powermetrics");
        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"[PROBE] {name}: status={(int)response.StatusCode} body={body}");
    }

    [Fact]
    public void Probe_DtoWithNullCollections()
    {
        try
        {
            var root = new ProductionDataDto.Root(null!, new[] { Cons("net-consumption", -500) }, Array.Empty<ProductionDataDto.Storage>());
            var dto = PowerMetricsDto.FromProductionData(root);
            Console.WriteLine($"[PROBE] dto-null-production: OK {dto}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PROBE] dto-null-production: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
