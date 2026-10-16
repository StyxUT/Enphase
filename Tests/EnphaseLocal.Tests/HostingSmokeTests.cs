using System.Diagnostics;
using System.Net;
using System.Text.Json;
using EnphaseLocal.Models;
using EnphaseLocal.Models.DTO;
using EnphaseLocal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnphaseLocal.Tests;

public sealed class HostingSmokeTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task HostedEnvironment_ServesSwaggerHealthAndDataRoutes(string environment)
    {
        using var factory = CreateFactory(environment, new StaticEnphaseService(CreateData(netPower: 250)));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(environment == "Production" ? "http://localhost" : "https://localhost"),
            AllowAutoRedirect = false
        });

        var health = await client.GetAsync("/healthcheck");
        var swaggerUi = await client.GetAsync("/swagger/index.html");
        var swaggerResponse = await client.GetAsync("/swagger/v1/swagger.json");
        var swaggerBody = await swaggerResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NoContent, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swaggerUi.StatusCode);
        Assert.Contains("Swagger UI", await swaggerUi.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);

        using var swagger = JsonDocument.Parse(swaggerBody);
        var paths = swagger.RootElement.GetProperty("paths");
        Assert.Contains("/production", paths.EnumerateObject().Select(path => path.Name));
        Assert.Contains("/consumption", paths.EnumerateObject().Select(path => path.Name));
        Assert.Contains("/powermetrics", paths.EnumerateObject().Select(path => path.Name));
        Assert.Contains("/netpowerproduction", paths.EnumerateObject().Select(path => path.Name));

        foreach (var route in new[] { "/production", "/consumption", "/powermetrics", "/netpowerproduction" })
        {
            var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(-1.4, "-1", "alert", "1001")]
    [InlineData(0, "0", "warning", "1000")]
    [InlineData(250, "250", "warning", "750")]
    [InlineData(251, "251", "good", "749")]
    public async Task NetPowerDashboard_RendersRoundedMetricsAndStatus(
        double netPower,
        string displayedNetPower,
        string expectedStatus,
        string displayedConsumption)
    {
        using var factory = CreateFactory("Production", new StaticEnphaseService(CreateData(netPower)));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/netpowerproduction");

        Assert.Contains($"status-indicator status-{expectedStatus}", html);
        Assert.Contains($"{displayedNetPower} <span class=\"unit\">W</span>", html);
        Assert.Contains("1000 <span class=\"unit\">W</span>", html);
        Assert.Contains($"{displayedConsumption} <span class=\"unit\">W</span>", html);
        AssertTemplateResolved(html);
    }

    [Fact]
    public async Task PublishedTemplate_RendersFromPublishedOutputOutsideRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"enphase-publish-smoke-{Guid.NewGuid():N}");
        var publishDirectory = Path.Combine(temporaryDirectory, "publish");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var publish = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            publish.ArgumentList.Add("publish");
            publish.ArgumentList.Add(Path.Combine(repositoryRoot, "EnphaseLocal", "EnphaseLocal.csproj"));
            publish.ArgumentList.Add("-o");
            publish.ArgumentList.Add(publishDirectory);

            using var process = Process.Start(publish);
            Assert.NotNull(process);
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0,
                $"dotnet publish failed.{Environment.NewLine}{await standardOutput}{Environment.NewLine}{await standardError}");

            var publishedTemplate = Path.Combine(publishDirectory, "Views", "NetPowerProduction.html");
            Assert.True(File.Exists(publishedTemplate), "The dashboard template must be part of published output.");
            await File.AppendAllTextAsync(publishedTemplate, "\n<!-- published-output-smoke -->");

            using var factory = CreateFactory(
                "Production",
                new StaticEnphaseService(CreateData(netPower: 251)),
                publishDirectory);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost"),
                AllowAutoRedirect = false
            });
            var response = await client.GetAsync("/netpowerproduction");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("status-indicator status-good", html);
            Assert.Contains("published-output-smoke", html);
            Assert.Contains("251 <span class=\"unit\">W</span>", html);
            Assert.Contains("1000 <span class=\"unit\">W</span>", html);
            AssertTemplateResolved(html);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        IEnphaseService service,
        string? contentRoot = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseContentRoot(contentRoot ?? Path.Combine(FindRepositoryRoot(), "EnphaseLocal"));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEnphaseService>();
                services.AddSingleton(service);
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

    private static void AssertTemplateResolved(string html)
    {
        foreach (var placeholder in new[]
        {
            "{GetGradientColor(roundedNetPower)}",
            "{statusClass}",
            "{roundedNetPower}",
            "{GetPowerProductionGradient(currentProduction)}",
            "{currentProduction}",
            "{GetPowerConsumptionGradient(currentConsumption)}",
            "{currentConsumption}",
            "{version}"
        })
        {
            Assert.DoesNotContain(placeholder, html);
        }

        var version = typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "v0.1.3";
        Assert.Contains($"Version: {version}", html);
    }

    private static ProductionDataDto.Root CreateData(double netPower)
    {
        const double productionPower = 1000;
        var production = new[]
        {
            new ProductionDataDto.Production(
                "inverters", 1, 123, productionPower, 10000, "production",
                null, null, null, null, null, null, null, null, null, null, null, null, null, null)
        };
        var consumption = new[]
        {
            new ProductionDataDto.Consumption(
                "eim", 1, "total-consumption", 123, productionPower - netPower,
                5000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null)
        };
        return new ProductionDataDto.Root(production, consumption, Array.Empty<ProductionDataDto.Storage>());
    }

    private sealed class StaticEnphaseService(ProductionDataDto.Root data) : IEnphaseService
    {
        public Task<ProductionDataDto.Root> GetProductionDataAsync() => Task.FromResult(data);

        public Task<double> GetNetPowerProductionAsync() =>
            Task.FromResult(PowerMetricsDto.FromProductionData(data).NetPowerProduction);

        public double CalculateNetPowerProduction() => PowerMetricsDto.FromProductionData(data).NetPowerProduction;
    }
}
