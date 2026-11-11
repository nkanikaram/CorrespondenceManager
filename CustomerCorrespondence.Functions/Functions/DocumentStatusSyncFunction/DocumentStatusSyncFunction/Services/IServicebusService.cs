using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocumentStatusSyncFunction.Services
{
    /// <summary>
    /// Interface for Service Bus messaging service
    /// </summary>
    public interface IServicebusService
    {
        /// <summary>
        /// Sends a message to the specified Service Bus queue
        /// </summary>
        /// <param name="queueName">The name of the Service Bus queue</param>
        /// <param name="messageBody">The message body to send</param>
        Task SendMessageAsync(string queueName, string messageBody);
    }
}
