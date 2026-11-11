using System;
using System.Collections.Generic;
using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class SequentialProcessRequestTests
    {
        [TestMethod]
        public void Validate_ThrowsException_WhenProcessIsNull()
        {
            // Arrange
            var request = new SequentialProcessRequest
            {
                Process = null!,
                Route = new List<string> { "EndpointA", "EndpointB" }
            };

            // Act & Assert
            var ex = Assert.ThrowsException<InvalidOperationException>(() => request.Validate());
            Assert.AreEqual("Process is required and cannot be null.", ex.Message);
        }

        [TestMethod]
        public void Validate_DoesNotThrow_WhenProcessIsSet()
        {
            // Arrange
            var process = new SequentialProcess
            {
                DocId = "1234567890123456789",
                RequestingSystemName = "TestSystem",
                ECCOTrackingPolicyNumber = "PN1234567890",
                ECCOTrackingClaimNumber = "CL1234567890",
                DeliveryMethod = "Email",
                MailDeliveryInfo = new MailDeliveryInfo
                {
                    RecipientAddress = new RecipientAddress()
                },
                CompositionInfo = new CompositionInfo
                {
                    VersionCode = "v1",
                   // PDFCompositionSolutionId = "sol1",
                    //CompositionData = new Dictionary<string, string>()
                }
                //ArchiveInfo = new ArchiveInfo
                //{
                //    DocumentTypeCode = "TYPE",
                //    DocumentSubTypeCode = "SUBTYPE"
                //}
            };

            var request = new SequentialProcessRequest
            {
                Process = process,
                Route = new List<string> { "EndpointA", "EndpointB" }
            };

            // Act & Assert
            request.Validate();
        }
    }
}