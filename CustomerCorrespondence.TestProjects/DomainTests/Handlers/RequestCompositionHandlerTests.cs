using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using NServiceBus;
using ProcessRequestEndPoint.Handlers;
using System.Net;
using System.Text.Json;
using static ProcessRequestEndPoint.Handlers.RequestCompositionHandler;

namespace CustomerCorrespondenceTests.Handlers
{
    [TestClass]
    public class RequestCompositionHandlerTests
    {
        private Mock<IVentureFusionService>? _ventureFusionServiceMock;
        private Mock<IEncryptionService>? _encryptionServiceMock;
        private Mock<IMessageHandlerContext>? _contextMock;
        private CorrespondenceDBContext? _dbContext;
        private Dictionary<string, string>? _headers;
        private RequestCompositionHandler? _handler;

        [TestInitialize]
        public void Setup()
        {
            _ventureFusionServiceMock = new Mock<IVentureFusionService>();
            _encryptionServiceMock = new Mock<IEncryptionService>();
            _contextMock = new Mock<IMessageHandlerContext>();

            // Create actual in-memory database for tests
            _dbContext = CreateInMemoryDbContext();

            _headers = new Dictionary<string, string>();
            _contextMock.SetupGet(c => c.MessageHeaders).Returns(_headers);
            _contextMock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

            _handler = new RequestCompositionHandler(
                _ventureFusionServiceMock.Object,
                _encryptionServiceMock.Object,
                _dbContext);
        }

        private CorrespondenceDBContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<CorrespondenceDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            return new CorrespondenceDBContext(options);
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_NullVentureFusionService_ThrowsArgumentNullException()
        {
            using var dbContext = CreateInMemoryDbContext();

            Assert.ThrowsException<ArgumentNullException>(() =>
                new RequestCompositionHandler(
                    null!,
                    _encryptionServiceMock!.Object,
                    dbContext));
        }

        [TestMethod]
        public void Constructor_NullEncryptionService_ThrowsArgumentNullException()
        {
            using var dbContext = CreateInMemoryDbContext();

            Assert.ThrowsException<ArgumentNullException>(() =>
                new RequestCompositionHandler(
                    _ventureFusionServiceMock!.Object,
                    null!,
                    dbContext));
        }

        [TestMethod]
        public void Constructor_NullDbContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new RequestCompositionHandler(
                    _ventureFusionServiceMock!.Object,
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

        #endregion

        #region Handle Tests - Success Scenarios

        [TestMethod]
        public async Task Handle_SuccessfulComposition_MergesMailDeliveryInfo()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);
            SetupSuccessfulVentureFusionResponse();

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            Assert.IsNotNull(message.Process.AdditionalData);
            Assert.IsTrue(message.Process.AdditionalData.ContainsKey("MailFlag"));
            Assert.IsTrue(message.Process.AdditionalData.ContainsKey("ImageFlag"));
            Assert.IsTrue(message.Process.AdditionalData.ContainsKey("MailDeliveryInfo"));
        }

