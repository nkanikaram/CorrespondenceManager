using System;
using System.Collections.Generic;

namespace CustomerCorrespondence.Exceptions
{
    /// <summary>
    /// Exception thrown when input validation fails.
    /// </summary>
    public class ValidationException : CorrespondenceException
    {
        /// <summary>
        /// Dictionary of validation errors by field name.
        /// </summary>
        public Dictionary<string, List<string>> ValidationErrors { get; }

        public ValidationException(string message)
            : base(message, "VALIDATION_ERROR")
        {
            ValidationErrors = new Dictionary<string, List<string>>();
        }

        public ValidationException(string message, Dictionary<string, List<string>> validationErrors)
            : base(message, "VALIDATION_ERROR")
        {
            ValidationErrors = validationErrors;
        }
    }
}