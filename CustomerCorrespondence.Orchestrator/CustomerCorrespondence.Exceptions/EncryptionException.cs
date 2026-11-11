using System;

namespace CustomerCorrespondence.Exceptions
{
    /// <summary>
    /// Exception thrown when encryption or decryption operations fail.
    /// Used for AES encryption, PGP encryption, hashing, and key management errors.
    /// </summary>
    public class EncryptionException : CorrespondenceException
    {
        /// <summary>
        /// The type of encryption operation that failed (e.g., "AES", "PGP", "SHA256").
        /// </summary>
        public string? EncryptionType { get; set; }

        /// <summary>
        /// The operation that was being performed (e.g., "Encrypt", "Decrypt", "Hash").
        /// </summary>
        public string? Operation { get; set; }

        public EncryptionException(string message, string errorCode)
            : base(message, errorCode)
        {
        }

        public EncryptionException(string message, string errorCode, Exception innerException)
            : base(message, errorCode, innerException)
        {
        }

        public EncryptionException(string message, string errorCode, string encryptionType, string operation, Exception innerException)
            : base(message, errorCode, innerException)
        {
            EncryptionType = encryptionType;
            Operation = operation;
        }
    }
}