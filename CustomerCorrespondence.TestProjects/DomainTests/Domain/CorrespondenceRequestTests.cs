using System;
using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class CorrespondenceRequestTests
    {
        [TestMethod]
        public void Validate_WithValidDeliveryMethodCode_DoesNotThrow()
        {
            // Arrange
            var request = new CorrespondenceRequest
            {
                CorrespondenceId = Guid.NewGuid(),
                DeliveryMethodCode = "EMAIL",
                RequestCreationTS = DateTime.UtcNow
            };

            // Act & Assert
            var exception = Record.Exception(() => request.Validate());
            Assert.IsNull(exception);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Validate_WithInvalidDeliveryMethodCode_ThrowsInvalidOperationException(string? deliveryMethodCode)
        {
            // Arrange
            var request = new CorrespondenceRequest
            {
                CorrespondenceId = Guid.NewGuid(),
                DeliveryMethodCode = deliveryMethodCode!,
                RequestCreationTS = DateTime.UtcNow
            };

            // Act & Assert
            var ex = Assert.ThrowsException<InvalidOperationException>(() => request.Validate());
            Assert.AreEqual("DeliveryMethodCode is required and cannot be null or empty.", ex.Message);
        }
    }
}