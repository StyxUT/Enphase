using EnphaseLocal.Models;

namespace EnphaseLocal.Services;

public interface IEnphaseService
{
    Task<ProductionDataDto.Root> GetProductionDataAsync();
    Task<double> GetNetPowerProductionAsync();
    double CalculateNetPowerProduction();
}

