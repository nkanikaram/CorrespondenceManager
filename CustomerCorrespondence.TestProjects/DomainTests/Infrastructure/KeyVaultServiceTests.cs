using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CustomerCorrespondenceTests.Infrastructure
{
    [TestClass]
    public class KeyVaultServiceTests
    {
        private readonly Mock<IConfiguration> _mockConfig;
        private readonly Mock<ILogger<KeyVaultService>> _mockLogger;
        private readonly string _keyVaultUrl = "https://myvault.vault.azure.net/";

        public KeyVaultServiceTests()
        {
            _mockConfig = new Mock<IConfiguration>();
            _mockConfig.Setup(c => c["KeyVault:Url"]).Returns(_keyVaultUrl);
            _mockLogger = new Mock<ILogger<KeyVaultService>>();
        }

        [TestMethod]
        public void Constructor_ThrowsArgumentNullException_WhenConfigurationIsNull()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new KeyVaultService(null!, _mockLogger.Object));
        }

        [TestMethod]
        public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new KeyVaultService(_mockConfig.Object, null!));
        }

        [TestMethod]
        public void Constructor_ThrowsCorrespondenceException_WhenKeyVaultUrlIsMissing()
        {
            var config = new Mock<IConfiguration>();
            config.Setup(c => c["KeyVault:Url"]).Returns((string?)null);

            var exception = Assert.ThrowsException<CorrespondenceException>(() =>
                new KeyVaultService(config.Object, _mockLogger.Object));

            Assert.AreEqual("KEYVAULT_CONFIG_MISSING", exception.ErrorCode);
        }

        [TestMethod]
        public async Task GetSecretAsync_ReturnsSecretValue_WhenSecretExists()
        {
            // Arrange
            var secretName = "TestSecret";
            var secretValue = "SecretValue";
            var secret = new KeyVaultSecret(secretName, secretValue);

            var secretClientMock = new Mock<SecretClient>(MockBehavior.Strict, new Uri(_keyVaultUrl), new Azure.Identity.DefaultAzureCredential());
            secretClientMock
                .Setup(s => s.GetSecretAsync(secretName, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));

            var service = CreateServiceWithClient(secretClientMock.Object);

            // Act
            var result = await service.GetSecretAsync(secretName);

            // Assert
            Assert.AreEqual(secretValue, result);
        }

        [TestMethod]
        public async Task GetSecretAsync_ThrowsSecretNotFoundException_WhenSecretNotFound()
        {
            // Arrange
            var secretName = "MissingSecret";
            var secretClientMock = new Mock<SecretClient>(MockBehavior.Strict, new Uri(_keyVaultUrl), new Azure.Identity.DefaultAzureCredential());
            secretClientMock
                .Setup(s => s.GetSecretAsync(secretName, null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Not found"));

            var service = CreateServiceWithClient(secretClientMock.Object);

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<SecretNotFoundException>(
                () => service.GetSecretAsync(secretName));

            Assert.AreEqual(secretName, exception.SecretName);
            Assert.AreEqual("SECRET_NOT_FOUND", exception.ErrorCode);
        }

        [TestMethod]
        public async Task GetSecretAsync_ThrowsCorrespondenceException_WhenOtherErrorOccurs()
        {
            // Arrange
            var secretName = "TestSecret";
            var secretClientMock = new Mock<SecretClient>(MockBehavior.Strict, new Uri(_keyVaultUrl), new Azure.Identity.DefaultAzureCredential());
            secretClientMock
                .Setup(s => s.GetSecretAsync(secretName, null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(500, "Internal server error"));

            var service = CreateServiceWithClient(secretClientMock.Object);

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<CorrespondenceException>(
                () => service.GetSecretAsync(secretName));

            Assert.IsNotNull(exception.ErrorCode);
        }

        [TestMethod]
        public async Task GetSecretAsync_ThrowsArgumentNullException_WhenSecretNameIsNull()
        {
            // Arrange
            var service = new KeyVaultService(_mockConfig.Object, _mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => service.GetSecretAsync(null!));
        }

        [TestMethod]
        public async Task GetSecretAsync_ThrowsArgumentNullException_WhenSecretNameIsEmpty()
        {
            // Arrange
            var service = new KeyVaultService(_mockConfig.Object, _mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => service.GetSecretAsync(string.Empty));
        }

        [TestMethod]
        public async Task GetSecretAsync_ThrowsArgumentNullException_WhenSecretNameIsWhitespace()
        {
            // Arrange
            var service = new KeyVaultService(_mockConfig.Object, _mockLogger.Object);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => service.GetSecretAsync("   "));
        }

        [TestMethod]
        public async Task GetSecretAsync_LogsInformation_WhenSecretRetrievedSuccessfully()
        {
            // Arrange
            var secretName = "TestSecret";
            var secretValue = "SecretValue";
            var secret = new KeyVaultSecret(secretName, secretValue);

            var secretClientMock = new Mock<SecretClient>(MockBehavior.Strict, new Uri(_keyVaultUrl), new Azure.Identity.DefaultAzureCredential());
            secretClientMock
                .Setup(s => s.GetSecretAsync(secretName, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));

            var service = CreateServiceWithClient(secretClientMock.Object);

            // Act
            await service.GetSecretAsync(secretName);

            // Assert - Verify logging occurred
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [TestMethod]
        public async Task GetSecretAsync_LogsError_WhenExceptionOccurs()
        {
            // Arrange
            var secretName = "TestSecret";
            var secretClientMock = new Mock<SecretClient>(MockBehavior.Strict, new Uri(_keyVaultUrl), new Azure.Identity.DefaultAzureCredential());
            secretClientMock
                .Setup(s => s.GetSecretAsync(secretName, null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(500, "Server error"));

            var service = CreateServiceWithClient(secretClientMock.Object);

            // Act
            try
            {
                await service.GetSecretAsync(secretName);
            }
            catch
            {
                // Expected
            }

            // Assert - Verify error logging occurred
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        // Helper to inject a mock SecretClient via reflection
        private KeyVaultService CreateServiceWithClient(SecretClient client)
        {
            var service = new KeyVaultService(_mockConfig.Object, _mockLogger.Object);
            var field = typeof(KeyVaultService).GetField("_secretClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null)
                throw new InvalidOperationException("Field '_secretClient' not found in KeyVaultService.");
            field.SetValue(service, client);
            return service;
        }
    }
}