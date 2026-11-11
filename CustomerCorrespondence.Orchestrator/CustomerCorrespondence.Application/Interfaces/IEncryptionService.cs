using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Application.Interfaces
{
    /// <summary>
    /// Interface for a service that provides encryption and decryption functionality for sensitive data in customer correspondence. This service abstracts the underlying encryption mechanism, allowing for flexibility in implementation (e.g., using Azure Key Vault, local encryption libraries, etc.) while ensuring that sensitive information is securely handled throughout the application. It includes methods for encrypting and decrypting text, as well as generating deterministic hashes for database searches without exposing the original plaintext values.
    /// </summary>
    public interface IEncryptionService
    {
        /// <summary>
        /// Encrypts the specified plaintext.
        /// </summary>
        /// <param name="plainText">The plaintext to encrypt.</param>
        /// <returns>The encrypted text.</returns>
        Task<string> EncryptAsync(string plainText);
            
        /// <summary>
        /// Decrypts the specified encrypted text.
        /// </summary>
        /// <param name="encryptedText">The encrypted text to decrypt.</param>
        /// <returns>The decrypted plaintext.</returns>
        Task<string> DecryptAsync(string encryptedText);

        /// <summary>
        /// Generates a deterministic hash for database searches
        /// </summary>
        byte[] GenerateSearchHash(string plainText);
    }
}
