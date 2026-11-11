using Moq;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class IRoutingConfigurationRepositoryTests
    {
        private readonly Mock<IRoutingConfigurationRepository> _mockRepo;

        public IRoutingConfigurationRepositoryTests()
        {
            _mockRepo = new Mock<IRoutingConfigurationRepository>();
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByIdAsync_ReturnsExpectedConfiguration()
        {
            // Arrange
            var expected = new RoutingConfiguration { Id = 1, RouteName = "RouteA", CustomerName = "CustomerX" };
            _mockRepo.Setup(r => r.GetRoutingConfigurationByIdAsync(1))
                .ReturnsAsync(expected);

            // Act
            var result = await _mockRepo.Object.GetRoutingConfigurationByIdAsync(1);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expected.Id, result.Id);
            Assert.AreEqual(expected.RouteName, result.RouteName);
            Assert.AreEqual(expected.CustomerName, result.CustomerName);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByRouteNameAsync_ReturnsExpectedConfiguration()
        {
            // Arrange
            var expected = new RoutingConfiguration { Id = 2, RouteName = "RouteB", CustomerName = "CustomerY" };
            _mockRepo.Setup(r => r.GetRoutingConfigurationByRouteNameAsync("RouteB"))
                .ReturnsAsync(expected);

            // Act
            var result = await _mockRepo.Object.GetRoutingConfigurationByRouteNameAsync("RouteB");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expected.RouteName, result.RouteName);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByCustomerNameAsync_ReturnsExpectedConfiguration()
        {
            // Arrange
            var expected = new RoutingConfiguration { Id = 3, RouteName = "RouteC", CustomerName = "CustomerZ" };
            _mockRepo.Setup(r => r.GetRoutingConfigurationByCustomerNameAsync("CustomerZ"))
                .ReturnsAsync(expected);

            // Act
            var result = await _mockRepo.Object.GetRoutingConfigurationByCustomerNameAsync("CustomerZ");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expected.CustomerName, result.CustomerName);
        }

        [TestMethod]
        public async Task AddRoutingConfigurationAsync_CallsRepository()
        {
            // Arrange
            var config = new RoutingConfiguration { Id = 4, RouteName = "RouteD", CustomerName = "CustomerW" };

            // Act
            await _mockRepo.Object.AddRoutingConfigurationAsync(config);

            // Assert
            _mockRepo.Verify(r => r.AddRoutingConfigurationAsync(config), Times.Once);
        }

        [TestMethod]
        public async Task UpdateRoutingConfigurationAsync_CallsRepository()
        {
            // Arrange
            var config = new RoutingConfiguration { Id = 5, RouteName = "RouteE", CustomerName = "CustomerV" };

            // Act
            await _mockRepo.Object.UpdateRoutingConfigurationAsync(config);

            // Assert
            _mockRepo.Verify(r => r.UpdateRoutingConfigurationAsync(config), Times.Once);
        }
    }
}