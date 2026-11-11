using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using Moq;


namespace CustomerCorrespondenceTests.Application
{
    [TestClass]
    public class ICorrespondenceRequestServiceTests
    {
        private readonly Mock<ICorrespondenceRequestService> _serviceMock;

        public ICorrespondenceRequestServiceTests()
        {
            _serviceMock = new Mock<ICorrespondenceRequestService>();
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestByCorrIdAsync_ReturnsRequests()
        {
            // Arrange
            var corrId = Guid.NewGuid();
            var expected = new List<CorrespondenceRequest>
            {
                new CorrespondenceRequest
                {
                    CorrespondenceId = corrId,
                    DeliveryMethodCode = "EMAIL",
                    StatusHistories = new List<CorrespondenceRequestStatusHistory>()
                }
            };
            _serviceMock.Setup(s => s.GetCorrespondenceRequestByCorrIdAsync(corrId))
                .ReturnsAsync(expected);

            // Act
            var result = await _serviceMock.Object.GetCorrespondenceRequestByCorrIdAsync(corrId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual(corrId, result.First().CorrespondenceId);
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestByDocIdAsync_ReturnsRequests()
        {
            // Arrange
            var docId = "DOC123";
            var expected = new List<CorrespondenceRequest>
            {
                new CorrespondenceRequest
                {
                    CorrespondenceId = Guid.NewGuid(),
                    DocId = docId,
                    DeliveryMethodCode = "MAIL",
                    StatusHistories = new List<CorrespondenceRequestStatusHistory>()
                }
            };
            _serviceMock.Setup(s => s.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(expected);

            // Act
            var result = await _serviceMock.Object.GetCorrespondenceRequestByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual(docId, result.First().DocId);
        }

        [TestMethod]
        public async Task AddCorrespondenceRequestAsync_CallsService()
        {
            // Arrange
            var request = new CorrespondenceRequest
            {
                CorrespondenceId = Guid.NewGuid(),
                DeliveryMethodCode = "EMAIL",
                StatusHistories = new List<CorrespondenceRequestStatusHistory>()
            };

            // Act
            await _serviceMock.Object.AddCorrespondenceRequestAsync(request);

            // Assert
            _serviceMock.Verify(s => s.AddCorrespondenceRequestAsync(request), Times.Once);
        }

        [TestMethod]
        public async Task UpdateCorrespondenceRequestAsync_CallsService()
        {
            // Arrange
            var request = new CorrespondenceRequest
            {
                CorrespondenceId = Guid.NewGuid(),
                DeliveryMethodCode = "MAIL",
                StatusHistories = new List<CorrespondenceRequestStatusHistory>()
            };

            // Act
            await _serviceMock.Object.UpdateCorrespondenceRequestAsync(request);

            // Assert
            _serviceMock.Verify(s => s.UpdateCorrespondenceRequestAsync(request), Times.Once);
        }

        [TestMethod]
        public async Task DeleteCorrespondenceRequestAsync_CallsService()
        {
            // Arrange
            var corrId = Guid.NewGuid();

            // Act
            await _serviceMock.Object.DeleteCorrespondenceRequestAsync(corrId);

            // Assert
            _serviceMock.Verify(s => s.DeleteCorrespondenceRequestAsync(corrId), Times.Once);
        }
    }
}