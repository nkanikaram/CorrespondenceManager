using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using CustomerCorrespondence.Exceptions;
using Microsoft.Extensions.Logging;
using Moq;
using NServiceBus;
using ProcessRequestEndPoint.Handlers;
using System.Text.Json;

namespace CustomerCorrespondenceTests.Handlers
{
    [TestClass]
    public class AssignDocumentumIdHandlerTests
    {
        private Mock<IDocumentIdGeneratorService>? _documentIdGeneratorServiceMock;
        private Mock<IDocumentIdsService>? _documentIdsServiceMock;
        private Mock<IMessageHandlerContext>? _contextMock;
        private Dictionary<string, string>? _headers;
        private AssignDocumentumIdHandler? _handler;

        [TestInitialize]
        public void Setup()
        {
            _documentIdGeneratorServiceMock = new Mock<IDocumentIdGeneratorService>();
            _documentIdsServiceMock = new Mock<IDocumentIdsService>();
            _contextMock = new Mock<IMessageHandlerContext>();

            _headers = new Dictionary<string, string>();
            _contextMock.SetupGet(c => c.MessageHeaders).Returns(_headers);
            _contextMock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

            _handler = new AssignDocumentumIdHandler(
                _documentIdGeneratorServiceMock.Object,
                _documentIdsServiceMock.Object);
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_NullDocumentIdGeneratorService_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AssignDocumentumIdHandler(
                    null!,
                    _documentIdsServiceMock!.Object));
        }

