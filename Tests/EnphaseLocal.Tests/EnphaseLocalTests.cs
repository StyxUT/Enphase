using Castle.Components.DictionaryAdapter.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net.Http.Headers;
using EnphaseLocal.Services;
using EnphaseLocal;
using RichardSzalay.MockHttp;
using EnphaseLocal.Models;


namespace PowerProduction.Tests
{
    public class EnphaseLocalTests
    {
        private readonly EnphaseService _enphaseLocal;
        private readonly HttpClient _httpClient;
        private readonly IOptions<EnphaseOptions> _options;

        public EnphaseLocalTests()
        {
            _options = Options.Create(new EnphaseOptions { BearerToken = "asdf", BaseAddress = "http://localhost" });
            var mockHttp = new MockHttpMessageHandler();
            var fakeJson = @"{""production"": [{""type"": ""inverters"", ""activeCount"": 1, ""readingTime"": 123, ""wNow"": 1000.0, ""whLifetime"": 10000}], ""consumption"": [{""type"": ""eim"", ""activeCount"": 1, ""measurementType"": ""total-consumption"", ""readingTime"": 123, ""wNow"": 500.0, ""whLifetime"": 5000}], ""storage"": []}";
            mockHttp.When("http://localhost/production.json").Respond("application/json", fakeJson);
            _httpClient = new HttpClient(mockHttp) { BaseAddress = new Uri("http://localhost") };
            _enphaseLocal = new EnphaseService(_httpClient, _options);
        }

        [Fact]
        public async Task GetMeterDataAsync_ResultIsNotZero()
        {
            // Arrange
            _enphaseLocal.GetType().GetProperty("_client")?.SetValue(_enphaseLocal, _httpClient);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("Applicaiton/json"));

            // Act
            var result = await _enphaseLocal.GetNetPowerProductionAsync();

            // Assert
            Assert.True(result != 0, "Net power production should not be zero.");
        }

        [Fact]
        public async Task EnphaseLocal_GetNetPowerProduction_IsNotZero()
        {
            _enphaseLocal.GetType().GetProperty("_client")?.SetValue(_enphaseLocal, _httpClient);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("Applicaiton/json"));

            var netPowerProduction = await _enphaseLocal.GetNetPowerProductionAsync();

            Assert.True(netPowerProduction != 0, "Net power production should not be zero.");
        }

        [Fact]
        public async Task GetProductionDataAsync_ThrowsOnInvalidJson()
        {
            var mockHttp = new MockHttpMessageHandler();
            var invalidJson = @"{ invalid json }";
            mockHttp.When("http://localhost/production.json").Respond("application/json", invalidJson);
            var httpClient = new HttpClient(mockHttp) { BaseAddress = new Uri("http://localhost") };
            var service = new EnphaseService(httpClient, _options);
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => service.GetProductionDataAsync());
        }

        [Fact]
        public async Task GetProductionDataAsync_ThrowsOnHttpError()
        {
            var mockHttp = new MockHttpMessageHandler();
            mockHttp.When("http://localhost/production.json").Respond(System.Net.HttpStatusCode.InternalServerError);
            var httpClient = new HttpClient(mockHttp) { BaseAddress = new Uri("http://localhost") };
            var service = new EnphaseService(httpClient, _options);
            await Assert.ThrowsAsync<HttpRequestException>(() => service.GetProductionDataAsync());
        }

        [Fact]
        public void CalculateNetPowerProduction_ThrowsIfNoData()
        {
            Assert.Throws<InvalidOperationException>(() => _enphaseLocal.CalculateNetPowerProduction());
        }

        [Fact]
        public async Task GetNetPowerProductionAsync_ReturnsExpectedValue()
        {
            var result = await _enphaseLocal.GetNetPowerProductionAsync();
            Assert.Equal(500.0, result);
        }

        [Fact]
        public void CalculateNetPowerProduction_CorrectlyCalculatesNetPower()
        {
            // Arrange
            var production = new List<ProductionDataDto.Production>
            {
                new ProductionDataDto.Production(
                    Type: "inverters",
                    ActiveCount: 1,
                    ReadingTime: 123,
                    WNow: 1500.0,
                    WhLifetime: 10000,
                    MeasurementType: "production",
                    VarhLeadLifetime: null,
                    VarhLagLifetime: null,
                    VahLifetime: null,
                    RmsCurrent: null,
                    RmsVoltage: null,
                    ReactPwr: null,
                    ApprntPwr: null,
                    PwrFactor: null,
                    WhToday: null,
                    WhLastSevenDays: null,
                    VahToday: null,
                    VarhLeadToday: null,
                    VarhLagToday: null,
                    Lines: new List<ProductionDataDto.Line>()
                )
            };
            var consumption = new List<ProductionDataDto.Consumption>
            {
                new ProductionDataDto.Consumption(
                    Type: "eim",
                    ActiveCount: 1,
                    MeasurementType: "total-consumption",
                    ReadingTime: 123,
                    WNow: 400.0,
                    WhLifetime: 5000,
                    VarhLeadLifetime: 0,
                    VarhLagLifetime: 0,
                    VahLifetime: 0,
                    RmsCurrent: 0,
                    RmsVoltage: 0,
                    ReactPwr: 0,
                    ApprntPwr: 0,
                    PwrFactor: 0,
                    WhToday: 0,
                    WhLastSevenDays: 0,
                    VahToday: 0,
                    VarhLeadToday: 0,
                    VarhLagToday: 0,
                    Lines: new List<ProductionDataDto.Line>()
                )
            };
            var storage = new List<ProductionDataDto.Storage>();
            var root = new ProductionDataDto.Root(production, consumption, storage);

            var mockHttp = new MockHttpMessageHandler();
            var options = Options.Create(new EnphaseOptions { BearerToken = "asdf", BaseAddress = "http://localhost" });
            var httpClient = new HttpClient(mockHttp) { BaseAddress = new Uri("http://localhost") };
            var service = new EnphaseService(httpClient, options);

            // Use reflection to set the private _productionDataDto field
            var field = typeof(EnphaseService).GetField("_productionDataDto", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null)
                throw new Exception("Could not find _productionDataDto field via reflection");
            field.SetValue(service, root);

            // Act
            var netPower = service.CalculateNetPowerProduction();

            // Assert
            Assert.Equal(1100.0, netPower);
        }
    }
}