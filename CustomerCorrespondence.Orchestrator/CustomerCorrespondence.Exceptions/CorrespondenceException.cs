using System;

namespace CustomerCorrespondence.Exceptions
{
    /// <summary>
    /// Base exception for correspondence-related errors.
    /// </summary>
    public class CorrespondenceException : Exception
    {
        /// <summary>
        /// Error code for categorizing the exception.
        /// </summary>
        public string? ErrorCode { get; set; }

        public CorrespondenceException()
        {
        }

        public CorrespondenceException(string message) : base(message)
        {
        }

        public CorrespondenceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public CorrespondenceException(string message, string errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        public CorrespondenceException(string message, string errorCode, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }
}