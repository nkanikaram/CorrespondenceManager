using System;
using System.ComponentModel.DataAnnotations;
using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class SequentialProcessTests
    {
        private SequentialProcess CreateValidSequentialProcess()
        {
            return new SequentialProcess
            {
                DocId = "1234567890123456789",
                RequestingSystemName = "TestSystem",
                ECCOTrackingPolicyNumber = "PN1234567890",
                ECCOTrackingClaimNumber = "CL1234567890",
                ECCOTrackingCustomerId = "CUST123",
                DeliveryMethod = "Mail",
                MailDeliveryInfo = new MailDeliveryInfo
                {
                    RecipientFullName = "John Doe",
                    RecipientFirstName = "John",
                    RecipientLastName = "Doe",
                    RecipientAddress = new RecipientAddress
                    {
                        RecipientAddressLine1 = "123 Main St",
                        RecipientAddressCity = "City",
                        RecipientAddressST = "ST",
                        RecipientAddressZIP = "12345",
                        RecipientCountry = "USA"
                    }
                },
                CompositionInfo = new CompositionInfo
                {
                    VersionCode = "V1",
                }
            };
        }

        [TestMethod]
        public void Validate_WithValidData_DoesNotThrow()
        {
            var process = CreateValidSequentialProcess();
            var ex = Record.Exception(() => process.Validate());
            Assert.IsNull(ex);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" ")]
        public void Validate_MissingRequestingSystemName_Throws(string? value)
        {
            var process = CreateValidSequentialProcess();
            process.RequestingSystemName = value!;
            Assert.ThrowsException<ValidationException>(() => process.Validate());
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" ")]
        public void Validate_MissingDeliveryMethod_Throws(string? value)
        {
            var process = CreateValidSequentialProcess();
            process.DeliveryMethod = value!;
            Assert.ThrowsException<ValidationException>(() => process.Validate());
        }

        [TestMethod]
        public void Validate_MissingMailDeliveryInfo_Throws()
        {
            var process = CreateValidSequentialProcess();
            process.MailDeliveryInfo = null!;
            Assert.ThrowsException<ValidationException>(() => process.Validate());
        }

        [TestMethod]
        public void Validate_MissingCompositionInfo_Throws()
        {
            var process = CreateValidSequentialProcess();
            process.CompositionInfo = null!;
            Assert.ThrowsException<ValidationException>(() => process.Validate());
        }

        [TestMethod]
        public void AdditionalData_Is_Preserved()
        {
            var process = new SequentialProcess
            {
                RequestingSystemName = "TestSystem",
                ECCOTrackingPolicyNumber = "PN1234567890",
                ECCOTrackingClaimNumber = "CL1234567890",
                DeliveryMethod = "Mail",
                MailDeliveryInfo = new MailDeliveryInfo
                {
                    RecipientFullName = "John Doe",
                    RecipientFirstName = "John",
                    RecipientLastName = "Doe",
                    RecipientAddress = new RecipientAddress
                    {
                        RecipientAddressLine1 = "123 Main St",
                        RecipientAddressCity = "City",
                        RecipientAddressST = "ST",
                        RecipientAddressZIP = "12345",
                        RecipientCountry = "USA"
                    }
                },
                CompositionInfo = new CompositionInfo
                {
                    VersionCode = "V1"
                }
            };
        }
    }
}