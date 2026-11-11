using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Core
{
    /// <summary>
    /// Represents the result of an operation without a return value.
    /// Provides a functional approach to error handling without exceptions.
    /// </summary>
    public class Result
    {
        /// <summary>
        /// Indicates whether the operation was successful.
        /// </summary>
        public bool IsSuccess { get; protected set; }

        /// <summary>
        /// Indicates whether the operation failed.
        /// </summary>
        public bool IsFailure => !IsSuccess;

        /// <summary>
        /// Error message if the operation failed.
        /// </summary>
        public string? Error { get; protected set; }

        /// <summary>
        /// Collection of error messages for multiple failures.
        /// </summary>
        public IReadOnlyList<string> Errors { get; protected set; } = Array.Empty<string>();

        /// <summary>
        /// Error code for categorizing the failure.
        /// </summary>
        public string? ErrorCode { get; protected set; }

        protected Result(bool isSuccess, string? error, string? errorCode = null)
        {
            if (isSuccess && !string.IsNullOrEmpty(error))
                throw new InvalidOperationException("A successful result cannot have an error.");

            if (!isSuccess && string.IsNullOrEmpty(error))
                throw new InvalidOperationException("A failed result must have an error.");

            IsSuccess = isSuccess;
            Error = error;
            ErrorCode = errorCode;
        }

        protected Result(bool isSuccess, IEnumerable<string> errors, string? errorCode = null)
        {
            var errorList = errors?.ToList() ?? new List<string>();

            if (isSuccess && errorList.Any())
                throw new InvalidOperationException("A successful result cannot have errors.");

            if (!isSuccess && !errorList.Any())
                throw new InvalidOperationException("A failed result must have at least one error.");

            IsSuccess = isSuccess;
            Errors = errorList.AsReadOnly();
            Error = errorList.Any() ? string.Join("; ", errorList) : null;
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static Result Success() => new Result(true, (string?)null);

        /// <summary>
        /// Creates a failed result with an error message.
        /// </summary>
        public static Result Failure(string error, string? errorCode = null)
        {
            return new Result(false, error, errorCode);
        }

        /// <summary>
        /// Creates a failed result with multiple error messages.
        /// </summary>
        public static Result Failure(IEnumerable<string> errors, string? errorCode = null)
        {
            return new Result(false, errors, errorCode);
        }

        /// <summary>
        /// Creates a failed result from an exception.
        /// </summary>
        public static Result Failure(Exception exception, string? errorCode = null)
        {
            return new Result(false, exception.Message, errorCode ?? exception.GetType().Name);
        }

        /// <summary>
        /// Combines multiple results into a single result.
        /// Returns success only if all results are successful.
        /// </summary>
        public static Result Combine(params Result[] results)
        {
            if (results == null || !results.Any())
                return Success();

            var failures = results.Where(r => r.IsFailure).ToList();

            if (!failures.Any())
                return Success();

            var errors = failures.SelectMany(f =>
                f.Errors.Any() ? f.Errors : new[] { f.Error ?? "Unknown error" });

            return Failure(errors);
        }
    }
}
