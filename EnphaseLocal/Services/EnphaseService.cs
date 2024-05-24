using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using EnphaseLocal.Models;

namespace EnphaseLocal.Services;

public class EnphaseService : IEnphaseService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private ProductionDataDto.Root _productionDataDto;

    public EnphaseService(HttpClient httpClient, IOptions<EnphaseOptions> options)
    {
        _httpClient = httpClient;
        var bearerToken = options.Value.BearerToken;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    internal async Task<ProductionDataDto.Root> GetProductionDataAsync()
    {
        var response = await _httpClient.GetAsync("/production.json");
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        _productionDataDto = JsonSerializer.Deserialize<ProductionDataDto.Root>(responseContent, _jsonOptions);

        return _productionDataDto;
    }

    internal async Task<double> GetNetPowerProductionAsync()
    {
        var productionData = await GetProductionDataAsync();

        var netPowerProduction = CalculateNetPowerProduction();

        return netPowerProduction;
    }

    internal double CalculateNetPowerProduction()
    {
        var netPowerProduction = _productionDataDto.Production.FirstOrDefault().WNow - _productionDataDto.Consumption.FirstOrDefault().WNow;

        return netPowerProduction;
    }
}