        [TestMethod]
        public async Task Handle_MailDeliveryMethod_SetsMailFlagToY()
        {
            // Arrange
            var message = CreateTestMessage(deliveryMethod: "Mail");
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);
            SetupSuccessfulVentureFusionResponse();

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            Assert.AreEqual("Y", message.Process.AdditionalData!["MailFlag"]);
            Assert.AreEqual("Y", message.Process.AdditionalData["ImageFlag"]);
        }

        [TestMethod]
        public async Task Handle_InvalidDeliveryMethod_ThrowsValidationException()
        {
            // Arrange
            var message = CreateTestMessage(deliveryMethod: "Email"); // Invalid delivery method
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);
            SetupSuccessfulVentureFusionResponse();

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<ValidationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("VALIDATION_ERROR", ex.ErrorCode);
            Assert.IsTrue(ex.ValidationErrors.ContainsKey("DeliveryMethod"));
        }

        [TestMethod]
        public async Task Handle_SuccessfulComposition_CallsVentureFusionService()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);
            SetupSuccessfulVentureFusionResponse();

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _ventureFusionServiceMock!.Verify(
                s => s.PostDataRecords(It.Is<SequentialProcess>(p => p.DocId == "1234567890123456789")),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_SuccessfulComposition_AttachesResponseToMessage()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);
            SetupSuccessfulVentureFusionResponse();

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            Assert.IsNotNull(message.Process.VentureFusionResponse);
            Assert.IsTrue(message.Process.VentureFusionResponse.IsSuccessful);
            Assert.AreEqual(HttpStatusCode.Created, message.Process.VentureFusionResponse.StatusCode);
        }

        [TestMethod]
        public async Task Handle_SuccessfulComposition_ForwardsToNextStep()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);
            SetupSuccessfulVentureFusionResponse();

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            _contextMock!.Verify(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()), Times.Once);
        }

        [TestMethod]
        public async Task Handle_VersionCodePresent_AddsToAdditionalData()
        {
            // Arrange
            var message = CreateTestMessage();
            message.Process.CompositionInfo.VersionCode = "V2.0";
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);
            SetupSuccessfulVentureFusionResponse();

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            Assert.IsTrue(message.Process.AdditionalData!.ContainsKey("VersionCode"));
            Assert.AreEqual("V2.0", message.Process.AdditionalData["VersionCode"]);
        }

        [TestMethod]
        public async Task Handle_ResponseHeaders_CapturedInResponse()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Add("X-Transaction-Id", "TRANS-123");

            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            Assert.IsNotNull(message.Process.VentureFusionResponse!.ResponseHeaders);
            Assert.IsTrue(message.Process.VentureFusionResponse.ResponseHeaders.ContainsKey("X-Transaction-Id"));
        }

        #endregion

        #region Handle Tests - Error Scenarios

        [TestMethod]
        public async Task Handle_VentureFusionReturnsNullResponse_ThrowsCommunicationException()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync((HttpResponseMessage?)null);

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<CommunicationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("VF_NO_RESPONSE", ex.ErrorCode);
            Assert.AreEqual("VentureFusion", ex.ServiceName);

            // Verify compensation event was NOT published
            _contextMock!.Verify(c => c.Publish(
                It.IsAny<RequestCompositionHandler.CompositionRequestCompensationEvent>(),
                It.IsAny<PublishOptions>()),
                Times.Never);
        }

        [TestMethod]
        public async Task Handle_VentureFusionReturnsError_ThrowsCommunicationException()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.BadRequest, "Invalid request data");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<CommunicationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("VF_REQUEST_FAILED", ex.ErrorCode);
            Assert.AreEqual(400, ex.HttpStatusCode);

            // Verify compensation event was published
            _contextMock!.Verify(c => c.Publish(
                It.IsAny<RequestCompositionHandler.CompositionRequestCompensationEvent>(),
                It.IsAny<PublishOptions>()),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_VentureFusionServiceThrowsException_ThrowsCommunicationException()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<CommunicationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("VF_COMMUNICATION_ERROR", ex.ErrorCode);
        }

        [TestMethod]
        public async Task Handle_VentureFusionReturns500_ThrowsCommunicationException()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.InternalServerError, "Server error");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<CommunicationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("VF_REQUEST_FAILED", ex.ErrorCode);
            Assert.AreEqual(500, ex.HttpStatusCode);

            // Verify compensation event was published
            _contextMock!.Verify(c => c.Publish(
                It.IsAny<RequestCompositionHandler.CompositionRequestCompensationEvent>(),
                It.IsAny<PublishOptions>()),
                Times.Once);
        }

        #endregion

        #region Rollback Tests

        [TestMethod]
        public async Task Handle_FailureAfterMerge_RestoresOriginalAdditionalData()
        {
            // Arrange
            var message = CreateTestMessage();
            var originalAdditionalData = new Dictionary<string, object> { { "OriginalKey", "OriginalValue" } };
            message.Process.AdditionalData = new Dictionary<string, object>(originalAdditionalData);

            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ThrowsAsync(new Exception("VF Service failed"));

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<CommunicationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            // Verify original AdditionalData was restored
            Assert.IsNotNull(message.Process.AdditionalData);
            Assert.AreEqual(1, message.Process.AdditionalData.Count);
            Assert.IsTrue(message.Process.AdditionalData.ContainsKey("OriginalKey"));
            Assert.AreEqual("OriginalValue", message.Process.AdditionalData["OriginalKey"]);
        }

        [TestMethod]
        public async Task Handle_FailureAfterResponse_RestoresOriginalVentureFusionResponse()
        {
            // Arrange
            var message = CreateTestMessage();
            var originalResponse = new VentureFusionApiResponse
            {
                StatusCode = HttpStatusCode.OK,
                IsSuccessful = true,
                ResponseContent = "Original response"
            };
            message.Process.VentureFusionResponse = originalResponse;

            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var newResponse = CreateHttpResponseMessage(HttpStatusCode.Created, "New response");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(newResponse);

            // Simulate failure during forwarding
            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .ThrowsAsync(new Exception("Forwarding failed"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(
                () => _handler!.Handle(message, _contextMock.Object));

            // Verify original response was restored
            Assert.IsNotNull(message.Process.VentureFusionResponse);
            Assert.AreEqual(originalResponse.StatusCode, message.Process.VentureFusionResponse.StatusCode);
            Assert.AreEqual(originalResponse.ResponseContent, message.Process.VentureFusionResponse.ResponseContent);
        }

        [TestMethod]
        public async Task Handle_FailureAfterCompositionSent_PublishesCompensationEvent()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.Created);
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Simulate failure during forwarding
            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .ThrowsAsync(new Exception("Forwarding failed"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(
                () => _handler!.Handle(message, _contextMock.Object));

            // Verify compensation event was published
            _contextMock.Verify(c => c.Publish(
                It.Is<RequestCompositionHandler.CompositionRequestCompensationEvent>(e =>
                    e.DocId == "1234567890123456789" &&
                    e.WasRequestSentToVentureFusion == true &&
                    e.WasResponseProcessed == true &&
                    e.CompensationReason == "Forwarding failed" &&
                    e.HandlerName == nameof(RequestCompositionHandler)),
                It.IsAny<PublishOptions>()),
                Times.Once);
        }

        [TestMethod]
        public async Task Handle_FailureBeforeCompositionSent_DoesNotPublishCompensationEvent()
        {
            // Arrange
            var message = CreateTestMessage();
            message.Process.DeliveryMethod = "InvalidMethod"; // This will cause validation failure
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ValidationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            // Verify compensation event was NOT published
            _contextMock!.Verify(c => c.Publish(
                It.IsAny<RequestCompositionHandler.CompositionRequestCompensationEvent>(),
                It.IsAny<PublishOptions>()),
                Times.Never);
        }

        [TestMethod]
        public async Task Handle_RollbackPublishFails_ContinuesWithRollback()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.Created);
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Simulate failure during forwarding
            _contextMock!
                .Setup(c => c.Send(It.IsAny<object>(), It.IsAny<SendOptions>()))
                .ThrowsAsync(new Exception("Forwarding failed"));

            // Simulate failure during compensation event publish
            _contextMock
                .Setup(c => c.Publish(
                    It.IsAny<RequestCompositionHandler.CompositionRequestCompensationEvent>(),
                    It.IsAny<PublishOptions>()))
                .ThrowsAsync(new Exception("Publish failed"));

            // Act & Assert - Should still throw original exception
            await Assert.ThrowsExceptionAsync<Exception>(
                () => _handler!.Handle(message, _contextMock.Object));

            // Verify compensation event publish was attempted
            _contextMock.Verify(c => c.Publish(
                It.IsAny<RequestCompositionHandler.CompositionRequestCompensationEvent>(),
                It.IsAny<PublishOptions>()),
                Times.Once);
        }

        #endregion

        #region MarkRequestAsArchiveIncompleteAsync Tests

        [TestMethod]
        public async Task MarkRequestAsArchiveIncomplete_ValidDocId_MarksRequestAsFailed()
        {
            // Arrange
            _dbContext = CreateInMemoryDbContext();
            var handler = new RequestCompositionHandler(
                _ventureFusionServiceMock!.Object,
                _encryptionServiceMock!.Object,
                _dbContext);

            var correspondenceId = Guid.NewGuid();
            var docId = "TEST123456789";

            // Add test data to database
            var correspondenceRequest = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = docId,
                DeliveryMethodCode = "Mail",
                RequestCreationTS = DateTime.UtcNow
            };

            var failedStatusCode = new CorrespondenceStatusCode
            {
                StatusId = Guid.NewGuid(),
                StatusCode = "08",
                StatusDescription = "Failed"
            };

            _dbContext.CorrespondenceRequests.Add(correspondenceRequest);
            _dbContext.CorrespondenceStatusCodes.Add(failedStatusCode);
            await _dbContext.SaveChangesAsync();

            // Setup message that will trigger rollback
            var message = CreateTestMessage(docId: docId);
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.BadRequest, "Error from VF");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act & Assert - This will trigger rollback which calls MarkRequestAsArchiveIncompleteAsync
            await Assert.ThrowsExceptionAsync<CommunicationException>(
                () => handler.Handle(message, _contextMock!.Object));

            // Assert - Verify status history was created with "Failed" status
            var statusHistory = await _dbContext.CorrespondenceRequestStatusHistories
                .FirstOrDefaultAsync(h => h.CorrespondenceId == correspondenceId);

            Assert.IsNotNull(statusHistory, "Status history should be created");
            Assert.AreEqual("08", statusHistory.StatusCode);
            Assert.AreEqual("VentureFusionCompensation", statusHistory.StatusUpdatedBy);
            Assert.AreEqual(1, statusHistory.HistorySequenceNum);
        }

        [TestMethod]
        public async Task MarkRequestAsArchiveIncomplete_NullDocId_DoesNotUpdateDatabase()
        {
            // Arrange
            _dbContext = CreateInMemoryDbContext();
            var handler = new RequestCompositionHandler(
                _ventureFusionServiceMock!.Object,
                _encryptionServiceMock!.Object,
                _dbContext);

            var message = CreateTestMessage(docId: null);
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.BadRequest, "Error");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ValidationException>(
                () => handler.Handle(message, _contextMock!.Object));

            // Assert - No status history should be created
            var statusHistoryCount = await _dbContext.CorrespondenceRequestStatusHistories.CountAsync();
            Assert.AreEqual(0, statusHistoryCount, "No status history should be created for null DocId");
        }

        [TestMethod]
        public async Task MarkRequestAsArchiveIncomplete_EmptyDocId_DoesNotUpdateDatabase()
        {
            // Arrange
            _dbContext = CreateInMemoryDbContext();
            var handler = new RequestCompositionHandler(
                _ventureFusionServiceMock!.Object,
                _encryptionServiceMock!.Object,
                _dbContext);

            var message = CreateTestMessage(docId: "");

            // Act & Assert - Should fail validation before reaching rollback
            await Assert.ThrowsExceptionAsync<ValidationException>(
                () => handler.Handle(message, _contextMock!.Object));

            // Assert - No status history should be created
            var statusHistoryCount = await _dbContext.CorrespondenceRequestStatusHistories.CountAsync();
            Assert.AreEqual(0, statusHistoryCount, "No status history should be created for empty DocId");
        }

        [TestMethod]
        public async Task MarkRequestAsArchiveIncomplete_DocIdNotInDatabase_DoesNotCreateStatusHistory()
        {
            // Arrange
            _dbContext = CreateInMemoryDbContext();
            var handler = new RequestCompositionHandler(
                _ventureFusionServiceMock!.Object,
                _encryptionServiceMock!.Object,
                _dbContext);

            var docId = "NONEXISTENT123";
            var message = CreateTestMessage(docId: docId);
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.BadRequest, "Error");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<CommunicationException>(
                () => handler.Handle(message, _contextMock!.Object));

            // Assert - No status history should be created for non-existent DocId
            var statusHistoryCount = await _dbContext.CorrespondenceRequestStatusHistories.CountAsync();
            Assert.AreEqual(0, statusHistoryCount, "No status history should be created for non-existent DocId");
        }

        [TestMethod]
        public async Task MarkRequestAsArchiveIncomplete_AlreadyMarkedAsFailed_DoesNotCreateDuplicate()
        {
            // Arrange
            _dbContext = CreateInMemoryDbContext();
            var handler = new RequestCompositionHandler(
                _ventureFusionServiceMock!.Object,
                _encryptionServiceMock!.Object,
                _dbContext);

            var correspondenceId = Guid.NewGuid();
            var docId = "TEST123456789";

            // Add test data with existing Failed status
            var correspondenceRequest = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = docId,
                DeliveryMethodCode = "Mail",
                RequestCreationTS = DateTime.UtcNow
            };

            var existingStatusHistory = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = correspondenceId,
                HistorySequenceNum = 1,
                StatusCode = "08",
                StatusUpdatedBy = "PreviousProcess",
                StatusUpdateTS = DateTime.UtcNow.AddMinutes(-5)
            };

            _dbContext.CorrespondenceRequests.Add(correspondenceRequest);
            _dbContext.CorrespondenceRequestStatusHistories.Add(existingStatusHistory);
            await _dbContext.SaveChangesAsync();

            // Setup message that will trigger rollback
            var message = CreateTestMessage(docId: docId);
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.BadRequest, "Error");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<CommunicationException>(
                () => handler.Handle(message, _contextMock!.Object));

            // Assert - Should still have 1 status history record (no duplicate)
            var statusHistoryCount = await _dbContext.CorrespondenceRequestStatusHistories
                .CountAsync(h => h.CorrespondenceId == correspondenceId);
            Assert.AreEqual(1, statusHistoryCount, "Should not create duplicate Failed status history");
        }

        [TestMethod]
        public async Task MarkRequestAsArchiveIncomplete_WithExistingStatusHistory_IncrementsSequenceNumber()
        {
            // Arrange
            _dbContext = CreateInMemoryDbContext();
            var handler = new RequestCompositionHandler(
                _ventureFusionServiceMock!.Object,
                _encryptionServiceMock!.Object,
                _dbContext);

            var correspondenceId = Guid.NewGuid();
            var docId = "TEST123456789";

            // Add test data with existing status history
            var correspondenceRequest = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = docId,
                DeliveryMethodCode = "Mail",
                RequestCreationTS = DateTime.UtcNow
            };

            var existingStatusHistory = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = correspondenceId,
                HistorySequenceNum = 1,
                StatusCode = "Submitted",
                StatusUpdatedBy = "InitialSubmission",
                StatusUpdateTS = DateTime.UtcNow.AddMinutes(-10)
            };

            var failedStatusCode = new CorrespondenceStatusCode
            {
                StatusId = Guid.NewGuid(),
                StatusCode = "08",
                StatusDescription = "Failed"
            };

            _dbContext.CorrespondenceRequests.Add(correspondenceRequest);
            _dbContext.CorrespondenceRequestStatusHistories.Add(existingStatusHistory);
            _dbContext.CorrespondenceStatusCodes.Add(failedStatusCode);
            await _dbContext.SaveChangesAsync();

            // Setup message that will trigger rollback
            var message = CreateTestMessage(docId: docId);
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.BadRequest, "Error");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<CommunicationException>(
                () => handler.Handle(message, _contextMock!.Object));

            // Assert - Should have 2 status history records
            var statusHistoryCount = await _dbContext.CorrespondenceRequestStatusHistories
                .CountAsync(h => h.CorrespondenceId == correspondenceId);
            Assert.AreEqual(2, statusHistoryCount, "Should have 2 status history records");

            var newStatusHistory = await _dbContext.CorrespondenceRequestStatusHistories
                .FirstAsync(h => h.CorrespondenceId == correspondenceId && h.StatusCode == "08");
            Assert.AreEqual(2, newStatusHistory.HistorySequenceNum, "New status should have sequence number 2");
            Assert.AreEqual("VentureFusionCompensation", newStatusHistory.StatusUpdatedBy);
        }

        [TestMethod]
        public async Task MarkRequestAsArchiveIncomplete_StatusCodeNotInDatabase_UsesDefaultValue()
        {
            // Arrange
            _dbContext = CreateInMemoryDbContext();
            var handler = new RequestCompositionHandler(
                _ventureFusionServiceMock!.Object,
                _encryptionServiceMock!.Object,
                _dbContext);

            var correspondenceId = Guid.NewGuid();
            var docId = "TEST123456789";

            // Add test data WITHOUT adding the Failed status code to database
            var correspondenceRequest = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = docId,
                DeliveryMethodCode = "Mail",
                RequestCreationTS = DateTime.UtcNow
            };

            _dbContext.CorrespondenceRequests.Add(correspondenceRequest);
            await _dbContext.SaveChangesAsync();

            // Setup message that will trigger rollback
            var message = CreateTestMessage(docId: docId);
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.BadRequest, "Error");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<CommunicationException>(
                () => handler.Handle(message, _contextMock!.Object));

            // Assert - Should use default "Failed" status code
            var statusHistory = await _dbContext.CorrespondenceRequestStatusHistories
                .FirstOrDefaultAsync(h => h.CorrespondenceId == correspondenceId);

            Assert.IsNotNull(statusHistory, "Status history should be created");
            Assert.AreEqual("08", statusHistory.StatusCode, "Should use default 'Failed' status code");
        }

        #endregion

        #region Response Processing Tests

        [TestMethod]
        public async Task Handle_VentureFusionReturns201_MarksAsSuccessful()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.Created, "Resource created");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            Assert.IsNotNull(message.Process.VentureFusionResponse);
            Assert.IsTrue(message.Process.VentureFusionResponse.IsSuccessful);
            Assert.AreEqual(HttpStatusCode.Created, message.Process.VentureFusionResponse.StatusCode);
            Assert.AreEqual("Resource created", message.Process.VentureFusionResponse.ResponseContent);
        }

        [TestMethod]
        public async Task Handle_VentureFusionReturns200_MarksAsSuccessful()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.OK, "Success");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act
            await _handler!.Handle(message, _contextMock!.Object);

            // Assert
            Assert.IsNotNull(message.Process.VentureFusionResponse);
            Assert.IsTrue(message.Process.VentureFusionResponse.IsSuccessful);
            Assert.AreEqual(HttpStatusCode.OK, message.Process.VentureFusionResponse.StatusCode);
        }

        [TestMethod]
        public async Task Handle_VentureFusionReturns404_ThrowsCommunicationException()
        {
            // Arrange
            var message = CreateTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);

            var response = CreateHttpResponseMessage(HttpStatusCode.NotFound, "Not found");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);

            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<CommunicationException>(
                () => _handler!.Handle(message, _contextMock!.Object));

            Assert.AreEqual("VF_REQUEST_FAILED", ex.ErrorCode);
            Assert.AreEqual(404, ex.HttpStatusCode);

            // Verify compensation event was published
            _contextMock!.Verify(c => c.Publish(
                It.IsAny<RequestCompositionHandler.CompositionRequestCompensationEvent>(),
                It.IsAny<PublishOptions>()),
                Times.Once);
        }

        #endregion

        #region ePresent Delivery Tests

        [TestMethod]
        public async Task Handle_EPresentDelivery_SetsCorrectFlags()
        {
            var message = CreateEPresentTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);
            SetupSuccessfulVentureFusionResponse();

            await _handler!.Handle(message, _contextMock!.Object);

            Assert.AreEqual("N", message.Process.AdditionalData!["MailFlag"]);
            Assert.AreEqual("Y", message.Process.AdditionalData["ImageFlag"]);
            Assert.AreEqual("Y", message.Process.AdditionalData["EPresentFlag"]);
        }

        [TestMethod]
        public async Task Handle_EPresentDelivery_AddsJourneyDeliveryInfo()
        {
            var message = CreateEPresentTestMessage();
            SetupRoutingHeaders(new List<string> { "RequestCompositionHandler", "NextHandler" }, 0);
            SetupSuccessfulVentureFusionResponse();

            await _handler!.Handle(message, _contextMock!.Object);

            Assert.IsTrue(message.Process.AdditionalData!.ContainsKey("JourneyDeliveryInfo"));
            var journeyInfo = message.Process.AdditionalData["JourneyDeliveryInfo"] as Dictionary<string, object>;
            Assert.IsNotNull(journeyInfo);
            Assert.AreEqual(1449241, journeyInfo["AccountId"]);
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

        private void SetupSuccessfulVentureFusionResponse()
        {
            var response = CreateHttpResponseMessage(HttpStatusCode.Created, "Success");
            _ventureFusionServiceMock!
                .Setup(s => s.PostDataRecords(It.IsAny<SequentialProcess>()))
                .ReturnsAsync(response);
        }

        private HttpResponseMessage CreateHttpResponseMessage(HttpStatusCode statusCode, string? content = null)
        {
            var response = new HttpResponseMessage(statusCode);
            if (content != null)
            {
                response.Content = new StringContent(content);
            }
            return response;
        }

        private RequestCompositionHandlerMessage CreateTestMessage(
            string? docId = "1234567890123456789",
            string? deliveryMethod = "Mail")
        {
            return new RequestCompositionHandlerMessage
            {
                Process = new SequentialProcess
                {
                    DocId = docId,
                    RequestingSystemName = "TestSystem",
                    DeliveryMethod = deliveryMethod ?? "Mail",
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

        private RequestCompositionHandlerMessage CreateEPresentTestMessage()
        {
            var message = CreateTestMessage(deliveryMethod: "ePresent");
            message.Process.JourneyDeliveryInfo = new JourneyDeliveryInfo
            {
                AccountId = 1449241,
                MessageBody = new MessageBody
                {
                    ContactKey = "SH900",
                    EventDefinitionKey = "APIEvent-6326d8ca-24b8-4fd4-53b9-c02c8743ef59",
                    Data = new MessageData
                    {
                        SubscriberKey = "SH900",
                        EmailAddress = "test@example.com",
                        SourceSystem = "POSTMAN",
                        FirstName = "John"
                    }
                }
            };
            return message;
        }

        [TestCleanup]
        public void Cleanup()
        {
            _dbContext?.Dispose();
        }

        #endregion
    }
}