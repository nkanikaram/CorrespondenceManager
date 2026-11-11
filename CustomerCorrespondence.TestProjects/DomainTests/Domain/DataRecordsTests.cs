using System;
using Xunit;
using CustomerCorrespondence.Domain.Entities;

namespace CustomerCorrespondenceTests.Domain
{
    [TestClass]
    public class DataRecordsTests
    {
        [TestMethod]
        public void DataRecord_CanSetAndGetProperties()
        {
            // Arrange
            var record = new DataRecords.Record
            {
                recordHandle = "rec123",
                additionalDataSchemaId = "schema1"
            };

            var dataRecord = new DataRecords.DataRecord
            {
                orderId = "order123",
                usageId = 42,
                solutionsId = "sol123",
                subjectHandle = "subj456",
                numberOfRecords = 1,
                records = new[] { record }
            };

            // Assert
            Assert.AreEqual("order123", dataRecord.orderId);
            Assert.AreEqual(42, dataRecord.usageId);
            Assert.AreEqual("sol123", dataRecord.solutionsId);
            Assert.AreEqual("subj456", dataRecord.subjectHandle);
            Assert.AreEqual(1, dataRecord.numberOfRecords);
            Assert.IsNotNull(dataRecord.records);
            Assert.AreEqual(1, dataRecord.records.Length);
            var rec = dataRecord.records[0];
            Assert.AreEqual("rec123", rec.recordHandle);
            Assert.AreEqual("schema1", rec.additionalDataSchemaId);
        }

        [TestMethod]
        public void DataRecord_RecordsCanBeNull()
        {
            var dataRecord = new DataRecords.DataRecord
            {
                records = null
            };

            Assert.IsNull(dataRecord.records);
        }

        [TestMethod]
        public void Record_PropertiesDefaultToNull()
        {
            var record = new DataRecords.Record();

            Assert.IsNull(record.recordHandle);
            Assert.IsNull(record.additionalData);
            Assert.IsNull(record.additionalDataSchemaId);
        }
    }
}