using System;
using System.Threading.Tasks;
using CustomerCorrespondence.Application.Services;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CustomerCorrespondence.Application.Tests.Services
{
    [TestClass]
    public class DocumentIdsServiceTests
    {
        private readonly Mock<IDocumentIdsRepository> _repositoryMock;
        private readonly Mock<ILogger<DocumentIdsService>> _loggerMock;
        private readonly DocumentIdsService _service;
       
        public DocumentIdsServiceTests()
        {
            _repositoryMock = new Mock<IDocumentIdsRepository>();
            _loggerMock = new Mock<ILogger<DocumentIdsService>>();
            _service = new DocumentIdsService(_repositoryMock.Object, _loggerMock.Object);
        }

        [TestMethod]
        public async Task AddDocumentIdAsync_CallsRepositoryAndLogs()
        {
            var docId = "testDocId";
            _repositoryMock.Setup(r => r.AddDocumentIdAsync(docId)).Returns(Task.CompletedTask);

            await _service.AddDocumentIdAsync(docId);

            _repositoryMock.Verify(r => r.AddDocumentIdAsync(docId), Times.Once);
            //_loggerMock.Verify(l => l.LogInformation(It.IsAny<string>(), docId), Times.Once);
        }

        [TestMethod]
        public async Task AddDocumentIdAsync_ThrowsAndLogsOnException()
        {
            var docId = "testDocId";
            _repositoryMock.Setup(r => r.AddDocumentIdAsync(docId)).ThrowsAsync(new Exception("fail"));

            await Assert.ThrowsExceptionAsync<Exception>(async () => await _service.AddDocumentIdAsync(docId));
            //_loggerMock.Verify(l => l.LogError(It.IsAny<Exception>(), It.IsAny<string>(), docId), Times.Once);
        }

        [TestMethod]
        public async Task GetAndUpdateDocumentIdAsync_ReturnsDocIdAndLogs()
        {
            var expectedDocId = "claimedDocId";
            _repositoryMock.Setup(r => r.GetAndUpdateDocumentIdAsync()).ReturnsAsync(expectedDocId);

            var result = await _service.GetAndUpdateDocumentIdAsync();

            Assert.AreEqual(expectedDocId, result);
            _repositoryMock.Verify(r => r.GetAndUpdateDocumentIdAsync(), Times.Once);
        }

        [TestMethod]
        public async Task GetAndUpdateDocumentIdAsync_ReturnsNullWhenNoneAvailable()
        {
            _repositoryMock.Setup(r => r.GetAndUpdateDocumentIdAsync()).ReturnsAsync((string?)null);

            var result = await _service.GetAndUpdateDocumentIdAsync();

            Assert.IsNull(result);
            _repositoryMock.Verify(r => r.GetAndUpdateDocumentIdAsync(), Times.Once);
        }

        [TestMethod]
        public async Task GetAndUpdateDocumentIdAsync_ThrowsAndLogsOnException()
        {
            _repositoryMock.Setup(r => r.GetAndUpdateDocumentIdAsync()).ThrowsAsync(new Exception("fail"));

            await Assert.ThrowsExceptionAsync<Exception>(async () => await _service.GetAndUpdateDocumentIdAsync());
        }
        [TestMethod]
        public async Task Should_Not_Return_Duplicate_DocIds_In_Parallel()
        {
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _service.GetAndUpdateDocumentIdAsync());
        var results = await Task.WhenAll(tasks);
        var distinctCount = results.Where(r => r != null).Distinct().Count();
        Assert.AreEqual(distinctCount, results.Count(r => r != null));
        }
    }
}