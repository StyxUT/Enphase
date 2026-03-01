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
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, 'Open Sans', 'Helvetica Neue', sans-serif;
            background: linear-gradient(135deg, #f5f7fa 0%, #e4edf5 100%);
            color: #333;
            min-height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
            padding: 1rem;
        }}
        .container {{
            background: white;
            border-radius: 16px;
            box-shadow: 0 10px 25px rgba(0, 0, 0, 0.1);
            padding: 2.5rem;
            max-width: 450px;
            width: 100%;
            text-align: center;
            transition: transform 0.3s ease;
        }}
        .container:hover {{
            transform: translateY(-5px);
        }}
        h1 {{
            font-size: 1.8rem;
            margin-bottom: 1.5rem;
            font-weight: 600;
            color: #2c3e50;
            position: relative;
            padding-bottom: 0.5rem;
        }}
        h1:after {{
            content: '';
            position: absolute;
            bottom: 0;
            left: 50%;
            transform: translateX(-50%);
            width: 60px;
            height: 3px;
            background: linear-gradient(90deg, #3498db, #2ecc71);
            border-radius: 3px;
        }}
        .label {{
            font-size: 1.1rem;
            font-weight: 500;
            color: #7f8c8d;
            margin-bottom: 0.5rem;
        }}
        .value {{
            font-size: 3rem;
            font-weight: 700;
            margin: 1rem 0;
            background: linear-gradient(90deg, #3498db, #2ecc71);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            background-clip: text;
        }}
        .unit {{
            font-size: 1.2rem;
            color: #95a5a6;
            font-weight: 400;
        }}
        .status-indicator {{
            display: inline-block;
            width: 12px;
            height: 12px;
            border-radius: 50%;
            margin-right: 8px;
            vertical-align: middle;
        }}
        .status-good {{
            background-color: #28a745;
        }}
        .status-warning {{
            background-color: #ffc107;
        }}
        .status-alert {{
            background-color: #dc3545;
        }}
        .footer {{
            margin-top: 2rem;
            font-size: 0.9rem;
            color: #95a5a6;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Net Power Production</h1>
        <div class=""label"">
            <span class=""status-indicator status-{(roundedNetPower > 250 ? "good" : roundedNetPower >= 0 ? "warning" : "alert")}""></span>
            Current Net Power Production:
        </div>
        <div class=""value"">{roundedNetPower} <span class=""unit"">W</span></div>
        <div class=""footer"">
            Data updates every 60 seconds
        </div>
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
