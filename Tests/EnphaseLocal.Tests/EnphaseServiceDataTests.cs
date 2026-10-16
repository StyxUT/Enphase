using System.Text.Json;
using EnphaseLocal.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace EnphaseLocal.Tests;

/// <summary>
/// Service-level data-integrity contract: EnphaseService surfaces
/// well-formed-but-invalid upstream payloads (JSON null, empty body,
/// malformed body) as InvalidOperationException with a stable message, and
/// wraps the JsonException so callers can distinguish it from transport
/// failures.
/// </summary>
public sealed class EnphaseServiceDataTests
{
    [Fact]
    public async Task NullBody_ThrowsDeserializationFailure()
    {
        var service = CreateService("null");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetProductionDataAsync());

        Assert.Equal("Failed to deserialize production data.", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task EmptyBody_ThrowsWrappedJsonException()
    {
        var service = CreateService(string.Empty);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetProductionDataAsync());

        Assert.Equal("Envoy returned malformed production data.", ex.Message);
        Assert.IsType<JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task MalformedBody_ThrowsWrappedJsonException()
    {
        var service = CreateService("{ invalid");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetProductionDataAsync());

        Assert.Equal("Envoy returned malformed production data.", ex.Message);
        Assert.IsType<JsonException>(ex.InnerException);
    }

    private static EnphaseService CreateService(string payload)
    {
        var handler = new MockHttpMessageHandler();
        handler.When("http://localhost/production.json")
            .Respond("application/json", payload);

        return new EnphaseService(
            new HttpClient(handler),
            Options.Create(new EnphaseOptions
            {
                BaseAddress = "http://localhost",
                BearerToken = "test"
            }),
            NullLogger<EnphaseService>.Instance);
    }
}
