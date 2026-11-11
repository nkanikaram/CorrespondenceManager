using System;

namespace CustomerCorrespondence.Exceptions
{
    /// <summary>
    /// Exception thrown when document ID generation or retrieval fails.
    /// Used for Documentum ID pool exhaustion, generation errors, or allocation issues.
    /// </summary>
    public class DocumentIdGenerationException : CorrespondenceException
    {
        /// <summary>
        /// The document ID that was being generated or retrieved, if available.
        /// </summary>
        public string? DocumentId { get; set; }

        /// <summary>
        /// The source of the document ID (e.g., "Pool", "Generator", "Database").
        /// </summary>
        public string? Source { get; set; }

        public DocumentIdGenerationException(string message, string errorCode)
            : base(message, errorCode)
        {
        }

        public DocumentIdGenerationException(string message, string errorCode, Exception innerException)
            : base(message, errorCode, innerException)
        {
        }

        public DocumentIdGenerationException(string message, string errorCode, string documentId, string source, Exception innerException)
            : base(message, errorCode, innerException)
        {
            DocumentId = documentId;
            Source = source;
        }
    }
}