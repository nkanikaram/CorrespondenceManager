using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CustomerCorrespondence.Application.Services;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace CustomerCorrespondenceTests.Application
{
    [TestClass]
    public class CorrespondenceRequestServiceTests
    {
        private readonly Mock<ICorrespondenceRequestRepository> _repositoryMock;
        private readonly Mock<ILogger<CorrespondenceRequestService>> _loggerMock;
        private readonly CorrespondenceRequestService _service;

        public CorrespondenceRequestServiceTests()
        {
            _repositoryMock = new Mock<ICorrespondenceRequestRepository>();
            _loggerMock = new Mock<ILogger<CorrespondenceRequestService>>();
            _service = new CorrespondenceRequestService(_repositoryMock.Object, _loggerMock.Object);
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestByCorrIdAsync_ReturnsRequests()
        {
            // Arrange
            var corrId = Guid.NewGuid();
            var expected = new List<CorrespondenceRequest> { new CorrespondenceRequest { CorrespondenceId = corrId, DeliveryMethodCode = "EMAIL", StatusHistories = new List<CorrespondenceRequestStatusHistory>() } };
            _repositoryMock.Setup(r => r.GetCorrespondenceRequestByCorrIdAsync(corrId)).ReturnsAsync(expected);

            // Act
            var result = await _service.GetCorrespondenceRequestByCorrIdAsync(corrId);

            // Assert
            Assert.AreEqual(expected, result);
            _repositoryMock.Verify(r => r.GetCorrespondenceRequestByCorrIdAsync(corrId), Times.Once);
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestByDocIdAsync_ReturnsRequests()
        {
            // Arrange
            var docId = "doc123";
            var expected = new List<CorrespondenceRequest> { new CorrespondenceRequest { CorrespondenceId = Guid.NewGuid(), DocId = docId, DeliveryMethodCode = "EMAIL", StatusHistories = new List<CorrespondenceRequestStatusHistory>() } };
            _repositoryMock.Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId)).ReturnsAsync(expected);

            // Act
            var result = await _service.GetCorrespondenceRequestByDocIdAsync(docId);

            // Assert
            Assert.AreEqual(expected, result);
            _repositoryMock.Verify(r => r.GetCorrespondenceRequestByDocIdAsync(docId), Times.Once);
        }

        [TestMethod]
        public async Task AddCorrespondenceRequestAsync_CallsRepository()
        {
            // Arrange
            var request = new CorrespondenceRequest { CorrespondenceId = Guid.NewGuid(), DeliveryMethodCode = "EMAIL", StatusHistories = new List<CorrespondenceRequestStatusHistory>() };

            // Act
            await _service.AddCorrespondenceRequestAsync(request);

            // Assert
            _repositoryMock.Verify(r => r.AddCorrespondenceRequestAsync(request), Times.Once);
        }

        [TestMethod]
        public async Task UpdateCorrespondenceRequestAsync_CallsRepository()
        {
            // Arrange
            var request = new CorrespondenceRequest { CorrespondenceId = Guid.NewGuid(), DeliveryMethodCode = "EMAIL", StatusHistories = new List<CorrespondenceRequestStatusHistory>() };

            // Act
            await _service.UpdateCorrespondenceRequestAsync(request);

            // Assert
            _repositoryMock.Verify(r => r.UpdateCorrespondenceRequestAsync(request), Times.Once);
        }

        [TestMethod]
        public async Task DeleteCorrespondenceRequestAsync_CallsRepository()
        {
            // Arrange
            var corrId = Guid.NewGuid();

            // Act
            await _service.DeleteCorrespondenceRequestAsync(corrId);

            // Assert
            _repositoryMock.Verify(r => r.DeleteCorrespondenceRequestForCorrIdAsync(corrId), Times.Once);
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestByCorrIdAsync_LogsErrorOnException()
        {
            // Arrange
            var corrId = Guid.NewGuid();
            _repositoryMock.Setup(r => r.GetCorrespondenceRequestByCorrIdAsync(corrId)).ThrowsAsync(new Exception("Test error"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(() => _service.GetCorrespondenceRequestByCorrIdAsync(corrId));
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v != null && v.ToString() != null && v.ToString().Contains("Error fetching CorrespondenceRequest")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once
            );
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestsByDocumentTypeAsync_ReturnsRequests()
        {
            // Arrange
            var documentType = "POLICY";
            var expected = new List<CorrespondenceRequest> 
            { 
                new CorrespondenceRequest 
                { 
                    CorrespondenceId = Guid.NewGuid(), 
                    ArchiveDocumentTypeCode = "POLICY",
                    DeliveryMethodCode = "EMAIL", 
                    StatusHistories = new List<CorrespondenceRequestStatusHistory>() 
                },
                new CorrespondenceRequest 
                { 
                    CorrespondenceId = Guid.NewGuid(), 
                    ArchiveDocumentTypeCode = "POLICY",
                    DeliveryMethodCode = "MAIL", 
                    StatusHistories = new List<CorrespondenceRequestStatusHistory>() 
                }
            };
            _repositoryMock.Setup(r => r.GetCorrespondenceRequestsByDocumentTypeAsync(documentType)).ReturnsAsync(expected);

            // Act
            var result = await _service.GetCorrespondenceRequestsByDocumentTypeAsync(documentType);

            // Assert
            Assert.AreEqual(expected, result);
            _repositoryMock.Verify(r => r.GetCorrespondenceRequestsByDocumentTypeAsync(documentType), Times.Once);
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestsByDocumentTypeAsync_NoResults_ReturnsEmptyList()
        {
            // Arrange
            var documentType = "CLAIM";
            var expected = new List<CorrespondenceRequest>();
            _repositoryMock.Setup(r => r.GetCorrespondenceRequestsByDocumentTypeAsync(documentType)).ReturnsAsync(expected);

            // Act
            var result = await _service.GetCorrespondenceRequestsByDocumentTypeAsync(documentType);

            // Assert
            Assert.AreEqual(0, result.Count());
            _repositoryMock.Verify(r => r.GetCorrespondenceRequestsByDocumentTypeAsync(documentType), Times.Once);
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestsByDocumentTypeAsync_LogsErrorOnException()
        {
            // Arrange
            var documentType = "POLICY";
            _repositoryMock.Setup(r => r.GetCorrespondenceRequestsByDocumentTypeAsync(documentType))
                .ThrowsAsync(new Exception("Database error"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(() => _service.GetCorrespondenceRequestsByDocumentTypeAsync(documentType));
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v != null && v.ToString() != null && v.ToString().Contains("Error fetching CorrespondenceRequest")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
                Times.Once
            );
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }
    }
}