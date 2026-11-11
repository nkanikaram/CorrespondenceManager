using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using CustomerCorrespondence.Infrastructure.Messaging;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Core;
using NServiceBus;

namespace ProcessRequestEndPoint.Handlers
{
    public class BeginRequestHandler : IHandleMessages<BeginRequestHandlerMessage>
    {
        //We can rename this as BeginRequestHandler if needed
        public async Task Handle(BeginRequestHandlerMessage message, IMessageHandlerContext context)
        {
            Console.WriteLine("**** TEST HANDLER CALLED ****");
            Console.WriteLine($"Message received with Process DocId: {message.Process?.DocId ?? "null"}");
            Console.WriteLine($"Requesting System: {message.Process?.RequestingSystemName ?? "null"}");

            // Add some processing logic here if needed
            if (message.Process != null)
            {
                Console.WriteLine($"Processing message for Policy: {message.Process.ECCOTrackingPolicyNumber}");
                Console.WriteLine($"Claim Number: {message.Process.ECCOTrackingClaimNumber}");

                // Use RoutingSlipForwarder instead of manual SendLocal
                await RoutingSlipForwarder.ForwardToNextStepIfAny(context, message);
                Console.WriteLine("**** TEST HANDLER - Forwarded to next step via RoutingSlipForwarder ****");
            }
            else
            {
                throw new ValidationException("Process data is required but was not provided.",
                    new Dictionary<string, List<string>> { ["Process"] = new List<string> { "Process object cannot be null" } });
            }

            await Task.CompletedTask;
        }
    }
}
