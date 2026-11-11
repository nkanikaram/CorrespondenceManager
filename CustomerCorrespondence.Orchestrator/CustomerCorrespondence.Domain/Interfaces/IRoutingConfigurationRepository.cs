using CustomerCorrespondence.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Interfaces
{
    public interface IRoutingConfigurationRepository
    {
        Task<RoutingConfiguration?> GetRoutingConfigurationByIdAsync(int id);
        Task<RoutingConfiguration?> GetRoutingConfigurationByRouteNameAsync(string routeName);
        Task<RoutingConfiguration?> GetRoutingConfigurationByCustomerNameAsync(string customerName);
        Task AddRoutingConfigurationAsync(RoutingConfiguration routingConfiguration);
        Task UpdateRoutingConfigurationAsync(RoutingConfiguration routingConfiguration);
    }
}
