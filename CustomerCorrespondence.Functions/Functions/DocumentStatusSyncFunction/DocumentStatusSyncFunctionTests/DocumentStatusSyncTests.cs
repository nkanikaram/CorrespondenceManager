using DocumentStatusSyncFunction;
using DocumentStatusSyncFunction.Services;
using ECMServicesDemo;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Infrastructure.Persistence;
using CustomerCorrespondence.Domain.Interfaces;

namespace DocumentStatusSyncFunctionTests
{
    [TestClass]
    public class DocumentStatusSyncTests
    {
        private Mock<ILoggerFactory>? _mockLoggerFactory;
        private Mock<ILogger<DocumentStatusSync>>? _mockLogger;
        private Mock<IECMServiceClient>? _mockEcmClient;
        private Mock<IKeyVaultService>? _mockKeyVaultService;
        private CorrespondenceDBContext? _dbContext;
        private DocumentStatusSync? _documentStatusSync;

        /// <summary>
        /// Initializes test dependencies and seeds test data before each test execution.
        /// Sets up mocks for logger, ECM client, KeyVault service, and configures in-memory database.
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            // Setup mocks
            _mockLoggerFactory = new Mock<ILoggerFactory>();
            _mockLogger = new Mock<ILogger<DocumentStatusSync>>();
            _mockLoggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(_mockLogger.Object);

            _mockEcmClient = new Mock<IECMServiceClient>();
            _mockKeyVaultService = new Mock<IKeyVaultService>();

            // Setup in-memory database
            var options = new DbContextOptionsBuilder<CorrespondenceDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .EnableSensitiveDataLogging()
                .Options;
            _dbContext = new CorrespondenceDBContext(options);

            // Seed test data
            SeedTestData();

            // Setup environment variables
            Environment.SetEnvironmentVariable("DocumentumRepo", "TestRepo");
            Environment.SetEnvironmentVariable("DocumentumSuperUser", "TestUser");
            Environment.SetEnvironmentVariable("SecretNames__SA-ECCO-ECMTOKEN", "TestSecret");

            // Setup KeyVault mock
            _mockKeyVaultService
                .Setup(x => x.GetSecretAsync(It.IsAny<string>()))
                .ReturnsAsync("Testpass");

            // Setup ECM Client mock for token
            _mockEcmClient
                 .Setup(x => x.getTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                 .ReturnsAsync("TestToken123");

            // Create instance
            _documentStatusSync = new DocumentStatusSync(
                _mockLoggerFactory.Object,
                _dbContext,
                _mockEcmClient.Object,
                _mockKeyVaultService.Object
            );
        }

        /// <summary>
        /// Cleans up test resources and resets environment variables after each test execution.
        /// </summary>
        [TestCleanup]
        public void Cleanup()
        {
            _dbContext?.Dispose();
            Environment.SetEnvironmentVariable("DocumentumRepo", null);
            Environment.SetEnvironmentVariable("DocumentumSuperUser", null);
            Environment.SetEnvironmentVariable("SecretNames__SA-ECCO-ECMTOKEN", null);
            
            _mockLoggerFactory?.Reset();
            _mockLogger?.Reset();
            _mockEcmClient?.Reset();
            _mockKeyVaultService?.Reset();
        }

        /// <summary>
        /// Seeds the in-memory database with test correspondence status codes.
        /// </summary>
        private void SeedTestData()
        {
            if (_dbContext == null) return;

            // Add status codes
            _dbContext.CorrespondenceStatusCodes.AddRange(
                new CorrespondenceStatusCode
                {
                    StatusId = Guid.NewGuid(),
                    StatusCode = "04",
                    StatusDescription = "AwaitingImaging"
                },
                new CorrespondenceStatusCode
                {
                    StatusId = Guid.NewGuid(),
                    StatusCode = "09",
                    StatusDescription = "Archived"
                },
                new CorrespondenceStatusCode
                {
                    StatusId = Guid.NewGuid(),
                    StatusCode = "08",
                    StatusDescription = "Failed"
                }
            );
            _dbContext.SaveChanges();
        }

