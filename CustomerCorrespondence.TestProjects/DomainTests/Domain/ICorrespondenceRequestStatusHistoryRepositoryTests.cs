using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Moq;


namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class ICorrespondenceRequestStatusHistoryRepositoryTests
    {
        private readonly Mock<ICorrespondenceRequestStatusHistoryRepository> _mockRepo;

        public ICorrespondenceRequestStatusHistoryRepositoryTests()
        {
            _mockRepo = new Mock<ICorrespondenceRequestStatusHistoryRepository>();
        }

        [TestMethod]
        public async Task GetAllByCorrespondenceIdAsync_ReturnsExpectedHistories()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var expected = new List<CorrespondenceRequestStatusHistory>
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
            _mockRepo.Setup(r => r.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(expected);

            // Act
            var result = await _mockRepo.Object.GetAllByCorrespondenceIdAsync(correspondenceId);

            // Assert
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public async Task GetStatusHistoryByCorrIdSeqNumAsync_ReturnsCorrectHistory()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var seqNum = 2;
            var expected = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = correspondenceId,
                HistorySequenceNum = seqNum,
                StatusCode = "SENT",
                StatusUpdatedBy = "user2",
                StatusUpdateTS = DateTime.UtcNow
            };
            _mockRepo.Setup(r => r.GetStatusHistoryByCorrIdSeqNumAsync(correspondenceId, seqNum))
                .ReturnsAsync(expected);

            // Act
            var result = await _mockRepo.Object.GetStatusHistoryByCorrIdSeqNumAsync(correspondenceId, seqNum);

            // Assert
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public async Task GetMaxSequenceNumber_ReturnsMaxSequence()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            _mockRepo.Setup(r => r.GetMaxSequenceNumber(correspondenceId))
                .ReturnsAsync(5);

            // Act
            var result = await _mockRepo.Object.GetMaxSequenceNumber(correspondenceId);

            // Assert
            Assert.AreEqual(5, result);
        }

        [TestMethod]
        public async Task GetStatusCodeByStatus_ReturnsStatusCode()
        {
            // Arrange
            var statusName = "Completed";
            _mockRepo.Setup(r => r.GetStatusCodeByStatus(statusName))
                .ReturnsAsync("COMP");

            // Act
            var result = await _mockRepo.Object.GetStatusCodeByStatus(statusName);

            // Assert
            Assert.AreEqual("COMP", result);
        }

        [TestMethod]
        public async Task AddCorrespondenceRequestStatusHistoryAsync_CallsRepository()
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
            await _mockRepo.Object.AddCorrespondenceRequestStatusHistoryAsync(history);

            // Assert
            _mockRepo.Verify(r => r.AddCorrespondenceRequestStatusHistoryAsync(history), Times.Once);
        }

        [TestMethod]
        public async Task UpdateCorrespondenceRequesStatusHistorytAsync_CallsRepository()
        {
            // Arrange
            var history = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = Guid.NewGuid(),
                HistorySequenceNum = 2,
                StatusCode = "UPDATED",
                StatusUpdatedBy = "user2",
                StatusUpdateTS = DateTime.UtcNow
            };

            // Act
            await _mockRepo.Object.UpdateCorrespondenceRequesStatusHistorytAsync(history);

            // Assert
            _mockRepo.Verify(r => r.UpdateCorrespondenceRequesStatusHistorytAsync(history), Times.Once);
        }

        [TestMethod]
        public async Task DeleteCorrespondenceRequestStatusHistoryAsync_CallsRepository()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();

            // Act
            await _mockRepo.Object.DeleteCorrespondenceRequestStatusHistoryAsync(correspondenceId);

            // Assert
            _mockRepo.Verify(r => r.DeleteCorrespondenceRequestStatusHistoryAsync(correspondenceId), Times.Once);
        }
    }
}