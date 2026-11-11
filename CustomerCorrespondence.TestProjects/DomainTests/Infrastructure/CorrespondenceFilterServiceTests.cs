using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.DTOs;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Persistence;
using CustomerCorrespondence.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace CustomerCorrespondenceTests.Infrastructure
{
    [TestClass]
    public class CorrespondenceFilterServiceTests
    {
        private CorrespondenceDBContext _context;
        private Mock<IEncryptionService> _encryptionServiceMock;
        private Mock<ICorrespondenceRequestStatusHistoryRepository> _statusHistoryRepoMock;
        private Mock<ILogger<CorrespondenceFilterService>> _loggerMock;
        private CorrespondenceFilterService _service;

        [TestInitialize]
        public void Setup()
        {
            var options = new DbContextOptionsBuilder<CorrespondenceDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new CorrespondenceDBContext(options);
            _encryptionServiceMock = new Mock<IEncryptionService>();
            _statusHistoryRepoMock = new Mock<ICorrespondenceRequestStatusHistoryRepository>();
            _loggerMock = new Mock<ILogger<CorrespondenceFilterService>>();

            _service = new CorrespondenceFilterService(
                _context,
                _encryptionServiceMock.Object,
                _statusHistoryRepoMock.Object,
                _loggerMock.Object
            );
        }

        [TestCleanup]
        public void Cleanup()
        {
            _context?.Dispose();
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WithValidRequest_ReturnsFilteredResults()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "Payment Guard Advantage",
                DocumentDescription = "PGA Welcome Kit",
                DeliveryMethod = "Mail",
                CorrespondenceRequestStatusCode = "04",
                CreateBeginDate = "01/01/2026",
                CreateEndDate = "02/26/2026"
            };

            var correspondenceId = Guid.NewGuid();
            
            var deliveryMethod = new CorrespondenceDeliveryMethod
            {
                DeliveryMethodCode = "MAIL",
                DeliveryMethodName = "Mail"
            };

            var correspondenceRequest = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = "0000000821531847833",
                RequestingDomainName = "Payment Guard Advantage",
                ArchiveDocumentTypeCode = "PGA Welcome Kit",
                DeliveryMethodCode = "MAIL",
                RequestCreationTS = new DateTime(2026, 1, 12, 17, 19, 22)
            };

            var statusHistory = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceId = correspondenceId,
                StatusCode = "04",
                StatusUpdateTS = new DateTime(2026, 1, 12, 17, 20, 44),
                HistorySequenceNum = 2,
                StatusUpdatedBy = "TestUser"
            };

            _context.CorrespondenceDeliveryMethods.Add(deliveryMethod);
            _context.CorrespondenceRequests.Add(correspondenceRequest);
            _context.CorrespondenceRequestStatusHistories.Add(statusHistory);
            await _context.SaveChangesAsync();

            _statusHistoryRepoMock.Setup(r => r.GetStatusDescriptionByStatusCode("04"))
                .ReturnsAsync("Completed");

            // Act
            var result = await _service.GetFilteredCorrespondenceAsync(request);

            // Assert
            Assert.IsNotNull(result);
            var resultList = result.ToList();
            Assert.AreEqual(1, resultList.Count);
            var firstResult = resultList.First();
            Assert.AreEqual("0000000821531847833", firstResult.DocId);
            Assert.AreEqual(correspondenceId, firstResult.CorrespondenceId);
            Assert.AreEqual("Mail", firstResult.DeliveryMethodName);
            Assert.AreEqual("04", firstResult.CurrentStatusCode);
            Assert.AreEqual("Completed", firstResult.CurrentStatusDescription);
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WithOnlyMandatoryFields_ReturnsResults()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "Payment Guard Advantage",
                CreateBeginDate = "01/01/2026"
            };

            var correspondenceId = Guid.NewGuid();

            var deliveryMethod = new CorrespondenceDeliveryMethod
            {
                DeliveryMethodCode = "MAIL",
                DeliveryMethodName = "Mail"
            };

            var correspondenceRequest = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = "0000000821531847833",
                RequestingDomainName = "Payment Guard Advantage",
                ArchiveDocumentTypeCode = "PGA Welcome Kit",
                DeliveryMethodCode = "MAIL",
                RequestCreationTS = new DateTime(2026, 1, 12, 17, 19, 22)
            };

            var statusHistory = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceId = correspondenceId,
                StatusCode = "04",
                StatusUpdateTS = new DateTime(2026, 1, 12, 17, 20, 44),
                HistorySequenceNum = 2,
                StatusUpdatedBy = "TestUser"
            };

            _context.CorrespondenceDeliveryMethods.Add(deliveryMethod);
            _context.CorrespondenceRequests.Add(correspondenceRequest);
            _context.CorrespondenceRequestStatusHistories.Add(statusHistory);
            await _context.SaveChangesAsync();

            _statusHistoryRepoMock.Setup(r => r.GetStatusDescriptionByStatusCode("04"))
                .ReturnsAsync("Completed");

            // Act
            var result = await _service.GetFilteredCorrespondenceAsync(request);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count());
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WithFutureBeginDate_ThrowsArgumentException()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "Payment Guard Advantage",
                CreateBeginDate = DateTime.UtcNow.AddDays(1).ToString("MM/dd/yyyy")
            };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.GetFilteredCorrespondenceAsync(request));
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WithNullRequest_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _service.GetFilteredCorrespondenceAsync(null));
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WithInvalidBeginDateFormat_ThrowsArgumentException()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "Payment Guard Advantage",
                CreateBeginDate = "invalid-date"
            };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.GetFilteredCorrespondenceAsync(request));
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WithInvalidEndDateFormat_ThrowsArgumentException()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "Payment Guard Advantage",
                CreateBeginDate = "01/01/2026",
                CreateEndDate = "invalid-date"
            };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.GetFilteredCorrespondenceAsync(request));
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WithNoMatchingResults_ReturnsEmptyList()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "NonExistent System",
                CreateBeginDate = "01/01/2026",
                CreateEndDate = "02/26/2026"
            };

            // Act
            var result = await _service.GetFilteredCorrespondenceAsync(request);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count());
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WithPolicyNumber_HashesPolicyNumber()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "Payment Guard Advantage",
                CreateBeginDate = "01/01/2026",
                CreateEndDate = "02/26/2026",
                PolicyNumber = "PN12345678"
            };

            var policyNumberHash = new byte[] { 1, 2, 3, 4 };
            _encryptionServiceMock.Setup(e => e.GenerateSearchHash("PN12345678"))
                .Returns(policyNumberHash);

            var correspondenceId = Guid.NewGuid();

            var deliveryMethod = new CorrespondenceDeliveryMethod
            {
                DeliveryMethodCode = "MAIL",
                DeliveryMethodName = "Mail"
            };

            var correspondenceRequest = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = "0000000821531847833",
                RequestingDomainName = "Payment Guard Advantage",
                ArchiveDocumentTypeCode = "PGA Welcome Kit",
                DeliveryMethodCode = "MAIL",
                RequestCreationTS = new DateTime(2026, 1, 12, 17, 19, 22),
                PolicyNumberHash = policyNumberHash
            };

            var statusHistory = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceId = correspondenceId,
                StatusCode = "04",
                StatusUpdateTS = new DateTime(2026, 1, 12, 17, 20, 44),
                HistorySequenceNum = 2,
                StatusUpdatedBy = "TestUser"
            };

            _context.CorrespondenceDeliveryMethods.Add(deliveryMethod);
            _context.CorrespondenceRequests.Add(correspondenceRequest);
            _context.CorrespondenceRequestStatusHistories.Add(statusHistory);
            await _context.SaveChangesAsync();

            _statusHistoryRepoMock.Setup(r => r.GetStatusDescriptionByStatusCode("04"))
                .ReturnsAsync("Completed");

            // Act
            var result = await _service.GetFilteredCorrespondenceAsync(request);

            // Assert
            _encryptionServiceMock.Verify(e => e.GenerateSearchHash("PN12345678"), Times.Once);
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count());
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WithoutEndDate_UsesTodayAsEndDate()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "Payment Guard Advantage",
                CreateBeginDate = "01/01/2026"
            };

            var correspondenceId = Guid.NewGuid();

            var deliveryMethod = new CorrespondenceDeliveryMethod
            {
                DeliveryMethodCode = "MAIL",
                DeliveryMethodName = "Mail"
            };

            var correspondenceRequest = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = "0000000821531847833",
                RequestingDomainName = "Payment Guard Advantage",
                ArchiveDocumentTypeCode = "PGA Welcome Kit",
                DeliveryMethodCode = "MAIL",
                RequestCreationTS = DateTime.UtcNow.AddHours(-1)
            };

            var statusHistory = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceId = correspondenceId,
                StatusCode = "04",
                StatusUpdateTS = DateTime.UtcNow.AddHours(-1),
                HistorySequenceNum = 2,
                StatusUpdatedBy = "TestUser"
            };

            _context.CorrespondenceDeliveryMethods.Add(deliveryMethod);
            _context.CorrespondenceRequests.Add(correspondenceRequest);
            _context.CorrespondenceRequestStatusHistories.Add(statusHistory);
            await _context.SaveChangesAsync();

            _statusHistoryRepoMock.Setup(r => r.GetStatusDescriptionByStatusCode("04"))
                .ReturnsAsync("Completed");

            // Act
            var result = await _service.GetFilteredCorrespondenceAsync(request);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count());
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WithOptionalFieldsNotProvided_DoesNotFilterByThem()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "Payment Guard Advantage",
                CreateBeginDate = "01/01/2026",
                CreateEndDate = "02/26/2026"
            };

            var correspondenceId = Guid.NewGuid();

            var deliveryMethod = new CorrespondenceDeliveryMethod
            {
                DeliveryMethodCode = "EMAIL",
                DeliveryMethodName = "Email"
            };

            var correspondenceRequest = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = "0000000821531847833",
                RequestingDomainName = "Payment Guard Advantage",
                ArchiveDocumentTypeCode = "Different Document",
                DeliveryMethodCode = "EMAIL",
                RequestCreationTS = new DateTime(2026, 1, 12, 17, 19, 22)
            };

            var statusHistory = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceId = correspondenceId,
                StatusCode = "01",
                StatusUpdateTS = new DateTime(2026, 1, 12, 17, 20, 44),
                HistorySequenceNum = 2,
                StatusUpdatedBy = "TestUser"
            };

            _context.CorrespondenceDeliveryMethods.Add(deliveryMethod);
            _context.CorrespondenceRequests.Add(correspondenceRequest);
            _context.CorrespondenceRequestStatusHistories.Add(statusHistory);
            await _context.SaveChangesAsync();

            _statusHistoryRepoMock.Setup(r => r.GetStatusDescriptionByStatusCode("01"))
                .ReturnsAsync("Pending");

            // Act
            var result = await _service.GetFilteredCorrespondenceAsync(request);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count());
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WithMultipleResults_ReturnsAllResults()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "Payment Guard Advantage",
                CreateBeginDate = "01/01/2026",
                CreateEndDate = "02/26/2026"
            };

            var correspondenceId1 = Guid.NewGuid();
            var correspondenceId2 = Guid.NewGuid();

            var deliveryMethod = new CorrespondenceDeliveryMethod
            {
                DeliveryMethodCode = "MAIL",
                DeliveryMethodName = "Mail"
            };

            var correspondenceRequest1 = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId1,
                DocId = "0000000821531847833",
                RequestingDomainName = "Payment Guard Advantage",
                ArchiveDocumentTypeCode = "PGA Welcome Kit",
                DeliveryMethodCode = "MAIL",
                RequestCreationTS = new DateTime(2026, 1, 12, 17, 19, 22)
            };

            var correspondenceRequest2 = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId2,
                DocId = "0000000821531847834",
                RequestingDomainName = "Payment Guard Advantage",
                ArchiveDocumentTypeCode = "PGA Welcome Kit",
                DeliveryMethodCode = "MAIL",
                RequestCreationTS = new DateTime(2026, 2, 15, 10, 30, 45)
            };

            var statusHistory1 = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = correspondenceId1,
                StatusCode = "04",
                StatusUpdateTS = new DateTime(2026, 1, 12, 17, 20, 44),
                HistorySequenceNum = 2,
                StatusUpdatedBy = "TestUser"
            };

            var statusHistory2 = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = correspondenceId2,
                StatusCode = "04",
                StatusUpdateTS = new DateTime(2026, 2, 15, 10, 31, 00),
                HistorySequenceNum = 2,
                StatusUpdatedBy = "TestUser"
            };

            _context.CorrespondenceDeliveryMethods.Add(deliveryMethod);
            _context.CorrespondenceRequests.AddRange(correspondenceRequest1, correspondenceRequest2);
            _context.CorrespondenceRequestStatusHistories.AddRange(statusHistory1, statusHistory2);
            await _context.SaveChangesAsync();

            _statusHistoryRepoMock.Setup(r => r.GetStatusDescriptionByStatusCode("04"))
                .ReturnsAsync("Completed");

            // Act
            var result = await _service.GetFilteredCorrespondenceAsync(request);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count());
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_WhenStatusDescriptionFails_UsesFallback()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "Payment Guard Advantage",
                CreateBeginDate = "01/01/2026",
                CreateEndDate = "02/26/2026"
            };

            var correspondenceId = Guid.NewGuid();

            var deliveryMethod = new CorrespondenceDeliveryMethod
            {
                DeliveryMethodCode = "MAIL",
                DeliveryMethodName = "Mail"
            };

            var correspondenceRequest = new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = "0000000821531847833",
                RequestingDomainName = "Payment Guard Advantage",
                ArchiveDocumentTypeCode = "PGA Welcome Kit",
                DeliveryMethodCode = "MAIL",
                RequestCreationTS = new DateTime(2026, 1, 12, 17, 19, 22)
            };

            var statusHistory = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceId = correspondenceId,
                StatusCode = "04",
                StatusUpdateTS = new DateTime(2026, 1, 12, 17, 20, 44),
                HistorySequenceNum = 2,
                StatusUpdatedBy = "TestUser"
            };

            _context.CorrespondenceDeliveryMethods.Add(deliveryMethod);
            _context.CorrespondenceRequests.Add(correspondenceRequest);
            _context.CorrespondenceRequestStatusHistories.Add(statusHistory);
            await _context.SaveChangesAsync();

            _statusHistoryRepoMock.Setup(r => r.GetStatusDescriptionByStatusCode("04"))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _service.GetFilteredCorrespondenceAsync(request);

            // Assert
            Assert.IsNotNull(result);
            var resultList = result.ToList();
            Assert.AreEqual(1, resultList.Count);
            var firstResult = resultList.First();
            Assert.AreEqual("Status: 04", firstResult.CurrentStatusDescription);
        }

        [TestMethod]
        public async Task GetFilteredCorrespondenceAsync_LogsErrorOnException()
        {
            // Arrange
            var request = new CorrespondenceFilterRequest
            {
                RequestingSystemName = "Payment Guard Advantage",
                CreateBeginDate = "01/01/2026",
                CreateEndDate = "02/26/2026"
            };

            // Dispose the current context to simulate a database error
            _context.Dispose();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() =>
                _service.GetFilteredCorrespondenceAsync(request));

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.AtLeastOnce
            );
        }
    }
}
