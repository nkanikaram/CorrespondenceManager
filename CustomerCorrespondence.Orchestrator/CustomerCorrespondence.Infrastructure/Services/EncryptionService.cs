using CustomerCorrespondence.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace CustomerCorrespondence.Infrastructure.Services
{
    public class EncryptionService : IEncryptionService
    {
        private readonly IPgpEncryptionService _pgpEncryptionService;

        public EncryptionService(IPgpEncryptionService pgpEncryptionService)
        {
            _pgpEncryptionService = pgpEncryptionService ?? throw new ArgumentNullException(nameof(pgpEncryptionService));
        }

        /// <summary>
        /// Encrypts plain text using PGP encryption with keys from Azure Key Vault
        /// </summary>
        /// <param name="plainText">The text to encrypt</param>
        /// <returns>PGP encrypted text (ASCII armored)</returns>
        public async Task<string> EncryptAsync(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                return await _pgpEncryptionService.EncryptAsync(plainText);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"PGP encryption failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Decrypts PGP encrypted text using private key from Azure Key Vault
        /// </summary>
        /// <param name="encryptedText">PGP encrypted text (ASCII armored)</param>
        /// <returns>The decrypted plain text</returns>
        public async Task<string> DecryptAsync(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return encryptedText;

            try
            {
                return await _pgpEncryptionService.DecryptAsync(encryptedText);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"PGP decryption failed: {ex.Message}", ex);
            }
        }
        /// <summary>
        /// Generates a SHA-256 hash for searchable fields (deterministic)
        /// </summary>
        public byte[] GenerateSearchHash(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return Array.Empty<byte>();

            try
            {
                using var sha256 = SHA256.Create();
                var inputBytes = Encoding.UTF8.GetBytes(plainText.ToUpperInvariant()); // Normalize case
                return sha256.ComputeHash(inputBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Hash generation failed: {ex.Message}", ex);
            }
        }
    }
}
