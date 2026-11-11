using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Core
{
    /// <summary>
    /// Represents the result of an operation with a return value.
    /// Provides a functional approach to error handling without exceptions.
    /// </summary>
    /// <typeparam name="T">The type of the value returned on success</typeparam>
    public class Result<T> : Result
    {
        /// <summary>
        /// The value returned by the operation if successful.
        /// </summary>
        public T? Value { get; protected set; }

        protected Result(T value) : base(true, (string?)null)
        {
            Value = value;
        }

        protected Result(string error, string? errorCode = null) : base(false, error, errorCode)
        {
        }

        protected Result(IEnumerable<string> errors, string? errorCode = null) : base(false, errors, errorCode)
        {
        }

        /// <summary>
        /// Creates a successful result with a value.
        /// </summary>
        public static Result<T> Success(T value) => new Result<T>(value);

        /// <summary>
        /// Creates a failed result with an error message.
        /// </summary>
        public new static Result<T> Failure(string error, string? errorCode = null)
        {
            return new Result<T>(error, errorCode);
        }

        /// <summary>
        /// Creates a failed result with multiple error messages.
        /// </summary>
        public new static Result<T> Failure(IEnumerable<string> errors, string? errorCode = null)
        {
            return new Result<T>(errors, errorCode);
        }

        /// <summary>
        /// Creates a failed result from an exception.
        /// </summary>
        public new static Result<T> Failure(Exception exception, string? errorCode = null)
        {
            return new Result<T>(exception.Message, errorCode ?? exception.GetType().Name);
        }
    }
}
