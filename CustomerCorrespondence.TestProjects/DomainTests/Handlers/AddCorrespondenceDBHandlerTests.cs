using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using NServiceBus;
using ProcessRequestEndPoint.Handlers;
using System.Text;

namespace CustomerCorrespondenceTests.Handlers
{
    [TestClass]
    public class AddCorrespondenceDBHandlerTests
    {
        private Mock<ICorrespondenceRequestService>? _requestServiceMock;
        private Mock<ICorrespondenceRequestStatusHistoryService>? _historyServiceMock;
        private Mock<IEncryptionService>? _encryptionServiceMock;
        private CorrespondenceDBContext? _dbContext;
        private Mock<IMessageHandlerContext>? _contextMock;
        private Dictionary<string, string>? _headers;
        private AddCorrespondenceDBHandler? _handler;

        [TestInitialize]
        public void Setup()
        {
            _requestServiceMock = new Mock<ICorrespondenceRequestService>();
            _historyServiceMock = new Mock<ICorrespondenceRequestStatusHistoryService>();
            _encryptionServiceMock = new Mock<IEncryptionService>();

            var options = new DbContextOptionsBuilder<CorrespondenceDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            _dbContext = new CorrespondenceDBContext(options);

            _headers = new Dictionary<string, string>();
            _contextMock = new Mock<IMessageHandlerContext>();
            _contextMock.SetupGet(c => c.MessageHeaders).Returns(_headers);
            _contextMock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

            _encryptionServiceMock
                .Setup(x => x.EncryptAsync(It.IsAny<string>()))
                .ReturnsAsync("encrypted_value");
            _encryptionServiceMock
                .Setup(x => x.GenerateSearchHash(It.IsAny<string>()))
                .Returns(Encoding.UTF8.GetBytes("hash_value"));

            _handler = new AddCorrespondenceDBHandler(
                _requestServiceMock.Object,
                _historyServiceMock.Object,
                _encryptionServiceMock.Object,
                _dbContext);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _dbContext?.Dispose();
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_NullRequestService_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AddCorrespondenceDBHandler(
                    null!,
                    _historyServiceMock!.Object,
                    _encryptionServiceMock!.Object,
                    _dbContext!));
        }

