using CustomerCorrespondence.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondenceTests.Core
{
    [TestClass]
    public class ResultGenericTests
    {
        #region Success Tests

        [TestMethod]
        public void Success_WithValue_CreatesSuccessfulResultWithValue()
        {
            // Arrange
            var value = "Test Value";

            // Act
            var result = Result<string>.Success(value);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(result.IsFailure);
            Assert.AreEqual(value, result.Value);
            Assert.IsNull(result.Error);
            Assert.IsNull(result.ErrorCode);
        }

        [TestMethod]
        public void Success_WithNullValue_CreatesSuccessfulResultWithNull()
        {
            // Act
            var result = Result<string?>.Success(null);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(result.IsFailure);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void Success_WithComplexType_CreatesSuccessfulResultWithObject()
        {
            // Arrange
            var value = new TestClass { Id = 1, Name = "Test" };

            // Act
            var result = Result<TestClass>.Success(value);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1, result.Value?.Id);
            Assert.AreEqual("Test", result.Value?.Name);
        }

        #endregion

        #region Failure Tests - Single Error

        [TestMethod]
        public void Failure_WithErrorMessage_CreatesFailedResult()
        {
            // Arrange
            var errorMessage = "Operation failed";

            // Act
            var result = Result<string>.Failure(errorMessage);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(errorMessage, result.Error);
            Assert.IsNull(result.ErrorCode);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void Failure_WithErrorMessageAndCode_CreatesFailedResultWithCode()
        {
            // Arrange
            var errorMessage = "Operation failed";
            var errorCode = "OP_FAILED";

            // Act
            var result = Result<int>.Failure(errorMessage, errorCode);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(errorMessage, result.Error);
            Assert.AreEqual(errorCode, result.ErrorCode);
            Assert.AreEqual(default(int), result.Value);
        }

        #endregion

        #region Failure Tests - Multiple Errors

        [TestMethod]
        public void Failure_WithMultipleErrors_CreatesFailedResultWithAllErrors()
        {
            // Arrange
            var errors = new List<string> { "Error 1", "Error 2", "Error 3" };

            // Act
            var result = Result<string>.Failure(errors);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(3, result.Errors.Count);
            Assert.AreEqual("Error 1; Error 2; Error 3", result.Error);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void Failure_WithMultipleErrorsAndCode_CreatesFailedResultWithCodeAndAllErrors()
        {
            // Arrange
            var errors = new List<string> { "Error 1", "Error 2" };
            var errorCode = "MULTIPLE_ERRORS";

            // Act
            var result = Result<bool>.Failure(errors, errorCode);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(2, result.Errors.Count);
            Assert.AreEqual("Error 1; Error 2", result.Error);
            Assert.AreEqual(errorCode, result.ErrorCode);
        }

        #endregion

        #region Failure Tests - From Exception

        [TestMethod]
        public void Failure_FromException_CreatesFailedResultWithExceptionMessage()
        {
            // Arrange
            var exception = new InvalidOperationException("Test exception");

            // Act
            var result = Result<string>.Failure(exception);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual("Test exception", result.Error);
            Assert.AreEqual("InvalidOperationException", result.ErrorCode);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void Failure_FromExceptionWithCustomCode_CreatesFailedResultWithCustomCode()
        {
            // Arrange
            var exception = new ArgumentNullException("param", "Parameter is null");
            var errorCode = "CUSTOM_NULL_ERROR";

            // Act
            var result = Result<object>.Failure(exception, errorCode);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFailure);
            Assert.IsTrue(result.Error?.Contains("Parameter is null"));
            Assert.AreEqual(errorCode, result.ErrorCode);
        }

        #endregion

        #region Type Inference Tests

        [TestMethod]
        public void Success_InfersTypeCorrectly()
        {
            // Act
            var result = Result<int>.Success(42);

            // Assert
            Assert.IsInstanceOfType(result, typeof(Result<int>));
            Assert.AreEqual(42, result.Value);
        }

        [TestMethod]
        public void Failure_InfersTypeCorrectly()
        {
            // Act
            var result = Result<decimal>.Failure("Error");

            // Assert
            Assert.IsInstanceOfType(result, typeof(Result<decimal>));
            Assert.AreEqual(default(decimal), result.Value);
        }

        #endregion

        #region Helper Classes

        private class TestClass
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        #endregion
    }
}
