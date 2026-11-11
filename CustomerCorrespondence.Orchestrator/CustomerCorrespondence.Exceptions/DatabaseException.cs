namespace CustomerCorrespondence.Exceptions
{
    /// <summary>
    /// Exception thrown when database operations fail.
    /// Used for SQL errors, connection issues, constraint violations, and data access errors.
    /// </summary>
    public class DatabaseException : CorrespondenceException
    {
        /// <summary>
        /// The SQL error code, if available.
        /// </summary>
        public int? SqlErrorCode { get; set; }

        /// <summary>
        /// The database operation that failed (e.g., "Insert", "Update", "Delete", "Query").
        /// </summary>
        public string? Operation { get; set; }

        /// <summary>
        /// The table or entity involved in the operation.
        /// </summary>
        public string? TableName { get; set; }

        /// <summary>
        /// Creates a new DatabaseException with a message.
        /// </summary>
        /// <param name="message">The error message</param>
        public DatabaseException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates a new DatabaseException with a message and error code.
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="errorCode">The error code for categorizing the exception</param>
        public DatabaseException(string message, string errorCode) : base(message, errorCode)
        {
        }

        /// <summary>
        /// Creates a new DatabaseException with a message, error code, and inner exception.
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="errorCode">The error code for categorizing the exception</param>
        /// <param name="innerException">The underlying exception that caused this error</param>
        public DatabaseException(string message, string errorCode, Exception innerException)
            : base(message, errorCode, innerException)
        {
        }

        /// <summary>
        /// Creates a new DatabaseException with full details.
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="errorCode">The error code for categorizing the exception</param>
        /// <param name="operation">The database operation that failed</param>
        /// <param name="tableName">The table or entity involved</param>
        /// <param name="innerException">The underlying exception that caused this error</param>
        public DatabaseException(
            string message,
            string errorCode,
            string operation,
            string tableName,
            Exception innerException)
            : base(message, errorCode, innerException)
        {
            Operation = operation;
            TableName = tableName;
        }
    }
}