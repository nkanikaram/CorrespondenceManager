using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Application.Services;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;


namespace CustomerCorrespondenceTests.Application
{
    [TestClass]
    public class RoutingConfigurationServiceTests
    {
        private readonly Mock<IRoutingConfigurationRepository> _repositoryMock;
        private readonly Mock<ILogger<RoutingConfigurationService>> _loggerMock;
        private readonly RoutingConfigurationService _service;

        public RoutingConfigurationServiceTests()
        {
            _repositoryMock = new Mock<IRoutingConfigurationRepository>();
            _loggerMock = new Mock<ILogger<RoutingConfigurationService>>();
            _service = new RoutingConfigurationService(_repositoryMock.Object, _loggerMock.Object);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByCustomerNameAsync_ReturnsConfig_WhenFound()
        {
            var config = new RoutingConfiguration
            {
                Id = 1,
                CustomerName = "TestCustomer",
                RouteName = "TestRoute",
                Route = new List<string> { "A", "B" }
            };
            _repositoryMock.Setup(r => r.GetRoutingConfigurationByCustomerNameAsync("TestCustomer"))
                .ReturnsAsync(config);

            var result = await _service.GetRoutingConfigurationByCustomerNameAsync("TestCustomer");

            Assert.IsNotNull(result);
            Assert.AreEqual("TestCustomer", result.CustomerName);
            Assert.AreEqual(2, result.Route.Count);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByCustomerNameAsync_ReturnsNull_WhenNotFound()
        {
            _repositoryMock.Setup(r => r.GetRoutingConfigurationByCustomerNameAsync("Unknown"))
                .ReturnsAsync((RoutingConfiguration?)null);

            var result = await _service.GetRoutingConfigurationByCustomerNameAsync("Unknown");

            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByIdAsync_ReturnsConfig_WhenFound()
        {
            var config = new RoutingConfiguration
            {
                Id = 2,
                CustomerName = "Customer2",
                RouteName = "Route2",
                Route = new List<string> { "X", "Y" }
            };
            _repositoryMock.Setup(r => r.GetRoutingConfigurationByIdAsync(2))
                .ReturnsAsync(config);

            var result = await _service.GetRoutingConfigurationByIdAsync(2);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Id);
            Assert.AreEqual(2, result.Route.Count);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByIdAsync_ReturnsNull_WhenNotFound()
        {
            _repositoryMock.Setup(r => r.GetRoutingConfigurationByIdAsync(99))
                .ReturnsAsync((RoutingConfiguration?)null);

            var result = await _service.GetRoutingConfigurationByIdAsync(99);

            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByRouteNameAsync_ReturnsConfig_WhenFound()
        {
            var config = new RoutingConfiguration
            {
                Id = 3,
                CustomerName = "Customer3",
                RouteName = "Route3",
                Route = new List<string> { "M", "N" }
            };
            _repositoryMock.Setup(r => r.GetRoutingConfigurationByRouteNameAsync("Route3"))
                .ReturnsAsync(config);

            var result = await _service.GetRoutingConfigurationByRouteNameAsync("Route3");

            Assert.IsNotNull(result);
            Assert.AreEqual("Route3", result.RouteName);
            Assert.AreEqual(2, result.Route.Count);
        }

        [TestMethod]
        public async Task GetRoutingConfigurationByRouteNameAsync_ReturnsNull_WhenNotFound()
        {
            _repositoryMock.Setup(r => r.GetRoutingConfigurationByRouteNameAsync("UnknownRoute"))
                .ReturnsAsync((RoutingConfiguration?)null);

            var result = await _service.GetRoutingConfigurationByRouteNameAsync("UnknownRoute");

            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task AddRoutingConfigurationAsync_CallsRepository()
        {
            var config = new RoutingConfiguration
            {
                Id = 4,
                CustomerName = "Customer4",
                RouteName = "Route4",
                Route = new List<string> { "P", "Q" }
            };

            await _service.AddRoutingConfigurationAsync(config);

            _repositoryMock.Verify(r => r.AddRoutingConfigurationAsync(config), Times.Once);
        }

        [TestMethod]
        public async Task AddRoutingConfigurationAsync_ThrowsArgumentNullException_WhenNull()
        {
            await Task.Run(async () =>
            {
                try
                {
                    await _service.AddRoutingConfigurationAsync(null!);
                    Assert.Fail("Expected ArgumentNullException was not thrown.");
                }
                catch (ArgumentNullException)
                {
                    // Expected exception
                }
            });
        }

        [TestMethod]
        public async Task UpdateRoutingConfigurationAsync_CallsRepository()
        {
            var config = new RoutingConfiguration
            {
                Id = 5,
                CustomerName = "Customer5",
                RouteName = "Route5",
                Route = new List<string> { "R", "S" }
            };

            await _service.UpdateRoutingConfigurationAsync(config);

            _repositoryMock.Verify(r => r.UpdateRoutingConfigurationAsync(config), Times.Once);
        }

        [TestMethod]
        public async Task UpdateRoutingConfigurationAsync_ThrowsArgumentNullException_WhenNull()
        {
            await Task.Run(async () =>
            {
                try
                {
                    await _service.UpdateRoutingConfigurationAsync(null!);
                    Assert.Fail("Expected ArgumentNullException was not thrown.");
                }
                catch (ArgumentNullException)
                {
                    // Expected exception
                }
            });
        }
    }
}