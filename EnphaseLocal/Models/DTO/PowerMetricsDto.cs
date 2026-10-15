namespace EnphaseLocal.Models.DTO;

// Household watts: production and consumption are nonnegative; net is export-positive.
public record PowerMetricsDto(
    double NetPowerProduction,
    double PowerProduction,
    double PowerConsumption
)
{
    public static PowerMetricsDto FromProductionData(ProductionDataDto.Root data)
    {
        var productionMeter = data.Production.FirstOrDefault(p =>
            string.Equals(p.Type, "eim", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.MeasurementType, "production", StringComparison.OrdinalIgnoreCase))
            ?? data.Production.FirstOrDefault(p =>
                string.Equals(p.Type, "inverters", StringComparison.OrdinalIgnoreCase));
        if (productionMeter == null)
            throw new InvalidOperationException("An identified production measurement is required.");

        var production = productionMeter.WNow;
        var gridMeter = data.Consumption.FirstOrDefault(c =>
            string.Equals(c.Type, "eim", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.MeasurementType, "net-consumption", StringComparison.OrdinalIgnoreCase));

        double consumption;
        if (gridMeter != null)
        {
            // Envoy net-consumption is positive for Import, negative for Export.
            // Do not subtract this from production or take its absolute value.
            consumption = production + gridMeter.WNow;
        }
        else
        {
            var totalMeter = data.Consumption.FirstOrDefault(c =>
                string.Equals(c.Type, "eim", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.MeasurementType, "total-consumption", StringComparison.OrdinalIgnoreCase));
            if (totalMeter == null)
                throw new InvalidOperationException("An identified consumption measurement is required.");
            consumption = totalMeter.WNow;
        }

        if (!double.IsFinite(production) || !double.IsFinite(consumption) || production < 0 || consumption < 0)
            throw new InvalidOperationException("Household production and consumption must be finite, nonnegative watts.");

        production = Math.Round(production, 2);
        consumption = Math.Round(consumption, 2);
        return new PowerMetricsDto(Math.Round(production - consumption, 2), production, consumption);
    }
}
