using System;
using System.Text;
using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class VentureFusionTokenOptionsTests
    {
        [TestMethod]
        public void Constructor_SetsPropertiesFromEnvironmentVariables()
        {
            // Arrange
            var tenantId = "test-tenant";
            var clientId = "test-client";
            var clientSecret = "test-secret";
            var apimKey = "test-apim-key";

            Environment.SetEnvironmentVariable("VentureFusionService:TenantId", tenantId);
            Environment.SetEnvironmentVariable("VentureFusionService:ClientId", clientId);
            Environment.SetEnvironmentVariable("VentureFusionService:ClientSecret", clientSecret);
            Environment.SetEnvironmentVariable("VentureFusionService:ApimSubscriptionKey", apimKey);

            // Act
            var options = new VentureFusionTokenOptions();

            // Assert
            Assert.AreEqual("client_credentials", options.grant_type);
            Assert.AreEqual("https://venturestageb2c.onmicrosoft.com/4422d0f6-7702-40d2-9883-eb308ebd7693/.default", options.scope);
            Assert.AreEqual(apimKey, options.apimSubscriptionKey);
            Assert.AreEqual($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token", options.tokenEndpoint);

            var expectedAuthString = $"{clientId}:{clientSecret}";
            var expectedEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(expectedAuthString));
            Assert.AreEqual(expectedEncoded, options.encodedAuthenticationString);
        }
    }
}