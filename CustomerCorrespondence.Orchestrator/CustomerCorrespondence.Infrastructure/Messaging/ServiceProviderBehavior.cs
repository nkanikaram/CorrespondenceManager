using NServiceBus.Pipeline;

namespace CustomerCorrespondence.Infrastructure.Messaging
{
    public class ServiceProviderBehavior : Behavior<IIncomingPhysicalMessageContext>
    {
        private readonly IServiceProvider _serviceProvider;

        public ServiceProviderBehavior(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public override Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            context.Extensions.Set(_serviceProvider);
            return next();
        }
    }
}