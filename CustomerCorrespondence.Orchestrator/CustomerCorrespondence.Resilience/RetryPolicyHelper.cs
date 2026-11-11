using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace CustomerCorrespondence.Resilience
{
    /// <summary>
    /// Provides reusable retry policies for resilient operations.
    /// </summary>
    public static class RetryPolicyHelper
    {
        /// <summary>
        /// Creates a standard exponential backoff retry policy for HTTP operations.
        /// </summary>
        public static AsyncRetryPolicy<HttpResponseMessage> CreateHttpRetryPolicy(
            int retryCount = 3,
            int initialDelaySeconds = 1)
        {
            return Policy
                .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
                .Or<HttpRequestException>()
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromSeconds(initialDelaySeconds * Math.Pow(2, retryAttempt - 1)));
        }

        /// <summary>
        /// Creates a retry policy for transient errors with logging.
        /// </summary>
        public static AsyncRetryPolicy CreateRetryPolicyWithLogging(
            ILogger logger,
            int retryCount = 3,
            int initialDelaySeconds = 1)
        {
            return Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromSeconds(initialDelaySeconds * Math.Pow(2, retryAttempt - 1)),
                    onRetry: (exception, timeSpan, retry, ctx) =>
                    {
                        logger.LogWarning(
                            exception,
                            "Retry {Retry} after {Delay}s delay due to: {Message}",
                            retry,
                            timeSpan.TotalSeconds,
                            exception.Message);
                    });
        }

        /// <summary>
        /// Executes an async operation with retry policy.
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            int retryCount = 3,
            int initialDelaySeconds = 1)
        {
            var policy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromSeconds(initialDelaySeconds * Math.Pow(2, retryAttempt - 1)));

            return await policy.ExecuteAsync(operation);
        }
    }
}
