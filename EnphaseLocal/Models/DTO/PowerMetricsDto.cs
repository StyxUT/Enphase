namespace EnphaseLocal.Models.DTO;

public record PowerMetricsDto(
    double NetPowerProduction,
    double PowerProduction,
    double PowerConsumption
);
