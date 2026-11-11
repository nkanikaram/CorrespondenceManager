using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Infrastructure.Persistence;
using CustomerCorrespondence.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CustomerCorrespondenceTests.Infrastructure
{
    [TestClass]
    public class CorrespondenceRequestStatusHistoryRepositoryTests
    {
        private CorrespondenceDBContext GetDbContext()
        {
            var options = new DbContextOptionsBuilder<CorrespondenceDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new CorrespondenceDBContext(options);
        }

        [TestMethod]
        public async Task AddCorrespondenceRequestStatusHistoryAsync_AddsHistory()
        {
            // Arrange
            var dbContext = GetDbContext();
            var repo = new CorrespondenceRequestStatusHistoryRepository(dbContext);
            var history = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = Guid.NewGuid(),
                HistorySequenceNum = 1,
                StatusCode = "NEW",
                StatusUpdatedBy = "tester",
                StatusUpdateTS = DateTime.UtcNow
            };

            // Act
            await repo.AddCorrespondenceRequestStatusHistoryAsync(history);

            // Assert
            var result = await dbContext.CorrespondenceRequestStatusHistories.FirstOrDefaultAsync();
            Assert.IsNotNull(result);
            Assert.AreEqual(history.CorrespondenceId, result.CorrespondenceId);
        }

        [TestMethod]
        public async Task DeleteCorrespondenceRequestStatusHistoryAsync_DeletesHistory()
        {
            // Arrange
            var dbContext = GetDbContext();
            var repo = new CorrespondenceRequestStatusHistoryRepository(dbContext);
            var corrId = Guid.NewGuid();
            dbContext.CorrespondenceRequestStatusHistories.Add(new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = corrId,
                HistorySequenceNum = 1,
                StatusCode = "NEW",
                StatusUpdatedBy = "tester",
                StatusUpdateTS = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();

            // Act
            await repo.DeleteCorrespondenceRequestStatusHistoryAsync(corrId);

            // Assert
            var result = await dbContext.CorrespondenceRequestStatusHistories
                .Where(h => h.CorrespondenceId == corrId).ToListAsync();
            Assert.IsTrue(result.Count == 0);
        }

        [TestMethod]
        public async Task GetAllByCorrespondenceIdAsync_ReturnsOrderedHistories()
        {
            // Arrange
            var dbContext = GetDbContext();
            var repo = new CorrespondenceRequestStatusHistoryRepository(dbContext);
            var corrId = Guid.NewGuid();
            dbContext.CorrespondenceRequestStatusHistories.AddRange(
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = corrId,
                    HistorySequenceNum = 2,
                    StatusCode = "INPROGRESS",
                    StatusUpdatedBy = "user2",
                    StatusUpdateTS = DateTime.UtcNow
                },
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = corrId,
                    HistorySequenceNum = 1,
                    StatusCode = "NEW",
                    StatusUpdatedBy = "user1",
                    StatusUpdateTS = DateTime.UtcNow
                }
            );
            await dbContext.SaveChangesAsync();

            // Act
            var result = await repo.GetAllByCorrespondenceIdAsync(corrId);

            // Assert
            Assert.AreEqual(2, result.Count());
            Assert.AreEqual(1, result.First().HistorySequenceNum);
        }

        [TestMethod]
        public async Task GetMaxSequenceNumber_ReturnsMax()
        {
            // Arrange
            var dbContext = GetDbContext();
            var repo = new CorrespondenceRequestStatusHistoryRepository(dbContext);
            var corrId = Guid.NewGuid();
            dbContext.CorrespondenceRequestStatusHistories.AddRange(
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = corrId,
                    HistorySequenceNum = 1,
                    StatusCode = "NEW",
                    StatusUpdatedBy = "user1",
                    StatusUpdateTS = DateTime.UtcNow
                },
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = corrId,
                    HistorySequenceNum = 3,
                    StatusCode = "DONE",
                    StatusUpdatedBy = "user2",
                    StatusUpdateTS = DateTime.UtcNow
                }
            );
            await dbContext.SaveChangesAsync();

            // Act
            var maxSeq = await repo.GetMaxSequenceNumber(corrId);

            // Assert
            Assert.AreEqual(3, maxSeq);
        }

        [TestMethod]
        public async Task GetStatusCodeByStatus_ReturnsStatusCode()
        {
            // Arrange
            var dbContext = GetDbContext();
            var repo = new CorrespondenceRequestStatusHistoryRepository(dbContext);
            dbContext.CorrespondenceStatusCodes.Add(new CorrespondenceStatusCode
            {
                StatusId = Guid.NewGuid(),
                StatusCode = "NEW",
                StatusDescription = "New Request"
            });
            await dbContext.SaveChangesAsync();

            // Act
            var code = await repo.GetStatusCodeByStatus("New Request");

            // Assert
            Assert.AreEqual("NEW", code);
        }

        [TestMethod]
        public async Task GetStatusHistoryByCorrIdSeqNumAsync_ReturnsCorrectHistory()
        {
            // Arrange
            var dbContext = GetDbContext();
            var repo = new CorrespondenceRequestStatusHistoryRepository(dbContext);
            var corrId = Guid.NewGuid();
            dbContext.CorrespondenceRequestStatusHistories.Add(new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = corrId,
                HistorySequenceNum = 5,
                StatusCode = "DONE",
                StatusUpdatedBy = "user",
                StatusUpdateTS = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();

            // Act
            var result = await repo.GetStatusHistoryByCorrIdSeqNumAsync(corrId, 5);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(5, result.HistorySequenceNum);
        }

        [TestMethod]
        public async Task UpdateCorrespondenceRequesStatusHistorytAsync_UpdatesHistory()
        {
            // Arrange
            var dbContext = GetDbContext();
            var repo = new CorrespondenceRequestStatusHistoryRepository(dbContext);
            var corrId = Guid.NewGuid();
            var history = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = corrId,
                HistorySequenceNum = 1,
                StatusCode = "NEW",
                StatusUpdatedBy = "user",
                StatusUpdateTS = DateTime.UtcNow
            };
            dbContext.CorrespondenceRequestStatusHistories.Add(history);
            await dbContext.SaveChangesAsync();

            // Act
            history.StatusCode = "UPDATED";
            await repo.UpdateCorrespondenceRequesStatusHistorytAsync(history);

            // Assert
            var updated = await dbContext.CorrespondenceRequestStatusHistories
                .FirstOrDefaultAsync(h => h.CorrespondenceRequestStatusHistoryId == history.CorrespondenceRequestStatusHistoryId);
            Assert.IsNotNull(updated);
            Assert.AreEqual("UPDATED", updated.StatusCode);
        }
    }
}