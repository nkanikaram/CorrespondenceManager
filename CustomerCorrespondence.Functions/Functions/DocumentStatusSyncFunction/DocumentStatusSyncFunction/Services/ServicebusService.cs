using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace DocumentStatusSyncFunction.Services
{
    /// <summary>
    /// Service for sending messages to Azure Service Bus queues
    /// </summary>
    public class ServicebusService : IServicebusService
    {
        private readonly ServiceBusClient? _serviceBusClient;
        private readonly ILogger<ServicebusService> _logger;

        public ServicebusService(ServiceBusClient? serviceBusClient, ILogger<ServicebusService> logger)
        {
            _serviceBusClient = serviceBusClient;
            _logger = logger;
        }

        public async Task SendMessageAsync(string queueName, string messageBody)
        {
            // Check if Service Bus client is available
            if (_serviceBusClient == null)
            {
                _logger.LogWarning("Service Bus client is not configured. Message will not be sent to queue '{QueueName}'. Message: {MessageBody}", queueName, messageBody);
                return;
            }

            ServiceBusSender? sender = null;
            try
            {
                sender = _serviceBusClient.CreateSender(queueName);
                ServiceBusMessage message = new ServiceBusMessage(messageBody);
                
                await sender.SendMessageAsync(message);
                
                _logger.LogInformation("Successfully sent message to queue '{QueueName}'", queueName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to Service Bus queue '{QueueName}'", queueName);
                throw;
            }
            finally
            {
                if (sender != null)
                {
                    await sender.DisposeAsync();
                }
            }
        }
    }
}
