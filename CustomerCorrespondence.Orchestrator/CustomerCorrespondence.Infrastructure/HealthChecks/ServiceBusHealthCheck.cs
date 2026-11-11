using Azure.Identity;
using Azure.Messaging.ServiceBus;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Infrastructure.HealthChecks
{
    public class ServiceBusHealthCheck : IHealthCheck
    {
        private readonly ISecretsCacheService _secretsCache;
        private readonly IConfiguration _configuration;

        public ServiceBusHealthCheck(ISecretsCacheService secretsCache, IConfiguration configuration)
        {
            _secretsCache = secretsCache;
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {

            //var serviceBusNamespaceFqdn = await _secretsCache.GetSecretAsync("AzureServiceBusFQURL");
            // Get the name of the secret from configuration to match existing pattern and avoid CVE 259 from Veracode
            var azureServiceBusFQURLName = _configuration["SecretNames:AzureServiceBusFQURL"];
            if (string.IsNullOrEmpty(azureServiceBusFQURLName))
                throw new InvalidOperationException("SecretNames:AzureServiceBusFQURL is not configured");
            var serviceBusNamespaceFqdn = await _secretsCache.GetSecretAsync(azureServiceBusFQURLName);

            var queueName = "eccofailedmessagesqueue";
            try
            {
                var client = new ServiceBusClient(serviceBusNamespaceFqdn, new DefaultAzureCredential());

                // Check active queue for messages
                var receiver = client.CreateReceiver(queueName);
                var message = await receiver.PeekMessageAsync(cancellationToken: cancellationToken);

                if (message != null)
                {
                    string body = message.Body.ToString();
                    return HealthCheckResult.Healthy($"Peeked message body: {body}");
                }
                else
                {
                    // If no active messages, check dead-letter queue
                    var dlqReceiver = client.CreateReceiver(
                        queueName,
                        new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter }
                    );
                    var dlqMessage = await dlqReceiver.PeekMessageAsync(cancellationToken: cancellationToken);

                    if (dlqMessage != null)
                    {
                        string dlqBody = dlqMessage.Body.ToString();
                        string reason = dlqMessage.DeadLetterReason ?? "N/A";
                        string errorDesc = dlqMessage.DeadLetterErrorDescription ?? "N/A";
                        return HealthCheckResult.Degraded($"No active messages. Dead-letter message body: {dlqBody}, Reason: {reason}, Error: {errorDesc}");
                    }
                    else
                    {
                        return HealthCheckResult.Healthy("No messages available in active or dead-letter queue.");
                    }
                }
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("ServiceBus unreachable or unauthorized.", ex);
            }
        }
    }
}
