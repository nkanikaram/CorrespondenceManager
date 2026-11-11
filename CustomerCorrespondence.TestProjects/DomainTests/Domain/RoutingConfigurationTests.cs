using System.Collections.Generic;
using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class RoutingConfigurationTests
    {
        [TestMethod]
        public void RoutingConfiguration_DefaultConstructor_InitializesProperties()
        {
            // Arrange & Act
            var config = new RoutingConfiguration();

            // Assert
            Assert.AreEqual(0, config.Id);
            Assert.IsNull(config.RouteName);
            Assert.IsNull(config.CustomerName);
            Assert.IsNull(config.CorrespondenceMethod);
            Assert.IsNotNull(config.Route);
            Assert.AreEqual(0, config.Route.Count);
        }

        [TestMethod]
        public void RoutingConfiguration_SetProperties_PropertiesAreSetCorrectly()
        {
            // Arrange
            var config = new RoutingConfiguration
            {
                Id = 1,
                RouteName = "RouteA",
                CustomerName = "CustomerX",
                CorrespondenceMethod = "Email",
                Route = new List<string> { "Step1", "Step2" }
            };

            // Assert
            Assert.AreEqual(1, config.Id);
            Assert.AreEqual("RouteA", config.RouteName);
            Assert.AreEqual("CustomerX", config.CustomerName);
            Assert.AreEqual("Email", config.CorrespondenceMethod);
            Assert.AreEqual(2, config.Route.Count);
            Assert.IsTrue(config.Route.Contains("Step1"));
            Assert.IsTrue(config.Route.Contains("Step2"));
        }
    }
}