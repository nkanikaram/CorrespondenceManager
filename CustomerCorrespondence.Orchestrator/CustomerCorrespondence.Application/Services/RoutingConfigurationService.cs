using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Application.Services
{
    public class RoutingConfigurationService : IRoutingConfigurationService
    {
        private readonly IRoutingConfigurationRepository _routingConfigurationRepository;
        private readonly ILogger<RoutingConfigurationService> _logger;

        public RoutingConfigurationService(IRoutingConfigurationRepository routingConfigurationRepository,
                                           ILogger<RoutingConfigurationService> logger)
        {
            _routingConfigurationRepository = routingConfigurationRepository;
            _logger = logger;
        }
        public async Task<RoutingConfiguration?> GetRoutingConfigurationByCustomerNameAsync(string customerName)
        {
            try
            {
                var routeConfig = await _routingConfigurationRepository.GetRoutingConfigurationByCustomerNameAsync(customerName);
                if (routeConfig == null) {
                    _logger.LogWarning("No routing configuration found for CustomerName: {CustomerName}", customerName?.Replace(Environment.NewLine, " ")?.Replace("\r", " ")?.Replace("\n", " ")?.Replace("{", "{{")?.Replace("}", "}}"));
                    return null;
                }
                routeConfig.Route = System.Text.Json.JsonSerializer.Deserialize<List<string>>(System.Text.Json.JsonSerializer.Serialize(routeConfig.Route)) ?? new List<string>();
                return routeConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving routing configuration for CustomerName: {CustomerName}", customerName?.Replace(Environment.NewLine, " ")?.Replace("\r", " ")?.Replace("\n", " ")?.Replace("{", "{{")?.Replace("}", "}}"));
                throw;
            }
        }
        public async Task<RoutingConfiguration> GetRoutingConfigurationByIdAsync(int id)
        {
            try
            {
                var routeConfig = await _routingConfigurationRepository.GetRoutingConfigurationByIdAsync(id);
                if (routeConfig == null)
                {
                    _logger.LogWarning("No routing configuration found for CustomerId: {Id}", id);
                    return null!;
                }
                routeConfig.Route = System.Text.Json.JsonSerializer.Deserialize<List<string>>(System.Text.Json.JsonSerializer.Serialize(routeConfig.Route)) ?? new List<string>();
                return routeConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving routing configuration for CustomerId: {Id}", id);
                throw;
            }
        }
        public async Task<RoutingConfiguration?> GetRoutingConfigurationByRouteNameAsync(string routeName)
        {
            try
            {
                var routeConfig = await _routingConfigurationRepository.GetRoutingConfigurationByRouteNameAsync(routeName);
                if (routeConfig == null)
                {
                    _logger.LogWarning("No routing configuration found for RouteName: {RouteName}", routeName);
                    return null;
                }
                routeConfig.Route = System.Text.Json.JsonSerializer.Deserialize<List<string>>(System.Text.Json.JsonSerializer.Serialize(routeConfig.Route)) ?? new List<string>();
                return routeConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving routing configuration for RouteName: {RouteName}", routeName);
                throw;
            }
        }
        public async Task AddRoutingConfigurationAsync(RoutingConfiguration routingConfiguration)
        {
            if (routingConfiguration == null)
                throw new ArgumentNullException(nameof(routingConfiguration));

            try
            {
                await _routingConfigurationRepository.AddRoutingConfigurationAsync(routingConfiguration);
                _logger.LogInformation("Added routing configuration for CustomerName: {CustomerName}, RouteName: {RouteName}",
                    routingConfiguration.CustomerName, routingConfiguration.RouteName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding routing configuration for CustomerName: {CustomerName}, RouteName: {RouteName}",
                    routingConfiguration.CustomerName, routingConfiguration.RouteName);
                throw;
            }
        }

        public async Task UpdateRoutingConfigurationAsync(RoutingConfiguration routingConfiguration)
        {
            if (routingConfiguration == null)
                throw new ArgumentNullException(nameof(routingConfiguration));

            try
            {
                await _routingConfigurationRepository.UpdateRoutingConfigurationAsync(routingConfiguration);
                _logger.LogInformation("Updated routing configuration for ID: {Id}, CustomerName: {CustomerName}, RouteName: {RouteName}",
                    routingConfiguration.Id, routingConfiguration.CustomerName, routingConfiguration.RouteName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating routing configuration for ID: {Id}, CustomerName: {CustomerName}, RouteName: {RouteName}",
                    routingConfiguration.Id, routingConfiguration.CustomerName, routingConfiguration.RouteName);
                throw;
            }
        }
    }
}
