using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CustomerCorrespondence.Exceptions; // Add this using directive
using CustomerCorrespondence.Domain.Interfaces;

namespace CustomerCorrespondence.Core.Extensions
{
    /// <summary>
    /// Extension methods for KeyVault operations using the Result pattern for functional error handling.
    /// </summary>
    public static class KeyVaultResultExtensions
    {
        /// <summary>
        /// Retrieves a secret and returns a Result with error handling.
        /// </summary>
        /// <param name="keyVaultService">The KeyVault service instance.</param>
        /// <param name="secretName">The name of the secret to retrieve.</param>
        /// <returns>A Result containing the secret value or error information.</returns>
        public static async Task<Result<string>> GetSecretAsResultAsync(
            this IKeyVaultService keyVaultService, // Fix type reference
            string secretName)
        {
            try
            {
                var secret = await keyVaultService.GetSecretAsync(secretName);
                
                if (string.IsNullOrEmpty(secret))
                    return Result<string>.Failure($"Secret '{secretName}' is empty or null", "SECRET_EMPTY");
                
                return Result<string>.Success(secret);
            }
            catch (SecretNotFoundException ex)
            {
                return Result<string>.Failure($"Secret '{secretName}' not found", ex.ErrorCode ?? "SECRET_NOT_FOUND");
            }
            catch (CorrespondenceException ex)
            {
                return Result<string>.Failure(ex.Message, ex.ErrorCode ?? "KEYVAULT_ERROR");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure($"Unexpected error retrieving secret: {ex.Message}", "KEYVAULT_UNEXPECTED");
            }
        }

        /// <summary>
        /// Retrieves a secret and returns a Result with a default value on failure.
        /// </summary>
        public static async Task<Result<string>> GetSecretOrDefaultAsync(
            this IKeyVaultService keyVaultService, // Fix type reference
            string secretName,
            string defaultValue)
        {
            try
            {
                var secret = await keyVaultService.GetSecretAsync(secretName);
                return Result<string>.Success(secret ?? defaultValue);
            }
            catch (SecretNotFoundException)
            {
                return Result<string>.Success(defaultValue);
            }
            catch (CorrespondenceException ex)
            {
                return Result<string>.Failure(ex.Message, ex.ErrorCode ?? "KEYVAULT_ERROR");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure($"Unexpected error: {ex.Message}", "KEYVAULT_UNEXPECTED");
            }
        }
    }
}
