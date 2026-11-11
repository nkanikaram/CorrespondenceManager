using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using Moq;
using static CustomerCorrespondence.Domain.Entities.DataRecords;


namespace CustomerCorrespondenceTests.Application
{
    [TestClass]
    public class IVentureFusionServiceTests
    {
        private readonly Mock<IVentureFusionService> _serviceMock;

        public IVentureFusionServiceTests()
        {
            _serviceMock = new Mock<IVentureFusionService>();
        }

        [TestMethod]
        public async Task CheckHealth_Returns_HttpResponseMessage()
        {
            // Arrange
            var expectedResponse = new HttpResponseMessage(HttpStatusCode.OK);
            _serviceMock.Setup(s => s.CheckHealth()).ReturnsAsync(expectedResponse);

            // Act
            var result = await _serviceMock.Object.CheckHealth();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        }

        [TestMethod]
        public async Task PostDataRecords_Returns_HttpResponseMessage()
        {
            // Arrange
            var request = new SequentialProcess
            {
                RequestingSystemName = "TestSystem",
                ECCOTrackingPolicyNumber = "123456789012",
                ECCOTrackingClaimNumber = "987654321012",
                DeliveryMethod = "Email",
                MailDeliveryInfo = new MailDeliveryInfo
                {
                    RecipientAddress = new RecipientAddress()
                },
                CompositionInfo = new CompositionInfo
                {
                    VersionCode = "v1",
                   // PDFCompositionSolutionId = "sol1",
                   // CompositionData = new Dictionary<string, string>()
                }
                //ArchiveInfo = new ArchiveInfo
                //{
                //    DocumentTypeCode = "TYPE",
                //    DocumentSubTypeCode = "SUBTYPE"
                //}
            };
            var expectedResponse = new HttpResponseMessage(HttpStatusCode.Accepted);
            _serviceMock.Setup(s => s.PostDataRecords(request)).ReturnsAsync(expectedResponse);

            // Act
            var result = await _serviceMock.Object.PostDataRecords(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.Accepted, result.StatusCode);
        }

        [TestMethod]
        public async Task GetBearerToken_Returns_Token()
        {
            // Arrange
            var expectedToken = new VentureFusionBearerToken
            {
                token_type = "Bearer",
                access_token = "abc123",
                expires_in = 3600,
                ext_expires_in = 3600
            };
            _serviceMock.Setup(s => s.GetBearerToken()).ReturnsAsync(expectedToken);

            // Act
            var result = await _serviceMock.Object.GetBearerToken();

            // Assert
            Assert.AreEqual("Bearer", result.token_type);
            Assert.AreEqual("abc123", result.access_token);
        }

        [TestMethod]
        public void SetBearerTokenOnVentureFusionClient_Sets_Token()
        {
            // Arrange
            var token = new VentureFusionBearerToken
            {
                token_type = "Bearer",
                access_token = "xyz789",
                expires_in = 3600,
                ext_expires_in = 3600
            };

            // Act
            _serviceMock.Object.SetBearerTokenOnVentureFusionClient(token);

            // Assert
            _serviceMock.Verify(s => s.SetBearerTokenOnVentureFusionClient(token), Times.Once);
        }

        [TestMethod]
        public void MapCorrespondenceRequestToVentureFusionRequest_Returns_DataRecord()
        {
            // Arrange
            var request = new SequentialProcess
            {
                RequestingSystemName = "TestSystem",
                ECCOTrackingPolicyNumber = "123456789012",
                ECCOTrackingClaimNumber = "987654321012",
                DeliveryMethod = "Email",
                MailDeliveryInfo = new MailDeliveryInfo
                {
                    RecipientAddress = new RecipientAddress()
                },
                CompositionInfo = new CompositionInfo
                {
                    VersionCode = "v1",
                   // PDFCompositionSolutionId = "sol1",
                   // CompositionData = new Dictionary<string, string>()
                }
                //ArchiveInfo = new ArchiveInfo
                //{
                //    DocumentTypeCode = "TYPE",
                //    DocumentSubTypeCode = "SUBTYPE"
                //}
            };
            var expectedDataRecord = new DataRecord
            {
                orderId = "order1",
                usageId = 1,
                solutionsId = "sol1",
                subjectHandle = "handle",
                numberOfRecords = 1,
                records = new Record[0]
            };
            _serviceMock.Setup(s => s.MapCorrespondenceRequestToVentureFusionRequest(request)).Returns(expectedDataRecord);

            // Act
            var result = _serviceMock.Object.MapCorrespondenceRequestToVentureFusionRequest(request);

            // Assert
            Assert.AreEqual("order1", result.orderId);
            Assert.AreEqual(1, result.usageId);
        }
    }
}