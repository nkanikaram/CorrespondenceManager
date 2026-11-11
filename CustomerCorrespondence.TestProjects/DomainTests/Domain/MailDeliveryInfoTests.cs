using System.ComponentModel.DataAnnotations;
using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class MailDeliveryInfoTests
    {
        [TestMethod]
        public void Can_Create_MailDeliveryInfo_With_Valid_Properties()
        {
            var address = new RecipientAddress
            {
                RecipientAddressLine1 = "123 Main St",
                RecipientAddressCity = "Springfield",
                RecipientAddressST = "IL",
                RecipientAddressZIP = "62704",
                RecipientCountry = "USA"
            };

            var info = new MailDeliveryInfo
            {
                RecipientFullName = "John Doe",
                RecipientFirstName = "John",
                RecipientLastName = "Doe",
                RecipientAddress = address,
                NonUSAddressFlag = false,
                BadAddressFlag = false
            };

            Assert.AreEqual("John Doe", info.RecipientFullName);
            Assert.AreEqual("John", info.RecipientFirstName);
            Assert.AreEqual("Doe", info.RecipientLastName);
            Assert.AreEqual(address, info.RecipientAddress);
            Assert.IsFalse(info.NonUSAddressFlag);
            Assert.IsFalse(info.BadAddressFlag);
        }

        [TestMethod]
        public void RecipientAddress_Is_Required()
        {
            var info = new MailDeliveryInfo
            {
                RecipientFullName = "Jane Smith",
                RecipientFirstName = "Jane",
                RecipientLastName = "Smith",
                RecipientAddress = null // Fix: set to null instead of string.Empty
            };

            var context = new ValidationContext(info);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(info, context, results, true);

            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(MailDeliveryInfo.RecipientAddress))));
        }

        [DataTestMethod]
        [DataRow("ThisNameIsWayTooLongToBeValidBecauseItExceedsSixtyCharactersWhichIsTheLimitSetByAttribute")]
        [DataRow(null)]
        public void RecipientFullName_Respects_StringLength(string? fullName)
        {
            var address = new RecipientAddress
            {
                RecipientAddressLine1 = "456 Elm St",
                RecipientAddressCity = "Metropolis",
                RecipientAddressST = "NY",
                RecipientAddressZIP = "10001",
                RecipientCountry = "USA"
            };

            var info = new MailDeliveryInfo
            {
                RecipientFullName = fullName,
                RecipientFirstName = "Alice",
                RecipientLastName = "Johnson",
                RecipientAddress = address
            };

            var context = new ValidationContext(info);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(info, context, results, true);

            if (fullName is not null && fullName.Length > 60)
            {
                Assert.IsFalse(isValid);
                Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(MailDeliveryInfo.RecipientFullName))));
            }
            else
            {
                Assert.IsTrue(isValid);
            }
        }
    }
}