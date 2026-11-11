using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class DataRecords
    {
        public class DataRecord
        {
            public string? orderId { get; set; }
            public int usageId { get; set; }
            public string? solutionsId { get; set; }
            public string? subjectHandle { get; set; }
            public int numberOfRecords { get; set; }
            public Record[]? records { get; set; }
        }

        public class Record
        {
            public string? recordHandle { get; set; }
            // Changed to Dictionary to support dynamic properties
            public Dictionary<string, object>? additionalData { get; set; }
            public string? additionalDataSchemaId { get; set; }
        }
    }
}
