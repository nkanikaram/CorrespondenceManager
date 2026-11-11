using CustomerCorrespondence.Domain.Entities;
using System.ComponentModel.DataAnnotations;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class SequentialProcessEPresentTests
    {
        #region DeliveryMethod Property Tests

        [TestMethod]
        public void IsEPresentDelivery_Returns_True_For_EPresent()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "ePresent";

            Assert.IsTrue(process.IsEPresentDelivery);
            Assert.IsFalse(process.IsMailDelivery);
        }

        [TestMethod]
        public void IsEPresentDelivery_Returns_True_Case_Insensitive()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "EPRESENT";

            Assert.IsTrue(process.IsEPresentDelivery);
        }

        [TestMethod]
        public void IsEPresentDelivery_Returns_True_With_Whitespace()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "  ePresent  ";

            Assert.IsTrue(process.IsEPresentDelivery);
        }

        [TestMethod]
        public void IsEPresentDelivery_Returns_False_For_Mail()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "Mail";

            Assert.IsFalse(process.IsEPresentDelivery);
            Assert.IsTrue(process.IsMailDelivery);
        }

        [TestMethod]
        public void IsMailDelivery_Returns_True_For_Mail()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "Mail";

            Assert.IsTrue(process.IsMailDelivery);
            Assert.IsFalse(process.IsEPresentDelivery);
        }

        [TestMethod]
        public void IsMailDelivery_Returns_True_Case_Insensitive()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "MAIL";

            Assert.IsTrue(process.IsMailDelivery);
        }

        #endregion

        #region Validate DeliveryMethod Tests

        [TestMethod]
        public void Validate_Throws_For_Invalid_DeliveryMethod()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "InvalidMethod";

            var exception = Assert.ThrowsException<ValidationException>(() => process.Validate());
            Assert.IsTrue(exception.Message.Contains("DeliveryMethod must be"));
        }

        [TestMethod]
        public void Validate_Throws_For_Empty_DeliveryMethod()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "";

            var exception = Assert.ThrowsException<ValidationException>(() => process.Validate());
            Assert.IsTrue(exception.Message.Contains("DeliveryMethod is required"));
        }

        [TestMethod]
        public void Validate_Passes_For_Mail_DeliveryMethod()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "Mail";

            process.Validate(); // Should not throw
        }

        [TestMethod]
        public void Validate_Passes_For_EPresent_With_Valid_JourneyDeliveryInfo()
        {
            var process = CreateValidEPresentProcess();

            process.Validate(); // Should not throw
        }

        [TestMethod]
        public void Validate_Throws_For_EPresent_Without_JourneyDeliveryInfo()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "ePresent";
            process.JourneyDeliveryInfo = null;

            var exception = Assert.ThrowsException<ValidationException>(() => process.Validate());
            Assert.IsTrue(exception.Message.Contains("JourneyDeliveryInfo is required for ePresent delivery"));
        }

        #endregion

        #region ValidateEPresentEligibility Tests

        [TestMethod]
        public void ValidateEPresentEligibility_Throws_When_JourneyDeliveryInfo_Is_Null()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "ePresent";
            process.JourneyDeliveryInfo = null;

            var exception = Assert.ThrowsException<ValidationException>(() => process.ValidateEPresentEligibility());
            Assert.IsTrue(exception.Message.Contains("JourneyDeliveryInfo is required"));
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Throws_When_AccountId_Is_Zero()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.AccountId = 0;

            var exception = Assert.ThrowsException<ValidationException>(() => process.ValidateEPresentEligibility());
            Assert.IsTrue(exception.Message.Contains("AccountId must be a positive integer"));
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Throws_When_AccountId_Is_Negative()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.AccountId = -1;

            var exception = Assert.ThrowsException<ValidationException>(() => process.ValidateEPresentEligibility());
            Assert.IsTrue(exception.Message.Contains("AccountId must be a positive integer"));
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Throws_When_MessageBody_Is_Null()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "ePresent";
            process.JourneyDeliveryInfo = new JourneyDeliveryInfo
            {
                AccountId = 1449241,
                MessageBody = null!
            };

            var exception = Assert.ThrowsException<ValidationException>(() => process.ValidateEPresentEligibility());
            Assert.IsTrue(exception.Message.Contains("MessageBody is required"));
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Throws_When_ContactKey_Is_Empty()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.ContactKey = "";

            var exception = Assert.ThrowsException<ValidationException>(() => process.ValidateEPresentEligibility());
            Assert.IsTrue(exception.Message.Contains("ContactKey is required"));
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Throws_When_ContactKey_Is_Whitespace()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.ContactKey = "   ";

            var exception = Assert.ThrowsException<ValidationException>(() => process.ValidateEPresentEligibility());
            Assert.IsTrue(exception.Message.Contains("ContactKey is required"));
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Throws_When_EventDefinitionKey_Is_Empty()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.EventDefinitionKey = "";

            var exception = Assert.ThrowsException<ValidationException>(() => process.ValidateEPresentEligibility());
            Assert.IsTrue(exception.Message.Contains("EventDefinitionKey is required"));
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Throws_When_Data_Is_Null()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.Data = null!;

            var exception = Assert.ThrowsException<ValidationException>(() => process.ValidateEPresentEligibility());
            Assert.IsTrue(exception.Message.Contains("Data is required"));
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Throws_When_SubscriberKey_Is_Empty()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.Data.SubscriberKey = "";

            var exception = Assert.ThrowsException<ValidationException>(() => process.ValidateEPresentEligibility());
            Assert.IsTrue(exception.Message.Contains("SubscriberKey is required"));
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Throws_When_SubscriberKey_Is_Whitespace()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.Data.SubscriberKey = "   ";

            var exception = Assert.ThrowsException<ValidationException>(() => process.ValidateEPresentEligibility());
            Assert.IsTrue(exception.Message.Contains("SubscriberKey is required"));
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Throws_For_Invalid_Email_Format()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.Data.EmailAddress = "invalid-email";

            var exception = Assert.ThrowsException<ValidationException>(() => process.ValidateEPresentEligibility());
            Assert.IsTrue(exception.Message.Contains("EmailAddress is not a valid email format"));
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Passes_When_Email_Is_Null()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.Data.EmailAddress = null;

            process.ValidateEPresentEligibility(); // Should not throw
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Passes_When_Email_Is_Empty()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.Data.EmailAddress = "";

            process.ValidateEPresentEligibility(); // Should not throw
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Passes_With_Valid_Email()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.Data.EmailAddress = "jqsmith@cunamutual.com";

            process.ValidateEPresentEligibility(); // Should not throw
        }

        [TestMethod]
        public void ValidateEPresentEligibility_Passes_With_Valid_Data()
        {
            var process = CreateValidEPresentProcess();

            process.ValidateEPresentEligibility(); // Should not throw
        }

        #endregion

        #region Mail Delivery Does Not Trigger ePresent Validation

        [TestMethod]
        public void Validate_Does_Not_Call_ValidateEPresentEligibility_For_Mail_Delivery()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "Mail";
            process.JourneyDeliveryInfo = null; // No JourneyDeliveryInfo for Mail

            process.Validate(); // Should not throw - ePresent validation skipped
        }

        [TestMethod]
        public void Validate_Mail_Delivery_Ignores_JourneyDeliveryInfo()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "Mail";
            // Even with invalid JourneyDeliveryInfo, Mail delivery should pass
            process.JourneyDeliveryInfo = new JourneyDeliveryInfo
            {
                AccountId = 0, // Invalid for ePresent
                MessageBody = null! // Invalid for ePresent
            };

            process.Validate(); // Should not throw - ePresent validation not triggered
        }

        #endregion

        #region Integration Tests

        [TestMethod]
        public void FullValidation_EPresent_WithAllValidFields_Passes()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.Data.SourceSystem = "POSTMAN";
            process.JourneyDeliveryInfo.MessageBody.Data.FirstName = "John";
            process.JourneyDeliveryInfo.MessageBody.Data.AttachmentName = "document.pdf";

            process.Validate(); // Should not throw
        }

        [TestMethod]
        public void FullValidation_EPresent_WithOptionalFieldsNull_Passes()
        {
            var process = CreateValidEPresentProcess();
            process.JourneyDeliveryInfo!.MessageBody.Data.EmailAddress = null;
            process.JourneyDeliveryInfo.MessageBody.Data.SourceSystem = null;
            process.JourneyDeliveryInfo.MessageBody.Data.FirstName = null;
            process.JourneyDeliveryInfo.MessageBody.Data.AttachmentName = null;

            process.Validate(); // Should not throw - optional fields can be null
        }
        [TestMethod]
        public void IsEPresentDelivery_Returns_True_For_EPresentment()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "ePresentment";

            Assert.IsTrue(process.IsEPresentDelivery);
            Assert.IsFalse(process.IsMailDelivery);
        }

        [TestMethod]
        public void IsEPresentDelivery_Returns_True_For_EPresentment_Case_Insensitive()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "EPRESENTMENT";

            Assert.IsTrue(process.IsEPresentDelivery);
        }

        [DataTestMethod]
        [DataRow("ePresent")]
        [DataRow("EPRESENT")]
        [DataRow("EPresent")]
        [DataRow("ePresentment")]
        [DataRow("EPRESENTMENT")]
        [DataRow("EPresentment")]
        [DataRow("  ePresent  ")]
        [DataRow("  ePresentment  ")]
        public void IsEPresentDelivery_Returns_True_For_All_Variations(string deliveryMethod)
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = deliveryMethod;

            Assert.IsTrue(process.IsEPresentDelivery);
        }
        #endregion

        #region Helper Methods

        private static SequentialProcess CreateBaseProcess()
        {
            return new SequentialProcess
            {
                RequestingSystemName = "TestSystem",
                DeliveryMethod = "Mail",
                MailDeliveryInfo = new MailDeliveryInfo
                {
                    RecipientFullName = "John Doe",
                    RecipientAddress = new RecipientAddress
                    {
                        RecipientAddressLine1 = "123 Main St",
                        RecipientAddressCity = "Springfield",
                        RecipientAddressST = "IL",
                        RecipientAddressZIP = "62704"
                    }
                },
                CompositionInfo = new CompositionInfo
                {
                    SolutionsId = "SOL123",
                    VersionCode = "V1"
                }
            };
        }

        private static SequentialProcess CreateValidEPresentProcess()
        {
            var process = CreateBaseProcess();
            process.DeliveryMethod = "ePresent";
            process.JourneyDeliveryInfo = new JourneyDeliveryInfo
            {
                AccountId = 1449241,
                MessageBody = new MessageBody
                {
                    ContactKey = "SH900",
                    EventDefinitionKey = "APIEvent-6326d8ca-24b8-4fd4-53b9-c02c8743ef59",
                    Data = new MessageData
                    {
                        SubscriberKey = "SH900",
                        EmailAddress = "jqsmith@cunamutual.com",
                        SourceSystem = "POSTMAN",
                        FirstName = "John",
                        AttachmentName = ""
                    }
                }
            };
            return process;
        }

        #endregion
    }
}