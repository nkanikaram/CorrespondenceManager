using CustomerCorrespondence.Resilience;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondenceTests.Resilience
{
    [TestClass]
    public class RetryPolicyHelperTests
    {
        private Mock<ILogger>? _loggerMock;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [TestMethod]
        public void CreateRetryPolicyWithLogging_WithValidParameters_CreatesPolicy()
        {
            // Act
            var policy = RetryPolicyHelper.CreateRetryPolicyWithLogging(_loggerMock!.Object, 3, 2);

            // Assert
            Assert.IsNotNull(policy);
        }

        [TestMethod]
        public async Task CreateRetryPolicyWithLogging_ExecutesSuccessfully_DoesNotRetry()
        {
            // Arrange
            var policy = RetryPolicyHelper.CreateRetryPolicyWithLogging(_loggerMock!.Object, 3, 2);
            var executionCount = 0;

            // Act
            var result = await policy.ExecuteAsync(async () =>
            {
                executionCount++;
                await Task.CompletedTask;
                return "Success";
            });

            // Assert
            Assert.AreEqual(1, executionCount);
            Assert.AreEqual("Success", result);
        }

        [TestMethod]
        public async Task CreateRetryPolicyWithLogging_FailsThenSucceeds_RetriesCorrectly()
        {
            // Arrange
            var policy = RetryPolicyHelper.CreateRetryPolicyWithLogging(_loggerMock!.Object, 3, 1);
            var executionCount = 0;

            // Act
            var result = await policy.ExecuteAsync(async () =>
            {
                executionCount++;
                await Task.CompletedTask;

                if (executionCount < 2)
                    throw new InvalidOperationException("Temporary failure");

                return "Success";
            });

            // Assert
            Assert.AreEqual(2, executionCount);
            Assert.AreEqual("Success", result);
        }

        [TestMethod]
        public async Task CreateRetryPolicyWithLogging_AlwaysFails_ThrowsAfterRetries()
        {
            // Arrange
            var policy = RetryPolicyHelper.CreateRetryPolicyWithLogging(_loggerMock!.Object, 3, 1);
            var executionCount = 0;

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
            {
                await policy.ExecuteAsync(async () =>
                {
                    executionCount++;
                    await Task.CompletedTask;
                    throw new InvalidOperationException("Persistent failure");
                });
            });

            // Assert - Initial attempt + 3 retries
            Assert.AreEqual(4, executionCount);
        }

        [TestMethod]
        public async Task CreateRetryPolicyWithLogging_LogsRetryAttempts()
        {
            // Arrange
            var policy = RetryPolicyHelper.CreateRetryPolicyWithLogging(_loggerMock!.Object, 2, 1);
            var executionCount = 0;

            // Act
            try
            {
                await policy.ExecuteAsync(async () =>
                {
                    executionCount++;
                    await Task.CompletedTask;
                    throw new InvalidOperationException("Test failure");
                });
            }
            catch
            {
                // Expected to throw
            }

            // Assert - Verify logging was called (2 retries = 2 log calls)
            _loggerMock!.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeast(2));
        }

        [TestMethod]
        public async Task CreateRetryPolicyWithLogging_WithZeroRetries_DoesNotRetry()
        {
            // Arrange
            var policy = RetryPolicyHelper.CreateRetryPolicyWithLogging(_loggerMock!.Object, 0, 1);
            var executionCount = 0;

            // Act
            try
            {
                await policy.ExecuteAsync(async () =>
                {
                    executionCount++;
                    await Task.CompletedTask;
                    throw new InvalidOperationException("Failure");
                });
            }
            catch
            {
                // Expected
            }

            // Assert - Only initial attempt, no retries
            Assert.AreEqual(1, executionCount);
        }

        [TestMethod]
        public async Task CreateRetryPolicyWithLogging_DifferentExceptionTypes_RetriesAll()
        {
            // Arrange
            var policy = RetryPolicyHelper.CreateRetryPolicyWithLogging(_loggerMock!.Object, 3, 1);
            var executionCount = 0;

            // Act
            var result = await policy.ExecuteAsync(async () =>
            {
                executionCount++;
                await Task.CompletedTask;

                switch (executionCount)
                {
                    case 1:
                        throw new InvalidOperationException("Error 1");
                    case 2:
                        throw new ArgumentException("Error 2");
                    case 3:
                        return "Success";
                    default:
                        throw new Exception("Should not reach here");
                }
            });

            // Assert
            Assert.AreEqual(3, executionCount);
            Assert.AreEqual("Success", result);
        }
    }
}
