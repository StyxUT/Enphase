using EnphaseLocal.Services;

namespace EnphaseLocal;

public static class ProductionEndpoints
{
    public static void Configure(WebApplication app)
    {
        app.MapGet("/EnvoyData", async (EnphaseService envoyClient, ILogger<Program> logger) =>
        {
            logger.LogDebug("GET EnvoyData called");
            var productionData = await envoyClient.GetProductionDataAsync();

            return Results.Ok(productionData);
        });

        app.MapGet("/NetPowerProduction", async (EnphaseService envoyClient, ILogger<Program> logger) =>
        {
            logger.LogDebug("GET NetPowerProduction called");
            var netPowerProduction = await envoyClient.GetNetPowerProductionAsync();

            return Results.Ok(netPowerProduction);
        });

        app.MapGet("/HealthCheck", async (EnphaseService envoyClient, ILogger<Program> logger) =>
        {
            logger.LogDebug("GET HealthCheck called");

            return Results.NoContent();
        });
    }
}