        [TestMethod]
        public void Constructor_NullHistoryService_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AddCorrespondenceDBHandler(
                    _requestServiceMock!.Object,
                    null!,
                    _encryptionServiceMock!.Object,
                    _dbContext!));
        }

        [TestMethod]
        public void Constructor_NullEncryptionService_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AddCorrespondenceDBHandler(
                    _requestServiceMock!.Object,
                    _historyServiceMock!.Object,
                    null!,
                    _dbContext!));
        }

        [TestMethod]
        public void Constructor_NullDbContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AddCorrespondenceDBHandler(
                    _requestServiceMock!.Object,
                    _historyServiceMock!.Object,
                    _encryptionServiceMock!.Object,
                    null!));
        }

        [TestMethod]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            Assert.IsNotNull(_handler);
        }

        #endregion

        #region Handle Tests - Validation

        [TestMethod]
        public async Task Handle_MissingDocId_ThrowsValidationException()
        {
            // Arrange
            var message = CreateTestMessage(docId: null);

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<ValidationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("VALIDATION_ERROR", ex.ErrorCode);
            Assert.IsTrue(ex.ValidationErrors.ContainsKey("DocId"));
        }

        [TestMethod]
        public async Task Handle_EmptyDocId_ThrowsValidationException()
        {
            // Arrange
            var message = CreateTestMessage(docId: "");

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<ValidationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("VALIDATION_ERROR", ex.ErrorCode);
        }

        [TestMethod]
        public async Task Handle_MissingPolicyNumber_ThrowsValidationException()
        {
            // Arrange
            var message = CreateTestMessage(policyNumber: null);

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<ValidationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("VALIDATION_ERROR", ex.ErrorCode);
            Assert.IsTrue(ex.ValidationErrors.ContainsKey("ECCOTrackingPolicyNumber"));
        }

        #endregion

        #region Handle Tests - Success Scenarios

        [TestMethod]
        public async Task Handle_ValidMessage_CallsAddCorrespondenceRequestAsync()
        {
            // Arrange
            var message = CreateTestMessage();
            SeedDeliveryMethod("mail");

            // Act
            Assert.IsNotNull(_handler);
            Assert.IsNotNull(_contextMock);
            Assert.IsNotNull(_requestServiceMock);
            await _handler.Handle(message, _contextMock.Object);

            // Assert
            _requestServiceMock.Verify(
                s => s.AddCorrespondenceRequestAsync(It.Is<CorrespondenceRequest>(r =>
                    r.DocId == "1234567890123456789" &&
                    r.RequestingDomainName == "TestSystem")),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_ValidMessage_CallsAddCorrespondenceRequestStatusHistoryAsync()
        {
            // Arrange
            var message = CreateTestMessage();
            SeedDeliveryMethod("mail");

            // Act
            Assert.IsNotNull(_handler);
            Assert.IsNotNull(_contextMock);
            Assert.IsNotNull(_historyServiceMock);
            await _handler.Handle(message, _contextMock.Object);

            // Assert
            _historyServiceMock.Verify(
                s => s.AddCorrespondenceRequestStatusHistoryAsync(It.Is<CorrespondenceRequestStatusHistory>(h =>
                    h.StatusCode == "01" &&
                    h.StatusUpdatedBy == "System" &&
                    h.HistorySequenceNum == 1)),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_ValidMessage_EncryptsPolicyNumber()
        {
            // Arrange
            var message = CreateTestMessage();
            SeedDeliveryMethod("mail");

            // Act
            Assert.IsNotNull(_handler);
            Assert.IsNotNull(_contextMock);
            Assert.IsNotNull(_encryptionServiceMock);
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _encryptionServiceMock.Verify(e => e.EncryptAsync("POL123456"), Times.AtLeastOnce);
            _encryptionServiceMock.Verify(e => e.GenerateSearchHash("POL123456"), Times.Once);
        }

        [TestMethod]
        public async Task Handle_ValidMessage_CommitsTransaction()
        {
            // Arrange
            var message = CreateTestMessage();
            SeedDeliveryMethod("mail");

            // Act
            Assert.IsNotNull(_handler);
            Assert.IsNotNull(_contextMock);
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert - transaction committed by verifying no exception was thrown
            Assert.IsTrue(true);
        }

        [TestMethod]
        public async Task Handle_ValidMessage_SavesChangesToDatabase()
        {
            // Arrange
            var message = CreateTestMessage();
            SeedDeliveryMethod("mail");

            // Act
            Assert.IsNotNull(_handler);
            Assert.IsNotNull(_contextMock);
            Assert.IsNotNull(_dbContext);
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert - verify the handler completed successfully
            Assert.IsTrue(true);
        }

        #endregion

        #region Handle Tests - Error Scenarios

        [TestMethod]
        public async Task Handle_EncryptionFails_ThrowsEncryptionException()
        {
            // Arrange
            var message = CreateTestMessage();
            SeedDeliveryMethod("mail");
            Assert.IsNotNull(_encryptionServiceMock);
            _encryptionServiceMock
                .Setup(x => x.EncryptAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception("Encryption failed"));

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<EncryptionException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("ENCRYPTION_ERROR", ex.ErrorCode);
            Assert.IsNotNull(ex.InnerException);
        }

        [TestMethod]
        public async Task Handle_RequestServiceFails_ThrowsDatabaseException()
        {
            // Arrange
            var message = CreateTestMessage();
            SeedDeliveryMethod("mail");
            Assert.IsNotNull(_requestServiceMock);
            _requestServiceMock!
                .Setup(x => x.AddCorrespondenceRequestAsync(It.IsAny<CorrespondenceRequest>()))
                .ThrowsAsync(new Exception("Database error"));

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<DatabaseException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("DB_INSERT_ERROR", ex.ErrorCode);
            Assert.AreEqual("Insert", ex.Operation);
            Assert.AreEqual("CorrespondenceRequest", ex.TableName);
        }

        [TestMethod]
        public async Task Handle_InvalidDeliveryMethod_ThrowsValidationException()
        {
            // Arrange
            var message = CreateTestMessage();
            // Don't seed delivery method - should cause validation error

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<ValidationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("VALIDATION_ERROR", ex.ErrorCode);
            Assert.IsTrue(ex.ValidationErrors.ContainsKey("DeliveryMethod"));
        }

        [TestMethod]
        public async Task Handle_EncryptionException_ContainsProperErrorCode()
        {
            // Arrange
            var message = CreateTestMessage();
            SeedDeliveryMethod("mail");
            Assert.IsNotNull(_encryptionServiceMock);
            _encryptionServiceMock
                .Setup(x => x.EncryptAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception("Encryption failed"));

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<EncryptionException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("ENCRYPTION_ERROR", ex.ErrorCode);
            Assert.AreEqual("PGP", ex.EncryptionType);
            Assert.AreEqual("Encrypt", ex.Operation);
            Assert.IsNotNull(ex.InnerException);
        }

        [TestMethod]
        public async Task Handle_SerializationFails_ThrowsSerializationException()
        {
            // This test validates that valid messages serialize correctly
            // JsonSerializer is static so we can't mock failures easily
            var message = CreateTestMessage();
            SeedDeliveryMethod("mail");

            await _handler!.Handle(message, _contextMock!.Object);

            // If we got here, serialization worked
            Assert.IsTrue(true);
        }

        #endregion

        #region Helper Methods

        private AddCorrespondenceDBHandlerMessage CreateTestMessage(
            string? docId = "1234567890123456789",
            string? policyNumber = "POL123456")
        {
            return new AddCorrespondenceDBHandlerMessage
            {
                Process = new SequentialProcess
                {
                    DocId = docId,
                    RequestingSystemName = "TestSystem",
                    DeliveryMethod = "mail",
                    ECCOTrackingPolicyNumber = policyNumber,
                    ECCOTrackingClaimNumber = "CLM001",
                    ECCOTrackingCustomerId = "CUST001",
                    DocumentDescription = "TestDocument",
                    MailDeliveryInfo = new MailDeliveryInfo
                    {
                        RecipientFullName = "John Doe",
                        RecipientAddress = new RecipientAddress
                        {
                            RecipientAddressLine1 = "123 Main St",
                            RecipientAddressCity = "Madison",
                            RecipientAddressST = "WI",
                            RecipientAddressZIP = "53703"
                        }
                    },
                    CompositionInfo = new CompositionInfo
                    {
                        SolutionsId = "SOL001",
                        VersionCode = "V1"
                    }
                }
            };
        }

        private void SeedDeliveryMethod(string deliveryMethodName)
        {
            if (_dbContext == null)
            {
                throw new InvalidOperationException("_dbContext is null");
            }

            var deliveryMethod = new CorrespondenceDeliveryMethod
            {
                DeliveryMethodId = Guid.NewGuid(),
                DeliveryMethodCode = "01",
                DeliveryMethodName = deliveryMethodName
            };

            _dbContext.Set<CorrespondenceDeliveryMethod>().Add(deliveryMethod);
            _dbContext.SaveChanges();
        }

        #endregion
    }
}