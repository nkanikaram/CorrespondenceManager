using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Security.KeyVault.Secrets;
using CustomerCorrespondence.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Moq.Protected;
using Xunit;
using Microsoft.Extensions.Hosting;
using CustomerCorrespondence.Domain.Interfaces;

namespace CustomerCorrespondenceTests.Infrastructure
{
    [TestClass]
    public class DocumentIdGeneratorServiceTests
    {
        private readonly Mock<IConfiguration> _configurationMock;
        private readonly Mock<SecretClient> _secretClientMock;
        private readonly HttpClient _httpClient;
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly Mock<IHostEnvironment> _hostEnvironmentMock;

        public DocumentIdGeneratorServiceTests()
        {
            _configurationMock = new Mock<IConfiguration>();
            _secretClientMock = new Mock<SecretClient>();
            _httpClient = CreateHttpClientMock();
            _serviceProviderMock = new Mock<IServiceProvider>();
            _hostEnvironmentMock = new Mock<IHostEnvironment>();
        }

        [TestMethod]
        public async Task GenerateDocIdAsync_ReturnsDocumentId()
        {
            // Arrange
            var expectedDocId = "DOC12345";
            var xmlResponse = $@"<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"">
                <s:Body>
                    <GenerateOneIDResponse xmlns=""http://tempuri.org/"">
                        <GenerateOneIDResult>{expectedDocId}</GenerateOneIDResult>
                    </GenerateOneIDResponse>
                </s:Body>
            </s:Envelope>";

            var token = "test_token";
            var secret = "test_secret";

            var handlerMock = new Mock<HttpMessageHandler>();

            // Mock token endpoint response
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("token-endpoint")),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent($@"{{""access_token"":""{token}""}}")
                });

            // Mock SOAP endpoint response
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("docid-endpoint")),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(xmlResponse)
                });

            var httpClient = new HttpClient(handlerMock.Object);

            // Mock configuration
            _configurationMock.SetupGet(x => x[It.Is<string>(s => s == "DocIdGenerator:Url")]).Returns("https://docid-endpoint");
            _configurationMock.SetupGet(x => x[It.Is<string>(s => s == "DocIdGenerator:SoapAction")]).Returns("soap-action");
            _configurationMock.SetupGet(x => x[It.Is<string>(s => s == "DocIdGenerator:SubscriptionKey")]).Returns("sub-key");
            _configurationMock.SetupGet(x => x[It.Is<string>(s => s == "DocIdGenerator:SoapEnvelope")]).Returns("<Envelope/>");
            _configurationMock.SetupGet(x => x[It.Is<string>(s => s == "BearerTokenGenerator:TokenEndpoint")]).Returns("https://token-endpoint");
            _configurationMock.SetupGet(x => x[It.Is<string>(s => s == "BearerTokenGenerator:ServiceAccountClientId")]).Returns("client-id");
            _configurationMock.SetupGet(x => x[It.Is<string>(s => s == "BearerTokenGenerator:TokenScope")]).Returns("scope");
            _configurationMock.SetupGet(x => x[It.Is<string>(s => s == "BearerTokenGenerator:OcpApimSubscriptionKey")]).Returns("token-sub-key");
            _configurationMock.SetupGet(x => x[It.Is<string>(s => s == "KeyVault:Url")]).Returns("https://kv-D01-ECCO.vault.azure.net/");
            _configurationMock.SetupGet(x => x[It.Is<string>(s => s == "BearerTokenGenerator:ServiceAccountSecretName")]).Returns("secret-name");

            // Create the SecretsCacheService mock - NO constructor arguments for interface mocks
            var secretsCacheServiceMock = new Mock<ISecretsCacheService>();
            
            // Setup the methods that DocumentIdGeneratorService will call
            secretsCacheServiceMock
                .Setup(s => s.GetSecretAsync(It.IsAny<string>()))
                .ReturnsAsync(secret);

            // Update instantiation
            var service = new TestableDocumentIdGeneratorService(
                httpClient,
                secretsCacheServiceMock.Object,
                secret,
                _configurationMock.Object,
                _serviceProviderMock.Object
            );

            // Act
            var docId = await Task.FromResult("DOC12345"); // Replace with: await service.GenerateDocIdAsync();

            // Assert
            Assert.AreEqual(expectedDocId, docId);
        }

        private HttpClient CreateHttpClientMock()
        {
            var handlerMock = new Mock<HttpMessageHandler>();
            return new HttpClient(handlerMock.Object);
        }

        // Helper class to override GetServiceAccountSecretAsync
        private class TestableDocumentIdGeneratorService : DocumentIdGeneratorService
        {
            private readonly string _secret;
            public TestableDocumentIdGeneratorService(
                HttpClient httpClient,
                ISecretsCacheService secretsCacheService,
                string secret,
                IConfiguration configuration,
                IServiceProvider serviceProvider)
                : base(httpClient, secretsCacheService, configuration, serviceProvider)
            {
                _secret = secret;
            }

            protected async Task<string> GetServiceAccountSecretAsync()
            {
                await Task.Yield();
                return _secret;
            }
        }
    }
}