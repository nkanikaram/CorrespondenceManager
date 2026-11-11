using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Infrastructure.Repositories
{
    public class RoutingConfigurationRepository(CorrespondenceDBContext dbContext) : IRoutingConfigurationRepository
    {
        public async Task AddRoutingConfigurationAsync(RoutingConfiguration routingConfiguration)
        {
            try
            {
                await dbContext.RoutingConfigurations.AddAsync(routingConfiguration);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception)
            {
                Console.Error.WriteLine("An error occurred while adding RoutingConfiguration.");
                throw;
            }
        }

        public async Task<RoutingConfiguration?> GetRoutingConfigurationByCustomerNameAsync(string customerName)
        {
            try
            {
                return await dbContext.RoutingConfigurations
                    .FirstOrDefaultAsync(rc => rc.CustomerName != null && rc.CustomerName.Equals(customerName));
            }
            catch (Exception)
            {
                var safeCustomerName = System.Net.WebUtility.HtmlEncode(customerName);
                Console.Error.WriteLine("Error fetching RoutingConfiguration by CustomerName: " + safeCustomerName);
                throw;
            }
        }

        public async Task<RoutingConfiguration?> GetRoutingConfigurationByIdAsync(int id)
        {
            try
            {
                return await dbContext.RoutingConfigurations
                    .FirstOrDefaultAsync(rc => rc.Id == id);
            }
            catch (Exception)
            {
                Console.Error.WriteLine("Error fetching RoutingConfiguration by Id " + id);
                throw;
            }
        }

        public async Task<RoutingConfiguration?> GetRoutingConfigurationByRouteNameAsync(string routeName)
        {
            try
            {
                return await dbContext.RoutingConfigurations
                    .FirstOrDefaultAsync(rc => rc.RouteName != null && rc.RouteName.Equals(routeName));
            }
            catch (Exception)
            {
                Console.Error.WriteLine("Error fetching RoutingConfiguration by RouteName " + System.Net.WebUtility.HtmlEncode(routeName));
                throw;
            }
        }

        public async Task UpdateRoutingConfigurationAsync(RoutingConfiguration routingConfiguration)
        {
            try
            {
                dbContext.RoutingConfigurations.Update(routingConfiguration);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception)
            {
                Console.Error.WriteLine("Error updating RoutingConfiguration for Id " + routingConfiguration.Id);
                throw;
            }
        }
    }
}
