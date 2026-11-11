using System;

namespace CustomerCorrespondence.Exceptions
{
    /// <summary>
    /// Exception thrown when a secret cannot be retrieved from Azure Key Vault.
    /// This includes secrets not found (404), access denied (403), throttling (429), and other retrieval failures.
    /// </summary>
    public class SecretNotFoundException : CorrespondenceException
    {
        /// <summary>
        /// The name of the secret that could not be retrieved.
        /// </summary>
        public string SecretName { get; }

        /// <summary>
        /// The HTTP status code from Key Vault, if applicable.
        /// </summary>
        public int? HttpStatusCode { get; set; }

        public SecretNotFoundException(string secretName)
            : base($"Secret '{secretName}' was not found in Key Vault.", "SECRET_NOT_FOUND")
        {
            SecretName = secretName;
        }

        public SecretNotFoundException(string secretName, Exception innerException)
            : base($"Secret '{secretName}' was not found in Key Vault.", "SECRET_NOT_FOUND", innerException)
        {
            SecretName = secretName;
        }

        public SecretNotFoundException(string secretName, string errorCode, string message, Exception innerException)
            : base(message, errorCode, innerException)
        {
            SecretName = secretName;
        }

        public SecretNotFoundException(string secretName, string errorCode, string message, int httpStatusCode, Exception innerException)
            : base(message, errorCode, innerException)
        {
            SecretName = secretName;
            HttpStatusCode = httpStatusCode;
        }
    }
}