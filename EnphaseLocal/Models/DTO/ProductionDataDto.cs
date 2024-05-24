namespace EnphaseLocal.Models
{
    public class ProductionDataDto
    {
        public record Root(
            IReadOnlyList<Production> Production,
            IReadOnlyList<Consumption> Consumption,
            IReadOnlyList<Storage> Storage
        );

        public record Consumption(
            string Type,
            int ActiveCount,
            string MeasurementType,
            int ReadingTime,
            double WNow,
            double WhLifetime,
            double VarhLeadLifetime,
            double VarhLagLifetime,
            double VahLifetime,
            double RmsCurrent,
            double RmsVoltage,
            double ReactPwr,
            double ApprntPwr,
            double PwrFactor,
            double WhToday,
            double WhLastSevenDays,
            double VahToday,
            double VarhLeadToday,
            double VarhLagToday,
            IReadOnlyList<Line> Lines
        );

        public record Line(
            double WNow,
            double WhLifetime,
            double VarhLeadLifetime,
            double VarhLagLifetime,
            double VahLifetime,
            double RmsCurrent,
            double RmsVoltage,
            double ReactPwr,
            double ApprntPwr,
            double PwrFactor,
            double WhToday,
            double WhLastSevenDays,
            double VahToday,
            double VarhLeadToday,
            double VarhLagToday
        );

        public record Production(
            string Type,
            int ActiveCount,
            int ReadingTime,
            double WNow,
            double WhLifetime,
            string MeasurementType,
            double? VarhLeadLifetime,
            double? VarhLagLifetime,
            double? VahLifetime,
            double? RmsCurrent,
            double? RmsVoltage,
            double? ReactPwr,
            double? ApprntPwr,
            double? PwrFactor,
            double? WhToday,
            double? WhLastSevenDays,
            double? VahToday,
            double? VarhLeadToday,
            double? VarhLagToday,
            IReadOnlyList<Line> Lines
        );

        public record Storage(
            string Type,
            int ActiveCount,
            int ReadingTime,
            int WNow,
            int WhNow,
            string State
        );




    }
}
