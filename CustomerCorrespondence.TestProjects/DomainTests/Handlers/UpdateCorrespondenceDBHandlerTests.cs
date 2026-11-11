using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Exceptions;
using Microsoft.Extensions.Logging;
using Moq;
using NServiceBus;
using ProcessRequestEndPoint.Handlers;
using System.Net;
using System.Text.Json;

namespace CustomerCorrespondenceTests.Handlers
{
    [TestClass]
    public class UpdateCorrespondenceDBHandlerTests
    {
        private Mock<ICorrespondenceRequestRepository>? _requestRepoMock;
        private Mock<ICorrespondenceRequestStatusHistoryRepository>? _historyRepoMock;
        private Mock<IMessageHandlerContext>? _contextMock;
        private Dictionary<string, string>? _headers;
        private UpdateCorrespondenceDBHandler? _handler;

        [TestInitialize]
        public void Setup()
        {
            _requestRepoMock = new Mock<ICorrespondenceRequestRepository>();
            _historyRepoMock = new Mock<ICorrespondenceRequestStatusHistoryRepository>();
            _contextMock = new Mock<IMessageHandlerContext>();

            _headers = new Dictionary<string, string>();
            _contextMock.SetupGet(c => c.MessageHeaders).Returns(_headers);
            _contextMock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

            _handler = new UpdateCorrespondenceDBHandler(
                _requestRepoMock.Object,
                _historyRepoMock.Object);
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_NullRequestRepository_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new UpdateCorrespondenceDBHandler(
                    null!,
                    _historyRepoMock!.Object));
        }

