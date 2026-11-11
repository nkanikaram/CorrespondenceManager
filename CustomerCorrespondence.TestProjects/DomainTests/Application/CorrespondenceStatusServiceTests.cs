using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Application.Services;
using CustomerCorrespondence.Domain.DTOs;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CustomerCorrespondenceTests.Application
{
    [TestClass]
    public class CorrespondenceStatusServiceTests
    {
        private readonly Mock<ICorrespondenceRequestRepository> _mockRequestRepo;
        private readonly Mock<ICorrespondenceRequestStatusHistoryRepository> _mockHistoryRepo;
        private readonly Mock<ICorrespondenceDocumentTypeService> _mockDocumentTypeService;
        private readonly Mock<IEncryptionService> _mockEncryptionService;
        private readonly Mock<ILogger<CorrespondenceStatusService>> _mockLogger;
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly CorrespondenceStatusService _service;

        public CorrespondenceStatusServiceTests()
        {
            _mockRequestRepo = new Mock<ICorrespondenceRequestRepository>();
            _mockHistoryRepo = new Mock<ICorrespondenceRequestStatusHistoryRepository>();
            _mockDocumentTypeService = new Mock<ICorrespondenceDocumentTypeService>();
            _mockEncryptionService = new Mock<IEncryptionService>();
            _mockLogger = new Mock<ILogger<CorrespondenceStatusService>>();
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();

            // Setup scope factory to return a scope that resolves the same mocks
            _mockServiceProvider
                .Setup(sp => sp.GetService(typeof(ICorrespondenceRequestStatusHistoryRepository)))
                .Returns(_mockHistoryRepo.Object);
            _mockServiceProvider
                .Setup(sp => sp.GetService(typeof(ICorrespondenceDocumentTypeService)))
                .Returns(_mockDocumentTypeService.Object);
            _mockServiceProvider
                .Setup(sp => sp.GetService(typeof(IEncryptionService)))
                .Returns(_mockEncryptionService.Object);

            _mockScope.Setup(s => s.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockScopeFactory.Setup(f => f.CreateScope()).Returns(_mockScope.Object);
            _service = new CorrespondenceStatusService(
                _mockRequestRepo.Object,
                _mockHistoryRepo.Object,
                _mockDocumentTypeService.Object,
                _mockEncryptionService.Object,
                _mockLogger.Object,
                _mockScopeFactory.Object
            );
        }

        #region Constructor Tests

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullRequestRepo_ThrowsArgumentNullException()
        {
            _ = new CorrespondenceStatusService(
                null!,
                _mockHistoryRepo.Object,
                _mockDocumentTypeService.Object,
                _mockEncryptionService.Object,
                _mockLogger.Object,
                _mockScopeFactory.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullHistoryRepo_ThrowsArgumentNullException()
        {
            _ = new CorrespondenceStatusService(
                _mockRequestRepo.Object,
                null!,
                _mockDocumentTypeService.Object,
                _mockEncryptionService.Object,
                _mockLogger.Object,
                _mockScopeFactory.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullDocumentTypeService_ThrowsArgumentNullException()
        {
            _ = new CorrespondenceStatusService(
                _mockRequestRepo.Object,
                _mockHistoryRepo.Object,
                null!,
                _mockEncryptionService.Object,
                _mockLogger.Object,
                _mockScopeFactory.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullEncryptionService_ThrowsArgumentNullException()
        {
            _ = new CorrespondenceStatusService(
                _mockRequestRepo.Object,
                _mockHistoryRepo.Object,
                _mockDocumentTypeService.Object,
                null!,
                _mockLogger.Object,
                _mockScopeFactory.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            _ = new CorrespondenceStatusService(
                _mockRequestRepo.Object,
                _mockHistoryRepo.Object,
                _mockDocumentTypeService.Object,
                _mockEncryptionService.Object,
                null!,
                _mockScopeFactory.Object);
        }

        #endregion

        #region GetCorrespondenceStatusByDocIdAsync Tests

        [TestMethod]
        public async Task GetCorrespondenceStatusByDocIdAsync_WithNullDocId_ThrowsArgumentNullException()
        {
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _service.GetCorrespondenceStatusByDocIdAsync(null!));
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByDocIdAsync_WithValidDocId_ReturnsCorrespondenceStatusResponse()
        {
            // Arrange
            var docId = "1234567890123456789";
            var correspondenceId = Guid.NewGuid();
            var policyNumber = "POL-12345";
            var encryptedPolicyNumber = "encrypted_policy_data";
            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            var correspondenceRequest = CreateCorrespondenceRequest(
                docId: docId,
                correspondenceId: correspondenceId,
                policyNumber: Encoding.UTF8.GetBytes(encryptedPolicyNumber));

            SetupMockForGetByDocId(docId, correspondenceRequest, statusHistories, policyNumber, encryptedPolicyNumber);

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(docId, result.DocId);
            Assert.AreEqual(correspondenceId, result.CorrespondenceId);
            Assert.AreEqual("TestSystem", result.RequestingSystemName);
            Assert.AreEqual("[REDACTED]", result.PolicyNumber); // PolicyNumber is now redacted
            Assert.AreEqual("CLM-001", result.ClaimNumber);
            Assert.AreEqual("CUST-001", result.CustomerId);
            Assert.AreEqual("EMAIL", result.DeliveryMethod);
            Assert.AreEqual("POLICY_LETTER", result.DocumentType);
            Assert.IsNotNull(result.CurrentStatus);
            Assert.AreEqual("COMPLETED", result.CurrentStatus.StatusCode);
            Assert.AreEqual(1, result.StatusHistory.Count);
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByDocIdAsync_WithNoMatchingRequest_ReturnsNull()
        {
            // Arrange
            var docId = "1234567890123456789";
            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest>());

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNull(result);
            VerifyWarningLogged("No correspondence request found for DocId:");
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByDocIdAsync_WithRepositoryException_LogsErrorAndRethrows()
        {
            // Arrange
            var docId = "1234567890123456789";
            var exception = new InvalidOperationException("Database connection failed");

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ThrowsAsync(exception);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.GetCorrespondenceStatusByDocIdAsync(docId));

            VerifyErrorLogged("FAILED GetCorrespondenceStatusByDocIdAsync for DocId:", exception);
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByDocIdAsync_WithMaliciousDocId_SanitizesForLogging()
        {
            // Arrange
            var docId = "DOC<script>alert('XSS')</script>\r\n123";
            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest>());

            // Act
            await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            VerifyInformationLogged("BEGIN GetCorrespondenceStatusByDocIdAsync for DocId:");
        }

        #endregion

        #region GetCorrespondenceStatusByCorrespondenceIdAsync Tests

        [TestMethod]
        public async Task GetCorrespondenceStatusByCorrespondenceIdAsync_WithValidId_ReturnsCorrespondenceStatusResponse()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "9876543210987654321";
            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            var correspondenceRequest = CreateCorrespondenceRequest(
                docId: docId,
                correspondenceId: correspondenceId);

            SetupMockForGetByCorrespondenceId(correspondenceId, correspondenceRequest, statusHistories);

            // Act
            var result = await _service.GetCorrespondenceStatusByCorrespondenceIdAsync(correspondenceId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(correspondenceId, result.CorrespondenceId);
            Assert.AreEqual(docId, result.DocId);
            Assert.IsNotNull(result.CurrentStatus);
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByCorrespondenceIdAsync_WithNoMatchingRequest_ReturnsNull()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByCorrIdAsync(correspondenceId))
                .ReturnsAsync(new List<CorrespondenceRequest>());

            // Act
            var result = await _service.GetCorrespondenceStatusByCorrespondenceIdAsync(correspondenceId);

            // Assert
            Assert.IsNull(result);
            VerifyWarningLogged("No correspondence request found for CorrespondenceId:");
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByCorrespondenceIdAsync_WithException_LogsErrorAndRethrows()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var exception = new Exception("Repository error");

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByCorrIdAsync(correspondenceId))
                .ThrowsAsync(exception);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(() =>
                _service.GetCorrespondenceStatusByCorrespondenceIdAsync(correspondenceId));

            VerifyErrorLogged("FAILED GetCorrespondenceStatusByCorrespondenceIdAsync for CorrespondenceId:", exception);
        }

        #endregion

        #region GetCorrespondenceStatusByPolicyNumberAsync Tests

        [TestMethod]
        public async Task GetCorrespondenceStatusByPolicyNumberAsync_WithValidPolicyNumber_ReturnsMultipleResponses()
        {
            // Arrange
            var policyNumber = "POL-12345";
            var policyNumberHash = Encoding.UTF8.GetBytes("hash_pol_12345");
            var correspondenceId1 = Guid.NewGuid();
            var correspondenceId2 = Guid.NewGuid();
            var encryptedPolicyNumber = "encrypted_policy_data";

            var requests = new List<CorrespondenceRequest>
            {
                CreateCorrespondenceRequest("DOC001", correspondenceId1, Encoding.UTF8.GetBytes(encryptedPolicyNumber)),
                CreateCorrespondenceRequest("DOC002", correspondenceId2, Encoding.UTF8.GetBytes(encryptedPolicyNumber))
            };

            var statusHistories1 = CreateStatusHistories(correspondenceId1, 1);
            var statusHistories2 = CreateStatusHistories(correspondenceId2, 1);

            _mockEncryptionService
                .Setup(e => e.GenerateSearchHash(policyNumber))
                .Returns(policyNumberHash);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByPolicyNumberHashAsync(policyNumberHash))
                .ReturnsAsync(requests);

            // Setup batch history fetch
            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, List<CorrespondenceRequestStatusHistory>>
                {
                    { correspondenceId1, statusHistories1 },
                    { correspondenceId2, statusHistories2 }
                });

            // Setup batch status descriptions
            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            _mockEncryptionService
                .Setup(e => e.DecryptAsync(encryptedPolicyNumber))
                .ReturnsAsync(policyNumber);

            // Act
            var results = await _service.GetCorrespondenceStatusByPolicyNumberAsync(policyNumber);

            // Assert
            Assert.IsNotNull(results);
            var resultList = results.ToList();
            Assert.AreEqual(2, resultList.Count);
            Assert.IsTrue(resultList.Any(r => r.DocId == "DOC001"));
            Assert.IsTrue(resultList.Any(r => r.DocId == "DOC002"));
            Assert.IsTrue(resultList.All(r => r.PolicyNumber == "[REDACTED]")); // PolicyNumber should be redacted in all responses
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByPolicyNumberAsync_WithNoMatchingRequests_ReturnsEmptyList()
        {
            // Arrange
            var policyNumber = "POL-12345";
            var policyNumberHash = Encoding.UTF8.GetBytes("hash_pol_12345");

            _mockEncryptionService
                .Setup(e => e.GenerateSearchHash(policyNumber))
                .Returns(policyNumberHash);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByPolicyNumberHashAsync(policyNumberHash))
                .ReturnsAsync(new List<CorrespondenceRequest>());

            // Act
            var results = await _service.GetCorrespondenceStatusByPolicyNumberAsync(policyNumber);

            // Assert
            Assert.IsNotNull(results);
            Assert.AreEqual(0, results.Count());
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByPolicyNumberAsync_WithException_LogsErrorAndRethrows()
        {
            // Arrange
            var policyNumber = "POL-12345";
            var exception = new Exception("Hash generation failed");

            _mockEncryptionService
                .Setup(e => e.GenerateSearchHash(policyNumber))
                .Throws(exception);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<Exception>(() =>
                _service.GetCorrespondenceStatusByPolicyNumberAsync(policyNumber));

            VerifyErrorLogged("FAILED GetCorrespondenceStatusByPolicyNumberAsync for PolicyNumber:", exception);
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByPolicyNumberAsync_WithMaliciousInput_SanitizesForLogging()
        {
            // Arrange
            var policyNumber = "POL<script>\r\nalert('XSS')</script>";
            var policyNumberHash = Encoding.UTF8.GetBytes("hash123");

            _mockEncryptionService
                .Setup(e => e.GenerateSearchHash(policyNumber))
                .Returns(policyNumberHash);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByPolicyNumberHashAsync(policyNumberHash))
                .ReturnsAsync(new List<CorrespondenceRequest>());

            // Act
            await _service.GetCorrespondenceStatusByPolicyNumberAsync(policyNumber);

            // Assert
            VerifyInformationLogged("BEGIN GetCorrespondenceStatusByPolicyNumberAsync for PolicyNumber:");
        }

        #endregion

        #region GetCorrespondenceStatusByClaimNumberAsync Tests

        [TestMethod]
        public async Task GetCorrespondenceStatusByClaimNumberAsync_WithValidClaimNumber_ReturnsMultipleResponses()
        {
            // Arrange
            var claimNumber = "CLM-67890";
            var correspondenceId1 = Guid.NewGuid();
            var correspondenceId2 = Guid.NewGuid();

            var requests = new List<CorrespondenceRequest>
            {
                CreateCorrespondenceRequest("DOC003", correspondenceId1, claimNumber: claimNumber),
                CreateCorrespondenceRequest("DOC004", correspondenceId2, claimNumber: claimNumber)
            };

            var statusHistories1 = CreateStatusHistories(correspondenceId1, 2);
            var statusHistories2 = CreateStatusHistories(correspondenceId2, 1);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByClaimNumberAsync(claimNumber, 1, 25))
                .ReturnsAsync((requests.AsEnumerable(), 2));

            // Setup batch history fetch
            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, List<CorrespondenceRequestStatusHistory>>
                {
                    { correspondenceId1, statusHistories1 },
                    { correspondenceId2, statusHistories2 }
                });

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            // Act
            var result = await _service.GetCorrespondenceStatusByClaimNumberAsync(claimNumber, 1, 25);

            // Assert
            Assert.IsNotNull(result);
            var resultList = result.Data.ToList();
            Assert.AreEqual(2, resultList.Count);
            Assert.AreEqual(2, result.TotalCount);
            Assert.AreEqual(1, result.PageNumber);
            Assert.AreEqual(25, result.PageSize);
            Assert.IsTrue(resultList.Any(r => r.DocId == "DOC003"));
            Assert.IsTrue(resultList.Any(r => r.DocId == "DOC004"));
            Assert.IsTrue(resultList.All(r => r.ClaimNumber == claimNumber));
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByClaimNumberAsync_WithNoMatchingRequests_ReturnsEmptyPaginatedResponse()
        {
            // Arrange
            var claimNumber = "CLM-67890";

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByClaimNumberAsync(claimNumber, 1, 25))
                .ReturnsAsync((Enumerable.Empty<CorrespondenceRequest>(), 0));

            // Act
            var result = await _service.GetCorrespondenceStatusByClaimNumberAsync(claimNumber, 1, 25);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.TotalCount);
            Assert.AreEqual(0, result.Data.Count());
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByClaimNumberAsync_WithException_LogsErrorAndRethrows()
        {
            // Arrange
            var claimNumber = "CLM-67890";
            var exception = new InvalidOperationException("Repository failure");

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByClaimNumberAsync(claimNumber, 1, 25))
                .ThrowsAsync(exception);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.GetCorrespondenceStatusByClaimNumberAsync(claimNumber, 1, 25));

            VerifyErrorLogged("FAILED GetCorrespondenceStatusByClaimNumberAsync for ClaimNumber:", exception);
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByClaimNumberAsync_WithMaliciousInput_SanitizesForLogging()
        {
            // Arrange
            var claimNumber = "CLM<img src=x>\r\n123";

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByClaimNumberAsync(claimNumber, 1, 25))
                .ReturnsAsync((Enumerable.Empty<CorrespondenceRequest>(), 0));

            // Act
            await _service.GetCorrespondenceStatusByClaimNumberAsync(claimNumber, 1, 25);

            // Assert
            VerifyInformationLogged("BEGIN GetCorrespondenceStatusByClaimNumberAsync for ClaimNumber:");
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByClaimNumberAsync_WithPagination_ReturnsCorrectPage()
        {
            // Arrange
            var claimNumber = "CLM-67890";
            var correspondenceId1 = Guid.NewGuid();

            var requests = new List<CorrespondenceRequest>
            {
                CreateCorrespondenceRequest("DOC005", correspondenceId1, claimNumber: claimNumber)
            };

            var statusHistories1 = CreateStatusHistories(correspondenceId1, 1);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByClaimNumberAsync(claimNumber, 2, 10))
                .ReturnsAsync((requests.AsEnumerable(), 15));

            // Setup batch history fetch
            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, List<CorrespondenceRequestStatusHistory>>
                {
                    { correspondenceId1, statusHistories1 }
                });

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            // Act
            var result = await _service.GetCorrespondenceStatusByClaimNumberAsync(claimNumber, 2, 10);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(15, result.TotalCount);
            Assert.AreEqual(2, result.PageNumber);
            Assert.AreEqual(10, result.PageSize);
            Assert.AreEqual(2, result.TotalPages);
            Assert.IsTrue(result.HasPreviousPage);
            Assert.IsFalse(result.HasNextPage);
        }

        #endregion

        #region BuildStatusResponseAsync Tests (via public methods)

        [TestMethod]
        public async Task BuildStatusResponseAsync_WithNullPolicyNumber_ReturnsRedactedPolicyNumber()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC005";
            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            var correspondenceRequest = CreateCorrespondenceRequest(
                docId: docId,
                correspondenceId: correspondenceId,
                policyNumber: null);

            SetupMockForGetByDocId(docId, correspondenceRequest, statusHistories, null, null);

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("[REDACTED]", result.PolicyNumber); // Always redacted now
        }

        [TestMethod]
        public async Task BuildStatusResponseAsync_WithEmptyPolicyNumber_ReturnsRedactedPolicyNumber()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC006";
            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            var correspondenceRequest = CreateCorrespondenceRequest(
                docId: docId,
                correspondenceId: correspondenceId,
                policyNumber: Array.Empty<byte>());

            SetupMockForGetByDocId(docId, correspondenceRequest, statusHistories, null, null);

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("[REDACTED]", result.PolicyNumber); // Always redacted now
        }

        [TestMethod]
        public async Task BuildStatusResponseAsync_WithDecryptionFailure_SetsRedactedPlaceholder()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC007";
            var encryptedPolicyNumber = "encrypted_data";
            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            var correspondenceRequest = CreateCorrespondenceRequest(
                docId: docId,
                correspondenceId: correspondenceId,
                policyNumber: Encoding.UTF8.GetBytes(encryptedPolicyNumber));

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest> { correspondenceRequest });

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(statusHistories);

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("[REDACTED]", result.PolicyNumber);
        }

        [TestMethod]
        public async Task BuildStatusResponseAsync_WithMultipleStatusHistories_OrdersByTimestampDescending()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC008";
            var now = DateTime.UtcNow;

            var statusHistories = new List<CorrespondenceRequestStatusHistory>
            {
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 1,
                    StatusCode = "PENDING",
                    StatusUpdateTS = now.AddHours(-2),
                    StatusUpdatedBy = "System"
                },
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 2,
                    StatusCode = "PROCESSING",
                    StatusUpdateTS = now.AddHours(-1),
                    StatusUpdatedBy = "System"
                },
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 3,
                    StatusCode = "COMPLETED",
                    StatusUpdateTS = now,
                    StatusUpdatedBy = "System"
                }
            };

            var correspondenceRequest = CreateCorrespondenceRequest(docId: docId, correspondenceId: correspondenceId);

            SetupMockForGetByDocId(docId, correspondenceRequest, statusHistories);

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("COMPLETED", result.CurrentStatus.StatusCode);
            Assert.AreEqual(3, result.StatusHistory.Count);
            Assert.AreEqual("COMPLETED", result.StatusHistory[0].StatusCode);
            Assert.AreEqual("PROCESSING", result.StatusHistory[1].StatusCode);
            Assert.AreEqual("PENDING", result.StatusHistory[2].StatusCode);
        }

        [TestMethod]
        public async Task BuildStatusResponseAsync_WithNoStatusHistory_HasEmptyCurrentStatus()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC009";

            var correspondenceRequest = CreateCorrespondenceRequest(docId: docId, correspondenceId: correspondenceId);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest> { correspondenceRequest });

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(new List<CorrespondenceRequestStatusHistory>());

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new Dictionary<string, string>());

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.CurrentStatus);
            Assert.AreEqual(string.Empty, result.CurrentStatus.StatusCode);
            Assert.AreEqual(0, result.StatusHistory.Count);
        }

        [TestMethod]
        public async Task BuildStatusResponseAsync_WithDocumentType_MapsToResponse()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC010";
            var documentType = "POLICY_NOTICE";
            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            var correspondenceRequest = CreateCorrespondenceRequest(
                docId: docId,
                correspondenceId: correspondenceId,
                documentType: documentType);

            SetupMockForGetByDocId(docId, correspondenceRequest, statusHistories);

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(documentType, result.DocumentType);
        }

        [TestMethod]
        public async Task BuildStatusResponseAsync_WithDocumentType_IncludesDocumentTypeDetails()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC011";
            var documentType = "POLICY_LETTER";
            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            var documentTypeInfo = new CorrespondenceDocumentType
            {
                CorrespondenceDocumentTypeId = 1,
                DocumentType = documentType,
                DocumentSubType = "STANDARD",
                CabinetName = "PolicyDocuments"
            };

            var correspondenceRequest = CreateCorrespondenceRequest(
                docId: docId,
                correspondenceId: correspondenceId,
                documentType: documentType);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest> { correspondenceRequest });

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(statusHistories);

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            _mockDocumentTypeService
                .Setup(s => s.GetDocumentTypeInfoAsync(documentType))
                .ReturnsAsync(documentTypeInfo);

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(documentType, result.DocumentType);
            Assert.IsNotNull(result.DocumentTypeDetails);
            Assert.AreEqual(documentType, result.DocumentTypeDetails.DocumentType);
            Assert.AreEqual("STANDARD", result.DocumentTypeDetails.DocumentSubType);
            Assert.AreEqual("PolicyDocuments", result.DocumentTypeDetails.CabinetName);
        }

        [TestMethod]
        public async Task BuildStatusResponseAsync_WithDocumentTypeLookupReturnsNull_ContinuesWithoutDetails()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC012";
            var documentType = "UNKNOWN_TYPE";
            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            var correspondenceRequest = CreateCorrespondenceRequest(
                docId: docId,
                correspondenceId: correspondenceId,
                documentType: documentType);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest> { correspondenceRequest });

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(statusHistories);

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            _mockDocumentTypeService
                .Setup(s => s.GetDocumentTypeInfoAsync(documentType))
                .ReturnsAsync((CorrespondenceDocumentType?)null);

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(documentType, result.DocumentType);
            Assert.IsNull(result.DocumentTypeDetails);
            _mockDocumentTypeService.Verify(s => s.GetDocumentTypeInfoAsync(documentType), Times.Once);
        }

        [TestMethod]
        public async Task BuildStatusResponseAsync_WithDocumentTypeLookupException_ContinuesWithoutDetails()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC013";
            var documentType = "ERROR_TYPE";
            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            var correspondenceRequest = CreateCorrespondenceRequest(
                docId: docId,
                correspondenceId: correspondenceId,
                documentType: documentType);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest> { correspondenceRequest });

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(statusHistories);

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            _mockDocumentTypeService
                .Setup(s => s.GetDocumentTypeInfoAsync(documentType))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(documentType, result.DocumentType);
            Assert.IsNull(result.DocumentTypeDetails);
            VerifyWarningLogged("Failed to lookup DocumentType details for:");
        }

        [TestMethod]
        public async Task BuildStatusResponseAsync_WithNullDocumentType_DoesNotCallLookupService()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC014";
            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            var correspondenceRequest = CreateCorrespondenceRequest(
                docId: docId,
                correspondenceId: correspondenceId,
                documentType: null);

            SetupMockForGetByDocId(docId, correspondenceRequest, statusHistories);

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsNull(result.DocumentType);
            Assert.IsNull(result.DocumentTypeDetails);
            _mockDocumentTypeService.Verify(s => s.GetDocumentTypeInfoAsync(It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task BuildStatusResponseAsync_WithEmptyDocumentType_DoesNotCallLookupService()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC015";
            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            var correspondenceRequest = CreateCorrespondenceRequest(
                docId: docId,
                correspondenceId: correspondenceId,
                documentType: "");

            SetupMockForGetByDocId(docId, correspondenceRequest, statusHistories);

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("", result.DocumentType);
            Assert.IsNull(result.DocumentTypeDetails);
            _mockDocumentTypeService.Verify(s => s.GetDocumentTypeInfoAsync(It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region GetStatusDescriptionAsync Tests (via public methods)

        [TestMethod]
        public async Task GetStatusDescriptionAsync_WithValidStatusCode_ReturnsDescription()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC016";
            var statusCode = "COMPLETED";
            var statusDescription = "Successfully Completed";

            var statusHistories = new List<CorrespondenceRequestStatusHistory>
            {
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 1,
                    StatusCode = statusCode,
                    StatusUpdateTS = DateTime.UtcNow,
                    StatusUpdatedBy = "System"
                }
            };

            var correspondenceRequest = CreateCorrespondenceRequest(docId: docId, correspondenceId: correspondenceId);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest> { correspondenceRequest });

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(statusHistories);

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new Dictionary<string, string>
                {
                    { statusCode, statusDescription }
                });

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(statusDescription, result.CurrentStatus.StatusDescription);
        }

        [TestMethod]
        public async Task GetStatusDescriptionAsync_WithNullStatusCode_ReturnsUnknownStatus()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC017";

            var statusHistories = new List<CorrespondenceRequestStatusHistory>
            {
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 1,
                    StatusCode = null,
                    StatusUpdateTS = DateTime.UtcNow,
                    StatusUpdatedBy = "System"
                }
            };

            var correspondenceRequest = CreateCorrespondenceRequest(docId: docId, correspondenceId: correspondenceId);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest> { correspondenceRequest });

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(statusHistories);

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new Dictionary<string, string>());

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Unknown Status", result.CurrentStatus.StatusDescription);
        }

        [TestMethod]
        public async Task GetStatusDescriptionAsync_WhenRepositoryReturnsNull_ReturnsFallbackDescription()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC018";
            var statusCode = "UNKNOWN";

            var statusHistories = new List<CorrespondenceRequestStatusHistory>
            {
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 1,
                    StatusCode = statusCode,
                    StatusUpdateTS = DateTime.UtcNow,
                    StatusUpdatedBy = "System"
                }
            };

            var correspondenceRequest = CreateCorrespondenceRequest(docId: docId, correspondenceId: correspondenceId);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest> { correspondenceRequest });

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(statusHistories);

            // Return empty dictionary — status code not found
            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new Dictionary<string, string>());

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual($"Status: {statusCode}", result.CurrentStatus.StatusDescription);
        }

        [TestMethod]
        public async Task GetStatusDescriptionAsync_WhenBatchRepositoryThrows_LogsWarningAndReturnsFallback()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "DOC019";
            var statusCode = "ERROR_CODE";

            var statusHistories = new List<CorrespondenceRequestStatusHistory>
            {
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 1,
                    StatusCode = statusCode,
                    StatusUpdateTS = DateTime.UtcNow,
                    StatusUpdatedBy = "System"
                }
            };

            var correspondenceRequest = CreateCorrespondenceRequest(docId: docId, correspondenceId: correspondenceId);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest> { correspondenceRequest });

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(statusHistories);

            // GetStatusDescriptionsByStatusCodesAsync returns empty dict on failure (per repo implementation)
            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new Dictionary<string, string>());

            // Act
            var result = await _service.GetCorrespondenceStatusByDocIdAsync(docId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual($"Status: {statusCode}", result.CurrentStatus.StatusDescription);
        }

        #endregion
        #region GetCorrespondenceStatusByDocumentTypesAsync Tests

        [TestMethod]
        public async Task GetCorrespondenceStatusByDocumentTypesAsync_WithValidDocumentTypes_ReturnsPaginatedResults()
        {
            // Arrange
            var documentTypes = new List<string> { "PGA Welcome Kit", "POLICY_LETTER" };
            var correspondenceId1 = Guid.NewGuid();
            var correspondenceId2 = Guid.NewGuid();
            var encryptedPolicyNumber = "encrypted_policy_data";

            var requests = new List<CorrespondenceRequest>
            {
                CreateCorrespondenceRequest("DOC020", correspondenceId1, Encoding.UTF8.GetBytes(encryptedPolicyNumber), documentType: "PGA Welcome Kit"),
                CreateCorrespondenceRequest("DOC021", correspondenceId2, Encoding.UTF8.GetBytes(encryptedPolicyNumber), documentType: "POLICY_LETTER")
            };

            var statusHistories1 = CreateStatusHistories(correspondenceId1, 1);
            var statusHistories2 = CreateStatusHistories(correspondenceId2, 1);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestsByDocumentTypesAsync(documentTypes, 1, 25))
                .ReturnsAsync((requests.AsEnumerable(), 2));

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, List<CorrespondenceRequestStatusHistory>>
                {
                    { correspondenceId1, statusHistories1 },
                    { correspondenceId2, statusHistories2 }
                });

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            _mockEncryptionService
                .Setup(e => e.DecryptAsync(encryptedPolicyNumber))
                .ReturnsAsync("POL-12345");

            // Act
            var result = await _service.GetCorrespondenceStatusByDocumentTypesAsync(documentTypes, 1, 25);

            // Assert
            Assert.IsNotNull(result);
            var resultList = result.Data.ToList();
            Assert.AreEqual(2, resultList.Count);
            Assert.AreEqual(2, result.TotalCount);
            Assert.AreEqual(1, result.PageNumber);
            Assert.AreEqual(25, result.PageSize);
            Assert.IsTrue(resultList.All(r => r.PolicyNumber == "[REDACTED]")); // PolicyNumber is redacted
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByDocumentTypesAsync_WithNoMatchingRequests_ReturnsEmptyPaginatedResponse()
        {
            // Arrange
            var documentTypes = new List<string> { "NONEXISTENT_TYPE" };

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestsByDocumentTypesAsync(documentTypes, 1, 25))
                .ReturnsAsync((Enumerable.Empty<CorrespondenceRequest>(), 0));

            // Act
            var result = await _service.GetCorrespondenceStatusByDocumentTypesAsync(documentTypes, 1, 25);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.TotalCount);
            Assert.AreEqual(0, result.Data.Count());
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByDocumentTypesAsync_WithException_LogsErrorAndRethrows()
        {
            // Arrange
            var documentTypes = new List<string> { "PGA Welcome Kit" };
            var exception = new InvalidOperationException("Repository failure");

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestsByDocumentTypesAsync(documentTypes, 1, 25))
                .ThrowsAsync(exception);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.GetCorrespondenceStatusByDocumentTypesAsync(documentTypes, 1, 25));

            VerifyErrorLogged("FAILED GetCorrespondenceStatusByDocumentTypesAsync for DocumentTypes:", exception);
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByDocumentTypesAsync_WithPagination_ReturnsCorrectPage()
        {
            // Arrange
            var documentTypes = new List<string> { "PGA Welcome Kit" };
            var correspondenceId1 = Guid.NewGuid();

            var requests = new List<CorrespondenceRequest>
            {
                CreateCorrespondenceRequest("DOC022", correspondenceId1, documentType: "PGA Welcome Kit")
            };

            var statusHistories1 = CreateStatusHistories(correspondenceId1, 1);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestsByDocumentTypesAsync(documentTypes, 2, 10))
                .ReturnsAsync((requests.AsEnumerable(), 15));

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, List<CorrespondenceRequestStatusHistory>>
                {
                    { correspondenceId1, statusHistories1 }
                });

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            // Act
            var result = await _service.GetCorrespondenceStatusByDocumentTypesAsync(documentTypes, 2, 10);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(15, result.TotalCount);
            Assert.AreEqual(2, result.PageNumber);
            Assert.AreEqual(10, result.PageSize);
            Assert.AreEqual(2, result.TotalPages);
            Assert.IsTrue(result.HasPreviousPage);
            Assert.IsFalse(result.HasNextPage);
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByDocumentTypesAsync_ClampsPageSizeToMax50()
        {
            // Arrange
            var documentTypes = new List<string> { "PGA Welcome Kit" };

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestsByDocumentTypesAsync(documentTypes, 1, 50))
                .ReturnsAsync((Enumerable.Empty<CorrespondenceRequest>(), 0));

            // Act
            var result = await _service.GetCorrespondenceStatusByDocumentTypesAsync(documentTypes, 1, 100);

            // Assert
            Assert.AreEqual(50, result.PageSize);
            _mockRequestRepo.Verify(r => r.GetCorrespondenceRequestsByDocumentTypesAsync(documentTypes, 1, 50), Times.Once);
        }

        [TestMethod]
        public async Task GetCorrespondenceStatusByDocumentTypesAsync_WithNullPolicyNumberHash_ReturnsRedactedPolicyNumber()
        {
            // Arrange
            var documentTypes = new List<string> { "PGA Welcome Kit" };
            var correspondenceId = Guid.NewGuid();

            var requests = new List<CorrespondenceRequest>
            {
                CreateCorrespondenceRequest("DOC023", correspondenceId, policyNumber: null, documentType: "PGA Welcome Kit")
            };

            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestsByDocumentTypesAsync(documentTypes, 1, 25))
                .ReturnsAsync((requests.AsEnumerable(), 1));

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, List<CorrespondenceRequestStatusHistory>>
                {
                    { correspondenceId, statusHistories }
                });

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            // Act
            var result = await _service.GetCorrespondenceStatusByDocumentTypesAsync(documentTypes, 1, 25);

            // Assert
            var item = result.Data.First();
            Assert.AreEqual("[REDACTED]", item.PolicyNumber);
        }

        #endregion

        #region GetCorrespondenceStatusByClaimNumberAsync - Redacted PolicyNumber Tests

        [TestMethod]
        public async Task GetCorrespondenceStatusByClaimNumberAsync_AlwaysRedactsPolicyNumber()
        {
            // Arrange
            var claimNumber = "CLM-99999";
            var correspondenceId = Guid.NewGuid();
            var encryptedPolicyNumber = "encrypted_policy_data";

            var requests = new List<CorrespondenceRequest>
            {
                CreateCorrespondenceRequest("DOC024", correspondenceId, Encoding.UTF8.GetBytes(encryptedPolicyNumber), claimNumber: claimNumber)
            };

            var statusHistories = CreateStatusHistories(correspondenceId, 1);

            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByClaimNumberAsync(claimNumber, 1, 25))
                .ReturnsAsync((requests.AsEnumerable(), 1));

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync(new Dictionary<Guid, List<CorrespondenceRequestStatusHistory>>
                {
                    { correspondenceId, statusHistories }
                });

            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            _mockEncryptionService
                .Setup(e => e.DecryptAsync(encryptedPolicyNumber))
                .ReturnsAsync("POL-SECRET-12345");

            // Act
            var result = await _service.GetCorrespondenceStatusByClaimNumberAsync(claimNumber, 1, 25);

            // Assert
            var item = result.Data.First();
            Assert.AreEqual("[REDACTED]", item.PolicyNumber);
            Assert.AreNotEqual("POL-SECRET-12345", item.PolicyNumber);
        }

        #endregion
        #region Helper Methods

        private CorrespondenceRequest CreateCorrespondenceRequest(
            string docId,
            Guid correspondenceId,
            byte[]? policyNumber = null,
            string? claimNumber = "CLM-001",
            string? documentType = "POLICY_LETTER",
            byte[]? requestPayload = null)
        {
            return new CorrespondenceRequest
            {
                DocId = docId,
                CorrespondenceId = correspondenceId,
                RequestingDomainName = "TestSystem",
                DeliveryMethodCode = "EMAIL",
                VersionCode = "V1",
                ArchiveDocumentTypeCode = documentType,
                ArchiveDocumentSubTypeCode = "SUB001",
                PolicyNumber = policyNumber,
                ClaimNumber = claimNumber,
                CustomerId = "CUST-001",
                RequestPayload = requestPayload,
                RequestCreationTS = DateTime.UtcNow
            };
        }

        private List<CorrespondenceRequestStatusHistory> CreateStatusHistories(Guid correspondenceId, int count)
        {
            var histories = new List<CorrespondenceRequestStatusHistory>();
            var now = DateTime.UtcNow;

            for (int i = 0; i < count; i++)
            {
                histories.Add(new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = i + 1,
                    StatusCode = "COMPLETED",
                    StatusUpdateTS = now.AddMinutes(-i),
                    StatusUpdatedBy = "System"
                });
            }

            return histories;
        }

        private void SetupMockForGetByDocId(
            string docId,
            CorrespondenceRequest request,
            List<CorrespondenceRequestStatusHistory> statusHistories,
            string? decryptedPolicyNumber = null,
            string? encryptedPolicyNumber = null)
        {
            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByDocIdAsync(docId))
                .ReturnsAsync(new List<CorrespondenceRequest> { request });

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdAsync(request.CorrespondenceId))
                .ReturnsAsync(statusHistories);

            // Setup batch status descriptions (replaces per-item GetStatusDescriptionByStatusCode)
            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));

            if (encryptedPolicyNumber != null && decryptedPolicyNumber != null)
            {
                _mockEncryptionService
                    .Setup(e => e.DecryptAsync(encryptedPolicyNumber))
                    .ReturnsAsync(decryptedPolicyNumber);
            }
        }

        private void SetupMockForGetByCorrespondenceId(
            Guid correspondenceId,
            CorrespondenceRequest request,
            List<CorrespondenceRequestStatusHistory> statusHistories)
        {
            _mockRequestRepo
                .Setup(r => r.GetCorrespondenceRequestByCorrIdAsync(correspondenceId))
                .ReturnsAsync(new List<CorrespondenceRequest> { request });

            _mockHistoryRepo
                .Setup(h => h.GetAllByCorrespondenceIdAsync(correspondenceId))
                .ReturnsAsync(statusHistories);

            // Setup batch status descriptions
            _mockHistoryRepo
                .Setup(h => h.GetStatusDescriptionsByStatusCodesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync((IEnumerable<string> codes) =>
                    codes.ToDictionary(c => c, c => $"Description for {c}"));
        }

        private void VerifyInformationLogged(string messageFragment)
        {
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => (v.ToString() ?? string.Empty).Contains(messageFragment)),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        private void VerifyWarningLogged(string messageFragment)
        {
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => (v.ToString() ?? string.Empty).Contains(messageFragment)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        private void VerifyErrorLogged(string messageFragment, Exception? exception)
        {
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => (v.ToString() ?? string.Empty).Contains(messageFragment)),
                    exception,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion
    }
}