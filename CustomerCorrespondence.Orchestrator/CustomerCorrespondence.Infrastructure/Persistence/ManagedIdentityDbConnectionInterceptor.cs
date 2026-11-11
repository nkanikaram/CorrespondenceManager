using Azure.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Infrastructure.Persistence
{
    public class ManagedIdentityDbConnectionInterceptor : DbConnectionInterceptor
    {
        private readonly TokenCredential _credential;
        private readonly ILogger _logger;
        private const string AzureSqlScope = "https://database.windows.net/.default";

        public ManagedIdentityDbConnectionInterceptor(TokenCredential credential, ILogger logger)
        {
            _credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (connection is SqlConnection sqlConnection)
            {
                try
                {
                    _logger.LogDebug("Acquiring Azure AD token for database connection using managed identity");

                    var tokenRequestContext = new TokenRequestContext(new[] { AzureSqlScope });
                    var token = await _credential.GetTokenAsync(tokenRequestContext, cancellationToken);

                    sqlConnection.AccessToken = token.Token;
                    _logger.LogDebug("Azure AD token successfully applied to database connection");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to acquire Azure AD token for database connection");
                    throw new InvalidOperationException("Failed to authenticate with managed identity for database", ex);
                }
            }

            return await base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
        }

        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result)
        {
            if (connection is SqlConnection sqlConnection)
            {
                try
                {
                    _logger.LogDebug("Acquiring Azure AD token for database connection using managed identity (sync)");

                    var tokenRequestContext = new TokenRequestContext(new[] { AzureSqlScope });
                    var token = _credential.GetTokenAsync(tokenRequestContext, CancellationToken.None).GetAwaiter().GetResult();

                    sqlConnection.AccessToken = token.Token;
                    _logger.LogDebug("Azure AD token successfully applied to database connection (sync)");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to acquire Azure AD token for database connection (sync)");
                    throw new InvalidOperationException("Failed to authenticate with managed identity for database", ex);
                }
            }

            return base.ConnectionOpening(connection, eventData, result);
        }
    }
}
