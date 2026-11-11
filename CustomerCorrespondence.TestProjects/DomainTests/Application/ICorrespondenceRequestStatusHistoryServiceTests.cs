using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using Moq;


namespace CustomerCorrespondenceTests.Application
{
    [TestClass]
    public class ICorrespondenceRequestStatusHistoryServiceTests
    {
        private readonly Mock<ICorrespondenceRequestStatusHistoryService> _mockService;

        public ICorrespondenceRequestStatusHistoryServiceTests()
        {
            _mockService = new Mock<ICorrespondenceRequestStatusHistoryService>();
        }

        [TestMethod]
        public async Task GetAllByCorrespondenceIdAsync_ReturnsHistories()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var histories = new List<CorrespondenceRequestStatusHistory>
            {
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 1,
                    StatusCode = "NEW",
                    StatusUpdatedBy = "user1",
                    StatusUpdateTS = DateTime.UtcNow
                }
            };
            _mockService.Setup(s => s.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(histories);

            // Act
            var result = await _mockService.Object.GetAllByCorrespondenceIdAsync(correspondenceId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual(correspondenceId, result.FirstOrDefault()?.CorrespondenceId);
        }

        [TestMethod]
        public async Task GetByHistorySequenceNumAsync_ReturnsCorrectHistory()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var history = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = correspondenceId,
                HistorySequenceNum = 2,
                StatusCode = "SENT",
                StatusUpdatedBy = "user2",
                StatusUpdateTS = DateTime.UtcNow
            };
            _mockService.Setup(s => s.GetByHistorySequenceNumAsync(correspondenceId, 2))
                .ReturnsAsync(history);

            // Act
            var result = await _mockService.Object.GetByHistorySequenceNumAsync(correspondenceId, 2);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.HistorySequenceNum);
        }

        [TestMethod]
        public async Task AddCorrespondenceRequestStatusHistoryAsync_CallsService()
        {
            // Arrange
            var history = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = Guid.NewGuid(),
                HistorySequenceNum = 1,
                StatusCode = "NEW",
                StatusUpdatedBy = "user1",
                StatusUpdateTS = DateTime.UtcNow
            };

            // Act
            await _mockService.Object.AddCorrespondenceRequestStatusHistoryAsync(history);

            // Assert
            _mockService.Verify(s => s.AddCorrespondenceRequestStatusHistoryAsync(history), Times.Once);
        }

        [TestMethod]
        public async Task UpdateCorrespondenceRequesStatusHistorytAsync_CallsService()
        {
            // Arrange
            var history = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = Guid.NewGuid(),
                HistorySequenceNum = 1,
                StatusCode = "UPDATED",
                StatusUpdatedBy = "user2",
                StatusUpdateTS = DateTime.UtcNow
            };

            // Act
            await _mockService.Object.UpdateCorrespondenceRequesStatusHistorytAsync(history);

            // Assert
            _mockService.Verify(s => s.UpdateCorrespondenceRequesStatusHistorytAsync(history), Times.Once);
        }

        [TestMethod]
        public async Task DeleteCorrespondenceRequestStatusHistoryAsync_CallsService()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();

            // Act
            await _mockService.Object.DeleteCorrespondenceRequestStatusHistoryAsync(correspondenceId);

            // Assert
            _mockService.Verify(s => s.DeleteCorrespondenceRequestStatusHistoryAsync(correspondenceId), Times.Once);
        }
    }
}