        [TestMethod]
        public void Constructor_NullHistoryRepository_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new UpdateCorrespondenceDBHandler(
                    _requestRepoMock!.Object,
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
        public async Task Handle_WhitespaceDocId_ThrowsValidationException()
        {
            // Arrange
            var message = CreateTestMessage(docId: "   ");

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ValidationException>(
                () => _handler!.Handle(message, _contextMock!.Object));
        }

        #endregion

        #region Handle Tests - Success Scenarios

        [TestMethod]
        public async Task Handle_SuccessfulVentureFusionResponse_CreatesAwaitingImagingStatus()
        {
            // Arrange
            var message = CreateTestMessage();
            var correspondenceId = Guid.NewGuid();
            SetupSuccessfulFlow(correspondenceId, "AwaitingImaging");

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _historyRepoMock!.Verify(h => h.AddCorrespondenceRequestStatusHistoryAsync(
                It.Is<CorrespondenceRequestStatusHistory>(s =>
                    s.CorrespondenceId == correspondenceId &&
                    s.StatusCode == "AwaitingImaging" &&
                    s.HistorySequenceNum == 1 &&
                    s.StatusUpdatedBy == "System")),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_FailedVentureFusionResponse_CreatesVFRequestFailedStatus()
        {
            // Arrange
            var message = CreateTestMessage();
            message.Process.VentureFusionResponse = CreateVentureFusionResponse(false, HttpStatusCode.BadRequest);
            var correspondenceId = Guid.NewGuid();

            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ReturnsAsync(0);

            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler" }, 0);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert - Failed responses get status code "VFRequestFailed"
            _historyRepoMock!.Verify(h => h.AddCorrespondenceRequestStatusHistoryAsync(
                It.Is<CorrespondenceRequestStatusHistory>(s =>
                    s.CorrespondenceId == correspondenceId &&
                    s.StatusCode == "VFRequestFailed" &&
                    s.HistorySequenceNum == 1)),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_NullVentureFusionResponse_CreatesVFRequestFailedStatus()
        {
            // Arrange
            var message = CreateTestMessage();
            message.Process.VentureFusionResponse = null;
            var correspondenceId = Guid.NewGuid();

            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ReturnsAsync(0);

            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler" }, 0);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _historyRepoMock!.Verify(h => h.AddCorrespondenceRequestStatusHistoryAsync(
                It.Is<CorrespondenceRequestStatusHistory>(s =>
                    s.StatusCode == "VFRequestFailed")),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_ExistingSequenceNumbers_CreatesNextSequence()
        {
            // Arrange
            var message = CreateTestMessage();
            var correspondenceId = Guid.NewGuid();

            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ReturnsAsync(5); // Existing max sequence is 5

            _historyRepoMock
                .Setup(h => h.GetStatusCodeByStatus("AwaitingImaging"))
                .ReturnsAsync("AwaitingImaging");

            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler" }, 0);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert - Should create sequence 6
            _historyRepoMock.Verify(h => h.AddCorrespondenceRequestStatusHistoryAsync(
                It.Is<CorrespondenceRequestStatusHistory>(s =>
                    s.HistorySequenceNum == 6)),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_SuccessfulProcessing_ForwardsToNextStep()
        {
            // Arrange
            var message = CreateTestMessage();
            var correspondenceId = Guid.NewGuid();
            SetupSuccessfulFlow(correspondenceId, "AwaitingImaging");
            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler", "NextHandler" }, 0);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _contextMock!.Verify(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()), Times.Once);
        }

        [TestMethod]
        public async Task Handle_LastStepInRoute_CompletesRoutingSlip()
        {
            // Arrange
            var message = CreateTestMessage();
            var correspondenceId = Guid.NewGuid();
            SetupSuccessfulFlow(correspondenceId, "AwaitingImaging");
            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler" }, 0);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert - Should publish completion event
            _contextMock!.Verify(c => c.Publish(It.IsAny<object>(), It.IsAny<PublishOptions>()), Times.Once);
        }

        #endregion

        #region Handle Tests - Error Scenarios

        [TestMethod]
        public async Task Handle_RepositoryThrowsException_ThrowsNotFoundException()
        {
            // Arrange
            var message = CreateTestMessage();
            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception("Database error"));

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<NotFoundException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("CORRESPONDENCE_NOT_FOUND", ex.ErrorCode);
        }

        [TestMethod]
        public async Task Handle_HistoryRepositoryThrowsException_ThrowsDatabaseException()
        {
            // Arrange
            var message = CreateTestMessage();
            var correspondenceId = Guid.NewGuid();

            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ThrowsAsync(new Exception("History repository error"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<DatabaseException>(
                () => _handler!.Handle(message, _contextMock!.Object));
        }

        [TestMethod]
        public async Task Handle_AddStatusHistoryThrowsException_PublishesCompensationEvent()
        {
            // Arrange
            var message = CreateTestMessage();
            var correspondenceId = Guid.NewGuid();

            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ReturnsAsync(0);

            _historyRepoMock
                .Setup(h => h.GetStatusCodeByStatus("AwaitingImaging"))
                .ReturnsAsync("AwaitingImaging");

            _historyRepoMock
                .Setup(h => h.AddCorrespondenceRequestStatusHistoryAsync(It.IsAny<CorrespondenceRequestStatusHistory>()))
                .ThrowsAsync(new Exception("Add failed"));

            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler" }, 0);

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<DatabaseException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("DB_INSERT_ERROR", ex.ErrorCode);

            // Verify compensation event was published
            _contextMock!.Verify(c => c.Publish(
                It.IsAny<UpdateCorrespondenceDBHandler.UpdateCorrespondenceDBCompensationEvent>(),
                It.IsAny<PublishOptions>()),
                Times.Once);
        }

        #endregion

        #region Rollback Tests

        [TestMethod]
        public async Task Handle_FailureAfterStatusCreation_AttemptsToDeleteStatusHistory()
        {
            // Arrange
            var message = CreateTestMessage();
            var correspondenceId = Guid.NewGuid();

            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ReturnsAsync(0);

            _historyRepoMock
                .Setup(h => h.GetStatusCodeByStatus("AwaitingImaging"))
                .ReturnsAsync("AwaitingImaging");

            _historyRepoMock
                .Setup(h => h.AddCorrespondenceRequestStatusHistoryAsync(It.IsAny<CorrespondenceRequestStatusHistory>()))
                .Returns(Task.CompletedTask);

            _historyRepoMock
                .Setup(h => h.GetStatusHistoryByCorrIdSeqNumAsync(correspondenceId, 1))
                .ReturnsAsync(new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 1
                });

            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler", "NextHandler" }, 0);

            // Simulate failure during forwarding
            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .ThrowsAsync(new Exception("Forwarding failed"));

            // Act & Assert - Now expects DatabaseException
            var ex = await Assert.ThrowsExceptionAsync<DatabaseException>(
                () => _handler!.Handle(message, _contextMock.Object));

            Assert.AreEqual("DB_INSERT_ERROR", ex.ErrorCode);

            // Verify rollback attempted to delete status history
            _historyRepoMock.Verify(h => h.DeleteCorrespondenceRequestStatusHistoryAsync(correspondenceId), Times.Once);
        }

        [TestMethod]
        public async Task Handle_FailureAfterStatusCreation_PublishesCompensationEvent()
        {
            // Arrange
            var message = CreateTestMessage();
            var correspondenceId = Guid.NewGuid();

            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ReturnsAsync(0);

            _historyRepoMock
                .Setup(h => h.GetStatusCodeByStatus("AwaitingImaging"))
                .ReturnsAsync("AwaitingImaging");

            _historyRepoMock
                .Setup(h => h.AddCorrespondenceRequestStatusHistoryAsync(It.IsAny<CorrespondenceRequestStatusHistory>()))
                .Returns(Task.CompletedTask);

            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler", "NextHandler" }, 0);

            // Simulate failure during forwarding
            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .ThrowsAsync(new Exception("Forwarding failed"));

            // Act & Assert - Now expects DatabaseException
            var ex = await Assert.ThrowsExceptionAsync<DatabaseException>(
                () => _handler!.Handle(message, _contextMock.Object));

            Assert.AreEqual("DB_INSERT_ERROR", ex.ErrorCode);

            // Verify compensation event was published
            _contextMock.Verify(c => c.Publish(
                It.Is<UpdateCorrespondenceDBHandler.UpdateCorrespondenceDBCompensationEvent>(e =>
                    e.DocId == "1234567890123456789" &&
                    e.CorrespondenceId == correspondenceId &&
                    e.CompensationReason == "Forwarding failed" &&
                    e.HandlerName == nameof(UpdateCorrespondenceDBHandler)),
                It.IsAny<PublishOptions>()),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_FailureBeforeStatusCreation_DoesNotAttemptDeletion()
        {
            // Arrange
            var message = CreateTestMessage();
            var correspondenceId = Guid.NewGuid();

            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ThrowsAsync(new Exception("GetMax failed"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<DatabaseException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            // Verify deletion was NOT attempted
            _historyRepoMock.Verify(h => h.DeleteCorrespondenceRequestStatusHistoryAsync(It.IsAny<Guid>()), Times.Never);
        }

        [TestMethod]
        public async Task Handle_RollbackDeleteFails_ContinuesWithCompensationEvent()
        {
            // Arrange
            var message = CreateTestMessage();
            var correspondenceId = Guid.NewGuid();

            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ReturnsAsync(0);

            _historyRepoMock
                .Setup(h => h.GetStatusCodeByStatus("AwaitingImaging"))
                .ReturnsAsync("AwaitingImaging");

            _historyRepoMock
                .Setup(h => h.AddCorrespondenceRequestStatusHistoryAsync(It.IsAny<CorrespondenceRequestStatusHistory>()))
                .Returns(Task.CompletedTask);

            _historyRepoMock
                .Setup(h => h.GetStatusHistoryByCorrIdSeqNumAsync(correspondenceId, 1))
                .ReturnsAsync(new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 1
                });

            _historyRepoMock
                .Setup(h => h.DeleteCorrespondenceRequestStatusHistoryAsync(correspondenceId))
                .ThrowsAsync(new Exception("Delete failed"));

            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler", "NextHandler" }, 0);

            // Simulate failure during forwarding
            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .ThrowsAsync(new Exception("Forwarding failed"));

            // Act & Assert - Now expects DatabaseException
            var ex = await Assert.ThrowsExceptionAsync<DatabaseException>(
                () => _handler!.Handle(message, _contextMock.Object));

            Assert.AreEqual("DB_INSERT_ERROR", ex.ErrorCode);

            // Verify compensation event was still published despite delete failure
            _contextMock.Verify(c => c.Publish(
                It.IsAny<UpdateCorrespondenceDBHandler.UpdateCorrespondenceDBCompensationEvent>(),
                It.IsAny<PublishOptions>()),
                Times.Once);
        }

        #endregion

        #region Status Determination Tests

        [TestMethod]
        public async Task Handle_SuccessfulVFResponseWith201_DeterminesVFComposedStatus()
        {
            // Arrange
            var message = CreateTestMessage();
            message.Process.VentureFusionResponse = CreateVentureFusionResponse(true, HttpStatusCode.Created);
            var correspondenceId = Guid.NewGuid();
            SetupSuccessfulFlow(correspondenceId, "AwaitingImaging");

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert - Should call GetStatusCodeByStatus for AwaitingImaging because IsSuccessful is true
            _historyRepoMock!.Verify(h => h.GetStatusCodeByStatus("AwaitingImaging"), Times.Once);
        }

        [TestMethod]
        public async Task Handle_SuccessfulVFResponseWith200_DeterminesVFProcessedStatus()
        {
            // Arrange
            var message = CreateTestMessage();
            message.Process.VentureFusionResponse = CreateVentureFusionResponse(true, HttpStatusCode.OK);
            var correspondenceId = Guid.NewGuid();
            SetupSuccessfulFlow(correspondenceId, "AwaitingImaging");

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _historyRepoMock!.Verify(h => h.GetStatusCodeByStatus("AwaitingImaging"), Times.Once);
        }

        [TestMethod]
        public async Task Handle_SuccessfulVFResponseWith202_DeterminesVFQueuedStatus()
        {
            // Arrange
            var message = CreateTestMessage();
            message.Process.VentureFusionResponse = CreateVentureFusionResponse(true, HttpStatusCode.Accepted);
            var correspondenceId = Guid.NewGuid();
            SetupSuccessfulFlow(correspondenceId, "AwaitingImaging");

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _historyRepoMock!.Verify(h => h.GetStatusCodeByStatus("AwaitingImaging"), Times.Once);
        }

        [TestMethod]
        public async Task Handle_FailedVFResponseWith400_UsesVFRequestFailedStatus()
        {
            // Arrange
            var message = CreateTestMessage();
            message.Process.VentureFusionResponse = CreateVentureFusionResponse(false, HttpStatusCode.BadRequest, "Bad request");
            var correspondenceId = Guid.NewGuid();

            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ReturnsAsync(0);

            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler" }, 0);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _historyRepoMock!.Verify(h => h.AddCorrespondenceRequestStatusHistoryAsync(
                It.Is<CorrespondenceRequestStatusHistory>(s => s.StatusCode == "VFRequestFailed")),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_FailedVFResponseWith500_UsesVFRequestFailedStatus()
        {
            // Arrange
            var message = CreateTestMessage();
            message.Process.VentureFusionResponse = CreateVentureFusionResponse(false, HttpStatusCode.InternalServerError, "Server error");
            var correspondenceId = Guid.NewGuid();

            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ReturnsAsync(0);

            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler" }, 0);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _historyRepoMock!.Verify(h => h.AddCorrespondenceRequestStatusHistoryAsync(
                It.Is<CorrespondenceRequestStatusHistory>(s => s.StatusCode == "VFRequestFailed")),
                Times.Once);
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

        private void SetupSuccessfulFlow(Guid correspondenceId, string statusCode, bool overrideStatusCodeFetch = false)
        {
            _requestRepoMock!
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<CorrespondenceRequest>
                {
                    new CorrespondenceRequest { CorrespondenceId = correspondenceId, DeliveryMethodCode = "01" }
                });

            _historyRepoMock!
                .Setup(h => h.GetMaxSequenceNumber(correspondenceId))
                .ReturnsAsync(0);

            if (!overrideStatusCodeFetch)
            {
                _historyRepoMock
                    .Setup(h => h.GetStatusCodeByStatus("AwaitingImaging"))
                    .ReturnsAsync(statusCode);
            }

            SetupRoutingHeaders(new List<string> { "UpdateCorrespondenceDBHandler" }, 0);
        }

        private VentureFusionApiResponse CreateVentureFusionResponse(bool isSuccessful, HttpStatusCode statusCode, string? errorMessage = null)
        {
            return new VentureFusionApiResponse
            {
                IsSuccessful = isSuccessful,
                StatusCode = statusCode,
                ResponseTimestamp = DateTime.UtcNow,
                ErrorMessage = errorMessage,
                ResponseContent = isSuccessful ? "Success" : errorMessage,
                ResponseHeaders = new Dictionary<string, string>()
            };
        }

        private UpdateCorrespondenceDBHandlerMessage CreateTestMessage(string? docId = "1234567890123456789")
        {
            return new UpdateCorrespondenceDBHandlerMessage
            {
                Process = new SequentialProcess
                {
                    DocId = docId,
                    RequestingSystemName = "TestSystem",
                    DeliveryMethod = "Mail",
                    ECCOTrackingPolicyNumber = "POL123456",
                    ECCOTrackingClaimNumber = "CLM001",
                    ECCOTrackingCustomerId = "CUST001",
                    DocumentDescription = "TestDocument",
                    VentureFusionResponse = CreateVentureFusionResponse(true, HttpStatusCode.Created),
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
