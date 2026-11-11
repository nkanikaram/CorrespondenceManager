using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using NServiceBus;
using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Infrastructure.Messaging
{
    //  public static class RoutingSlipForwarder
    //  {
    //      public const string RouteHeader = "RoutingSlip-Route";
    //      public const string IndexHeader = "RoutingSlip-Index";
    //      public const string OriginatingIdHeader = "RoutingSlip-OriginatingId";

    //      static readonly ILog Log = LogManager.GetLogger(typeof(RoutingSlipForwarder));

    //      /// <summary>
    //      /// Forwards the provided message to the next endpoint in the routing slip, if any.
    //      /// Now awaits the send and logs success/failure to help diagnose why a handler didn't execute.
    //      /// </summary>
    //      public static async Task ForwardToNextStepIfAny<T>(IMessageHandlerContext context, T message)
    //      {
    //          if (context == null) throw new ArgumentNullException(nameof(context));

    //          if (!context.MessageHeaders.TryGetValue(RouteHeader, out var routeJson))
    //          {
    //              Log.Debug($"[{nameof(RoutingSlipForwarder)}] Header '{RouteHeader}' not found. No forwarding will occur.");
    //              return;
    //          }

    //          List<string>? route;
    //          try
    //          {
    //              route = JsonSerializer.Deserialize<List<string>>(routeJson);
    //          }
    //          catch (Exception ex)
    //          {
    //              Log.Warn($"[{nameof(RoutingSlipForwarder)}] Failed to deserialize route header. RouteJson='{routeJson}'. Exception: {ex.Message}", ex);
    //              return;
    //          }

    //          if (route == null || route.Count == 0)
    //          {
    //              Log.Debug($"[{nameof(RoutingSlipForwarder)}] Route is empty or null after deserialization. RouteJson='{routeJson}'.");
    //              return;
    //          }

    //          var index = 0;
    //          if (context.MessageHeaders.TryGetValue(IndexHeader, out var idxStr))
    //          {
    //              if (!int.TryParse(idxStr, out index))
    //              {
    //                  Log.Warn($"[{nameof(RoutingSlipForwarder)}] Could not parse '{IndexHeader}' header value '{idxStr}'. Defaulting current index to 0.");
    //                  index = 0;
    //              }
    //          }
    //          else
    //          {
    //              Log.Debug($"[{nameof(RoutingSlipForwarder)}] Header '{IndexHeader}' not present. Assuming current index = 0.");
    //          }

    //          var nextIndex = index + 1;

    //          var originatingId = context.MessageHeaders.TryGetValue(OriginatingIdHeader, out var orig) ? orig : "(none)";
    //          var currentEndpoint = index >= 0 && index < route.Count ? route[index] : "(unknown)";
    //          var nextEndpoint = nextIndex >= 0 && nextIndex < route.Count ? route[nextIndex] : "(none)";

    //          Log.Info($@"[{nameof(RoutingSlipForwarder)}] Routing slip debug:
    //OriginatingId: {originatingId}
    //RouteLength: {route.Count}
    //CurrentIndex: {index}
    //CurrentEndpoint: {currentEndpoint}
    //NextIndex: {nextIndex}
    //NextEndpoint: {nextEndpoint}
    //RouteJson: {routeJson}");

    //          if (nextIndex >= route.Count)
    //          {
    //              Log.Info($"[{nameof(RoutingSlipForwarder)}] Next index {nextIndex} is >= route length {route.Count}. Chain complete; no forwarding.");
    //              return;
    //          }

    //          if (string.Equals(currentEndpoint, nextEndpoint, StringComparison.OrdinalIgnoreCase))
    //          {
    //              Log.Warn($"[{nameof(RoutingSlipForwarder)}] Next endpoint '{nextEndpoint}' is the same as current endpoint '{currentEndpoint}'. Skipping to avoid a loop.");
    //              return;
    //          }

    //          var sendOptions = new SendOptions();
    //          sendOptions.SetDestination(nextEndpoint);
    //          sendOptions.SetHeader(RouteHeader, routeJson);
    //          sendOptions.SetHeader(IndexHeader, nextIndex.ToString());

    //          if (!string.IsNullOrEmpty(originatingId) && originatingId != "(none)")
    //              sendOptions.SetHeader(OriginatingIdHeader, originatingId);

    //          Log.Debug($"[{nameof(RoutingSlipForwarder)}] Attempting to forward message to next endpoint '{nextEndpoint}' with nextIndex={nextIndex}.");

    //          try
    //          {
    //              await context.Send(message!, sendOptions).ConfigureAwait(false);
    //              Log.Info($"[{nameof(RoutingSlipForwarder)}] Successfully forwarded message to '{nextEndpoint}' (nextIndex={nextIndex}).");
    //          }
    //          catch (Exception ex)
    //          {
    //              Log.Error($"[{nameof(RoutingSlipForwarder)}] Failed to forward message to '{nextEndpoint}'. Exception: {ex}", ex);
    //              // rethrow so immediate/delayed retries behave normally
    //              throw;
    //          }
    //      }
    //  }

    public static class RoutingSlipForwarder
    {
        public const string RouteHeader = "RoutingSlip-Route";
        public const string IndexHeader = "RoutingSlip-Index";
        public const string OriginatingIdHeader = "RoutingSlip-OriginatingId";

        static readonly ILog Log = LogManager.GetLogger(typeof(RoutingSlipForwarder));

        /// <summary>
        /// Forwards the provided message to the next step in the routing slip.
        /// If no more steps exist, completes the routing slip with proper completion logging.
        /// </summary>
        public static async Task ForwardToNextStepIfAny<T>(IMessageHandlerContext context, T message)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!context.MessageHeaders.TryGetValue(RouteHeader, out var routeJson))
            {
                Log.Debug($"[{nameof(RoutingSlipForwarder)}] Header '{RouteHeader}' not found. No forwarding will occur.");
                return;
            }

            List<string>? simpleRoute;
            try
            {
                simpleRoute = JsonSerializer.Deserialize<List<string>>(routeJson);
                Log.Debug($"[{nameof(RoutingSlipForwarder)}] Successfully deserialized route: {string.Join(", ", simpleRoute ?? new List<string>())}");
            }
            catch (Exception ex)
            {
                Log.Warn($"[{nameof(RoutingSlipForwarder)}] Failed to deserialize route header. RouteJson='{routeJson}'. Exception: {ex.Message}", ex);
                return;
            }

            if (simpleRoute == null || simpleRoute.Count == 0)
            {
                Log.Debug($"[{nameof(RoutingSlipForwarder)}] Route is empty or null after deserialization. RouteJson='{routeJson}'.");
                return;
            }

            var index = 0;
            if (context.MessageHeaders.TryGetValue(IndexHeader, out var idxStr))
            {
                if (!int.TryParse(idxStr, out index))
                {
                    Log.Warn($"[{nameof(RoutingSlipForwarder)}] Could not parse '{IndexHeader}' header value '{idxStr}'. Defaulting current index to 0.");
                    index = 0;
                }
            }

            var nextIndex = index + 1;
            var originatingId = context.MessageHeaders.TryGetValue(OriginatingIdHeader, out var orig) ? orig : "(none)";
            var currentHandler = index >= 0 && index < simpleRoute.Count ? simpleRoute[index] : "(unknown)";

            // Check if we've reached the end of the routing slip
            if (nextIndex >= simpleRoute.Count)
            {
                await CompleteRoutingSlip(context, message, simpleRoute, index, originatingId);
                return;
            }

            var nextHandler = simpleRoute[nextIndex];

            Log.Info($@"[{nameof(RoutingSlipForwarder)}] Routing slip progress:
  OriginatingId: {originatingId}
  RouteLength: {simpleRoute.Count}
  CurrentIndex: {index}
  CurrentHandler: {currentHandler}
  NextIndex: {nextIndex}
  NextHandler: {nextHandler}
  Progress: {nextIndex}/{simpleRoute.Count} steps completed
  RouteJson: {routeJson}");

            // Transform message to the appropriate type for the next step
            object nextMessage;
            try
            {
                nextMessage = TransformMessage(message, nextHandler);
                Log.Debug($"[{nameof(RoutingSlipForwarder)}] Successfully transformed message to type: {nextMessage.GetType().Name}");
            }
            catch (Exception ex)
            {
                Log.Error($"[{nameof(RoutingSlipForwarder)}] Failed to transform message for next step '{nextHandler}'. Exception: {ex.Message}", ex);
                throw;
            }

            // Use Send with explicit destination for commands (required by NServiceBus)
            var sendOptions = new SendOptions();
            sendOptions.SetDestination("ProcessRequestEndPoint");
            sendOptions.SetHeader(RouteHeader, routeJson);
            sendOptions.SetHeader(IndexHeader, nextIndex.ToString());

            if (!string.IsNullOrEmpty(originatingId) && originatingId != "(none)")
                sendOptions.SetHeader(OriginatingIdHeader, originatingId);

            Log.Debug($"[{nameof(RoutingSlipForwarder)}] Forwarding to next step: '{nextHandler}'.");

            try
            {
                await context.Send(nextMessage, sendOptions).ConfigureAwait(false);
                Log.Info($"[{nameof(RoutingSlipForwarder)}] Successfully sent command message to '{nextHandler}' (step {nextIndex + 1} of {simpleRoute.Count}).");
            }
            catch (Exception ex)
            {
                Log.Error($"[{nameof(RoutingSlipForwarder)}] Failed to send command message to '{nextHandler}'. Exception: {ex}", ex);
                throw;
            }
        }

        /// <summary>
        /// Completes the routing slip when all steps have been executed.
        /// Logs comprehensive completion information and optionally publishes completion event.
        /// </summary>
        private static async Task CompleteRoutingSlip<T>(IMessageHandlerContext context, T message, List<string> route, int completedIndex, string originatingId)
        {
            var currentHandler = completedIndex >= 0 && completedIndex < route.Count ? route[completedIndex] : "(unknown)";
            var process = ExtractSequentialProcess(message);

            // Create numbered step list for better readability
            var numberedSteps = route.Select((step, index) => $"{index + 1}. {step}").ToList();
            var flowSteps = string.Join(" -> ", route);

            var completionMessage = $@"
=== ROUTING SLIP COMPLETED SUCCESSFULLY ===
  OriginatingId: {originatingId}
  Total Steps: {route.Count}
  Steps Executed: {flowSteps}
  Step Details:
    {string.Join("\n    ", numberedSteps)}
  Final Handler: {currentHandler}
  Process DocId: {process?.DocId ?? "(not set)"}
  Process Policy: {process?.ECCOTrackingPolicyNumber ?? "(not set)"}
  Process Claim: {process?.ECCOTrackingClaimNumber ?? "(not set)"}
  Completion Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
==========================================";
            Log.Info($"[{nameof(RoutingSlipForwarder)}] {completionMessage}");
            Console.WriteLine($"[{nameof(RoutingSlipForwarder)}] {completionMessage}");

            // Example: Publish a RoutingSlipCompletedEvent for monitoring/auditing
            var completedEvent = new RoutingSlipCompletedEvent
            {
                OriginatingId = originatingId,
                Route = route,
                CompletedSteps = route.Count,
                ProcessDocId = process?.DocId,
                ProcessPolicyNumber = process?.ECCOTrackingPolicyNumber,
                ProcessClaimNumber = process?.ECCOTrackingClaimNumber,
                CompletionTime = DateTime.UtcNow
            };

            await context.Publish(completedEvent, new PublishOptions()).ConfigureAwait(false);
        }

        /// <summary>
        /// Transforms the current message to the required message type for the next step.
        /// </summary>
        private static object TransformMessage<T>(T currentMessage, string nextHandlerName)
        {
            var process = ExtractSequentialProcess(currentMessage);
            var messageType = nextHandlerName?.ToLowerInvariant();

            Log.Info($"[{nameof(RoutingSlipForwarder)}] Transforming message for handler: '{nextHandlerName}' (normalized: '{messageType}')");


            object result = messageType switch
            {
                var type when type?.Contains("beginrequesthandler") == true =>
                    new BeginRequestHandlerMessage { Process = process },

                var type when type?.Contains("assigndocumentumid") == true =>
                    new AssignDocumentumIdHandlerMessage { Process = process },

                var type when type?.Contains("addcorrespondencesbus") == true ||
                                 type?.Contains("correspondencesbus") == true ||
                                 type?.Contains("servicebus") == true ||
                                 type?.Contains("sbus") == true =>
                       new AddCorrespondenceSBusHandlerMessage { Process = process },
                
                var type when type?.Contains("updatecorrespondencedbhandler") == true ||
                              type?.Contains("updatecorrespondencedb") == true =>
                    new UpdateCorrespondenceDBHandlerMessage { Process = process },

                var type when type?.Contains("addcorrespondencedb") == true ||
                              type?.Contains("correspondencedb") == true =>
                    new AddCorrespondenceDBHandlerMessage { Process = process },

                var type when type?.Contains("requestcomposition") == true ||
                              type?.Contains("composition") == true =>
                    new RequestCompositionHandlerMessage { Process = process },
                
                var type when type?.Contains("updatearchivedstatus") == true ||
                              type?.Contains("updatearchived") == true ||
                              type?.Contains("archived") == true =>
                    new UpdateArchivedStatusDBHandlerMessage { Process = process },

                _ => currentMessage ?? throw new InvalidOperationException($"Unable to transform message for handler type: {nextHandlerName}. " +
                    $"Supported types: BeginRequestHandler, AssignDocumentumIdHandler, AddCorrespondenceDBHandler, RequestCompositionHandler, " +
                    $"UpdateCorrespondenceDBHandler, UpdateArchivedStatusDBHandler")
            };

            Log.Info($"[{nameof(RoutingSlipForwarder)}] TransformMessage RESULT type: {result.GetType().FullName}");
            return result;
        }

        /// <summary>
        /// Extracts SequentialProcess from various message wrapper types.
        /// </summary>
        private static SequentialProcess ExtractSequentialProcess<T>(T message)
        {
            Log.Debug($"[{nameof(RoutingSlipForwarder)}] Extracting SequentialProcess from message type: {typeof(T).Name}");

            return message switch
            {
                BeginRequestHandlerMessage beginRequestMsg => beginRequestMsg.Process,
                AssignDocumentumIdHandlerMessage assignMsg => assignMsg.Process,
                AddCorrespondenceSBusHandlerMessage sbusMsg => sbusMsg.Process,
                AddCorrespondenceDBHandlerMessage dbMsg => dbMsg.Process,
                RequestCompositionHandlerMessage compMsg => compMsg.Process,
                UpdateCorrespondenceDBHandlerMessage updateCorrMsg => updateCorrMsg.Process,
                UpdateArchivedStatusDBHandlerMessage updateMsg => updateMsg.Process,
                SequentialProcess directProcess => directProcess,
                _ => throw new InvalidOperationException($"Unable to extract SequentialProcess from message type: {typeof(T).Name}. " +
                    $"Supported types: BeginRequestHandlerMessage, AssignDocumentumIdHandlerMessage, AddCorrespondenceSBusHandlerMessage, AddCorrespondenceDBHandlerMessage, " +
                    $"RequestCompositionHandlerMessage, UpdateCorrespondenceDBHandlerMessage, UpdateArchivedStatusDBHandlerMessage, SequentialProcess")
            };
        }
    }

    /// <summary>
    /// Event published when a routing slip completes successfully.
    /// Useful for monitoring, auditing, and triggering downstream processes.
    /// </summary>
    public class RoutingSlipCompletedEvent : IEvent
    {
        public string OriginatingId { get; set; } = string.Empty;
        public List<string> Route { get; set; } = new();
        public int CompletedSteps { get; set; }
        public string? ProcessDocId { get; set; }
        public string? ProcessPolicyNumber { get; set; }
        public string? ProcessClaimNumber { get; set; }
        public DateTime CompletionTime { get; set; }
    }
}
