using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Core
{
    /// <summary>
    /// Standard response wrapper for API operations providing consistent success/failure handling.
    /// </summary>
    /// <typeparam name="T">The type of data being returned</typeparam>
    public class BaseResponse<T>
    {
        /// <summary>
        /// Indicates whether the operation was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The data payload returned by the operation.
        /// </summary>
        public T? Data { get; set; }

        /// <summary>
        /// Error message if the operation failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Additional error details for debugging.
        /// </summary>
        public string? ErrorDetails { get; set; }

        /// <summary>
        /// Validation errors if applicable.
        /// </summary>
        public Dictionary<string, List<string>>? ValidationErrors { get; set; }

        /// <summary>
        /// Timestamp when the response was created.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Creates a successful response with data.
        /// </summary>
        public static BaseResponse<T> CreateSuccess(T data)
        {
            return new BaseResponse<T>
            {
                Success = true,
                Data = data
            };
        }

        /// <summary>
        /// Creates a failure response with an error message.
        /// </summary>
        public static BaseResponse<T> CreateFailure(string errorMessage, string? errorDetails = null)
        {
            return new BaseResponse<T>
            {
                Success = false,
                ErrorMessage = errorMessage,
                ErrorDetails = errorDetails
            };
        }

        /// <summary>
        /// Creates a failure response with validation errors.
        /// </summary>
        public static BaseResponse<T> CreateValidationFailure(Dictionary<string, List<string>> validationErrors)
        {
            return new BaseResponse<T>
            {
                Success = false,
                ErrorMessage = "Validation failed",
                ValidationErrors = validationErrors
            };
        }

        /// <summary>
        /// Creates a failure response from an exception.
        /// </summary>
        public static BaseResponse<T> CreateFailure(string errorMessage, Exception exception)
        {
            return new BaseResponse<T>
            {
                Success = false,
                ErrorMessage = errorMessage,
                ErrorDetails = exception.Message
            };
        }
    }
}
