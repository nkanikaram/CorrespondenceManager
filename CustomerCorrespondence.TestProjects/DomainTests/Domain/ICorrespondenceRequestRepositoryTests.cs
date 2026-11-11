using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Moq;


namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class ICorrespondenceRequestRepositoryTests
    {
        private readonly Mock<ICorrespondenceRequestRepository> _mockRepo;
        private readonly Guid _testCorrId = Guid.NewGuid();
        private readonly string _testDocId = "DOC123";
        private readonly CorrespondenceRequest _testRequest;

        public ICorrespondenceRequestRepositoryTests()
        {
            _mockRepo = new Mock<ICorrespondenceRequestRepository>();
            _testRequest = new CorrespondenceRequest
            {
                CorrespondenceId = _testCorrId,
                DocId = _testDocId,
                DeliveryMethodCode = "EMAIL",
                RequestCreationTS = DateTime.UtcNow,
                StatusHistories = new List<CorrespondenceRequestStatusHistory>()
            };
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestByCorrIdAsync_ReturnsRequests()
        {
            _mockRepo.Setup(r => r.GetCorrespondenceRequestByCorrIdAsync(_testCorrId))
                .ReturnsAsync(new List<CorrespondenceRequest> { _testRequest });

            var result = await _mockRepo.Object.GetCorrespondenceRequestByCorrIdAsync(_testCorrId);

            Assert.AreEqual(1, result.Count());
            Assert.AreEqual(_testCorrId, result.First().CorrespondenceId);
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestByDocIdAsync_ReturnsRequests()
        {
            _mockRepo.Setup(r => r.GetCorrespondenceRequestByDocIdAsync(_testDocId))
                .ReturnsAsync(new List<CorrespondenceRequest> { _testRequest });

            var result = await _mockRepo.Object.GetCorrespondenceRequestByDocIdAsync(_testDocId);

            Assert.AreEqual(1, result.Count());
            Assert.AreEqual(_testDocId, result.First().DocId);
        }

        [TestMethod]
        public async Task AddCorrespondenceRequestAsync_CallsRepository()
        {
            _mockRepo.Setup(r => r.AddCorrespondenceRequestAsync(_testRequest))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await _mockRepo.Object.AddCorrespondenceRequestAsync(_testRequest);

            _mockRepo.Verify();
        }

        [TestMethod]
        public async Task UpdateCorrespondenceRequestAsync_CallsRepository()
        {
            _mockRepo.Setup(r => r.UpdateCorrespondenceRequestAsync(_testRequest))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await _mockRepo.Object.UpdateCorrespondenceRequestAsync(_testRequest);

            _mockRepo.Verify();
        }

        [TestMethod]
        public async Task DeleteCorrespondenceRequestForCorrIdAsync_CallsRepository()
        {
            _mockRepo.Setup(r => r.DeleteCorrespondenceRequestForCorrIdAsync(_testCorrId))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await _mockRepo.Object.DeleteCorrespondenceRequestForCorrIdAsync(_testCorrId);

            _mockRepo.Verify();
        }

        [TestMethod]
        public async Task DeleteCorrespondenceRequestForDocIdAsync_CallsRepository()
        {
            _mockRepo.Setup(r => r.DeleteCorrespondenceRequestForDocIdAsync(_testDocId))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await _mockRepo.Object.DeleteCorrespondenceRequestForDocIdAsync(_testDocId);

            _mockRepo.Verify();
        }

        //[TestMethod]
        //public async Task GetCorrespondenceIdByDocIdAsync_ReturnsGuid()
        //{
        //    _mockRepo.Setup(r => r.GetCorrespondenceIdByDocIdAsync(_testDocId))
        //        .ReturnsAsync(_testCorrId);

        //    var result = await _mockRepo.Object.GetCorrespondenceIdByDocIdAsync(_testDocId);

        //    Assert.AreEqual(_testCorrId, result);
        //}
    }
}