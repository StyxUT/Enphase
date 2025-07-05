using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using EnphaseLocal.Models;
using Microsoft.Extensions.Logging;

namespace EnphaseLocal.Services;

public class EnphaseService : IEnphaseService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private ProductionDataDto.Root? _productionDataDto;
    private readonly ILogger<EnphaseService> _logger;

    public EnphaseService(HttpClient httpClient, IOptions<EnphaseOptions> options, ILogger<EnphaseService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        var bearerToken = options.Value.BearerToken;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<ProductionDataDto.Root> GetProductionDataAsync()
    {
        try
        {
            var url = "/production.json";
            _logger.LogInformation($"Requesting production data from {url}");
            var response = await _httpClient.GetAsync(url);
            _logger.LogInformation($"Received status code: {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            _productionDataDto = JsonSerializer.Deserialize<ProductionDataDto.Root>(responseContent, _jsonOptions);

            if (_productionDataDto == null)
            {
                _logger.LogError("Failed to deserialize production data.");
                throw new InvalidOperationException("Failed to deserialize production data.");
            }

            return _productionDataDto;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, $"Error fetching production data: {ex.Message}");
            Console.WriteLine($"Error fetching production data: {ex.Message}");
            throw;
        }
    }

    public async Task<double> GetNetPowerProductionAsync()
    {
        var productionData = await GetProductionDataAsync();

        var netPowerProduction = CalculateNetPowerProduction();

        return netPowerProduction;
    }

    public double CalculateNetPowerProduction()
    {
        if (_productionDataDto == null)
        {
            throw new InvalidOperationException("Production data is not available.");
        }

        // Use the 'eim' type for net production if available, otherwise fallback to first
        var productionEim = _productionDataDto.Production
            .FirstOrDefault(p => string.Equals(p.Type, "eim", StringComparison.OrdinalIgnoreCase));
        var productionWNow = productionEim?.WNow ?? _productionDataDto.Production.FirstOrDefault()?.WNow ?? 0;

        var consumptionWNow = _productionDataDto.Consumption.FirstOrDefault()?.WNow ?? 0;

        var netPowerProduction = productionWNow - consumptionWNow;

        return netPowerProduction;
    }
}

