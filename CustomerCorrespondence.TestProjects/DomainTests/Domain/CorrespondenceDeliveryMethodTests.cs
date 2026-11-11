using System;
using CustomerCorrespondence.Domain.Entities;
using Xunit;
using Record = CustomerCorrespondence.Domain.Entities.DataRecords.Record;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class CorrespondenceDeliveryMethodTests
    {
        [TestMethod]
        public void Validate_WithValidProperties_DoesNotThrow()
        {
            var method = new CorrespondenceDeliveryMethod
            {
                DeliveryMethodId = Guid.NewGuid(),
                DeliveryMethodCode = "EMAIL",
                DeliveryMethodName = "Email"
            };

            Exception? exception = null;
            try
            {
                method.Validate();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            Assert.IsNull(exception);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Validate_WithInvalidDeliveryMethodCode_Throws(string code)
        {
            var method = new CorrespondenceDeliveryMethod
            {
                DeliveryMethodId = Guid.NewGuid(),
                DeliveryMethodCode = code,
                DeliveryMethodName = "Email"
            };

            var ex = Assert.ThrowsException<InvalidOperationException>(() => method.Validate());
            Assert.AreEqual("DeliveryMethodCode is required and cannot be null or empty.", ex.Message);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Validate_WithInvalidDeliveryMethodName_Throws(string name)
        {
            var method = new CorrespondenceDeliveryMethod
            {
                DeliveryMethodId = Guid.NewGuid(),
                DeliveryMethodCode = "EMAIL",
                DeliveryMethodName = name
            };

            var ex = Assert.ThrowsException<InvalidOperationException>(() => method.Validate());
            Assert.AreEqual("DeliveryMethodName is required and cannot be null or empty.", ex.Message);
        }
    }
}