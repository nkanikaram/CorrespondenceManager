using CustomerCorrespondence.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondenceTests.Core
{
    [TestClass]
    public class ResultTests
    {
        #region Success Tests

        [TestMethod]
        public void Success_CreatesSuccessfulResult()
        {
            // Act
            var result = Result.Success();

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(result.IsFailure);
            Assert.IsNull(result.Error);
            Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(0, result.Errors.Count);
        }

        #endregion

        #region Failure Tests - Single Error

        [TestMethod]
        public void Failure_WithErrorMessage_CreatesFailedResult()
        {
            // Arrange
            var errorMessage = "Operation failed";

            // Act
            var result = Result.Failure(errorMessage);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(errorMessage, result.Error);
            Assert.IsNull(result.ErrorCode);
        }

        [TestMethod]
        public void Failure_WithErrorMessageAndCode_CreatesFailedResultWithCode()
        {
            // Arrange
            var errorMessage = "Operation failed";
            var errorCode = "OP_FAILED";

            // Act
            var result = Result.Failure(errorMessage, errorCode);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(errorMessage, result.Error);
            Assert.AreEqual(errorCode, result.ErrorCode);
        }

        #endregion

        #region Failure Tests - Multiple Errors

        [TestMethod]
        public void Failure_WithMultipleErrors_CreatesFailedResultWithAllErrors()
        {
            // Arrange
            var errors = new List<string> { "Error 1", "Error 2", "Error 3" };

            // Act
            var result = Result.Failure(errors);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(3, result.Errors.Count);
            Assert.AreEqual("Error 1; Error 2; Error 3", result.Error);
        }

        [TestMethod]
        public void Failure_WithMultipleErrorsAndCode_CreatesFailedResultWithCodeAndAllErrors()
        {
            // Arrange
            var errors = new List<string> { "Error 1", "Error 2" };
            var errorCode = "MULTIPLE_ERRORS";

            // Act
            var result = Result.Failure(errors, errorCode);

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
            var result = Result.Failure(exception);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual("Test exception", result.Error);
            Assert.AreEqual("InvalidOperationException", result.ErrorCode);
        }

        [TestMethod]
        public void Failure_FromExceptionWithCustomCode_CreatesFailedResultWithCustomCode()
        {
            // Arrange
            var exception = new InvalidOperationException("Test exception");
            var errorCode = "CUSTOM_ERROR";

            // Act
            var result = Result.Failure(exception, errorCode);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual("Test exception", result.Error);
            Assert.AreEqual(errorCode, result.ErrorCode);
        }

        #endregion

        #region Combine Tests

        [TestMethod]
        public void Combine_AllSuccessful_ReturnsSuccess()
        {
            // Arrange
            var result1 = Result.Success();
            var result2 = Result.Success();
            var result3 = Result.Success();

            // Act
            var combined = Result.Combine(result1, result2, result3);

            // Assert
            Assert.IsTrue(combined.IsSuccess);
            Assert.IsFalse(combined.IsFailure);
        }

        [TestMethod]
        public void Combine_SomeFailures_ReturnsCombinedFailure()
        {
            // Arrange
            var result1 = Result.Success();
            var result2 = Result.Failure("Error 1");
            var result3 = Result.Failure("Error 2");

            // Act
            var combined = Result.Combine(result1, result2, result3);

            // Assert
            Assert.IsFalse(combined.IsSuccess);
            Assert.IsTrue(combined.IsFailure);
            Assert.AreEqual(2, combined.Errors.Count);
            CollectionAssert.Contains(combined.Errors.ToList(), "Error 1");
            CollectionAssert.Contains(combined.Errors.ToList(), "Error 2");
        }

        [TestMethod]
        public void Combine_EmptyArray_ReturnsSuccess()
        {
            // Act
            var combined = Result.Combine();

            // Assert
            Assert.IsTrue(combined.IsSuccess);
        }

        [TestMethod]
        public void Combine_NullArray_ReturnsSuccess()
        {
            // Act
            var combined = Result.Combine(null!);

            // Assert
            Assert.IsTrue(combined.IsSuccess);
        }

        #endregion
    }
}