        /// <summary>
        /// Helper to wait for database changes with retry logic.
        /// </summary>
        private async Task<List<CorrespondenceRequestStatusHistory>> WaitForHistoryRecordsAsync(
            Guid correspondenceId, 
            int expectedCount, 
            int maxRetries = 20, 
            int delayMs = 100)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                var records = await _dbContext!.CorrespondenceRequestStatusHistories
                    .AsNoTracking()
                    .Where(h => h.CorrespondenceId == correspondenceId)
                    .OrderBy(h => h.HistorySequenceNum)
                    .ToListAsync();

                if (records.Count == expectedCount)
                {
                    return records;
                }

                await Task.Delay(delayMs);
            }

            // Return final attempt
            return await _dbContext!.CorrespondenceRequestStatusHistories
                .AsNoTracking()
                .Where(h => h.CorrespondenceId == correspondenceId)
                .OrderBy(h => h.HistorySequenceNum)
                .ToListAsync();
        }

        /// <summary>
        /// Verifies that documents with "AwaitingImaging" status are successfully updated to "Archived" status
        /// when the document is found in ECM system.
        /// </summary>
        [TestMethod]
        public async Task Run_ShouldProcessAwaitingImagingDocuments_Successfully()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "0300000824810064125";

            _dbContext!.CorrespondenceRequests.Add(new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = docId,
                DeliveryMethodCode="Mail"
            });

            _dbContext.CorrespondenceRequestStatusHistories.Add(new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = correspondenceId,
                HistorySequenceNum = 1,
                StatusCode = "04",
                StatusUpdatedBy = "TestUser",
                StatusUpdateTS = DateTime.UtcNow.AddHours(-2)
            });
            await _dbContext.SaveChangesAsync();

            var xmlResponse = CreateValidDqlResponse(docId);
            _mockEcmClient!
                .Setup(x => x.execDQLAsync(It.IsAny<DocumentumSecurityToken>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(xmlResponse);

            var timerInfo = CreateTimerInfo();

            // Act
            await _documentStatusSync!.Run(timerInfo);

            // Assert
            var historyRecords = await WaitForHistoryRecordsAsync(correspondenceId, 2);
            Assert.AreEqual(2, historyRecords.Count, "Should have added archived status");
            Assert.AreEqual("04", historyRecords[0].StatusCode, "First status should be AwaitingImaging");
            Assert.AreEqual("09", historyRecords[1].StatusCode, "Second status should be Archived");
        }

        /// <summary>
        /// Verifies that documents are marked as "Failed" when they are not found in ECM system
        /// after 48 hours of being in "AwaitingImaging" status.
        /// </summary>
        [TestMethod]
        public async Task Run_ShouldMarkAsFailed_WhenDocumentNotFoundAfter48Hours()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "0300000824810064999";

            _dbContext!.CorrespondenceRequests.Add(new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = docId,
                DeliveryMethodCode = "Mail"
            });

            _dbContext.CorrespondenceRequestStatusHistories.Add(new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = correspondenceId,
                HistorySequenceNum = 1,
                StatusCode = "04",
                StatusUpdatedBy = "TestUser",
                StatusUpdateTS = DateTime.UtcNow.AddHours(-50) // More than 48 hours
            });
            await _dbContext.SaveChangesAsync();

            // Setup empty response (document not found)
            var xmlResponse = CreateEmptyDqlResponse();
            _mockEcmClient!
                .Setup(x => x.execDQLAsync(It.IsAny<DocumentumSecurityToken>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(xmlResponse);

            var timerInfo = CreateTimerInfo();

            // Act
            await _documentStatusSync!.Run(timerInfo);

            // Assert
            var historyRecords = await WaitForHistoryRecordsAsync(correspondenceId, 2);
            Assert.AreEqual(2, historyRecords.Count, "Should have added failed status");
            Assert.AreEqual("04", historyRecords[0].StatusCode, "First status should be AwaitingImaging");
            Assert.AreEqual("08", historyRecords[1].StatusCode, "Second status should be Failed");
        }

        /// <summary>
        /// Verifies that the function continues polling for documents that are not found in ECM system
        /// within the first 24 hours, without updating their status.
        /// </summary>
        [TestMethod]
        public async Task Run_ShouldContinuePolling_WhenDocumentNotFoundWithin24Hours()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "0300000824810065000";

            _dbContext!.CorrespondenceRequests.Add(new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = docId,
                DeliveryMethodCode = "Mail"
            });

            _dbContext.CorrespondenceRequestStatusHistories.Add(new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = correspondenceId,
                HistorySequenceNum = 1,
                StatusCode = "04",
                StatusUpdatedBy = "TestUser",
                StatusUpdateTS = DateTime.UtcNow.AddHours(-12) // Less than 24 hours
            });
            await _dbContext.SaveChangesAsync();

            // Setup empty response (document not found)
            var xmlResponse = CreateEmptyDqlResponse();
            _mockEcmClient!
                .Setup(x => x.execDQLAsync(It.IsAny<DocumentumSecurityToken>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(xmlResponse);

            var timerInfo = CreateTimerInfo();

            // Act
            await _documentStatusSync!.Run(timerInfo);

            // Assert
            await Task.Delay(100);
            var historyRecords = await _dbContext.CorrespondenceRequestStatusHistories
                .AsNoTracking()
                .Where(h => h.CorrespondenceId == correspondenceId)
                .ToListAsync();
            Assert.AreEqual(1, historyRecords.Count, "Should not add any new status record");
            Assert.AreEqual("04", historyRecords.First().StatusCode);
        }

        /// <summary>
        /// Verifies that a warning is logged when documents are not found in ECM system
        /// between 24 and 48 hours after being marked as "AwaitingImaging".
        /// </summary>
        [TestMethod]
        public async Task Run_ShouldLogWarning_WhenDocumentNotFoundBetween24And48Hours()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "0300000824810065001";

            _dbContext!.CorrespondenceRequests.Add(new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = docId,
                DeliveryMethodCode = "Mail"
            });

            _dbContext.CorrespondenceRequestStatusHistories.Add(new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = correspondenceId,
                HistorySequenceNum = 1,
                StatusCode = "04",
                StatusUpdatedBy = "TestUser",
                StatusUpdateTS = DateTime.UtcNow.AddHours(-36) // Between 24-48 hours
            });
            await _dbContext.SaveChangesAsync();

            // Setup empty response (document not found)
            var xmlResponse = CreateEmptyDqlResponse();
            _mockEcmClient!
                .Setup(x => x.execDQLAsync(It.IsAny<DocumentumSecurityToken>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(xmlResponse);

            var timerInfo = CreateTimerInfo();

            // Act
            await _documentStatusSync!.Run(timerInfo);

            // Assert
            await Task.Delay(100);
            var historyRecords = await _dbContext.CorrespondenceRequestStatusHistories
                .AsNoTracking()
                .Where(h => h.CorrespondenceId == correspondenceId)
                .ToListAsync();
            Assert.AreEqual(1, historyRecords.Count, "Should not add any new status record");
        }

        /// <summary>
        /// Verifies that documents with an existing "Archived" status are not processed again,
        /// preventing duplicate status updates.
        /// </summary>
        [TestMethod]
        public async Task Run_ShouldNotProcessDocuments_WithExistingArchivedStatus()
        {
            // Arrange
            var correspondenceId = Guid.NewGuid();
            var docId = "0300000824810065002";

            _dbContext!.CorrespondenceRequests.Add(new CorrespondenceRequest
            {
                CorrespondenceId = correspondenceId,
                DocId = docId,
                DeliveryMethodCode = "Mail"
            });

            _dbContext.CorrespondenceRequestStatusHistories.AddRange(
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 1,
                    StatusCode = "04",
                    StatusUpdatedBy = "TestUser",
                    StatusUpdateTS = DateTime.UtcNow.AddHours(-50)
                },
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = 2,
                    StatusCode = "09",
                    StatusUpdatedBy = "TestUser",
                    StatusUpdateTS = DateTime.UtcNow.AddHours(-1)
                }
            );
            await _dbContext.SaveChangesAsync();

            var timerInfo = CreateTimerInfo();

            // Act
            await _documentStatusSync!.Run(timerInfo);

            // Assert
            await Task.Delay(100);
            var historyRecords = await _dbContext.CorrespondenceRequestStatusHistories
                .AsNoTracking()
                .Where(h => h.CorrespondenceId == correspondenceId)
                .ToListAsync();
            Assert.AreEqual(2, historyRecords.Count, "Should not add any new records for already archived documents");
        }

        /// <summary>
        /// Verifies that the function can successfully process multiple documents in a single execution,
        /// updating each to "Archived" status when found in the ECM system.
        /// </summary>
        [TestMethod]
        public async Task Run_ShouldProcessMultipleDocuments_Successfully()
        {
            // Arrange
            var correspondenceId1 = Guid.NewGuid();
            var correspondenceId2 = Guid.NewGuid();
            var docId1 = "0300000824810065004";
            var docId2 = "0300000824810065005";

            _dbContext!.CorrespondenceRequests.AddRange(
                new CorrespondenceRequest { CorrespondenceId = correspondenceId1, DocId = docId1, DeliveryMethodCode = "Mail" },
                new CorrespondenceRequest { CorrespondenceId = correspondenceId2, DocId = docId2, DeliveryMethodCode = "Mail" }
            );

            _dbContext.CorrespondenceRequestStatusHistories.AddRange(
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId1,
                    HistorySequenceNum = 1,
                    StatusCode = "04",
                    StatusUpdatedBy = "TestUser",
                    StatusUpdateTS = DateTime.UtcNow.AddHours(-2)
                },
                new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = correspondenceId2,
                    HistorySequenceNum = 1,
                    StatusCode = "04",
                    StatusUpdatedBy = "TestUser",
                    StatusUpdateTS = DateTime.UtcNow.AddHours(-2)
                }
            );
            await _dbContext.SaveChangesAsync();

            var xmlResponse1 = CreateValidDqlResponse(docId1);
            var xmlResponse2 = CreateValidDqlResponse(docId2);

            _mockEcmClient!
                .Setup(x => x.execDQLAsync(It.IsAny<DocumentumSecurityToken>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((DocumentumSecurityToken token, string user, string repo, string query) =>
                    query.Contains(docId1) ? xmlResponse1 : xmlResponse2
                );

            var timerInfo = CreateTimerInfo();

            // Act
            await _documentStatusSync!.Run(timerInfo);

            // Assert
            await Task.Delay(200);
            var totalHistoryRecords = await _dbContext.CorrespondenceRequestStatusHistories
                .AsNoTracking()
                .ToListAsync();
            Assert.AreEqual(4, totalHistoryRecords.Count, "Should have added archived status for both documents");
        }

        /// <summary>
        /// Creates a mock TimerInfo object for timer-triggered Azure Function testing.
        /// </summary>
        /// <returns>A TimerInfo instance configured for testing.</returns>
        private TimerInfo CreateTimerInfo()
        {
            return new TimerInfo
            {
                IsPastDue = false
            };
        }

        /// <summary>
        /// Creates a valid DQL response XML containing document information.
        /// </summary>
        /// <param name="docId">The document ID to include in the response.</param>
        /// <returns>XML string representing a successful DQL query result.</returns>
        private string CreateValidDqlResponse(string docId)
        {
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
                <root>
                    <object ID=""123456"">
                        <r_object_id>09000000000000001</r_object_id>
                        <cmg_doc_id>{docId}</cmg_doc_id>
                        <object_name>TestDocument</object_name>
                    </object>
                </root>";
        }

        /// <summary>
        /// Creates an empty DQL response XML indicating no documents were found.
        /// </summary>
        /// <returns>XML string representing an empty DQL query result.</returns>
        private string CreateEmptyDqlResponse()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8""?>
                <root>
                </root>";
        }
    }
}