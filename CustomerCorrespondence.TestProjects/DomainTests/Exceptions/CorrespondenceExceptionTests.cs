using CustomerCorrespondence.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondenceTests.Exceptions
{
    [TestClass]
    public class CorrespondenceExceptionTests
    {
        [TestMethod]
        public void Constructor_WithMessage_CreatesException()
        {
            // Arrange
            var message = "Test error message";

            // Act
            var exception = new CorrespondenceException(message);

            // Assert
            Assert.AreEqual(message, exception.Message);
            Assert.IsNull(exception.ErrorCode);
            Assert.IsNull(exception.InnerException);
        }

        [TestMethod]
        public void Constructor_WithMessageAndErrorCode_CreatesExceptionWithCode()
        {
            // Arrange
            var message = "Test error message";
            var errorCode = "TEST_ERROR";

            // Act
            var exception = new CorrespondenceException(message, errorCode);

            // Assert
            Assert.AreEqual(message, exception.Message);
            Assert.AreEqual(errorCode, exception.ErrorCode);
            Assert.IsNull(exception.InnerException);
        }

        [TestMethod]
        public void Constructor_WithMessageAndInnerException_CreatesExceptionWithInner()
        {
            // Arrange
            var message = "Test error message";
            var innerException = new InvalidOperationException("Inner error");

            // Act
            var exception = new CorrespondenceException(message, innerException);

            // Assert
            Assert.AreEqual(message, exception.Message);
            Assert.IsNotNull(exception.InnerException);
            Assert.AreEqual("Inner error", exception.InnerException.Message);
            Assert.IsNull(exception.ErrorCode);
        }

        [TestMethod]
        public void Constructor_WithAllParameters_CreatesFullException()
        {
            // Arrange
            var message = "Test error message";
            var errorCode = "TEST_ERROR";
            var innerException = new ArgumentNullException("param");

            // Act
            var exception = new CorrespondenceException(message, errorCode, innerException);

            // Assert
            Assert.AreEqual(message, exception.Message);
            Assert.AreEqual(errorCode, exception.ErrorCode);
            Assert.IsNotNull(exception.InnerException);
            Assert.IsInstanceOfType(exception.InnerException, typeof(ArgumentNullException));
        }

        [TestMethod]
        public void ErrorCode_CanBeSetAfterConstruction()
        {
            // Arrange
            var exception = new CorrespondenceException("Test");

            // Act
            exception.ErrorCode = "NEW_CODE";

            // Assert
            Assert.AreEqual("NEW_CODE", exception.ErrorCode);
        }

        [TestMethod]
        public void Exception_CanBeCaught_AsCorrespondenceException()
        {
            // Arrange
            var wasCaught = false;

            // Act
            try
            {
                throw new CorrespondenceException("Test", "TEST_ERROR");
            }
            catch (CorrespondenceException ex)
            {
                wasCaught = true;
                Assert.AreEqual("TEST_ERROR", ex.ErrorCode);
            }

            // Assert
            Assert.IsTrue(wasCaught);
        }

        [TestMethod]
        public void Exception_CanBeCaught_AsBaseException()
        {
            // Arrange
            var wasCaught = false;

            // Act
            try
            {
                throw new CorrespondenceException("Test", "TEST_ERROR");
            }
            catch (Exception ex)
            {
                wasCaught = true;
                Assert.IsInstanceOfType(ex, typeof(CorrespondenceException));
            }

            // Assert
            Assert.IsTrue(wasCaught);
        }
    }
}
