using CustomerCorrespondence.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondenceTests.Exceptions
{
    [TestClass]
    public class ValidationExceptionTests
    {
        [TestMethod]
        public void Constructor_WithMessage_CreatesExceptionWithEmptyValidationErrors()
        {
            // Arrange
            var message = "Validation failed";

            // Act
            var exception = new ValidationException(message);

            // Assert
            Assert.AreEqual(message, exception.Message);
            Assert.AreEqual("VALIDATION_ERROR", exception.ErrorCode);
            Assert.IsNotNull(exception.ValidationErrors);
            Assert.AreEqual(0, exception.ValidationErrors.Count);
        }

        [TestMethod]
        public void Constructor_WithMessageAndValidationErrors_CreatesExceptionWithErrors()
        {
            // Arrange
            var message = "Validation failed";
            var validationErrors = new Dictionary<string, List<string>>
            {
                { "Field1", new List<string> { "Error 1", "Error 2" } },
                { "Field2", new List<string> { "Error 3" } }
            };

            // Act
            var exception = new ValidationException(message, validationErrors);

            // Assert
            Assert.AreEqual(message, exception.Message);
            Assert.AreEqual("VALIDATION_ERROR", exception.ErrorCode);
            Assert.IsNotNull(exception.ValidationErrors);
            Assert.AreEqual(2, exception.ValidationErrors.Count);
            Assert.AreEqual(2, exception.ValidationErrors["Field1"].Count);
            Assert.AreEqual(1, exception.ValidationErrors["Field2"].Count);
        }

        [TestMethod]
        public void ValidationException_InheritsFromCorrespondenceException()
        {
            // Arrange & Act
            var exception = new ValidationException("Test");

            // Assert
            Assert.IsInstanceOfType(exception, typeof(CorrespondenceException));
        }

        [TestMethod]
        public void ValidationException_CanBeCaughtAsCorrespondenceException()
        {
            // Arrange
            var wasCaught = false;

            // Act
            try
            {
                throw new ValidationException("Test validation error");
            }
            catch (CorrespondenceException ex)
            {
                wasCaught = true;
                Assert.AreEqual("VALIDATION_ERROR", ex.ErrorCode);
            }

            // Assert
            Assert.IsTrue(wasCaught);
        }

        [TestMethod]
        public void ValidationException_PreservesValidationErrorsWhenCaughtAsBase()
        {
            // Arrange
            var validationErrors = new Dictionary<string, List<string>>
            {
                { "Email", new List<string> { "Invalid email format" } }
            };

            // Act
            try
            {
                throw new ValidationException("Validation failed", validationErrors);
            }
            catch (CorrespondenceException ex)
            {
                // Assert
                Assert.IsInstanceOfType(ex, typeof(ValidationException));
                var validationEx = (ValidationException)ex;
                Assert.AreEqual(1, validationEx.ValidationErrors.Count);
                Assert.IsTrue(validationEx.ValidationErrors.ContainsKey("Email"));
            }
        }

        [TestMethod]
        public void ValidationErrors_CanContainMultipleErrorsPerField()
        {
            // Arrange
            var validationErrors = new Dictionary<string, List<string>>
            {
                { "Password", new List<string>
                    {
                        "Must be at least 8 characters",
                        "Must contain uppercase letter",
                        "Must contain number"
                    }
                }
            };

            // Act
            var exception = new ValidationException("Password validation failed", validationErrors);

            // Assert
            Assert.AreEqual(3, exception.ValidationErrors["Password"].Count);
        }
    }
}
