using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Infrastructure.Persistence;
using CustomerCorrespondence.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Xunit;

namespace CustomerCorrespondenceTests.Infrastructure
{
    [TestClass]
    public class RoutingConfigurationRepositoryTests
    {
        private CorrespondenceDBContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<CorrespondenceDBContext>()
                .UseInMemoryDatabase(databaseName: "TestDb_" + System.Guid.NewGuid())
                .Options;
            return new CorrespondenceDBContext(options);
        }

        [TestMethod]
        public async Task AddRoutingConfigurationAsync_AddsEntity()
        {
            // Arrange
            using var dbContext = GetInMemoryDbContext();
            var repo = new RoutingConfigurationRepository(dbContext);
            var config = new RoutingConfiguration
            {
                CustomerName = "TestCustomer",
                RouteName = "TestRoute",
                CorrespondenceMethod = "Email",
                Route = new() { "Step1", "Step2" }
            };

            // Act
            await repo.AddRoutingConfigurationAsync(config);
            var result = await dbContext.RoutingConfigurations.FirstOrDefaultAsync(rc => rc.CustomerName == "TestCustomer");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("TestRoute", result.RouteName);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByCustomerNameAsync_ReturnsCorrectEntity()
        {
            // Arrange
            using var dbContext = GetInMemoryDbContext();
            dbContext.RoutingConfigurations.Add(new RoutingConfiguration
            {
                CustomerName = "CustomerA",
                RouteName = "RouteA",
                CorrespondenceMethod = "SMS",
                Route = new() { "A", "B" }
            });
            await dbContext.SaveChangesAsync();
            var repo = new RoutingConfigurationRepository(dbContext);

            // Act
            var result = await repo.GetRoutingConfigurationByCustomerNameAsync("CustomerA");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("RouteA", result.RouteName);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByIdAsync_ReturnsCorrectEntity()
        {
            // Arrange
            using var dbContext = GetInMemoryDbContext();
            var config = new RoutingConfiguration
            {
                CustomerName = "CustomerB",
                RouteName = "RouteB",
                CorrespondenceMethod = "Mail",
                Route = new() { "X", "Y" }
            };
            dbContext.RoutingConfigurations.Add(config);
            await dbContext.SaveChangesAsync();
            var repo = new RoutingConfigurationRepository(dbContext);

            // Act
            var result = await repo.GetRoutingConfigurationByIdAsync(config.Id);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("CustomerB", result.CustomerName);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByRouteNameAsync_ReturnsCorrectEntity()
        {
            // Arrange
            using var dbContext = GetInMemoryDbContext();
            dbContext.RoutingConfigurations.Add(new RoutingConfiguration
            {
                CustomerName = "CustomerC",
                RouteName = "RouteC",
                CorrespondenceMethod = "Fax",
                Route = new() { "1", "2" }
            });
            await dbContext.SaveChangesAsync();
            var repo = new RoutingConfigurationRepository(dbContext);

            // Act
            var result = await repo.GetRoutingConfigurationByRouteNameAsync("RouteC");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("CustomerC", result.CustomerName);
        }

        [TestMethod]
        public async Task UpdateRoutingConfigurationAsync_UpdatesEntity()
        {
            // Arrange
            using var dbContext = GetInMemoryDbContext();
            var config = new RoutingConfiguration
            {
                CustomerName = "CustomerD",
                RouteName = "RouteD",
                CorrespondenceMethod = "Courier",
                Route = new() { "Start" }
            };
            dbContext.RoutingConfigurations.Add(config);
            await dbContext.SaveChangesAsync();
            var repo = new RoutingConfigurationRepository(dbContext);

            // Act
            config.CorrespondenceMethod = "UpdatedMethod";
            await repo.UpdateRoutingConfigurationAsync(config);
            var updated = await dbContext.RoutingConfigurations.FindAsync(config.Id);

            // Assert
            Assert.IsNotNull(updated);
            Assert.AreEqual("UpdatedMethod", updated.CorrespondenceMethod);
        }
    }
}