using CustomerCorrespondence.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Infrastructure.Messaging
{
    public class NonRecoverableLoggingBehavior : Behavior<IRecoverabilityContext>
    {
        //private readonly ICorrespondenceRequestStatusHistoryService _historyService;
        private readonly IServiceProvider _rootProvider;
        public NonRecoverableLoggingBehavior(IServiceProvider rootProvider)
        {
            _rootProvider = rootProvider;
        }

        public override async Task Invoke(IRecoverabilityContext context, Func<Task> next)
        {
            if (context.RecoverabilityAction is MoveToError)
            {
                var message = context.FailedMessage;
                var ex = context.Exception;

                using var scope = _rootProvider.CreateScope();
                var historyService = scope.ServiceProvider
                    .GetRequiredService<ICorrespondenceRequestStatusHistoryService>();

                await historyService.LogNonRecoverableErrorAsync(
                    message,
                    ex.ToString(),
                    ex.StackTrace ?? string.Empty);
            }
            await next();
        }
    }
}
