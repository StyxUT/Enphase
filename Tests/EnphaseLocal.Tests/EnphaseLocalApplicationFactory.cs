using EnphaseLocal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.IO;

namespace EnphaseLocal.Tests;

public sealed class EnphaseLocalApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        EnsureNetPowerProductionTemplateAvailable();

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEnphaseService>();
            services.AddSingleton<IEnphaseService>(new FakeEnphaseService());
        });
    }

    private static void EnsureNetPowerProductionTemplateAvailable()
    {
        var baseDirViews = Path.Combine(AppContext.BaseDirectory, "Views");
        var baseDirTemplate = Path.Combine(baseDirViews, "NetPowerProduction.html");
        if (File.Exists(baseDirTemplate))
        {
            return;
        }

        Directory.CreateDirectory(baseDirViews);

        var sourceTemplate = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "EnphaseLocal", "Views", "NetPowerProduction.html"));

        File.Copy(sourceTemplate, baseDirTemplate, overwrite: true);
    }

    private sealed class FakeEnphaseService : IEnphaseService
    {
        public Task<EnphaseLocal.Models.ProductionDataDto.Root> GetProductionDataAsync()
        {
            var production = new[]
            {
                new EnphaseLocal.Models.ProductionDataDto.Production(
                    Type: "inverters",
                    ActiveCount: 1,
                    ReadingTime: 123,
                    WNow: 1000.0,
                    WhLifetime: 10000,
                    MeasurementType: "production",
                    VarhLeadLifetime: null,
                    VarhLagLifetime: null,
                    VahLifetime: null,
                    RmsCurrent: null,
                    RmsVoltage: null,
                    ReactPwr: null,
                    ApprntPwr: null,
                    PwrFactor: null,
                    WhToday: null,
                    WhLastSevenDays: null,
                    VahToday: null,
                    VarhLeadToday: null,
                    VarhLagToday: null,
                    Lines: null)
            };

            var consumption = new[]
            {
                new EnphaseLocal.Models.ProductionDataDto.Consumption(
                    Type: "eim",
                    ActiveCount: 1,
                    MeasurementType: "total-consumption",
                    ReadingTime: 123,
                    WNow: 500.0,
                    WhLifetime: 5000,
                    VarhLeadLifetime: 0,
                    VarhLagLifetime: 0,
                    VahLifetime: 0,
                    RmsCurrent: 0,
                    RmsVoltage: 0,
                    ReactPwr: 0,
                    ApprntPwr: 0,
                    PwrFactor: 0,
                    WhToday: 0,
                    WhLastSevenDays: 0,
                    VahToday: 0,
                    VarhLeadToday: 0,
                    VarhLagToday: 0,
                    Lines: null)
            };

            var storage = Array.Empty<EnphaseLocal.Models.ProductionDataDto.Storage>();
            return Task.FromResult(new EnphaseLocal.Models.ProductionDataDto.Root(production, consumption, storage));
        }

        public Task<double> GetNetPowerProductionAsync() => Task.FromResult(500.0);
        public double CalculateNetPowerProduction() => 500.0;
    }
}
