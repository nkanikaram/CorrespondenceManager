using CustomerCorrespondence.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondenceTests.Core
{
    [TestClass]
    public class ErrorResponseTests
    {
        [TestMethod]
        public void ErrorResponse_DefaultConstructor_InitializesWithDefaults()
        {
            // Act
            var response = new ErrorResponse();

            // Assert
            Assert.AreEqual(string.Empty, response.Message);
            Assert.IsNull(response.ErrorCode);
            Assert.IsNull(response.Details);
            Assert.IsNull(response.ValidationErrors);
            Assert.IsTrue((DateTime.UtcNow - response.Timestamp).TotalSeconds < 1);
        }

        [TestMethod]
        public void ErrorResponse_SetMessage_StoresMessage()
        {
            // Arrange
            var response = new ErrorResponse();
            var message = "An error occurred";

            // Act
            response.Message = message;

            // Assert
            Assert.AreEqual(message, response.Message);
        }

        [TestMethod]
        public void ErrorResponse_SetErrorCode_StoresErrorCode()
        {
            // Arrange
            var response = new ErrorResponse();
            var errorCode = "ERR_001";

            // Act
            response.ErrorCode = errorCode;

            // Assert
            Assert.AreEqual(errorCode, response.ErrorCode);
        }

        [TestMethod]
        public void ErrorResponse_SetValidationErrors_StoresValidationErrors()
        {
            // Arrange
            var response = new ErrorResponse();
            var validationErrors = new Dictionary<string, List<string>>
            {
                { "Field1", new List<string> { "Error 1", "Error 2" } },
                { "Field2", new List<string> { "Error 3" } }
            };

            // Act
            response.ValidationErrors = validationErrors;

            // Assert
            Assert.IsNotNull(response.ValidationErrors);
            Assert.AreEqual(2, response.ValidationErrors.Count);
            Assert.AreEqual(2, response.ValidationErrors["Field1"].Count);
            Assert.AreEqual(1, response.ValidationErrors["Field2"].Count);
        }

        [TestMethod]
        public void ErrorResponse_FullyPopulated_StoresAllProperties()
        {
            // Arrange
            var message = "Test error";
            var errorCode = "TEST_ERROR";
            var details = "Test details";
            var timestamp = DateTime.UtcNow;
            var validationErrors = new Dictionary<string, List<string>>
            {
                { "TestField", new List<string> { "Test validation error" } }
            };

            // Act
            var response = new ErrorResponse
            {
                Message = message,
                ErrorCode = errorCode,
                Details = details,
                Timestamp = timestamp,
                ValidationErrors = validationErrors
            };

            // Assert
            Assert.AreEqual(message, response.Message);
            Assert.AreEqual(errorCode, response.ErrorCode);
            Assert.AreEqual(details, response.Details);
            Assert.AreEqual(timestamp, response.Timestamp);
            Assert.IsNotNull(response.ValidationErrors);
            Assert.AreEqual(1, response.ValidationErrors.Count);
        }
    }
}
