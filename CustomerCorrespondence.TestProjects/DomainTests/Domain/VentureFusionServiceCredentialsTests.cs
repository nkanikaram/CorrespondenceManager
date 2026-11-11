using System;
using System.Text;
using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class VentureFusionServiceCredentialsTests
    {
        [TestMethod]
        public void GetEncodedAuthenticationString_ReturnsBase64EncodedClientIdAndSecret()
        {
            // Arrange
            var credentials = new VentureFusionServiceCredentials
            {
                ClientId = "test-client-id",
                ClientSecret = "test-secret"
            };
            var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-client-id:test-secret"));

            // Act
            var result = credentials.GetEncodedAuthenticationString();

            // Assert
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public void GetEncodedAuthenticationString_WithNullClientIdOrSecret_ReturnsBase64EncodedNulls()
        {
            // Arrange
            var credentials = new VentureFusionServiceCredentials
            {
                ClientId = null,
                ClientSecret = null
            };
            var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(":"));

            // Act
            var result = credentials.GetEncodedAuthenticationString();

            // Assert
            Assert.AreEqual(expected, result);
        }
    }
}