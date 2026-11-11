using System;
using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class CorrespondenceStatusCodeTests
    {
        [TestMethod]
        public void Validate_WithValidProperties_DoesNotThrow()
        {
            var status = new CorrespondenceStatusCode
            {
                StatusId = Guid.NewGuid(),
                StatusCode = "ACTIVE",
                StatusDescription = "Active status"
            };

            var exception = Record.Exception(() => status.Validate());
            Assert.IsNull(exception);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Validate_WithInvalidStatusCode_Throws(string invalidCode)
        {
            var status = new CorrespondenceStatusCode
            {
                StatusId = Guid.NewGuid(),
                StatusCode = invalidCode,
                StatusDescription = "Some description"
            };

            var ex = Assert.ThrowsException<InvalidOperationException>(() => status.Validate());
            Assert.AreEqual("StatusCode is required and cannot be null or empty.", ex.Message);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Validate_WithInvalidStatusDescription_Throws(string invalidDescription)
        {
            var status = new CorrespondenceStatusCode
            {
                StatusId = Guid.NewGuid(),
                StatusCode = "CODE",
                StatusDescription = invalidDescription
            };

            var ex = Assert.ThrowsException<InvalidOperationException>(() => status.Validate());
            Assert.AreEqual("StatusDescription is required and cannot be null or empty.", ex.Message);
        }
    }
}