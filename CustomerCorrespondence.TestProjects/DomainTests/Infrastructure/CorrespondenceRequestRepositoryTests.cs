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
    public class CorrespondenceRequestRepositoryTests
    {
        private CorrespondenceDBContext GetDbContext()
        {
            var options = new DbContextOptionsBuilder<CorrespondenceDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new CorrespondenceDBContext(options);
        }

        private CorrespondenceRequest GetSampleRequest(Guid? id = null, string? docId = null)
        {
            return new CorrespondenceRequest
            {
                CorrespondenceId = id ?? Guid.NewGuid(),
                DocId = docId ?? Guid.NewGuid().ToString(),
                DeliveryMethodCode = "EMAIL",
                RequestCreationTS = DateTime.UtcNow,
                StatusHistories = new List<CorrespondenceRequestStatusHistory>()
            };
        }

        [TestMethod]
        public async Task AddCorrespondenceRequestAsync_AddsRequest()
        {
            var dbContext = GetDbContext();
            var repo = new CorrespondenceRequestRepository(dbContext);
            var request = GetSampleRequest();

            await repo.AddCorrespondenceRequestAsync(request);

            Assert.AreEqual(1, dbContext.CorrespondenceRequests.Count());
        }

        [TestMethod]
        public async Task DeleteCorrespondenceRequestForCorrIdAsync_DeletesRequest()
        {
            var dbContext = GetDbContext();
            var request = GetSampleRequest();
            dbContext.CorrespondenceRequests.Add(request);
            dbContext.SaveChanges();
            var repo = new CorrespondenceRequestRepository(dbContext);

            await repo.DeleteCorrespondenceRequestForCorrIdAsync(request.CorrespondenceId);

            Assert.AreEqual(0, dbContext.CorrespondenceRequests.Count());
        }

        //[TestMethod]
        //public async Task DeleteCorrespondenceRequestForDocIdAsync_DeletesRequest()
        //{
        //    var dbContext = GetDbContext();
        //    var request = GetSampleRequest();
        //    dbContext.CorrespondenceRequests.Add(request);
        //    dbContext.SaveChanges();
        //    var repo = new CorrespondenceRequestRepository(dbContext);

        //    await repo.DeleteCorrespondenceRequestForDocIdAsync(request.DocId!);

        //    Assert.AreEqual(0, dbContext.CorrespondenceRequests.Count());
        //}

        [TestMethod]
        public async Task GetCorrespondenceRequestByCorrIdAsync_ReturnsRequests()
        {
            var dbContext = GetDbContext();
            var id = Guid.NewGuid();
            var request = GetSampleRequest(id: id);
            dbContext.CorrespondenceRequests.Add(request);
            dbContext.SaveChanges();
            var repo = new CorrespondenceRequestRepository(dbContext);

            var result = await repo.GetCorrespondenceRequestByCorrIdAsync(id);

            Assert.AreEqual(1, result.Count());
            Assert.AreEqual(id, result.First().CorrespondenceId);
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestByDocIdAsync_ReturnsRequests()
        {
            var dbContext = GetDbContext();
            var docId = Guid.NewGuid().ToString();
            var request = GetSampleRequest(docId: docId);
            dbContext.CorrespondenceRequests.Add(request);
            dbContext.SaveChanges();
            var repo = new CorrespondenceRequestRepository(dbContext);

            var result = await repo.GetCorrespondenceRequestByDocIdAsync(docId);

            Assert.AreEqual(1, result.Count());
            Assert.AreEqual(docId, result.First().DocId);
        }

        //[TestMethod]
        //public async Task GetCorrespondenceIdByDocIdAsync_ReturnsId()
        //{
        //    var dbContext = GetDbContext();
        //    var id = Guid.NewGuid();
        //    var docId = Guid.NewGuid().ToString();
        //    var request = GetSampleRequest(id: id, docId: docId);
        //    dbContext.CorrespondenceRequests.Add(request);
        //    dbContext.SaveChanges();
        //    var repo = new CorrespondenceRequestRepository(dbContext);

        //    var result = await repo.GetCorrespondenceIdByDocIdAsync(docId);

        //    Assert.AreEqual(id, result);
        //}

        [TestMethod]
        public async Task UpdateCorrespondenceRequestAsync_UpdatesRequest()
        {
            var dbContext = GetDbContext();
            var request = GetSampleRequest();
            dbContext.CorrespondenceRequests.Add(request);
            dbContext.SaveChanges();
            var repo = new CorrespondenceRequestRepository(dbContext);

            request.DeliveryMethodCode = "POSTAL";
            await repo.UpdateCorrespondenceRequestAsync(request);

            var updated = dbContext.CorrespondenceRequests.First();
            Assert.AreEqual("POSTAL", updated.DeliveryMethodCode);
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestsByDocumentTypeAsync_ReturnsMatchingRequests()
        {
            // Arrange
            var dbContext = GetDbContext();
            
            // Add DocumentType to the database
            var documentType = new CorrespondenceDocumentType
            {
                DocumentType = "POLICY",
                DocumentSubType = "Life",
                CabinetName = "PolicyCabinet"
            };
            dbContext.CorrespondenceDocumentTypes.Add(documentType);
            
            // Add test requests
            var policyRequest1 = GetSampleRequest();
            policyRequest1.ArchiveDocumentTypeCode = "POLICY";
            
            var policyRequest2 = GetSampleRequest();
            policyRequest2.ArchiveDocumentTypeCode = "POLICY";
            
            var claimRequest = GetSampleRequest();
            claimRequest.ArchiveDocumentTypeCode = "CLAIM";
            
            dbContext.CorrespondenceRequests.AddRange(policyRequest1, policyRequest2, claimRequest);
            dbContext.SaveChanges();
            
            var repo = new CorrespondenceRequestRepository(dbContext);

            // Act
            var result = await repo.GetCorrespondenceRequestsByDocumentTypeAsync("POLICY");

            // Assert
            Assert.AreEqual(2, result.Count());
            Assert.IsTrue(result.All(r => r.ArchiveDocumentTypeCode == "POLICY"));
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestsByDocumentTypeAsync_InvalidDocType_ReturnsEmptyList()
        {
            // Arrange
            var dbContext = GetDbContext();
            
            var documentType = new CorrespondenceDocumentType
            {
                DocumentType = "POLICY",
                DocumentSubType = "Life",
                CabinetName = "PolicyCabinet"
            };
            dbContext.CorrespondenceDocumentTypes.Add(documentType);
            
            var request = GetSampleRequest();
            request.ArchiveDocumentTypeCode = "POLICY";
            dbContext.CorrespondenceRequests.Add(request);
            dbContext.SaveChanges();
            
            var repo = new CorrespondenceRequestRepository(dbContext);

            // Act
            var result = await repo.GetCorrespondenceRequestsByDocumentTypeAsync("INVALID");

            // Assert
            Assert.AreEqual(0, result.Count());
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestsByDocumentTypeAsync_NoRecords_ReturnsEmptyList()
        {
            // Arrange
            var dbContext = GetDbContext();
            var repo = new CorrespondenceRequestRepository(dbContext);

            // Act
            var result = await repo.GetCorrespondenceRequestsByDocumentTypeAsync("POLICY");

            // Assert
            Assert.AreEqual(0, result.Count());
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestsByDocumentTypeAsync_NullArchiveDocumentTypeCode_ExcludesFromResults()
        {
            // Arrange
            var dbContext = GetDbContext();
            
            var documentType = new CorrespondenceDocumentType
            {
                DocumentType = "POLICY",
                DocumentSubType = "Life",
                CabinetName = "PolicyCabinet"
            };
            dbContext.CorrespondenceDocumentTypes.Add(documentType);
            
            var requestWithType = GetSampleRequest();
            requestWithType.ArchiveDocumentTypeCode = "POLICY";
            
            var requestWithoutType = GetSampleRequest();
            requestWithoutType.ArchiveDocumentTypeCode = null;
            
            dbContext.CorrespondenceRequests.AddRange(requestWithType, requestWithoutType);
            dbContext.SaveChanges();
            
            var repo = new CorrespondenceRequestRepository(dbContext);

            // Act
            var result = await repo.GetCorrespondenceRequestsByDocumentTypeAsync("POLICY");

            // Assert
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual("POLICY", result.First().ArchiveDocumentTypeCode);
        }

        [TestMethod]
        public async Task GetCorrespondenceRequestsByDocumentTypeAsync_JoinValidation_OnlyReturnsValidTypes()
        {
            // Arrange
            var dbContext = GetDbContext();
            
            // Add only POLICY to DocumentType table
            var validDocType = new CorrespondenceDocumentType
            {
                DocumentType = "POLICY",
                DocumentSubType = "Life",
                CabinetName = "PolicyCabinet"
            };
            dbContext.CorrespondenceDocumentTypes.Add(validDocType);
            
            // Add requests with different document types
            var validRequest = GetSampleRequest();
            validRequest.ArchiveDocumentTypeCode = "POLICY"; // Valid - exists in DocumentType table
            
            var orphanRequest = GetSampleRequest();
            orphanRequest.ArchiveDocumentTypeCode = "ORPHAN"; // Invalid - does not exist in DocumentType table
            
            dbContext.CorrespondenceRequests.AddRange(validRequest, orphanRequest);
            dbContext.SaveChanges();
            
            var repo = new CorrespondenceRequestRepository(dbContext);

            // Act
            var result = await repo.GetCorrespondenceRequestsByDocumentTypeAsync("POLICY");

            // Assert
            Assert.AreEqual(1, result.Count());
            Assert.AreEqual("POLICY", result.First().ArchiveDocumentTypeCode);
        }
    }
}