using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CustomerCorrespondence.Domain.Entities;
using Xunit;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class CorrespondenceRequestStatusHistoryTests
    {
        [TestMethod]
        public void Can_Create_CorrespondenceRequestStatusHistory_With_Valid_Properties()
        {
            // Arrange
            var id = Guid.NewGuid();
            var correspondenceId = Guid.NewGuid();
            var statusCode = "SENT";
            var updatedBy = "user1";
            var updateTs = DateTime.UtcNow;

            // Act
            var history = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = id,
                CorrespondenceId = correspondenceId,
                HistorySequenceNum = 1,
                StatusCode = statusCode,
                StatusUpdatedBy = updatedBy,
                StatusUpdateTS = updateTs
            };

            // Assert
            Assert.AreEqual(id, history.CorrespondenceRequestStatusHistoryId);
            Assert.AreEqual(correspondenceId, history.CorrespondenceId);
            Assert.AreEqual(1, history.HistorySequenceNum);
            Assert.AreEqual(statusCode, history.StatusCode);
            Assert.AreEqual(updatedBy, history.StatusUpdatedBy);
            Assert.AreEqual(updateTs, history.StatusUpdateTS);
        }

        [TestMethod]
        public void StatusCode_Is_Required()
        {
            // Arrange
            var history = new CorrespondenceRequestStatusHistory();

            // Act & Assert
            var context = new ValidationContext(history);
            var result = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(history, context, result, true);

            Assert.IsTrue(result.Any(r => r.MemberNames.Contains(nameof(CorrespondenceRequestStatusHistory.StatusCode))));
            Assert.IsFalse(isValid);
        }
    }
}