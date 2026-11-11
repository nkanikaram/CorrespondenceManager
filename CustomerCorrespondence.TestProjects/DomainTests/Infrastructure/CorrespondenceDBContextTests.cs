using System;
using System.Collections.Generic;
using System.Linq;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory;

using Xunit;

namespace CustomerCorrespondenceTests.Infrastructure
{
    [TestClass]
    public class CorrespondenceDBContextTests
    {
        private CorrespondenceDBContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<CorrespondenceDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new CorrespondenceDBContext(options);
        }

        [TestMethod]
        public void CanInsertAndRetrieveCorrespondenceRequest()
        {
            using var context = CreateDbContext();
            var request = new CorrespondenceRequest
            {
                CorrespondenceId = Guid.NewGuid(),
                DeliveryMethodCode = "EMAIL",
                RequestCreationTS = DateTime.UtcNow,
                StatusHistories = new List<CorrespondenceRequestStatusHistory>()
            };
            context.CorrespondenceRequests.Add(request);
            context.SaveChanges();

            var retrieved = context.CorrespondenceRequests.FirstOrDefault(r => r.CorrespondenceId == request.CorrespondenceId);
            Assert.IsNotNull(retrieved);
            Assert.AreEqual("EMAIL", retrieved.DeliveryMethodCode);
        }

        [TestMethod]
        public void CanInsertAndRetrieveRoutingConfigurationWithRoute()
        {
            using var context = CreateDbContext();
            var config = new RoutingConfiguration
            {
                RouteName = "TestRoute",
                CustomerName = "TestCustomer",
                CorrespondenceMethod = "EMAIL",
                Route = new List<string> { "Step1", "Step2" }
            };
            context.RoutingConfigurations.Add(config);
            context.SaveChanges();

            var retrieved = context.RoutingConfigurations.FirstOrDefault(r => r.RouteName == "TestRoute");
            Assert.IsNotNull(retrieved);
            Assert.AreEqual(2, retrieved.Route.Count);
            Assert.IsTrue(retrieved.Route.Contains("Step1"));
            Assert.IsTrue(retrieved.Route.Contains("Step2"));
        }

        [TestMethod]
        public void CascadeDelete_RemovesStatusHistories()
        {
            using var context = CreateDbContext();
            var request = new CorrespondenceRequest
            {
                CorrespondenceId = Guid.NewGuid(),
                DeliveryMethodCode = "EMAIL",
                RequestCreationTS = DateTime.UtcNow,
                StatusHistories = new List<CorrespondenceRequestStatusHistory>
                {
                    new CorrespondenceRequestStatusHistory
                    {
                        CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                        CorrespondenceId = Guid.NewGuid(),
                        HistorySequenceNum = 1,
                        StatusCode = "NEW",
                        StatusUpdatedBy = "user",
                        StatusUpdateTS = DateTime.UtcNow
                    }
                }
            };
            context.CorrespondenceRequests.Add(request);
            context.SaveChanges();

            var statusHistoryId = request.StatusHistories.First().CorrespondenceRequestStatusHistoryId;
            context.CorrespondenceRequests.Remove(request);
            context.SaveChanges();

            var statusHistory = context.CorrespondenceRequestStatusHistories.FirstOrDefault(h => h.CorrespondenceRequestStatusHistoryId == statusHistoryId);
            Assert.IsNull(statusHistory);
        }
    }
}