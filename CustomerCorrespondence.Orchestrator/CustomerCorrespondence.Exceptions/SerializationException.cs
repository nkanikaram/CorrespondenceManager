using System;

namespace CustomerCorrespondence.Exceptions
{
    /// <summary>
    /// Exception thrown when JSON serialization or deserialization fails.
    /// Used for message serialization, request/response parsing, and data conversion errors.
    /// </summary>
    public class SerializationException : CorrespondenceException
    {
        /// <summary>
        /// The type being serialized or deserialized.
        /// </summary>
        public Type? TargetType { get; set; }

        /// <summary>
        /// The operation that failed (e.g., "Serialize", "Deserialize").
        /// </summary>
        public string? Operation { get; set; }

        public SerializationException(string message, string errorCode)
            : base(message, errorCode)
        {
        }

        public SerializationException(string message, string errorCode, Exception innerException)
            : base(message, errorCode, innerException)
        {
        }

        public SerializationException(string message, string errorCode, Type targetType, string operation, Exception innerException)
            : base(message, errorCode, innerException)
        {
            TargetType = targetType;
            Operation = operation;
        }
    }
}