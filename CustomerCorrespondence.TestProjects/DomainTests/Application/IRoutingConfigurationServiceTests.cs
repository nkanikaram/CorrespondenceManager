
using Moq;
using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;

namespace CustomerCorrespondenceTests.Application
{
    [TestClass]
    public class IRoutingConfigurationServiceTests
    {
        private readonly Mock<IRoutingConfigurationService> _mockService;

        public IRoutingConfigurationServiceTests()
        {
            _mockService = new Mock<IRoutingConfigurationService>();
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByIdAsync_ReturnsRoutingConfiguration()
        {
            // Arrange
            var expected = new RoutingConfiguration { Id = 1, RouteName = "RouteA", CustomerName = "CustomerA", CorrespondenceMethod = "Email", Route = new() { "Step1", "Step2" } };
            _mockService.Setup(s => s.GetRoutingConfigurationByIdAsync(1)).ReturnsAsync(expected);

            // Act
            var result = await _mockService.Object.GetRoutingConfigurationByIdAsync(1);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expected.Id, result.Id);
            Assert.AreEqual(expected.RouteName, result.RouteName);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByRouteNameAsync_ReturnsRoutingConfigurationOrNull()
        {
            // Arrange
            var expected = new RoutingConfiguration { Id = 2, RouteName = "RouteB", CustomerName = "CustomerB", CorrespondenceMethod = "Mail", Route = new() { "StepA" } };
            _mockService.Setup(s => s.GetRoutingConfigurationByRouteNameAsync("RouteB")).ReturnsAsync(expected);
            _mockService.Setup(s => s.GetRoutingConfigurationByRouteNameAsync("Unknown")).ReturnsAsync((RoutingConfiguration?)null);

            // Act
            var found = await _mockService.Object.GetRoutingConfigurationByRouteNameAsync("RouteB");
            var notFound = await _mockService.Object.GetRoutingConfigurationByRouteNameAsync("Unknown");

            // Assert
            Assert.IsNotNull(found);
            Assert.AreEqual("RouteB", found?.RouteName);
            Assert.IsNull(notFound);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByCustomerNameAsync_ReturnsRoutingConfigurationOrNull()
        {
            // Arrange
            var expected = new RoutingConfiguration { Id = 3, RouteName = "RouteC", CustomerName = "CustomerC", CorrespondenceMethod = "SMS", Route = new() { "StepX" } };
            _mockService.Setup(s => s.GetRoutingConfigurationByCustomerNameAsync("CustomerC")).ReturnsAsync(expected);
            _mockService.Setup(s => s.GetRoutingConfigurationByCustomerNameAsync("Unknown")).ReturnsAsync((RoutingConfiguration?)null);

            // Act
            var found = await _mockService.Object.GetRoutingConfigurationByCustomerNameAsync("CustomerC");
            var notFound = await _mockService.Object.GetRoutingConfigurationByCustomerNameAsync("Unknown");

            // Assert
            Assert.IsNotNull(found);
            Assert.AreEqual("CustomerC", found?.CustomerName);
            Assert.IsNull(notFound);
        }

        [TestMethod]
        public async Task AddRoutingConfigurationAsync_CallsService()
        {
            // Arrange
            var config = new RoutingConfiguration { Id = 4, RouteName = "RouteD", CustomerName = "CustomerD", CorrespondenceMethod = "Fax", Route = new() { "StepY" } };

            // Act
            await _mockService.Object.AddRoutingConfigurationAsync(config);

            // Assert
            _mockService.Verify(s => s.AddRoutingConfigurationAsync(config), Times.Once);
        }

        [TestMethod]
        public async Task UpdateRoutingConfigurationAsync_CallsService()
        {
            // Arrange
            var config = new RoutingConfiguration { Id = 5, RouteName = "RouteE", CustomerName = "CustomerE", CorrespondenceMethod = "Courier", Route = new() { "StepZ" } };

            // Act
            await _mockService.Object.UpdateRoutingConfigurationAsync(config);

            // Assert
            _mockService.Verify(s => s.UpdateRoutingConfigurationAsync(config), Times.Once);
        }
    }
}