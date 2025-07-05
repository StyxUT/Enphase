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

// Map endpoints (moved from Endpoint.cs)
app.MapGet("/netpowerproduction", async (IEnphaseService envoyClient, ILogger<Program> logger) =>
{
    logger.LogDebug("GET NetPowerProduction called");
    var netPowerProduction = await envoyClient.GetNetPowerProductionAsync();
    var roundedNetPower = Math.Round(netPowerProduction);
    string color = roundedNetPower > 250 ? "#28a745" : roundedNetPower >= 0 ? "#ffc107" : "#dc3545";
    var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Net Power Production</title>
    <meta http-equiv=""refresh"" content=""60"">
    <style>
        body {{ font-weight: bold; font-family: Arial, sans-serif; background: #f8f9fa; color: #222; margin: 2em; }}
        .container {{ background: #fff; border-radius: 8px; box-shadow: 0 2px 8px #0001; padding: 2em; max-width: 400px; margin: auto; }}
        h1 {{ font-size: 1.5em; margin-bottom: 1em; }}
        .label {{ font-weight: bold; }}
        .value {{ font-size: 2em; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Net Power Production</h1>
        <div><span class=""label"">Current Net Power Production:</span></div>
        <div class=""value"" style=""color: {color};"">{roundedNetPower} W</div>
    </div>
</body>
</html>";
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