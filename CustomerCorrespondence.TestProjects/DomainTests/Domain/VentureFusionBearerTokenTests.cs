using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class VentureFusionBearerTokenTests
    {
        [TestMethod]
        public void Can_Set_And_Get_Properties()
        {
            // Arrange
            var token = new VentureFusionBearerToken();

            // Act
            token.token_type = "Bearer";
            token.expires_in = 3600;
            token.ext_expires_in = 7200;
            token.access_token = "abc123";

            // Assert
            Assert.AreEqual("Bearer", token.token_type);
            Assert.AreEqual(3600, token.expires_in);
            Assert.AreEqual(7200, token.ext_expires_in);
            Assert.AreEqual("abc123", token.access_token);
        }

        [TestMethod]
        public void Properties_Default_To_Null_Or_Zero()
        {
            // Arrange & Act
            var token = new VentureFusionBearerToken();

            // Assert
            Assert.IsNull(token.token_type);
            Assert.AreEqual(0, token.expires_in);
            Assert.AreEqual(0, token.ext_expires_in);
            Assert.IsNull(token.access_token);
        }
    }
}