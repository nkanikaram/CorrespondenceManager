using System;

namespace CustomerCorrespondence.Exceptions
{
    /// <summary>
    /// Exception thrown when a requested resource or entity is not found.
    /// Used for database queries, API calls, and resource lookups that return no results.
    /// </summary>
    public class NotFoundException : CorrespondenceException
    {
        /// <summary>
        /// The type of resource that was not found (e.g., "CorrespondenceRequest", "Document").
        /// </summary>
        public string? ResourceType { get; set; }

        /// <summary>
        /// The identifier used to search for the resource (e.g., DocId, CorrespondenceId).
        /// </summary>
        public string? Identifier { get; set; }

        public NotFoundException(string message, string errorCode)
            : base(message, errorCode)
        {
        }

        public NotFoundException(string message, string errorCode, Exception innerException)
            : base(message, errorCode, innerException)
        {
        }

        public NotFoundException(string message, string errorCode, string resourceType, string identifier)
            : base(message, errorCode)
        {
            ResourceType = resourceType;
            Identifier = identifier;
        }
    }
}