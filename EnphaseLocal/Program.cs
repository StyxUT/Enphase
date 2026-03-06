using EnphaseLocal;
using EnphaseLocal.Services;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using System.Runtime.CompilerServices;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
// Load configuration from appsettings.json and environment variables
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>();

// Configure options for the Enphase settings
builder.Services.Configure<EnphaseOptions>(builder.Configuration.GetSection("Enphase"));

builder.Services.AddHttpLogging(logging =>
    {
        logging.LoggingFields = HttpLoggingFields.All;
        logging.RequestHeaders.Add("sec-ch-ua");
        logging.ResponseHeaders.Add("MyResponseHeader");
        logging.MediaTypeOptions.AddText("application/javascript");
        logging.RequestBodyLogLimit = 4096;
        logging.ResponseBodyLogLimit = 4096;
        logging.CombineLogs = true;
    });

// Define the retry policy
var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(3, retryAttempt)));

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient<IEnphaseService, EnphaseService>()
    .ConfigureHttpClient((serviceProvider, client) =>
    {
        var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<EnphaseOptions>>();
        var enphaseOptions = optionsMonitor.CurrentValue;
        client.BaseAddress = new Uri(enphaseOptions.BaseAddress);

        // React to changes in options
        optionsMonitor.OnChange(newOptions =>
        {
            client.BaseAddress = new Uri(newOptions.BaseAddress);
        });
    })
    .AddPolicyHandler(retryPolicy)
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        return handler;
    });

builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteOptions>(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = true;
    options.ConstraintMap["caseInsensitive"] = typeof(Microsoft.AspNetCore.Routing.Constraints.RegexRouteConstraint);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

// Only use HTTPS redirection outside production (e.g., for local dev)
if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}
app.UseHttpLogging();

// Helper method to get gradient color based on power value
string GetGradientColor(double powerValue)
{
    // Map power values to a color gradient from red to yellow to green
    if (powerValue < 0)
    {
        // Red for negative values (consumption)
        return "#dc3545";
    }
    else if (powerValue < 100)
    {
        // Yellow for low positive values (low production)
        return "#ffc107";
    }
    else
    {
        // Green for high positive values (high production)
        return "#28a745";
    }
}

// Helper method to get power production gradient (gray to green)
string GetPowerProductionGradient(double powerValue)
{
    // Return gray when production is 0, green when above 2000
    if (powerValue <= 0)
        return "#6c757d"; // Gray
    else if (powerValue >= 2000)
        return "#28a745"; // Green
    else
    {
        // Interpolate between gray and green
        double ratio = powerValue / 2000.0;
        int gray = (int)(108 - 108 * ratio); // 108 = 0x6c in decimal
        int green = (int)(167 * ratio); // 167 = 0xa7 in decimal
        return $"#{gray:x2}00{green:x2}";
    }
}

// Helper method to get power consumption gradient (gray to red)
string GetPowerConsumptionGradient(double powerValue)
{
    // Return gray when consumption is below 2000, red when above 4000
    if (powerValue <= 2000)
        return "#6c757d"; // Gray
    else if (powerValue >= 4000)
        return "#dc3545"; // Red
    else
    {
        // Interpolate between gray and red
        double ratio = (powerValue - 2000) / 2000.0;
        int gray = (int)(108 - 108 * ratio); // 108 = 0x6c in decimal
        int red = (int)(220 * ratio); // 220 = 0xdc in decimal
        return $"#{red:x2}{gray:x2}{gray:x2}";
    }
}

// Map endpoints (moved from Endpoint.cs)
app.MapGet("/netpowerproduction", async (IEnphaseService envoyClient, ILogger<Program> logger) =>
{
    logger.LogDebug("GET NetPowerProduction called");
    var netPowerProduction = await envoyClient.GetNetPowerProductionAsync();
    var roundedNetPower = Math.Round(netPowerProduction);
    string color = roundedNetPower > 250 ? "#28a745" : roundedNetPower >= 0 ? "#ffc107" : "#dc3545";
    
    // Get the actual production and consumption values for the new tiles
    var productionData = await envoyClient.GetProductionDataAsync();
    double currentProduction = productionData.Production.FirstOrDefault()?.WNow ?? 0;
    double currentConsumption = productionData.Consumption.FirstOrDefault()?.WNow ?? 0;
    
    // Read the HTML template
    var htmlTemplate = File.ReadAllText("./Views/NetPowerProduction.html");
    
    // Replace placeholders with actual values
    var html = htmlTemplate
        .Replace("{GetGradientColor(roundedNetPower)}", GetGradientColor(roundedNetPower))
        .Replace("{roundedNetPower}", roundedNetPower.ToString())
        .Replace("{GetPowerProductionGradient(currentProduction)}", GetPowerProductionGradient(currentProduction))
        .Replace("{currentProduction}", currentProduction.ToString("F0"))
        .Replace("{GetPowerConsumptionGradient(currentConsumption)}", GetPowerConsumptionGradient(currentConsumption))
        .Replace("{currentConsumption}", currentConsumption.ToString("F0"));
    
    return Results.Content(html, "text/html");
});

app.MapGet("/healthcheck", (IEnphaseService envoyClient, ILogger<Program> logger) =>
{
    logger.LogDebug("GET HealthCheck called");
    return Results.NoContent();
});

app.MapGet("/production", async (IEnphaseService envoyClient, ILogger<Program> logger) =>
{
    logger.LogDebug("GET Production called");
    var data = await envoyClient.GetProductionDataAsync();
    return Results.Ok(data.Production);
});

app.MapGet("/consumption", async (IEnphaseService envoyClient, ILogger<Program> logger) =>
{
    logger.LogDebug("GET Consumption called");
    var data = await envoyClient.GetProductionDataAsync();
    return Results.Ok(data.Consumption);
});

app.Logger.LogInformation("Enphase Local v0.1.2");

app.Run();
