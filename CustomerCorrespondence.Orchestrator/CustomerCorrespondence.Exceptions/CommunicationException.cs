namespace CustomerCorrespondence.Exceptions
{
    /// <summary>
    /// Exception thrown when communication with external services fails.
    /// Used for HTTP communication, API calls, Service Bus, and other network/messaging operations.
    /// </summary>
    public class CommunicationException : CorrespondenceException
    {
        /// <summary>
        /// The HTTP status code associated with the communication failure, if applicable.
        /// </summary>
        public int? HttpStatusCode { get; set; }

        /// <summary>
        /// The service or endpoint that failed (e.g., "VentureFusion API", "Azure Service Bus").
        /// </summary>
        public string? ServiceName { get; set; }

        /// <summary>
        /// Creates a new CommunicationException with a message.
        /// </summary>
        /// <param name="message">The error message</param>
        public CommunicationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates a new CommunicationException with a message and error code.
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="errorCode">The error code for categorizing the exception</param>
        public CommunicationException(string message, string errorCode) : base(message, errorCode)
        {
        }

        /// <summary>
        /// Creates a new CommunicationException with a message, error code, and inner exception.
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="errorCode">The error code for categorizing the exception</param>
        /// <param name="innerException">The underlying exception that caused this error</param>
        public CommunicationException(string message, string errorCode, Exception innerException)
            : base(message, errorCode, innerException)
        {
        }

        /// <summary>
        /// Creates a new CommunicationException with message, error code, service name, and inner exception.
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="errorCode">The error code for categorizing the exception</param>
        /// <param name="serviceName">The name of the service that failed</param>
        /// <param name="innerException">The underlying exception that caused this error</param>
        public CommunicationException(string message, string errorCode, string serviceName, Exception innerException)
            : base(message, errorCode, innerException)
        {
            ServiceName = serviceName;
        }

        /// <summary>
        /// Creates a new CommunicationException with full details including HTTP status code.
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="errorCode">The error code for categorizing the exception</param>
        /// <param name="serviceName">The name of the service that failed</param>
        /// <param name="httpStatusCode">The HTTP status code if applicable</param>
        /// <param name="innerException">The underlying exception that caused this error</param>
        public CommunicationException(
            string message,
            string errorCode,
            string serviceName,
            int httpStatusCode,
            Exception innerException)
            : base(message, errorCode, innerException)
        {
            ServiceName = serviceName;
            HttpStatusCode = httpStatusCode;
        }
    }
}