        [TestMethod]
        public void Constructor_NullDocumentIdsService_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AssignDocumentumIdHandler(
                    _documentIdGeneratorServiceMock!.Object,
                    null!));
        }

        [TestMethod]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            Assert.IsNotNull(_handler);
        }

        #endregion

        #region Handle Tests - DocId Already Exists

        [TestMethod]
        public async Task Handle_MessageAlreadyHasDocId_SkipsAssignment()
        {
            // Arrange
            var existingDocId = "EXISTING_DOC_ID_123";
            var message = CreateTestMessage(existingDocId);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            Assert.AreEqual(existingDocId, message.Process.DocId);
            _documentIdsServiceMock!.Verify(s => s.GetAndUpdateDocumentIdAsync(), Times.Never);
            _documentIdGeneratorServiceMock!.Verify(s => s.GenerateDocIdAsync(), Times.Never);
        }

        [TestMethod]
        public async Task Handle_MessageAlreadyHasDocId_ForwardsToNextStep()
        {
            // Arrange
            var existingDocId = "EXISTING_DOC_ID_123";
            var message = CreateTestMessage(existingDocId);
            SetupRoutingHeaders(new List<string> { "AssignDocumentumIdHandler", "NextHandler" }, 0);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _contextMock!.Verify(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()), Times.Once);
        }

        #endregion

        #region Handle Tests - DocId From Pool

        [TestMethod]
        public async Task Handle_DocIdFromPool_AssignsDocIdToMessage()
        {
            // Arrange
            var pooledDocId = "POOLED_DOC_ID_456";
            var message = CreateTestMessage(null);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync(pooledDocId);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            Assert.AreEqual(pooledDocId, message.Process.DocId);
        }

        [TestMethod]
        public async Task Handle_DocIdFromPool_DoesNotGenerateNewDocId()
        {
            // Arrange
            var pooledDocId = "POOLED_DOC_ID_456";
            var message = CreateTestMessage(null);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync(pooledDocId);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _documentIdGeneratorServiceMock!.Verify(s => s.GenerateDocIdAsync(), Times.Never);
        }

        [TestMethod]
        public async Task Handle_DocIdFromPool_ForwardsToNextStep()
        {
            // Arrange
            var pooledDocId = "POOLED_DOC_ID_456";
            var message = CreateTestMessage(null);
            SetupRoutingHeaders(new List<string> { "AssignDocumentumIdHandler", "NextHandler" }, 0);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync(pooledDocId);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _contextMock!.Verify(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()), Times.Once);
        }

        #endregion

        #region Handle Tests - DocId Generated

        [TestMethod]
        public async Task Handle_NoDocIdInPool_GeneratesNewDocId()
        {
            // Arrange
            var generatedDocId = "GENERATED_DOC_ID_789";
            var message = CreateTestMessage(null);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync((string?)null);

            _documentIdGeneratorServiceMock!
                .Setup(s => s.GenerateDocIdAsync())
                .ReturnsAsync(generatedDocId);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _documentIdGeneratorServiceMock.Verify(s => s.GenerateDocIdAsync(), Times.Once);
            Assert.AreEqual(generatedDocId, message.Process.DocId);
        }

        [TestMethod]
        public async Task Handle_GeneratedDocId_AssignsToMessage()
        {
            // Arrange
            var generatedDocId = "GENERATED_DOC_ID_789";
            var message = CreateTestMessage(null);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync((string?)null);

            _documentIdGeneratorServiceMock!
                .Setup(s => s.GenerateDocIdAsync())
                .ReturnsAsync(generatedDocId);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            Assert.AreEqual(generatedDocId, message.Process.DocId);
        }

        [TestMethod]
        public async Task Handle_GeneratedDocId_ForwardsToNextStep()
        {
            // Arrange
            var generatedDocId = "GENERATED_DOC_ID_789";
            var message = CreateTestMessage(null);
            SetupRoutingHeaders(new List<string> { "AssignDocumentumIdHandler", "NextHandler" }, 0);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync((string?)null);

            _documentIdGeneratorServiceMock!
                .Setup(s => s.GenerateDocIdAsync())
                .ReturnsAsync(generatedDocId);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _contextMock!.Verify(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()), Times.Once);
        }

        #endregion

        #region Handle Tests - Error Scenarios

        [TestMethod]
        public async Task Handle_GeneratedDocIdIsNull_ThrowsDocumentIdGenerationException()
        {
            // Arrange
            var message = CreateTestMessage(null);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync((string?)null);

            _documentIdGeneratorServiceMock!
                .Setup(s => s.GenerateDocIdAsync())
                .ReturnsAsync((string?)null);

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<DocumentIdGenerationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("DOCID_GENERATION_FAILED", ex.ErrorCode);
        }

        [TestMethod]
        public async Task Handle_GetAndUpdateDocumentIdAsyncFails_ThrowsDocumentIdGenerationException()
        {
            // Arrange
            var message = CreateTestMessage(null);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ThrowsAsync(new Exception("Database error"));

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<DocumentIdGenerationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("DOCID_ASSIGNMENT_ERROR", ex.ErrorCode);
            Assert.IsNotNull(ex.InnerException);
        }

        [TestMethod]
        public async Task Handle_GenerateDocIdAsyncFails_ThrowsDocumentIdGenerationException()
        {
            // Arrange
            var message = CreateTestMessage(null);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync((string?)null);

            _documentIdGeneratorServiceMock!
                .Setup(s => s.GenerateDocIdAsync())
                .ThrowsAsync(new Exception("Generation failed"));

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<DocumentIdGenerationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("DOCID_ASSIGNMENT_ERROR", ex.ErrorCode);
            Assert.IsNotNull(ex.InnerException);
        }

        #endregion

        #region Rollback Tests

        [TestMethod]
        public async Task Handle_FailureAfterDocIdAssignment_AttemptsRollback()
        {
            // Arrange
            var generatedDocId = "GENERATED_DOC_ID_ROLLBACK";
            var message = CreateTestMessage(null);
            SetupRoutingHeaders(new List<string> { "AssignDocumentumIdHandler", "NextHandler" }, 0);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync((string?)null);

            _documentIdGeneratorServiceMock!
                .Setup(s => s.GenerateDocIdAsync())
                .ReturnsAsync(generatedDocId);

            // Simulate failure during forwarding
            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .ThrowsAsync(new Exception("Forwarding failed"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(
                () => _handler!.Handle(message, _contextMock.Object));

            // Verify DocId was added back to pool
            _documentIdsServiceMock.Verify(s => s.AddDocumentIdAsync(generatedDocId), Times.Once);

            // Verify compensation event was published
            _contextMock.Verify(c => c.Publish(
                It.IsAny<DocumentIdAssignmentCompensationEvent>(),
                It.IsAny<PublishOptions>()),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_FailureAfterDocIdAssignment_PublishesCompensationEvent()
        {
            // Arrange
            var generatedDocId = "GENERATED_DOC_ID_COMPENSATION";
            var message = CreateTestMessage(null);
            SetupRoutingHeaders(new List<string> { "AssignDocumentumIdHandler", "NextHandler" }, 0);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync((string?)null);

            _documentIdGeneratorServiceMock!
                .Setup(s => s.GenerateDocIdAsync())
                .ReturnsAsync(generatedDocId);

            // Simulate failure during forwarding
            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .ThrowsAsync(new Exception("Forwarding failed"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(
                () => _handler!.Handle(message, _contextMock.Object));

            // Verify compensation event was published with correct details
            _contextMock.Verify(c => c.Publish(
                It.Is<DocumentIdAssignmentCompensationEvent>(e =>
                    e.DocId == generatedDocId &&
                    e.WasNewlyGenerated == true &&
                    e.CompensationReason == "Forwarding failed" &&
                    e.HandlerName == nameof(AssignDocumentumIdHandler)),
                It.IsAny<PublishOptions>()),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_FailureAfterPooledDocIdAssignment_RestoresOriginalDocId()
        {
            // Arrange
            var pooledDocId = "POOLED_DOC_ID_RESTORE";
            var message = CreateTestMessage(null);
            SetupRoutingHeaders(new List<string> { "AssignDocumentumIdHandler", "NextHandler" }, 0);
            var originalDocId = message.Process.DocId; // Should be null

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync(pooledDocId);

            // Simulate failure during forwarding
            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .ThrowsAsync(new Exception("Forwarding failed"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(
                () => _handler!.Handle(message, _contextMock.Object));

            // Verify original DocId was restored (should be null)
            Assert.AreEqual(originalDocId, message.Process.DocId);
        }

        [TestMethod]
        public async Task Handle_RollbackFailure_ContinuesWithCompensationEvent()
        {
            // Arrange
            var generatedDocId = "GENERATED_DOC_ID_ROLLBACK_FAIL";
            var message = CreateTestMessage(null);
            SetupRoutingHeaders(new List<string> { "AssignDocumentumIdHandler", "NextHandler" }, 0);

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync((string?)null);

            _documentIdGeneratorServiceMock!
                .Setup(s => s.GenerateDocIdAsync())
                .ReturnsAsync(generatedDocId);

            // Simulate failure during forwarding
            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .ThrowsAsync(new Exception("Forwarding failed"));

            // Simulate rollback failure
            _documentIdsServiceMock
                .Setup(s => s.AddDocumentIdAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception("Rollback failed"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(
                () => _handler!.Handle(message, _contextMock.Object));

            // Verify compensation event was still published despite rollback failure
            _contextMock.Verify(c => c.Publish(
                It.IsAny<DocumentIdAssignmentCompensationEvent>(),
                It.IsAny<PublishOptions>()),
                Times.Once);
        }

        #endregion

        #region Message Processing Flow Tests

        [TestMethod]
        public async Task Handle_CompleteFlow_WithPooledDocId_ExecutesInCorrectOrder()
        {
            // Arrange
            var pooledDocId = "POOLED_DOC_ID_ORDER";
            var message = CreateTestMessage(null);
            SetupRoutingHeaders(new List<string> { "AssignDocumentumIdHandler", "NextHandler" }, 0);
            var executionOrder = new List<string>();

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync(pooledDocId)
                .Callback(() => executionOrder.Add("GetAndUpdateDocumentIdAsync"));

            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .Returns(Task.CompletedTask)
                .Callback(() => executionOrder.Add("Send"));

            // Act
            await _handler!.Handle(message, _contextMock.Object);

            // Assert
            Assert.AreEqual(2, executionOrder.Count);
            Assert.AreEqual("GetAndUpdateDocumentIdAsync", executionOrder[0]);
            Assert.AreEqual("Send", executionOrder[1]);
        }

        [TestMethod]
        public async Task Handle_CompleteFlow_WithGeneratedDocId_ExecutesInCorrectOrder()
        {
            // Arrange
            var generatedDocId = "GENERATED_DOC_ID_ORDER";
            var message = CreateTestMessage(null);
            SetupRoutingHeaders(new List<string> { "AssignDocumentumIdHandler", "NextHandler" }, 0);
            var executionOrder = new List<string>();

            _documentIdsServiceMock!
                .Setup(s => s.GetAndUpdateDocumentIdAsync())
                .ReturnsAsync((string?)null)
                .Callback(() => executionOrder.Add("GetAndUpdateDocumentIdAsync"));

            _documentIdGeneratorServiceMock!
                .Setup(s => s.GenerateDocIdAsync())
                .ReturnsAsync(generatedDocId)
                .Callback(() => executionOrder.Add("GenerateDocIdAsync"));

            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .Returns(Task.CompletedTask)
                .Callback(() => executionOrder.Add("Send"));

            // Act
            await _handler!.Handle(message, _contextMock.Object);

            // Assert
            Assert.AreEqual(3, executionOrder.Count);
            Assert.AreEqual("GetAndUpdateDocumentIdAsync", executionOrder[0]);
            Assert.AreEqual("GenerateDocIdAsync", executionOrder[1]);
            Assert.AreEqual("Send", executionOrder[2]);
        }

        #endregion

        #region Helper Methods

        private void SetupRoutingHeaders(List<string> route, int currentIndex)
        {
            var routeJson = JsonSerializer.Serialize(route);
            _headers!["RoutingSlip-Route"] = routeJson;
            _headers["RoutingSlip-Index"] = currentIndex.ToString();
            _headers["RoutingSlip-OriginatingId"] = Guid.NewGuid().ToString();
        }

        private AssignDocumentumIdHandlerMessage CreateTestMessage(string? docId)
        {
            return new AssignDocumentumIdHandlerMessage
            {
                Process = new SequentialProcess
                {
                    DocId = docId,
                    RequestingSystemName = "TestSystem",
                    DeliveryMethod = "mail",
                    ECCOTrackingPolicyNumber = "POL123456",
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

        #endregion
    }
}
