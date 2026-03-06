using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EnphaseLocal.Tests;

public sealed class NetPowerProductionEndpointTests : IClassFixture<EnphaseLocalApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NetPowerProductionEndpointTests(EnphaseLocalApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetNetPowerProduction_ReturnsHtmlFromTemplate()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await client.GetAsync("/netpowerproduction");

        response.EnsureSuccessStatusCode();

        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Equal("text/html", response.Content.Headers.ContentType.MediaType);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>Net Power Production</title>", html);
        Assert.DoesNotContain("{roundedNetPower}", html);
        Assert.DoesNotContain("{statusClass}", html);
        Assert.DoesNotContain("{Math.Round", html);
    }
}
