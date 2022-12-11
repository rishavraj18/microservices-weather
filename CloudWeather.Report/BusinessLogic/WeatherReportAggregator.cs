﻿using CloudWeather.Report.Config;
using CloudWeather.Report.DataAccess;
using CloudWeather.Report.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CloudWeather.Report.BusinessLogic
{
    /// <summary>
    /// Aggregates data from multiple external sources to build a weather report
    /// </summary>
    public interface IWeatherReportAggregator
    {
        /// <summary>
        /// Builds and returns a Weather Report
        /// Persist weekly weather report data
        /// </summary> 
        /// <param name="zip"></param>
        /// <param name="days"></param>
        /// <returns></returns>

        public Task<WeatherReport> BuildReport(string zip, int days);

    }
    public class WeatherReportAggregator : IWeatherReportAggregator
    {
        private readonly IHttpClientFactory _http;
        private readonly ILogger<WeatherReportAggregator> _logger;
        private readonly WeatherDataConfig _weatherDataConfig;
        private readonly WeatherReportDbContext _db;

        public WeatherReportAggregator(
            IHttpClientFactory http,
            ILogger<WeatherReportAggregator> logger,
            IOptions<WeatherDataConfig> weatherDataConfig, // Options pattern to basically mapped our strongly typed data type Weather data config and inject into particular class
            WeatherReportDbContext db
        )
        {
            _http = http;
            _logger = logger;
            _weatherDataConfig = weatherDataConfig.Value;
            _db = db;
        }

        public async Task<WeatherReport> BuildReport(string zip, int days)
        {
            var httpClient = _http.CreateClient();
            
            // For Precip Data
            var precipData = await FetchPrecipitationData(httpClient, zip, days);
            var totalSnow = GetTotalSnow(precipData);
            var totalRain = GetTotalRain(precipData);

            // Logging
            _logger.LogInformation(
                $"zip: {zip} over last {days} days: "+
                $"total snow: {totalSnow}, rain: {totalRain}"
                );


            // For Temperature Data
            var tempData = await FetchTemperatureData(httpClient, zip, days);

            // Calculation of Temperature average high and average low
            var averageHighTemp = tempData.Average(t => t.TempHighF);
            var averageLowTemp = tempData.Average(t => t.TempLowF);

            // Logging
            _logger.LogInformation(
                $"zip: {zip} over last {days} days: " +
                $"low temp: {averageLowTemp}, high temp: {averageHighTemp}"
                );

            var weatherReport = new WeatherReport
            {
                AverageHighF = Math.Round(averageHighTemp, 1),
                AverageLowF = Math.Round(averageLowTemp, 1),
                RainFallTotalInches = totalRain,
                SnowTotalInches = totalSnow,
                ZipCode = zip,
                CreatedOn = DateTime.UtcNow
            };

            // TODO: Use 'cached' weather report instead of making round trips when possible
            _db.Add(weatherReport);
            await _db.SaveChangesAsync();

            return weatherReport;
        }

        // Calculation for Snow
        private static decimal GetTotalSnow(IEnumerable<PrecipitationModel> precipData)
        {
            var totalSnow = precipData
                            .Where(p => p.WeatherType == "snow")
                            .Sum(p => p.AmountInches);
            return Math.Round(totalSnow, 1);
        }
        // Calculation for Rain
        private static decimal GetTotalRain(IEnumerable<PrecipitationModel> precipData)
        {
            var totalRain = precipData
                            .Where(p => p.WeatherType == "rain")
                            .Sum(p => p.AmountInches);
            return Math.Round(totalRain, 1);
        }

        private async Task<List<TemperatureModel>> FetchTemperatureData(HttpClient httpClient, string zip, int days)
        {
            var endpoint = BuildTemperatureServiceEndpoint(zip, days);
            var temperatureRecords = await httpClient.GetAsync(endpoint);
            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var temperatureData = await temperatureRecords
                                        .Content
                                        .ReadFromJsonAsync<List<TemperatureModel>>(jsonSerializerOptions);

            return temperatureData ?? new List<TemperatureModel>();
        }

        private async Task<List<PrecipitationModel>> FetchPrecipitationData(HttpClient httpClient, string zip, int days)
        {
            var endpoint = BuildPrecipitationEndpoint(zip, days);
            var precipRecords = await httpClient.GetAsync(endpoint);
            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var precipData = await precipRecords
                                   .Content
                                   .ReadFromJsonAsync<List<PrecipitationModel>>(jsonSerializerOptions);

            return precipData ?? new List<PrecipitationModel>();
        }

        private string BuildTemperatureServiceEndpoint(string zip, int days)
        {
            var tempServiceProtocol = _weatherDataConfig.TempDataProtocol;
            var tempServiceHost = _weatherDataConfig.TempDataHost;
            var tempServicePort = _weatherDataConfig.TempDataPort;

            return $"{tempServiceProtocol}://{tempServiceHost}:{tempServicePort}/observation/{zip}?days={days}";
        }

        private string BuildPrecipitationEndpoint(string zip, int days)
        {
            var precipServiceProtocol = _weatherDataConfig.PrecipDataProtocol;
            var precipServiceHost = _weatherDataConfig.PrecipDataHost;
            var precipServicePort = _weatherDataConfig.PrecipDataPort;

            return $"{precipServiceProtocol}://{precipServiceHost}:{precipServicePort}/observation/{zip}?days={days}";
        }

    }
}
