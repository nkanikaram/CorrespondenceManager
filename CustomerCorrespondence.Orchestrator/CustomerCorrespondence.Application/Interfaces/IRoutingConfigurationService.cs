using CustomerCorrespondence.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Application.Interfaces
{
    /// <summary>
    /// Interface for a service that manages routing configurations for customer correspondence. This service provides methods to retrieve routing configurations based on various criteria (e.g., by ID, route name, customer name) and to add or update routing configurations. It allows the application to determine how to route customer correspondence based on the defined configurations, enabling dynamic and flexible routing logic that can be easily maintained and updated as needed.
    /// </summary>
    public interface IRoutingConfigurationService
    {
        /// <summary>
        /// Gets a routing configuration by its unique identifier.
        /// </summary>
        /// <param name="id">The unique identifier of the routing configuration.</param>
        /// <returns>The routing configuration if found; otherwise, null.</returns>
        Task<RoutingConfiguration> GetRoutingConfigurationByIdAsync(int id);

        /// <summary>
        /// Gets a routing configuration by its route name. This method allows you to retrieve the routing configuration associated with a specific route name, which can be used to determine how to route customer correspondence based on the defined route.
        /// </summary>
        /// <param name="routeName">The name of the route.</param>
        /// <returns>The routing configuration if found; otherwise, null.</returns>
        Task<RoutingConfiguration?> GetRoutingConfigurationByRouteNameAsync(string routeName);

        /// <summary>
        /// Gets a routing configuration by its customer name. This method allows you to retrieve the routing configuration associated with a specific customer name, which can be used to determine how to route customer correspondence based on the defined customer.
        /// </summary>
        /// <param name="customerName">The name of the customer.</param>
        /// <returns>The routing configuration if found; otherwise, null.</returns>
        Task<RoutingConfiguration?> GetRoutingConfigurationByCustomerNameAsync(string customerName);

        /// <summary>
        /// Adds a new routing configuration to the system.
        /// </summary>
        /// <param name="routingConfiguration">The routing configuration to add.</param>    
        Task AddRoutingConfigurationAsync(RoutingConfiguration routingConfiguration);

        /// <summary>
        /// Updates an existing routing configuration in the system. This method allows you to modify the details of an existing routing configuration, such as the route name, customer name, correspondence method, route data, or route list. It ensures that the routing configuration remains up-to-date and reflects any changes needed for routing customer correspondence effectively.
        /// </summary>
        /// <param name="routingConfiguration">The routing configuration to update.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task UpdateRoutingConfigurationAsync(RoutingConfiguration routingConfiguration);
    }
}
