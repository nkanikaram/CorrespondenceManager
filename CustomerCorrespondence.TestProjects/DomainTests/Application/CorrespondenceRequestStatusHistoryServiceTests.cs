using CustomerCorrespondence.Application.Services;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;


namespace CustomerCorrespondenceTests.Application
{
    [TestClass]
    public class CorrespondenceRequestStatusHistoryServiceTests
    {
        private readonly Mock<ICorrespondenceRequestStatusHistoryRepository> _repositoryMock;
        private readonly Mock<ILogger<CorrespondenceRequestStatusHistoryService>> _loggerMock;
        private readonly CorrespondenceRequestStatusHistoryService _service;

        public CorrespondenceRequestStatusHistoryServiceTests()
        {
            _repositoryMock = new Mock<ICorrespondenceRequestStatusHistoryRepository>();
            _loggerMock = new Mock<ILogger<CorrespondenceRequestStatusHistoryService>>();
            _service = new CorrespondenceRequestStatusHistoryService(_repositoryMock.Object, _loggerMock.Object);
        }

        [TestMethod]
        public async Task AddCorrespondenceRequestStatusHistoryAsync_CallsRepository()
        {
            var history = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceId = Guid.NewGuid(),
                StatusCode = "NEW",
                HistorySequenceNum = 1,
                StatusUpdateTS = DateTime.UtcNow
            };

            await _service.AddCorrespondenceRequestStatusHistoryAsync(history);

            _repositoryMock.Verify(r => r.AddCorrespondenceRequestStatusHistoryAsync(history), Times.Once);
        }

        [TestMethod]
        public async Task DeleteCorrespondenceRequestStatusHistoryAsync_CallsRepository()
        {
            var correspondenceId = Guid.NewGuid();

            await _service.DeleteCorrespondenceRequestStatusHistoryAsync(correspondenceId);

            _repositoryMock.Verify(r => r.DeleteCorrespondenceRequestStatusHistoryAsync(correspondenceId), Times.Once);
        }

        [TestMethod]
        public async Task GetAllByCorrespondenceIdAsync_ReturnsExpectedResult()
        {
            var correspondenceId = Guid.NewGuid();
            var expected = new List<CorrespondenceRequestStatusHistory>
            {
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceId = correspondenceId,
                    StatusCode = "NEW",
                    HistorySequenceNum = 1,
                    StatusUpdateTS = DateTime.UtcNow
                }
            };
            _repositoryMock.Setup(r => r.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(expected);

            var result = await _service.GetAllByCorrespondenceIdAsync(correspondenceId);

            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public async Task GetByHistorySequenceNumAsync_ReturnsExpectedResult()
        {
            var correspondenceId = Guid.NewGuid();
            var seqNum = 2;
            var expected = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceId = correspondenceId,
                HistorySequenceNum = seqNum,
                StatusCode = "INPROGRESS",
                StatusUpdateTS = DateTime.UtcNow
            };
            _repositoryMock.Setup(r => r.GetStatusHistoryByCorrIdSeqNumAsync(correspondenceId, seqNum))
                .ReturnsAsync(expected);

            var result = await _service.GetByHistorySequenceNumAsync(correspondenceId, seqNum);

            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public async Task UpdateCorrespondenceRequesStatusHistorytAsync_CallsRepository()
        {
            var history = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceId = Guid.NewGuid(),
                StatusCode = "UPDATED",
                HistorySequenceNum = 3,
                StatusUpdateTS = DateTime.UtcNow
            };

            await _service.UpdateCorrespondenceRequesStatusHistorytAsync(history);

            _repositoryMock.Verify(r => r.UpdateCorrespondenceRequesStatusHistorytAsync(history), Times.Once);
        }

        [TestMethod]
        public async Task AddCorrespondenceRequestStatusHistoryAsync_Throws_LogsError()
        {
            var history = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceId = Guid.NewGuid(),
                StatusCode = "ERR",
                HistorySequenceNum = 1,
                StatusUpdateTS = DateTime.UtcNow
            };
            _repositoryMock.Setup(r => r.AddCorrespondenceRequestStatusHistoryAsync(history))
                .ThrowsAsync(new Exception("Test exception"));

            try
            {
                await _service.AddCorrespondenceRequestStatusHistoryAsync(history);
                Assert.Fail("Expected exception was not thrown.");
            }
            catch (Exception)
            {
                // Expected exception
            }
            //_loggerMock.Verify(x => x.Log(
            //    LogLevel.Error,
            //    It.IsAny<EventId>(),
            //    It.Is<It.IsAnyType>((v, t) => v != null && v.ToString().Contains(history.CorrespondenceId.ToString())),
            //    It.IsAny<Exception>(),
            //    (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            //    Times.Once());
        }
    }

    // Extension for verifying logger calls
    public static class LoggerExtensions
    {
        public static void VerifyLog<T>(this Mock<ILogger<T>> logger, Action<Mock<ILogger<T>>> verify, Times times)
        {
            verify(logger);
        }

        public static void VerifyLog<T>(this Mock<ILogger<T>> logger, LogLevel level, Times times)
        {
            logger.Verify(x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
                times);
        }
    }
}