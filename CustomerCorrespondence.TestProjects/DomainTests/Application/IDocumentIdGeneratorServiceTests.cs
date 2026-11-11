using System.Threading.Tasks;
using CustomerCorrespondence.Application.Interfaces;


namespace CustomerCorrespondenceTests.Application
{
    [TestClass]
    public class IDocumentIdGeneratorServiceTests
    {
        private class TestDocumentIdGeneratorService : IDocumentIdGeneratorService
        {
            public Task<string?> GenerateDocIdAsync()
            {
                // Example implementation for testing
                return Task.FromResult<string?>("DOC-12345");
            }
        }

        [TestMethod]
        public async Task GenerateDocIdAsync_Returns_NonNullOrEmptyString()
        {
            // Arrange
            IDocumentIdGeneratorService service = new TestDocumentIdGeneratorService();

            // Act
            var docId = await service.GenerateDocIdAsync();

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(docId));
        }
    }
}