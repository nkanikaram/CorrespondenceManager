using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus;
using NServiceBus.Transport;

namespace CustomerCorrespondence.Infrastructure.Messaging
{     
    public static class CustomRecoverabilityPolicyConfig
    {
        public static void ConfigureRecoverability(this RecoverabilitySettings recoverabilitySettings)
        {
            recoverabilitySettings.CustomPolicy(CustomRecoverabilityPolicy);
        }

        public static RecoverabilityAction CustomRecoverabilityPolicy(RecoverabilityConfig config, ErrorContext context)
        {
            var totalFailures =
            context.ImmediateProcessingFailures +
            context.DelayedDeliveriesPerformed;

            var maxImmediate = config.Immediate.MaxNumberOfRetries;
            var maxDelayed = config.Delayed.MaxNumberOfRetries;

            var retriesExhausted =
                context.ImmediateProcessingFailures >= maxImmediate &&
                context.DelayedDeliveriesPerformed >= maxDelayed;

            var exception = context.Exception;

            if (exception is Microsoft.Data.SqlClient.SqlException sqlEx &&
            sqlEx.Message.Contains("nvarchar") && sqlEx.Message.Contains("varbinary"))
            {
                return RecoverabilityAction.MoveToError(config.Failed.ErrorQueue);
            }

            if (retriesExhausted)
            {
                // Log non-recoverable error to Corr DB
                //LogNonRecoverableError(context, config);

                // Move to error queue as usual
                return RecoverabilityAction.MoveToError(config.Failed.ErrorQueue);
            }
            return DefaultRecoverabilityPolicy.Invoke(config, context);
        }

    }
}
