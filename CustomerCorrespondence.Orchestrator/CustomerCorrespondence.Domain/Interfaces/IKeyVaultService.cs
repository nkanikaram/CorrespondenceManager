using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Interfaces
{
    /// <summary>
    /// Provides access to Azure Key Vault for retrieving secrets.
    /// Implementations should handle transient errors with retry logic and provide comprehensive logging.
    /// </summary>
    public interface IKeyVaultService
    {
        /// <summary>
        /// Retrieves a secret value from Azure Key Vault by its name.
        /// </summary>
        /// <param name="secretName">The name of the secret to retrieve.</param>
        /// <returns>The value of the secret, or null if not found.</returns>
        /// <exception cref="CustomerCorrespondence.Exceptions.SecretNotFoundException">Thrown when the secret is not found.</exception>
        /// <exception cref="CustomerCorrespondence.Exceptions.CorrespondenceException">Thrown when other errors occur.</exception>
        Task<string?> GetSecretAsync(string secretName);
    }
}
