using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Core
{
    /// <summary>
    /// Standardized error response model for API failures.
    /// </summary>
    public class ErrorResponse
    {
        /// <summary>
        /// Error message describing what went wrong.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Error code for categorizing the error type.
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// Additional error details for debugging.
        /// </summary>
        public string? Details { get; set; }

        /// <summary>
        /// Timestamp when the error occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Validation errors if applicable.
        /// </summary>
        public Dictionary<string, List<string>>? ValidationErrors { get; set; }
    }
}
