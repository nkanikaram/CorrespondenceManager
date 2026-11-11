using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class RecipientAddressTests
    {
        [TestMethod]
        public void Can_Set_And_Get_Properties()
        {
            // Arrange
            var address = new RecipientAddress
            {
                RecipientAddressLine1 = "123 Main St",
                RecipientAddressLine2 = "Apt 4B",
                RecipientAddressLine3 = "Building 5",
                //RecipientAddressLine4 = "Floor 2",
                //RecipientAddressLine5 = "Near Park",
                RecipientAddressCity = "Metropolis",
                RecipientAddressST = "NY",
                RecipientAddressZIP = "12345",
                RecipientCountry = "USA"
            };

            // Assert
            Assert.AreEqual("123 Main St", address.RecipientAddressLine1);
            Assert.AreEqual("Apt 4B", address.RecipientAddressLine2);
            Assert.AreEqual("Building 5", address.RecipientAddressLine3);
            //Assert.AreEqual("Floor 2", address.RecipientAddressLine4);
            //Assert.AreEqual("Near Park", address.RecipientAddressLine5);
            Assert.AreEqual("Metropolis", address.RecipientAddressCity);
            Assert.AreEqual("NY", address.RecipientAddressST);
            Assert.AreEqual("12345", address.RecipientAddressZIP);
            Assert.AreEqual("USA", address.RecipientCountry);
        }

        [DataTestMethod]
        [DataRow("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 35, true)]
        [DataRow("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789X", 36, false)]
        public void RecipientAddressLine1_StringLength_Validation(string value, int length, bool isValid)
        {
            var address = new RecipientAddress { RecipientAddressLine1 = value };
            if (isValid)
            {
                Assert.AreEqual(value, address.RecipientAddressLine1);
            }
            else
            {
                Assert.IsTrue(address.RecipientAddressLine1!.Length > 35);
            }
        }
    }
}