using System.ComponentModel.DataAnnotations;
using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class JourneyDeliveryInfoTests
    {
        #region JourneyDeliveryInfo Tests

        [TestMethod]
        public void Can_Create_JourneyDeliveryInfo_With_Valid_Properties()
        {
            var messageData = new MessageData
            {
                SubscriberKey = "SH900",
                EmailAddress = "jqsmith@cunamutual.com",
                SourceSystem = "POSTMAN",
                FirstName = "John",
                AttachmentName = ""
            };

            var messageBody = new MessageBody
            {
                ContactKey = "SH900",
                EventDefinitionKey = "APIEvent-6326d8ca-24b8-4fd4-53b9-c02c8743ef59",
                Data = messageData
            };

            var info = new JourneyDeliveryInfo
            {
                AccountId = 1449241,
                MessageBody = messageBody
            };

            Assert.AreEqual(1449241, info.AccountId);
            Assert.AreEqual("SH900", info.MessageBody.ContactKey);
            Assert.AreEqual("APIEvent-6326d8ca-24b8-4fd4-53b9-c02c8743ef59", info.MessageBody.EventDefinitionKey);
            Assert.AreEqual("SH900", info.MessageBody.Data.SubscriberKey);
            Assert.AreEqual("jqsmith@cunamutual.com", info.MessageBody.Data.EmailAddress);
        }

        [TestMethod]
        public void MessageBody_Is_Required()
        {
            var info = new JourneyDeliveryInfo
            {
                AccountId = 1449241,
                MessageBody = null!
            };

            var context = new ValidationContext(info);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(info, context, results, true);

            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(JourneyDeliveryInfo.MessageBody))));
        }

        #endregion

        #region MessageBody Tests

        [TestMethod]
        public void MessageBody_ContactKey_Is_Required()
        {
            var messageData = new MessageData
            {
                SubscriberKey = "SH900",
                EmailAddress = "test@test.com"
            };

            var messageBody = new MessageBody
            {
                ContactKey = null!,
                EventDefinitionKey = "APIEvent-123",
                Data = messageData
            };

            var context = new ValidationContext(messageBody);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(messageBody, context, results, true);

            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(MessageBody.ContactKey))));
        }

        [TestMethod]
        public void MessageBody_EventDefinitionKey_Is_Required()
        {
            var messageData = new MessageData
            {
                SubscriberKey = "SH900",
                EmailAddress = "test@test.com"
            };

            var messageBody = new MessageBody
            {
                ContactKey = "SH900",
                EventDefinitionKey = null!,
                Data = messageData
            };

            var context = new ValidationContext(messageBody);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(messageBody, context, results, true);

            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(MessageBody.EventDefinitionKey))));
        }

        [TestMethod]
        public void MessageBody_Data_Is_Required()
        {
            var messageBody = new MessageBody
            {
                ContactKey = "SH900",
                EventDefinitionKey = "APIEvent-123",
                Data = null!
            };

            var context = new ValidationContext(messageBody);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(messageBody, context, results, true);

            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(MessageBody.Data))));
        }

        #endregion

        #region MessageData Tests

        [TestMethod]
        public void MessageData_SubscriberKey_Is_Required()
        {
            var messageData = new MessageData
            {
                SubscriberKey = null!,
                EmailAddress = "test@test.com"
            };

            var context = new ValidationContext(messageData);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(messageData, context, results, true);

            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(MessageData.SubscriberKey))));
        }

        [TestMethod]
        public void MessageData_EmailAddress_Validates_Format()
        {
            var messageData = new MessageData
            {
                SubscriberKey = "SH900",
                EmailAddress = "invalid-email"
            };

            var context = new ValidationContext(messageData);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(messageData, context, results, true);

            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains(nameof(MessageData.EmailAddress))));
        }

        [TestMethod]
        public void MessageData_EmailAddress_Accepts_Valid_Email()
        {
            var messageData = new MessageData
            {
                SubscriberKey = "SH900",
                EmailAddress = "jqsmith@cunamutual.com"
            };

            var context = new ValidationContext(messageData);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(messageData, context, results, true);

            Assert.IsTrue(isValid);
        }

        [TestMethod]
        public void MessageData_EmailAddress_Can_Be_Null()
        {
            var messageData = new MessageData
            {
                SubscriberKey = "SH900",
                EmailAddress = null
            };

            var context = new ValidationContext(messageData);
            var results = new List<ValidationResult>();

            bool isValid = Validator.TryValidateObject(messageData, context, results, true);

            Assert.IsTrue(isValid);
        }

        #endregion

        #region ValidateNoSpecialCharacters Tests

        [TestMethod]
        public void ValidateNoSpecialCharacters_Throws_For_Forbidden_Characters_In_ContactKey()
        {
            var info = CreateValidJourneyDeliveryInfo();
            info.MessageBody.ContactKey = "SH900<script>";

            Assert.ThrowsException<ValidationException>(() => info.ValidateNoSpecialCharacters());
        }

        [TestMethod]
        public void ValidateNoSpecialCharacters_Throws_For_Forbidden_Characters_In_SubscriberKey()
        {
            var info = CreateValidJourneyDeliveryInfo();
            info.MessageBody.Data.SubscriberKey = "SH900{inject}";

            Assert.ThrowsException<ValidationException>(() => info.ValidateNoSpecialCharacters());
        }

        [TestMethod]
        public void ValidateNoSpecialCharacters_Throws_For_Forbidden_Characters_In_EmailAddress()
        {
            var info = CreateValidJourneyDeliveryInfo();
            info.MessageBody.Data.EmailAddress = "test@test.com<script>";

            Assert.ThrowsException<ValidationException>(() => info.ValidateNoSpecialCharacters());
        }

        [TestMethod]
        public void ValidateNoSpecialCharacters_Throws_For_Forbidden_Characters_In_SourceSystem()
        {
            var info = CreateValidJourneyDeliveryInfo();
            info.MessageBody.Data.SourceSystem = "POSTMAN|CMD";

            Assert.ThrowsException<ValidationException>(() => info.ValidateNoSpecialCharacters());
        }

        [TestMethod]
        public void ValidateNoSpecialCharacters_Passes_For_Valid_Data()
        {
            var info = CreateValidJourneyDeliveryInfo();

            info.ValidateNoSpecialCharacters(); // Should not throw
        }

        [DataTestMethod]
        [DataRow("<")]
        [DataRow(">")]
        [DataRow("{")]
        [DataRow("}")]
        [DataRow("[")]
        [DataRow("]")]
        [DataRow(";")]
        [DataRow("|")]
        [DataRow("\\")]
        [DataRow("^")]
        [DataRow("~")]
        [DataRow("`")]
        public void ValidateNoSpecialCharacters_Throws_For_Each_Forbidden_Character(string forbiddenChar)
        {
            var info = CreateValidJourneyDeliveryInfo();
            info.MessageBody.Data.FirstName = $"John{forbiddenChar}Doe";

            Assert.ThrowsException<ValidationException>(() => info.ValidateNoSpecialCharacters());
        }

        #endregion

        #region TrimAllStringFields Tests

        [TestMethod]
        public void TrimAllStringFields_Trims_ContactKey()
        {
            var info = CreateValidJourneyDeliveryInfo();
            info.MessageBody.ContactKey = "  SH900  ";

            info.TrimAllStringFields();

            Assert.AreEqual("SH900", info.MessageBody.ContactKey);
        }

        [TestMethod]
        public void TrimAllStringFields_Trims_EventDefinitionKey()
        {
            var info = CreateValidJourneyDeliveryInfo();
            info.MessageBody.EventDefinitionKey = "  APIEvent-123  ";

            info.TrimAllStringFields();

            Assert.AreEqual("APIEvent-123", info.MessageBody.EventDefinitionKey);
        }

        [TestMethod]
        public void TrimAllStringFields_Trims_SubscriberKey()
        {
            var info = CreateValidJourneyDeliveryInfo();
            info.MessageBody.Data.SubscriberKey = "  SH900  ";

            info.TrimAllStringFields();

            Assert.AreEqual("SH900", info.MessageBody.Data.SubscriberKey);
        }

        [TestMethod]
        public void TrimAllStringFields_Trims_EmailAddress()
        {
            var info = CreateValidJourneyDeliveryInfo();
            info.MessageBody.Data.EmailAddress = "  jqsmith@cunamutual.com  ";

            info.TrimAllStringFields();

            Assert.AreEqual("jqsmith@cunamutual.com", info.MessageBody.Data.EmailAddress);
        }

        [TestMethod]
        public void TrimAllStringFields_Trims_All_MessageData_Fields()
        {
            var info = CreateValidJourneyDeliveryInfo();
            info.MessageBody.Data.SourceSystem = "  POSTMAN  ";
            info.MessageBody.Data.FirstName = "  John  ";
            info.MessageBody.Data.AttachmentName = "  document.pdf  ";

            info.TrimAllStringFields();

            Assert.AreEqual("POSTMAN", info.MessageBody.Data.SourceSystem);
            Assert.AreEqual("John", info.MessageBody.Data.FirstName);
            Assert.AreEqual("document.pdf", info.MessageBody.Data.AttachmentName);
        }

        [TestMethod]
        public void TrimAllStringFields_Handles_Null_Optional_Fields()
        {
            var info = CreateValidJourneyDeliveryInfo();
            info.MessageBody.Data.EmailAddress = null;
            info.MessageBody.Data.SourceSystem = null;
            info.MessageBody.Data.FirstName = null;
            info.MessageBody.Data.AttachmentName = null;

            info.TrimAllStringFields(); // Should not throw

            Assert.IsNull(info.MessageBody.Data.EmailAddress);
            Assert.IsNull(info.MessageBody.Data.SourceSystem);
            Assert.IsNull(info.MessageBody.Data.FirstName);
            Assert.IsNull(info.MessageBody.Data.AttachmentName);
        }
        #endregion

        #region Helper Methods
        private static JourneyDeliveryInfo CreateValidJourneyDeliveryInfo()
        {
            var messageData = new MessageData
            {
                SubscriberKey = "SH900",
                EmailAddress = "jqsmith@cunamutual.com",
                SourceSystem = "POSTMAN",
                FirstName = "John",
                AttachmentName = ""
            };

            var messageBody = new MessageBody
            {
                ContactKey = "SH900",
                EventDefinitionKey = "APIEvent-6326d8ca-24b8-4fd4-53b9-c02c8743ef59",
                Data = messageData
            };

            return new JourneyDeliveryInfo
            {
                AccountId = 1449241,
                MessageBody = messageBody
            };
        }
        #endregion
    }
}