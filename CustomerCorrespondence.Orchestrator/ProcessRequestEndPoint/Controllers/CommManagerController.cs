using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Services;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NServiceBus.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace ProcessRequestEndPoint.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    //[Authorize(Policy = "ECCOUsersOrDevelopers")]
    public class CommManagerController : ControllerBase
    {
        private readonly IMessageSession _messageSession;
        private readonly IRoutingConfigurationService _routingConfigService;
        private readonly IDocumentIdGeneratorService _documentIdGeneratorService;
        private readonly ICorrespondenceStatusService _correspondenceStatusService;
        private readonly IDocumentIdsService _documentIdsService;
        private readonly ILogger<CommManagerController> _aspLogger;
        private static readonly ILog _logger = LogManager.GetLogger<CommManagerController>();
        private static readonly SemaphoreSlim _docIdReplenishLock = new(1, 1);
        private readonly ICorrespondenceRequestService _correspondenceRequestService;
        private readonly ICorrespondenceFilterService _correspondenceFilterService;
        private readonly ICorrespondenceDocumentTypeService _documentTypeService;

        public CommManagerController(
            IMessageSession messageSession,
            IRoutingConfigurationService routingConfigService,
            IDocumentIdGeneratorService documentIdGeneratorService,
            ICorrespondenceStatusService correspondenceStatusService,
            IDocumentIdsService documentIdsService,
            ILogger<CommManagerController> aspLogger,
            ICorrespondenceRequestService correspondenceRequestService,
            ICorrespondenceFilterService correspondenceFilterService,
            ICorrespondenceDocumentTypeService documentTypeService)
        {
            Console.Title = "Process Request End Point";
            _messageSession = messageSession ?? throw new ArgumentNullException(nameof(messageSession));
            _routingConfigService = routingConfigService ?? throw new ArgumentNullException(nameof(routingConfigService));
            _documentIdGeneratorService = documentIdGeneratorService ?? throw new ArgumentNullException(nameof(documentIdGeneratorService));
            _correspondenceStatusService = correspondenceStatusService ?? throw new ArgumentNullException(nameof(correspondenceStatusService));
            _documentIdsService = documentIdsService ?? throw new ArgumentNullException(nameof(documentIdsService));
            _aspLogger = aspLogger ?? throw new ArgumentNullException(nameof(aspLogger));
            _correspondenceRequestService = correspondenceRequestService ?? throw new ArgumentNullException(nameof(correspondenceRequestService));
            _correspondenceFilterService = correspondenceFilterService ?? throw new ArgumentNullException(nameof(correspondenceFilterService));
            _documentTypeService = documentTypeService ?? throw new ArgumentNullException(nameof(documentTypeService));
            _aspLogger.LogInformation("CommManagerController initialized successfully");
        }

        [AllowAnonymous]
        [HttpGet("healthcheck")]
        public IActionResult HealthCheck()
        {
            try
            {
                _aspLogger.LogInformation("Health check requested");
                return Ok(new
                {
                    status = "Healthy",
                    message = "CommManagerClient Health check success",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Health check failed");
                return StatusCode(500, new ErrorResponse
                {
                    Message = "Health check failed",
                    ErrorCode = "HEALTHCHECK_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpPost("processcorrespondencerequest")]
        public async Task<IActionResult> ProcessCorrespondenceRequest([FromBody] SequentialProcessRequest request)
        {
            var messageId = Guid.NewGuid();
            var requestingSystemName = "Unknown";

            try
            {
                _aspLogger.LogInformation("ProcessCorrespondenceRequest started - MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));

                // Validate request
                if (request == null)
                {
                    _aspLogger.LogWarning("ProcessCorrespondenceRequest failed - Request is null. MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Request cannot be null",
                        ErrorCode = "VALIDATION_ERROR",
                        Details = JsonSerializer.Serialize(new { messageId }),
                        Timestamp = DateTime.UtcNow
                    });
                }

                try
                {
                    request.Process?.TrimAllStringFields();
                    request.Process?.ValidateNoSpecialCharacters();
                    request.Process?.Validate();
                    _aspLogger.LogDebug("Request validation passed - MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));
                }
                catch (ValidationException validationEx)
                {
                    _aspLogger.LogWarning(validationEx, "Request validation failed - MessageId: {MessageId}, ErrorCode: {ErrorCode}",
                        SanitizeForLog(messageId.ToString()), validationEx.ErrorCode);
                    
                    return BadRequest(new ErrorResponse
                    {
                        Message = validationEx.Message,
                        ErrorCode = validationEx.ErrorCode ?? "VALIDATION_ERROR",
                        Timestamp = DateTime.UtcNow,
                        ValidationErrors = validationEx.ValidationErrors
                    });
                }
                catch (Exception validationEx)
                {
                    _aspLogger.LogWarning(validationEx, "Request validation failed - MessageId: {MessageId}, ValidationError: {ValidationError}",
                        SanitizeForLog(messageId.ToString()), SanitizeForLog(validationEx.Message));
                    return BadRequest(new ErrorResponse
                    {
                        Message = $"Request validation failed: {validationEx.Message}",
                        ErrorCode = "VALIDATION_ERROR",
                        Timestamp = DateTime.UtcNow
                    });
                }

                if (request.Process == null)
                {
                    _aspLogger.LogWarning("ProcessCorrespondenceRequest failed - Process is null. MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Process is required",
                        ErrorCode = "VALIDATION_ERROR",
                        Details = JsonSerializer.Serialize(new { messageId }),
                        Timestamp = DateTime.UtcNow
                    });
                }

                requestingSystemName = request.Process.RequestingSystemName ?? "Unknown";
                _aspLogger.LogInformation("Processing request for system: {RequestingSystemName}, DocId: {DocId}, MessageId: {MessageId}",
                    SanitizeForLog(requestingSystemName), SanitizeForLog(request.Process.DocId ?? "Not assigned"), SanitizeForLog(messageId.ToString()));

                // Get route configuration from database
                RoutingConfiguration? routeConfig;
                try
                {
                    _aspLogger.LogDebug("Retrieving route configuration for: {RequestingSystemName}, MessageId: {MessageId}",
                        SanitizeForLog(requestingSystemName), SanitizeForLog(messageId.ToString()));

                    routeConfig = await _routingConfigService.GetRoutingConfigurationByCustomerNameAsync(requestingSystemName);

                    if (routeConfig != null)
                    {
                        _aspLogger.LogInformation("Route configuration found for: {RequestingSystemName} - {Steps} steps, MessageId: {MessageId}",
                            SanitizeForLog(requestingSystemName), routeConfig.Route?.Count ?? 0, SanitizeForLog(messageId.ToString()));
                        _aspLogger.LogDebug("Route details - {RequestingSystemName}: [{Route}], MessageId: {MessageId}",
                            SanitizeForLog(requestingSystemName), string.Join(" -> ", routeConfig.Route ?? new List<string>()), SanitizeForLog(messageId.ToString()));
                    }
                    else
                    {
                        _aspLogger.LogWarning("No route configuration found for: {RequestingSystemName}, MessageId: {MessageId}",
                            SanitizeForLog(requestingSystemName), SanitizeForLog(messageId.ToString()));
                    }
                }
                catch (DatabaseException dbEx)
                {
                    _aspLogger.LogError(dbEx, "Database error retrieving route configuration for {RequestingSystemName}, ErrorCode: {ErrorCode}, MessageId: {MessageId}",
                        SanitizeForLog(requestingSystemName), dbEx.ErrorCode, SanitizeForLog(messageId.ToString()));
                    
                    return StatusCode(500, new ErrorResponse
                    {
                        Message = "Failed to retrieve route configuration from database",
                        ErrorCode = dbEx.ErrorCode ?? "DATABASE_ERROR",
                        Timestamp = DateTime.UtcNow,
                        Details = JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            ["messageId"] = messageId,
                            ["requestingSystem"] = requestingSystemName
                        })
                    });
                }
                catch (CorrespondenceException dbEx)
                {
                    _aspLogger.LogError(dbEx, "Correspondence error retrieving route configuration for {RequestingSystemName}, ErrorCode: {ErrorCode}, MessageId: {MessageId}",
                        SanitizeForLog(requestingSystemName), dbEx.ErrorCode, SanitizeForLog(messageId.ToString()));
                    
                    return StatusCode(500, new ErrorResponse
                    {
                        Message = "Failed to retrieve route configuration from database",
                        ErrorCode = dbEx.ErrorCode ?? "DATABASE_ERROR",
                        Timestamp = DateTime.UtcNow,
                        Details = JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            ["messageId"] = messageId,
                            ["requestingSystem"] = requestingSystemName
                        })
                    });
                }
                catch (Exception dbEx)
                {
                    _aspLogger.LogError(dbEx, "Unexpected error retrieving route configuration for {RequestingSystemName}, MessageId: {MessageId}",
                        SanitizeForLog(requestingSystemName), SanitizeForLog(messageId.ToString()));
                    
                    return StatusCode(500, new ErrorResponse
                    {
                        Message = "Failed to retrieve route configuration from database",
                        ErrorCode = "DATABASE_UNEXPECTED_ERROR",
                        Timestamp = DateTime.UtcNow,
                        Details = JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            ["messageId"] = messageId,
                            ["requestingSystem"] = requestingSystemName
                        })
                    });
                }

                if (routeConfig == null || routeConfig.Route == null || routeConfig.Route.Count == 0)
                {
                    _aspLogger.LogWarning("No valid route configuration found for {RequestingSystemName}, MessageId: {MessageId}",
                        SanitizeForLog(requestingSystemName), SanitizeForLog(messageId.ToString()));

                    return NotFound(new ErrorResponse
                    {
                        Message = $"No route configuration found for RequestingSystemName: {requestingSystemName}",
                        ErrorCode = "ROUTE_NOT_FOUND",
                        Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName }),
                        Timestamp = DateTime.UtcNow
                    });
                }

                // Validate if provided DocID is already in use
                if (!string.IsNullOrWhiteSpace(request.Process.DocId))
                {
                    var isDocIdInUse = await IsDocIdAlreadyInUseAsync(request.Process.DocId);
                    if (isDocIdInUse)
                    {
                        _aspLogger.LogWarning("Provided DocId is already in use, MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));
                        return Conflict(new ErrorResponse
                        {
                            Message = $"Provided DocId is already in use: {request.Process.DocId}",
                            ErrorCode = "DOCID_ALREADY_IN_USE",
                            Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName, docId = request.Process.DocId }),
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }

                //Validate if SolutionsID is present in the payload or not
                if (string.IsNullOrWhiteSpace(request.Process.CompositionInfo.SolutionsId))
                {
                    _aspLogger.LogWarning("SolutionsID is not provided, MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));
                    return BadRequest(new ErrorResponse
                    {
                        Message = "SolutionsID is missing from the payload",
                        ErrorCode = "SOLUTIONS_ID_MISSING",
                        Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName }),
                        Timestamp = DateTime.UtcNow
                    });
                }

                // Generate DocId if not provided and AssignDocumentumIdHandler is in the route
                if (string.IsNullOrWhiteSpace(request.Process.DocId) &&
                    routeConfig.Route.Any(handler => handler.ToLowerInvariant().Contains("assigndocumentumid")))
                {
                    try
                    {
                        _aspLogger.LogDebug("Generating DocId for MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));

                        //Attempt to atomically claim a DocId from the DocumentIds table
                        var docId = await _documentIdsService.GetAndUpdateDocumentIdAsync();

                        // If not found in DocumentIds table, acquire lock to generate new batch and claim one
                        if (string.IsNullOrWhiteSpace(docId))
                        {
                            await _docIdReplenishLock.WaitAsync();
                            try
                            {
                                // Re-check after acquiring lock — another thread may have replenished
                                docId = await _documentIdsService.GetAndUpdateDocumentIdAsync();
                                if (string.IsNullOrWhiteSpace(docId))
                                {
                                    // Generate new batch of DocIds and insert into DocumentIds table
                                    await _documentIdGeneratorService.GenerateDocIdAsync();
                                    docId = await _documentIdsService.GetAndUpdateDocumentIdAsync();
                                }
                            }
                            finally
                            {
                                _docIdReplenishLock.Release();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(docId))
                        {
                            request.Process.DocId = docId;
                        }

                        // Get threshold from Key Vault secret cache
                        var secretsCache = HttpContext.RequestServices.GetRequiredService<ISecretsCacheService>();
                        var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                        var thresholdSecretName = configuration["SecretNames:DocIDThreshold"];
                        
                        if (!string.IsNullOrEmpty(thresholdSecretName))
                        {
                            try
                            {
                                var thresholdString = await secretsCache.GetSecretAsync(thresholdSecretName);

                                // Parse threshold with fallback default value
                                if (!int.TryParse(thresholdString, out var docIdThreshold))
                                {
                                    docIdThreshold = 100; // Default fallback value
                                    _aspLogger.LogWarning("Failed to parse DocIDThreshold from secret, using default: {Default}", docIdThreshold);
                                }

                                var availableDocIdCount = await _documentIdsService.GetNewDocumentIdCountAsync();
                                if (availableDocIdCount <= docIdThreshold)
                                {
                                    // Non-blocking try: only one concurrent request triggers replenishment
                                    if (await _docIdReplenishLock.WaitAsync(0))
                                    {
                                        try
                                        {
                                            // Re-check count inside lock
                                            availableDocIdCount = await _documentIdsService.GetNewDocumentIdCountAsync();
                                            if (availableDocIdCount <= docIdThreshold)
                                            {
                                                _aspLogger.LogInformation("DocID pool below threshold ({Threshold}). Current count: {Count}. Triggering replenishment - MessageId: {MessageId}",
                                                    docIdThreshold, availableDocIdCount, SanitizeForLog(messageId.ToString()));
                                                await _documentIdGeneratorService.GenerateDocIdAsync();
                                                _aspLogger.LogInformation("DocID pool replenishment completed successfully");
                                            }
                                        }
                                        catch (Exception replenishEx)
                                        {
                                            _aspLogger.LogError(replenishEx, "Failed to replenish DocID pool");
                                        }
                                        finally
                                        {
                                            _docIdReplenishLock.Release();
                                        }
                                    }
                                    else
                                    {
                                        _aspLogger.LogDebug("DocID pool replenishment already in progress, skipping - MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));
                                    }
                                }
                            }
                            catch (SecretNotFoundException ex)
                            {
                                _aspLogger.LogError(ex, "DocID threshold secret not found: {SecretName}", ex.SecretName);
                                // Proceed without blocking the request
                            }
                        }
                        else
                        {
                            _aspLogger.LogError("DocID threshold secret name is not configured in app settings");
                            // Proceed without blocking the request, but log the issue for investigation
                        }

                        _aspLogger.LogInformation("Generated DocId: {DocId} for MessageId: {MessageId}",
                            SanitizeForLog(request.Process.DocId ?? "null"), SanitizeForLog(messageId.ToString()));
                    }
                    catch (DocumentIdGenerationException docIdEx)
                    {
                        _aspLogger.LogError(docIdEx, "Failed to generate DocId for MessageId: {MessageId}, ErrorCode: {ErrorCode}",
                            SanitizeForLog(messageId.ToString()), docIdEx.ErrorCode);

                        return StatusCode(500, new ErrorResponse
                        {
                            Message = "Failed to generate document ID",
                            ErrorCode = docIdEx.ErrorCode ?? "DOCID_GENERATION_ERROR",
                            Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName }),
                            Timestamp = DateTime.UtcNow
                        });
                    }
                    catch (Exception docIdEx)
                    {
                        _aspLogger.LogError(docIdEx, "Failed to generate DocId for MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));

                        return StatusCode(500, new ErrorResponse
                        {
                            Message = "Failed to generate document ID",
                            ErrorCode = "DOCID_GENERATION_ERROR",
                            Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName, error = docIdEx.Message }),
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
                else if (!string.IsNullOrWhiteSpace(request.Process.DocId))
                {
                    _aspLogger.LogInformation("DocId already provided: {DocId} for MessageId: {MessageId}",
                        SanitizeForLog(request.Process.DocId), SanitizeForLog(messageId.ToString()));
                }
                else
                {
                    _aspLogger.LogDebug("DocId generation not required - AssignDocumentumIdHandler not in route for MessageId: {MessageId}",
                        SanitizeForLog(messageId.ToString()));
                }
                // Fetch DocumentType details after all validations and before building the response
                var documentType = request.Process.DocumentDescription;
                CorrespondenceDocumentType? documentTypeDetails = null;
                if (!string.IsNullOrWhiteSpace(documentType))
                {
                    documentTypeDetails = await _documentTypeService.GetDocumentTypeInfoAsync(documentType);
                }

                // BUILD VENTURE FUSION RESPONSE - Same logic as PreviewVentureFusionData and RequestCompositionHandler
                // This is a preview/validation step only (ventureFusionData is echoed back in the response,
                // not used to build the actual message sent below) — safe to skip in local Development
                // when Venture Fusion isn't configured/reachable, so it doesn't block the rest of the request.
                DataRecords.DataRecord? ventureFusionFormattedResponse = null;
                var isDevelopmentEnv = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
                try
                {
                    _aspLogger.LogInformation("Building Venture Fusion formatted response - MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));

                    // Initialize AdditionalData if null
                    if (request.Process.AdditionalData == null)
                    {
                        request.Process.AdditionalData = new Dictionary<string, object>();
                    }

                    try
                    {
                        // Handle delivery method-specific processing
                        if (request.Process.IsMailDelivery)
                        {
                            request.Process.AdditionalData["MailFlag"] = "Y";
                            request.Process.AdditionalData["ImageFlag"] = "Y";
                        }
                        else if (request.Process.IsEPresentDelivery)
                        {
                            request.Process.AdditionalData["MailFlag"] = "N";
                            request.Process.AdditionalData["ImageFlag"] = "Y";
                            request.Process.AdditionalData["EPresentFlag"] = "Y";

                            // Add JourneyDeliveryInfo to AdditionalData for downstream processing
                            if (request.Process.JourneyDeliveryInfo != null)
                            {
                                request.Process.AdditionalData["JourneyDeliveryInfo"] = new Dictionary<string, object>
                                {
                                    ["AccountId"] = request.Process.JourneyDeliveryInfo.AccountId,
                                    ["MessageBody"] = new Dictionary<string, object>
                                    {
                                        ["ContactKey"] = request.Process.JourneyDeliveryInfo.MessageBody.ContactKey,
                                        ["EventDefinitionKey"] = request.Process.JourneyDeliveryInfo.MessageBody.EventDefinitionKey,
                                        ["Data"] = new Dictionary<string, object>
                                        {
                                            ["SubscriberKey"] = request.Process.JourneyDeliveryInfo.MessageBody.Data.SubscriberKey,
                                            ["EmailAddress"] = request.Process.JourneyDeliveryInfo.MessageBody.Data.EmailAddress ?? string.Empty,
                                            ["SourceSystem"] = request.Process.JourneyDeliveryInfo.MessageBody.Data.SourceSystem ?? string.Empty,
                                            ["FirstName"] = request.Process.JourneyDeliveryInfo.MessageBody.Data.FirstName ?? string.Empty,
                                            ["AttachmentName"] = request.Process.JourneyDeliveryInfo.MessageBody.Data.AttachmentName ?? string.Empty
                                        }
                                    }
                                };
                            }
                        }
                        else
                        {
                            throw new ValidationException($"Invalid DeliveryMethod '{request.Process.DeliveryMethod}'. Supported methods: Mail, ePresent.",
                                new Dictionary<string, List<string>> { ["DeliveryMethod"] = new List<string> { $"'{request.Process.DeliveryMethod}' is not valid" } });
                        }
                    }
                    catch (ValidationException)
                    {
                        throw;
                    }
                    catch (Exception mergeEx)
                    {
                        _aspLogger.LogError(mergeEx, "Invalid Delivery method. Cannot merge Mail Delivery Info into AdditionalData. MessageId: {MessageId}",
                            SanitizeForLog(messageId.ToString()));
                        
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Invalid Delivery method",
                            ErrorCode = "INVALID_DELIVERY_METHOD",
                            Details = JsonSerializer.Serialize(new { deliveryMethod = request.Process.DeliveryMethod, messageId, requestingSystem = requestingSystemName }),
                            Timestamp = DateTime.UtcNow
                        });
                    }

                    if (request.Process.CompositionInfo != null && !string.IsNullOrEmpty(request.Process.CompositionInfo.VersionCode))
                    {
                        request.Process.AdditionalData["VersionCode"] = request.Process.CompositionInfo.VersionCode;
                    }

                    if (request.Process.MailDeliveryInfo != null)
                    {
                        var mailDeliveryInfoDict = new Dictionary<string, object>
                        {
                            ["RecipientFullName"] = request.Process.MailDeliveryInfo.RecipientFullName ?? string.Empty,
                            ["RecipientFirstName"] = request.Process.MailDeliveryInfo.RecipientFirstName ?? string.Empty,
                            ["RecipientLastName"] = request.Process.MailDeliveryInfo.RecipientLastName ?? string.Empty
                        };

                        if (request.Process.MailDeliveryInfo.RecipientAddress != null)
                        {
                            mailDeliveryInfoDict["RecipientAddress"] = new Dictionary<string, object>
                            {
                                ["RecipientAddressLine1"] = request.Process.MailDeliveryInfo.RecipientAddress.RecipientAddressLine1 ?? string.Empty,
                                ["RecipientAddressLine2"] = request.Process.MailDeliveryInfo.RecipientAddress.RecipientAddressLine2 ?? string.Empty,
                                ["RecipientAddressLine3"] = request.Process.MailDeliveryInfo.RecipientAddress.RecipientAddressLine3 ?? string.Empty,
                                ["RecipientAddressCity"] = request.Process.MailDeliveryInfo.RecipientAddress.RecipientAddressCity ?? string.Empty,
                                ["RecipientAddressST"] = request.Process.MailDeliveryInfo.RecipientAddress.RecipientAddressST ?? string.Empty,
                                ["RecipientAddressZIP"] = request.Process.MailDeliveryInfo.RecipientAddress.RecipientAddressZIP ?? string.Empty,
                                ["RecipientCountry"] = request.Process.MailDeliveryInfo.RecipientAddress.RecipientCountry ?? string.Empty
                            };
                        }

                        mailDeliveryInfoDict["NonUSAddressFlag"] = request.Process.MailDeliveryInfo.NonUSAddressFlag ?? false;
                        mailDeliveryInfoDict["BadAddressFlag"] = request.Process.MailDeliveryInfo.BadAddressFlag ?? false;

                        request.Process.AdditionalData["MailDeliveryInfo"] = mailDeliveryInfoDict;
                    }

                    // NOW CALL VentureFusionService TO MAP TO FINAL FORMAT
                    var ventureFusionService = HttpContext.RequestServices.GetRequiredService<IVentureFusionService>();
                    ventureFusionFormattedResponse = ventureFusionService.MapCorrespondenceRequestToVentureFusionRequest(request.Process);

                    _aspLogger.LogInformation("Venture Fusion formatted response built successfully - MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));
                }
                catch (ValidationException vfEx) when (!isDevelopmentEnv)
                {
                    _aspLogger.LogError(vfEx, "Validation error building Venture Fusion response - MessageId: {MessageId}, ErrorCode: {ErrorCode}",
                        SanitizeForLog(messageId.ToString()), vfEx.ErrorCode);

                    return BadRequest(new ErrorResponse
                    {
                        Message = "Validation error building Venture Fusion formatted response",
                        ErrorCode = vfEx.ErrorCode ?? "VF_VALIDATION_ERROR",
                        ValidationErrors = vfEx.ValidationErrors,
                        Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName }),
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception vfEx) when (!isDevelopmentEnv)
                {
                    _aspLogger.LogError(vfEx, "Failed to build Venture Fusion formatted response - MessageId: {MessageId}", SanitizeForLog(messageId.ToString()));

                    return StatusCode(500, new ErrorResponse
                    {
                        Message = "Failed to build Venture Fusion formatted response",
                        ErrorCode = "VF_BUILD_ERROR",
                        Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName, error = vfEx.Message }),
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception vfEx)
                {
                    // Development only: Venture Fusion isn't configured/reachable locally — skip the
                    // preview instead of blocking the request, since ventureFusionFormattedResponse is
                    // only echoed in the response below and isn't used to build the actual message sent.
                    _aspLogger.LogWarning(vfEx, "Failed to build Venture Fusion formatted response — continuing without VF preview (Development). MessageId: {MessageId}",
                        SanitizeForLog(messageId.ToString()));
                    ventureFusionFormattedResponse = null;
                }

                // Create initial message
                object firstMessage;
                try
                {
                    var firstHandlerName = routeConfig.Route[0];
                    _aspLogger.LogDebug("Creating initial message for handler: {HandlerName}, MessageId: {MessageId}",
                        firstHandlerName, SanitizeForLog(messageId.ToString()));

                    firstMessage = CreateInitialMessage(firstHandlerName, request.Process);

                    _aspLogger.LogInformation("Initial message created successfully for {HandlerName}, MessageId: {MessageId}",
                        firstHandlerName, SanitizeForLog(messageId.ToString()));
                }
                catch (ValidationException handlerEx)
                {
                    _aspLogger.LogError(handlerEx, "Unknown handler type in route configuration for {RequestingSystemName}, MessageId: {MessageId}",
                        SanitizeForLog(requestingSystemName), SanitizeForLog(messageId.ToString()));

                    return BadRequest(new ErrorResponse
                    {
                        Message = handlerEx.Message,
                        ErrorCode = "UNKNOWN_HANDLER",
                        Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName, firstHandler = routeConfig.Route.FirstOrDefault(), availableRoute = routeConfig.Route }),
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception messageEx)
                {
                    _aspLogger.LogError(messageEx, "Error creating initial message for {RequestingSystemName}, MessageId: {MessageId}",
                        SanitizeForLog(requestingSystemName), SanitizeForLog(messageId.ToString()));

                    return StatusCode(500, new ErrorResponse
                    {
                        Message = "Failed to create initial message",
                        ErrorCode = "MESSAGE_CREATION_ERROR",
                        Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName }),
                        Timestamp = DateTime.UtcNow
                    });
                }

                // Send message via NServiceBus
                try
                {
                    var routeJson = JsonSerializer.Serialize(routeConfig.Route);
                    _aspLogger.LogDebug("Serialized route configuration: {Route}, MessageId: {MessageId}",
                        SanitizeForLog(routeJson), SanitizeForLog(messageId.ToString()));

                    var sendOptions = new SendOptions();
                    sendOptions.SetDestination("ProcessRequestEndPoint");
                    sendOptions.SetHeader("RoutingSlip-Route", routeJson);
                    sendOptions.SetHeader("RoutingSlip-Index", "0");
                    sendOptions.SetHeader("RoutingSlip-OriginatingId", messageId.ToString());

                    _aspLogger.LogInformation("Sending message to NServiceBus - MessageId: {MessageId}, Destination: ProcessRequestEndPoint", SanitizeForLog(messageId.ToString()));

                    await _messageSession.Send(firstMessage, sendOptions);

                    _aspLogger.LogInformation("Message sent successfully - MessageId: {MessageId}, RequestingSystem: {RequestingSystem}, DocId: {DocId}, RouteSteps: {RouteSteps}",
                        SanitizeForLog(messageId.ToString()), SanitizeForLog(requestingSystemName), SanitizeForLog(request.Process.DocId ?? "Not assigned"), routeConfig.Route.Count);
                }
                catch (SerializationException ex)
                {
                    _aspLogger.LogError(ex, "Serialization error in ProcessCorrespondenceRequest - MessageId: {MessageId}, ErrorCode: {ErrorCode}",
                        SanitizeForLog(messageId.ToString()), ex.ErrorCode);

                    return StatusCode(500, new ErrorResponse
                    {
                        Message = "Failed to serialize route configuration",
                        ErrorCode = ex.ErrorCode ?? "SERIALIZATION_ERROR",
                        Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName }),
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (CorrespondenceException ex)
                {
                    _aspLogger.LogError(ex, "Correspondence error in ProcessCorrespondenceRequest - MessageId: {MessageId}, ErrorCode: {ErrorCode}",
                        SanitizeForLog(messageId.ToString()), ex.ErrorCode);

                    return StatusCode(500, new ErrorResponse
                    {
                        Message = ex.Message,
                        ErrorCode = ex.ErrorCode ?? "CORRESPONDENCE_ERROR",
                        Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName }),
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    _aspLogger.LogError(ex, "Unexpected error in ProcessCorrespondenceRequest - MessageId: {MessageId}",
                        SanitizeForLog(messageId.ToString()));

                    return StatusCode(500, new ErrorResponse
                    {
                        Message = "An unexpected error occurred while processing the request",
                        ErrorCode = "UNEXPECTED_ERROR",
                        Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName, error = ex.Message }),
                        Timestamp = DateTime.UtcNow
                    });
                }

                // RETURN COMBINED RESPONSE
                return Accepted(new
                {
                    correlationId = messageId,
                    docId = request.Process.DocId,
                    deliveryMethod = request.Process.DeliveryMethod,
                    requestingSystem = requestingSystemName,
                    documentType,
                    documentTypeDetails = documentTypeDetails != null ? new
                    {
                        documentType = documentTypeDetails.DocumentType,
                        documentSubType = documentTypeDetails.DocumentSubType,
                        cabinetName = documentTypeDetails.CabinetName
                    } : null,
                    // ePresent-specific (only when applicable)
                    ePresentAccountId = request.Process.IsEPresentDelivery
                        ? request.Process.JourneyDeliveryInfo?.AccountId
                        : null,
                    status = "Accepted for processing",
                    timestamp = DateTime.UtcNow,
                    ventureFusionData = ventureFusionFormattedResponse
                });
            }
            catch (ValidationException ex)
            {
                _aspLogger.LogError(ex, "Validation error in ProcessCorrespondenceRequest - MessageId: {MessageId}, ErrorCode: {ErrorCode}",
                    SanitizeForLog(messageId.ToString()), ex.ErrorCode);

                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = ex.ErrorCode ?? "VALIDATION_ERROR",
                    ValidationErrors = ex.ValidationErrors,
                    Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName }),
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (CorrespondenceException ex)
            {
                _aspLogger.LogError(ex, "Correspondence error in ProcessCorrespondenceRequest - MessageId: {MessageId}, ErrorCode: {ErrorCode}",
                    SanitizeForLog(messageId.ToString()), ex.ErrorCode);

                return StatusCode(500, new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = ex.ErrorCode ?? "CORRESPONDENCE_ERROR",
                    Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName }),
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Unexpected error in ProcessCorrespondenceRequest - MessageId: {MessageId}",
                    SanitizeForLog(messageId.ToString()));

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An unexpected error occurred while processing the request",
                    ErrorCode = "UNEXPECTED_ERROR",
                    Details = JsonSerializer.Serialize(new { messageId, requestingSystem = requestingSystemName, error = ex.Message }),
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("status/docid/{docId}")]
        public async Task<IActionResult> GetCorrespondenceStatusByDocId(string docId)
        {
            try
            {
                _aspLogger.LogDebug("Get correspondence status by DocId requested: {DocId}", SanitizeForLog(docId));

                if (string.IsNullOrWhiteSpace(docId))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "DocId is required",
                        ErrorCode = "VALIDATION_ERROR",
                        Timestamp = DateTime.UtcNow
                    });
                }
                // Validate DocId format (19-character alphanumeric)
                if (docId.Length != 19 || !System.Text.RegularExpressions.Regex.IsMatch(docId, @"^[A-Za-z0-9-]{19}$"))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "DocId must be a 19-character alphanumeric string",
                        ErrorCode = "INVALID_DOCID_FORMAT",
                        Details = JsonSerializer.Serialize(new { docId }),
                        Timestamp = DateTime.UtcNow
                    });
                }
                var status = await _correspondenceStatusService.GetCorrespondenceStatusByDocIdAsync(docId);
                if (status == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        Message = $"No correspondence found for DocId: {docId}",
                        ErrorCode = "CORRESPONDENCE_NOT_FOUND",
                        Details = JsonSerializer.Serialize(new { docId }),
                        Timestamp = DateTime.UtcNow
                    });
                }
                return Ok(new
                {
                    success = true,
                    data = status,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (NotFoundException ex)
            {
                _aspLogger.LogError(ex, "Correspondence not found for DocID: {DocId}, ErrorCode: {ErrorCode}",
                    SanitizeForLog(docId), ex.ErrorCode);

                return NotFound(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = ex.ErrorCode ?? "CORRESPONDENCE_NOT_FOUND",
                    Details = JsonSerializer.Serialize(new { docId }),
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (CorrespondenceException ex)
            {
                _aspLogger.LogError(ex, "Correspondence error getting status for DocID: {DocId}, ErrorCode: {ErrorCode}",
                    SanitizeForLog(docId), ex.ErrorCode);

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred while retrieving the correspondence status",
                    ErrorCode = ex.ErrorCode ?? "STATUS_RETRIEVAL_ERROR",
                    Details = JsonSerializer.Serialize(new { docId }),
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Unexpected error getting correspondence status for DocId: {DocId}", SanitizeForLog(docId));

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred while retrieving the correspondence status",
                    ErrorCode = "UNEXPECTED_ERROR",
                    Details = JsonSerializer.Serialize(new { docId, error = ex.Message }),
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("status/correspondence/{correspondenceId:guid}")]
        public async Task<IActionResult> GetCorrespondenceStatusByCorrespondenceId(Guid correspondenceId)
        {
            try
            {
                _aspLogger.LogDebug("GetCorrespondenceStatusByCorrespondenceId: {CorrespondenceId}", correspondenceId);

                if (correspondenceId == Guid.Empty)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Valid CorrespondenceId is required",
                        ErrorCode = "VALIDATION_ERROR",
                        Timestamp = DateTime.UtcNow
                    });
                }

                var status = await _correspondenceStatusService.GetCorrespondenceStatusByCorrespondenceIdAsync(correspondenceId);

                if (status == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        Message = $"No correspondence found for CorrespondenceId: {correspondenceId}",
                        ErrorCode = "CORRESPONDENCE_NOT_FOUND",
                        Details = JsonSerializer.Serialize(new { correspondenceId }),
                        Timestamp = DateTime.UtcNow
                    });
                }
                return Ok(new
                {
                    success = true,
                    data = status,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Error getting correspondence status for CorrespondenceId: {CorrespondenceId}", correspondenceId);
                return StatusCode(500, new { success = false, error = "An error occurred while retrieving the correspondence status", correspondenceId, timestamp = DateTime.UtcNow });
            }
        }

        /// <summary>
        /// Get correspondence status by Policy Number
        /// </summary>
        /// <param name="policyNumber">The policy number</param>
        /// <returns>List of correspondence status information</returns>
        /// removed HTTPGET annotation and added non-action annotation so that it is not treated as an API endpoint and exposed to customer but can be used as helper method.
        [NonAction]
        public async Task<IActionResult> GetCorrespondenceStatusByPolicyNumber(string policyNumber)
        {
            try
            {
                _aspLogger.LogInformation("Get correspondence status by PolicyNumber requested: {PolicyNumber}", SanitizeForLog(policyNumber));

                if (string.IsNullOrWhiteSpace(policyNumber))
                {
                    _aspLogger.LogWarning("GetCorrespondenceStatusByPolicyNumber called with null or empty policyNumber");
                    return BadRequest(new ErrorResponse
                    {
                        Message = "PolicyNumber is required",
                        ErrorCode = "VALIDATION_ERROR",
                        Timestamp = DateTime.UtcNow
                    });
                }

                var statuses = await _correspondenceStatusService.GetCorrespondenceStatusByPolicyNumberAsync(policyNumber);
                var statusList = statuses.ToList();

                if (!statusList.Any())
                {
                    _aspLogger.LogInformation("No correspondence found for PolicyNumber: {PolicyNumber}", SanitizeForLog(policyNumber));
                    return NotFound(new ErrorResponse
                    {
                        Message = $"No correspondence found for PolicyNumber: {policyNumber}",
                        ErrorCode = "CORRESPONDENCE_NOT_FOUND",
                        Details = JsonSerializer.Serialize(new { policyNumber }),
                        Timestamp = DateTime.UtcNow
                    });
                }

                _aspLogger.LogInformation("Successfully retrieved {Count} correspondence records for PolicyNumber: {PolicyNumber}",
                    statusList.Count, SanitizeForLog(policyNumber));
                
                return Ok(new
                {
                    success = true,
                    count = statusList.Count,
                    data = statusList,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = ex.ErrorCode ?? "CORRESPONDENCE_NOT_FOUND",
                    Details = JsonSerializer.Serialize(new { policyNumber }),
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (CorrespondenceException ex)
            {
                _aspLogger.LogError(ex, "Correspondence error getting status for PolicyNumber: {PolicyNumber}, ErrorCode: {ErrorCode}",
                    SanitizeForLog(policyNumber), ex.ErrorCode);

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred while retrieving the correspondence status",
                    ErrorCode = ex.ErrorCode ?? "STATUS_RETRIEVAL_ERROR",
                    Details = JsonSerializer.Serialize(new { policyNumber }),
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Unexpected error getting correspondence status for PolicyNumber: {PolicyNumber}",
                    SanitizeForLog(policyNumber));

                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred while retrieving the correspondence status",
                    ErrorCode = "UNEXPECTED_ERROR",
                    Details = JsonSerializer.Serialize(new { policyNumber, error = ex.Message }),
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Get correspondence status by Claim Number (paginated)
        /// </summary>
        /// <param name="claimNumber">The claim number</param>
        /// <param name="pageNumber">Page number (default: 1)</param>
        /// <param name="pageSize">Page size (default: 25, max: 50)</param>
        /// <returns>Paginated list of correspondence status information</returns>
        [HttpGet("status/claim/{claimNumber}")]
        public async Task<IActionResult> GetCorrespondenceStatusByClaimNumber(string claimNumber, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 25)
        {
            try
            {
                _aspLogger.LogDebug("GetCorrespondenceStatusByClaimNumber: {ClaimNumber}, Page: {Page}, Size: {Size}",
                    SanitizeForLog(claimNumber), pageNumber, pageSize);

                if (string.IsNullOrWhiteSpace(claimNumber))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "ClaimNumber is required",
                        timestamp = DateTime.UtcNow
                    });
                }

                var paginatedResult = await _correspondenceStatusService.GetCorrespondenceStatusByClaimNumberAsync(claimNumber, pageNumber, pageSize);

                if (paginatedResult.TotalCount == 0)
                {
                    return NotFound(new
                    {
                        success = false,
                        error = $"No correspondence found for ClaimNumber: {claimNumber}",
                        claimNumber = claimNumber,
                        timestamp = DateTime.UtcNow
                    });
                }

                return Ok(new
                {
                    success = true,
                    totalCount = paginatedResult.TotalCount,
                    pageNumber = paginatedResult.PageNumber,
                    pageSize = paginatedResult.PageSize,
                    totalPages = paginatedResult.TotalPages,
                    hasNextPage = paginatedResult.HasNextPage,
                    hasPreviousPage = paginatedResult.HasPreviousPage,
                    data = paginatedResult.Data,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Error getting correspondence status for ClaimNumber: {ClaimNumber}", SanitizeForLog(claimNumber));
                return StatusCode(500, new
                {
                    success = false,
                    error = "An error occurred while retrieving the correspondence status",
                    claimNumber = claimNumber,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Get correspondence status by multiple Document Types (paginated).
        /// Uses partial matching - the search terms are matched against document types using a "contains" approach.
        /// </summary>
        /// <param name="documentTypes">Comma-separated list of document type search terms</param>
        /// <param name="pageNumber">Page number (default: 1)</param>
        /// <param name="pageSize">Page size (default: 25, max: 50)</param>
        /// <returns>Paginated list of correspondence status information</returns>
        [HttpGet("status/documenttypes")]
        public async Task<IActionResult> GetCorrespondenceStatusByDocumentTypes([FromQuery] string documentTypes, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 25)
        {
            try
            {
                _aspLogger.LogDebug("GetCorrespondenceStatusByDocumentTypes: {DocumentTypes}, Page: {Page}, Size: {Size}",
                    SanitizeForLog(documentTypes), pageNumber, pageSize);

                if (string.IsNullOrWhiteSpace(documentTypes))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "DocumentTypes query parameter is required. Provide comma-separated document type search terms.",
                        timestamp = DateTime.UtcNow
                    });
                }

                var documentTypeList = documentTypes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!documentTypeList.Any())
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "At least one valid DocumentType search term is required",
                        timestamp = DateTime.UtcNow
                    });
                }

                // Validate document types using partial match - separate valid from invalid
                var invalidTypes = new List<string>();
                var validTypes = new List<string>();
                foreach (var dt in documentTypeList)
                {
                    var exists = await _documentTypeService.DocumentTypeExistsPartialAsync(dt);
                    if (!exists)
                    {
                        invalidTypes.Add(dt);
                    }
                    else
                    {
                        validTypes.Add(dt);
                    }
                }

                // If ALL types are invalid, return 404
                if (!validTypes.Any())
                {
                    return NotFound(new
                    {
                        success = false,
                        error = $"None of the provided DocumentType search term(s) matched any records: {string.Join(", ", invalidTypes)}",
                        invalidDocumentTypes = invalidTypes,
                        timestamp = DateTime.UtcNow
                    });
                }

                // Log skipped invalid types as a warning
                if (invalidTypes.Any())
                {
                    _aspLogger.LogWarning("Skipping invalid DocumentType search term(s): {InvalidTypes}. Proceeding with valid terms: {ValidTypes}",
                        SanitizeForLog(string.Join(", ", invalidTypes)),
                        SanitizeForLog(string.Join(", ", validTypes)));
                }

                var paginatedResult = await _correspondenceStatusService.GetCorrespondenceStatusByDocumentTypesAsync(validTypes, pageNumber, pageSize);

                if (paginatedResult.TotalCount == 0)
                {
                    return NotFound(new
                    {
                        success = false,
                        error = $"No correspondence found for DocumentType search terms: {string.Join(", ", validTypes)}",
                        searchedDocumentTypes = validTypes,
                        skippedInvalidTypes = invalidTypes.Any() ? invalidTypes : null,
                        timestamp = DateTime.UtcNow
                    });
                }

                return Ok(new
                {
                    success = true,
                    totalCount = paginatedResult.TotalCount,
                    pageNumber = paginatedResult.PageNumber,
                    pageSize = paginatedResult.PageSize,
                    totalPages = paginatedResult.TotalPages,
                    hasNextPage = paginatedResult.HasNextPage,
                    hasPreviousPage = paginatedResult.HasPreviousPage,
                    searchedDocumentTypes = validTypes,
                    skippedInvalidTypes = invalidTypes.Any() ? invalidTypes : null,
                    data = paginatedResult.Data,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Error getting correspondence status for DocumentTypes: {DocumentTypes}", SanitizeForLog(documentTypes));
                return StatusCode(500, new
                {
                    success = false,
                    error = "An error occurred while retrieving the correspondence status",
                    documentTypes = documentTypes,
                    timestamp = DateTime.UtcNow
                });
            }
        }
        /// <summary>
        /// Get filtered correspondence data based on search criteria
        /// </summary>
        /// <param name="request">The filter criteria</param>
        /// <returns>List of filtered correspondence records</returns>
        [HttpPost("status/correspondencefilterrequest")]
        public async Task<IActionResult> GetFilteredCorrespondence([FromBody] CustomerCorrespondence.Domain.DTOs.CorrespondenceFilterRequest request)
        {
            try
            {
                _aspLogger.LogInformation("Get filtered correspondence requested");

                if (request == null)
                {
                    _aspLogger.LogWarning("GetFilteredCorrespondence called with null request");
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Request cannot be null",
                        ErrorCode = "VALIDATION_ERROR",
                        Timestamp = DateTime.UtcNow
                    });
                }

                var results = await _correspondenceFilterService.GetFilteredCorrespondenceAsync(request);
                var resultList = results.ToList();
                
                if (resultList.Count > 0)
                {
                    _aspLogger.LogInformation("Successfully retrieved {Count} filtered correspondence records", resultList.Count);
                    return Ok(new
                    {
                        success = true,
                        count = resultList.Count,
                        data = resultList,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    _aspLogger.LogInformation("No DocID's found for search criteria");
                    return NotFound(new ErrorResponse
                    {
                        Message = "No DocID's found for search criteria",
                        ErrorCode = "NO_RESULTS_FOUND",
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (CorrespondenceException ex)
            {
                _aspLogger.LogError(ex, "Correspondence error getting filtered correspondence, ErrorCode: {ErrorCode}", ex.ErrorCode);
                
                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred while retrieving filtered correspondence data",
                    ErrorCode = ex.ErrorCode ?? "FILTER_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Error getting filtered correspondence");
                
                return StatusCode(500, new ErrorResponse
                {
                    Message = "An error occurred while retrieving filtered correspondence data",
                    ErrorCode = "UNEXPECTED_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("health")]
        public async Task<IActionResult> GetDetailedHealth()
        {
            try
            {
                _aspLogger.LogInformation("Detailed health check requested via CommManager");

                // Get the health check service and run all health checks
                var healthCheckService = HttpContext.RequestServices.GetRequiredService<HealthCheckService>();
                var healthReport = await healthCheckService.CheckHealthAsync();

                var detailedResult = new
                {
                    status = healthReport.Status.ToString(),
                    totalDuration = healthReport.TotalDuration.TotalMilliseconds,
                    checkedAt = DateTime.UtcNow,
                    results = healthReport.Entries.Select(e => new
                    {
                        key = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        duration = e.Value.Duration.TotalMilliseconds,
                        error = e.Value.Exception?.Message,
                        errorCode = (e.Value.Exception as CorrespondenceException)?.ErrorCode,
                        stackTrace = e.Value.Exception?.StackTrace,
                        data = e.Value.Data,
                        tags = e.Value.Tags?.ToArray()
                    }).ToArray(),
                    systemInfo = new
                    {
                        machineName = Environment.MachineName,
                        osVersion = Environment.OSVersion.ToString(),
                        dotNetVersion = Environment.Version.ToString(),
                        processorCount = Environment.ProcessorCount,
                        workingSet = Environment.WorkingSet,
                        totalMemory = GC.GetTotalMemory(false),
                        uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()
                    },
                    configuration = new
                    {
                        environment = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName,
                        forceAzureMode = HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("ForceAzureMode"),
                        useAzureBehavior = HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("UseAzureBehavior"),
                        keyVaultUrl = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["KeyVault:Url"]
                    }
                };

                return Ok(detailedResult);
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Detailed health check failed: {Message}", ex.Message);

                return StatusCode(500, new ErrorResponse
                {
                    Message = "Detailed health check failed",
                    ErrorCode = "HEALTHCHECK_DETAILED_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("cache")]
        public IActionResult GetCacheInfo()
        {
            try
            {
                _aspLogger.LogInformation("Cache info requested via CommManager");

                var cache = HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                var secretsCache = HttpContext.RequestServices.GetRequiredService<ISecretsCacheService>();

                var cacheInfo = new
                {
                    timestamp = DateTime.UtcNow,
                    cacheType = cache.GetType().Name,
                    secretsCacheType = secretsCache.GetType().Name,
                    testResult = TestCacheDirectly(cache),
                    cacheContents = GetCacheContents(cache)
                };

                return Ok(cacheInfo);
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Failed to get cache info");
                return StatusCode(500, new ErrorResponse
                {
                    Message = "Failed to get cache info",
                    ErrorCode = "CACHE_INFO_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("cache/secrets")]
        public IActionResult GetCacheSecrets()
        {
            try
            {
                _aspLogger.LogInformation("Cache secrets requested via CommManager");

                var secretsCache = HttpContext.RequestServices.GetRequiredService<ISecretsCacheService>();
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();

                var secretMappings = new Dictionary<string, object>();
                var secretNames = new[]
                {
                    "AzureServiceBusFQURL", "AzureDbConnectionString",
                    "VFClientID", "VFClientSecret", "VFSubscriptionKey", "VFTenantID",
                    "VFUri", "VFScopes", "VFTokenEndpoint", "VFSolutionID", "VFCustomerID"
                };

                foreach (var secretName in secretNames)
                {
                    var configKey = $"SecretNames:{secretName}";
                    var secretKeyName = configuration[configKey];
                    
                    // FIX: Check if secretKeyName is null before calling IsSecretCached
                    secretMappings[secretName] = new
                    {
                        configKey = configKey,
                        secretKeyName = secretKeyName ?? "[NOT CONFIGURED]",
                        cacheKey = !string.IsNullOrEmpty(secretKeyName) ? $"secret:{secretKeyName}" : null,
                        isInCache = !string.IsNullOrEmpty(secretKeyName) && secretsCache.IsSecretCached(secretKeyName),
                        configExists = !string.IsNullOrEmpty(secretKeyName)
                    };
                }

                return Ok(new { timestamp = DateTime.UtcNow, secretMappings });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Failed to get cache secrets");
                return StatusCode(500, new ErrorResponse
                {
                    Message = "Failed to get cache secrets",
                    ErrorCode = "CACHE_SECRETS_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpPost("cache/warmup")]
        public async Task<IActionResult> WarmupCache()
        {
            try
            {
                _aspLogger.LogInformation("Cache warmup requested via CommManager");

                var secretsCache = HttpContext.RequestServices.GetRequiredService<ISecretsCacheService>();
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();

                await WarmupCacheAsync(secretsCache, configuration, _aspLogger);

                return Ok(new { success = true, message = "Cache warmup completed", timestamp = DateTime.UtcNow });
            }
            catch (SecretNotFoundException ex)
            {
                _aspLogger.LogError(ex, "Secret not found during cache warmup: {SecretName}", ex.SecretName);
                return StatusCode(500, new ErrorResponse
                {
                    Message = "Secret not found during cache warmup",
                    ErrorCode = ex.ErrorCode ?? "SECRET_NOT_FOUND",
                    Details = ex.SecretName,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Cache warmup failed");
                return StatusCode(500, new ErrorResponse
                {
                    Message = "Cache warmup failed",
                    ErrorCode = "CACHE_WARMUP_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpPost("cache/clear")]
        public IActionResult ClearCache()
        {
            try
            {
                _aspLogger.LogInformation("Cache clear requested via CommManager");

                var cache = HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                var secretsCache = HttpContext.RequestServices.GetRequiredService<ISecretsCacheService>();

                if (cache is ClearableMemoryCache clearableCache)
                {
                    var keysBefore = clearableCache.GetKeys().ToList();
                    clearableCache.Clear();
                    _aspLogger.LogInformation("Cache cleared successfully. Removed {Count} entries", keysBefore.Count);

                    return Ok(new
                    {
                        success = true,
                        message = "Cache cleared successfully",
                        entriesBeforeClear = keysBefore.Count,
                        clearedKeys = keysBefore.Take(10).ToList()
                    });
                }
                else
                {
                    secretsCache.ClearCache();
                    _aspLogger.LogInformation("Cache cleared using SecretsCacheService");
                    return Ok(new { success = true, message = "Cache cleared using SecretsCacheService" });
                }
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Failed to clear cache");
                return StatusCode(500, new ErrorResponse
                {
                    Message = "Failed to clear cache",
                    ErrorCode = "CACHE_CLEAR_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpDelete("cache/{secretName}")]
        public IActionResult RemoveSecretFromCache(string secretName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(secretName))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Secret name is required",
                        ErrorCode = "VALIDATION_ERROR",
                        Timestamp = DateTime.UtcNow
                    });
                }

                _aspLogger.LogInformation("Secret removal requested for: {SecretName}", SanitizeForLog(secretName));

                var secretsCache = HttpContext.RequestServices.GetRequiredService<ISecretsCacheService>();
                secretsCache.RemoveSecret(secretName);

                _aspLogger.LogInformation("Secret {SecretName} removed from cache", SanitizeForLog(secretName));
                return Ok(new { success = true, message = $"Secret '{secretName}' removed from cache" });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Failed to remove secret {SecretName}", SanitizeForLog(secretName));
                return StatusCode(500, new ErrorResponse
                {
                    Message = $"Failed to remove secret '{secretName}' from cache",
                    ErrorCode = "CACHE_REMOVE_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("cache/secret/{secretName}")]
        public async Task<IActionResult> GetSecretByName(string secretName, [FromQuery] bool includeValue = false)
        {
            try
            {
                // Block includeValue in non-development environments
                var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
                if (includeValue && !env.IsDevelopment())
                {
                    return BadRequest(new { success = false, error = "includeValue is only available in Development environment" });
                }

                _aspLogger.LogDebug("GetSecretByName requested: {SecretName}", SanitizeForLog(secretName));

                var cache = HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                var secretsCache = HttpContext.RequestServices.GetRequiredService<ISecretsCacheService>();
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();

                var configKey = $"SecretNames:{secretName}";
                var actualSecretName = configuration[configKey];
                var keyVaultSecretName = !string.IsNullOrEmpty(actualSecretName) ? actualSecretName : secretName;
                var cacheKey = $"secret:{keyVaultSecretName}";

                string secretValue;
                bool wasFromCache;
                double retrievalDuration = 0;

                var isInCache = cache.TryGetValue(cacheKey, out var cachedValue);

                if (isInCache && cachedValue is string cachedSecret)
                {
                    secretValue = cachedSecret;
                    wasFromCache = true;
                }
                else
                {
                    var startTime = DateTime.UtcNow;
                    secretValue = await secretsCache.GetSecretAsync(keyVaultSecretName);
                    retrievalDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    wasFromCache = false;
                }

                var response = new
                {
                    success = true,
                    requestedName = secretName,
                    actualSecretName = keyVaultSecretName,
                    source = wasFromCache ? "cache" : "keyvault",
                    hasValue = !string.IsNullOrEmpty(secretValue),
                    valueLength = secretValue?.Length ?? 0,
                    retrievalDuration = wasFromCache ? 0 : retrievalDuration,
                    timestamp = DateTime.UtcNow
                };

                if (includeValue)
                {
                    return Ok(new
                    {
                        response.success,
                        response.requestedName,
                        response.actualSecretName,
                        response.source,
                        response.hasValue,
                        response.valueLength,
                        response.retrievalDuration,
                        response.timestamp,
                        secretValue
                    });
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Failed to get secret: {SecretName}", SanitizeForLog(secretName));
                return StatusCode(500, new { success = false, error = ex.Message, secretName });
            }
        }

        [HttpGet("cache/secret/{secretName}/info")]
        public IActionResult GetSecretInfo(string secretName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(secretName))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Secret name is required",
                        ErrorCode = "VALIDATION_ERROR",
                        Timestamp = DateTime.UtcNow
                    });
                }

                _aspLogger.LogInformation("Get secret info requested: {SecretName}", SanitizeForLog(secretName));

                var cache = HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();

                // Check if this is a config name or actual KeyVault name
                var configKey = $"SecretNames:{secretName}";
                var actualSecretName = configuration[configKey];
                var keyVaultSecretName = !string.IsNullOrEmpty(actualSecretName) ? actualSecretName : secretName;
                var cacheKey = $"secret:{keyVaultSecretName}";

                var isInCache = cache.TryGetValue(cacheKey, out var cachedValue);

                return Ok(new
                {
                    requestedName = secretName,
                    actualSecretName = keyVaultSecretName,
                    configKey = configKey,
                    cacheKey = cacheKey,
                    isConfiguredSecret = !string.IsNullOrEmpty(actualSecretName),
                    isInCache = isInCache,
                    hasValue = cachedValue != null,
                    valueType = cachedValue?.GetType().Name,
                    valueLength = cachedValue is string str ? str.Length : (cachedValue?.ToString()?.Length ?? 0),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Failed to get secret info: {SecretName}", SanitizeForLog(secretName));
                return StatusCode(500, new ErrorResponse
                {
                    Message = $"Failed to get secret info: {secretName}",
                    ErrorCode = "SECRET_INFO_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpPost("cache/secret/{secretName}/refresh")]
        public async Task<IActionResult> RefreshSecret(string secretName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(secretName))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Secret name is required",
                        ErrorCode = "VALIDATION_ERROR",
                        Timestamp = DateTime.UtcNow
                    });
                }

                _aspLogger.LogInformation("Refresh secret requested: {SecretName}", SanitizeForLog(secretName));

                var secretsCache = HttpContext.RequestServices.GetRequiredService<ISecretsCacheService>();
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();

                // Check if this is a config name or actual KeyVault name
                var configKey = $"SecretNames:{secretName}";
                var actualSecretName = configuration[configKey];
                var keyVaultSecretName = !string.IsNullOrEmpty(actualSecretName) ? actualSecretName : secretName;

                // Remove from cache first
                secretsCache.RemoveSecret(keyVaultSecretName);
                _aspLogger.LogDebug("Removed {SecretName} from cache", SanitizeForLog(keyVaultSecretName));

                // Get fresh from KeyVault (this will cache it again)
                var startTime = DateTime.UtcNow;
                var secret = await secretsCache.GetSecretAsync(keyVaultSecretName);
                var duration = DateTime.UtcNow - startTime;

                return Ok(new
                {
                    success = true,
                    requestedName = secretName,
                    actualSecretName = keyVaultSecretName,
                    action = "refresh",
                    hasValue = !string.IsNullOrEmpty(secret),
                    valueLength = secret?.Length ?? 0,
                    retrievalDuration = duration.TotalMilliseconds,
                    fromKeyVault = true,
                    nowCached = true,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (SecretNotFoundException ex)
            {
                _aspLogger.LogError(ex, "Secret not found during refresh: {SecretName}", SanitizeForLog(secretName));
                return NotFound(new ErrorResponse
                {
                    Message = $"Secret not found: {secretName}",
                    ErrorCode = ex.ErrorCode ?? "SECRET_NOT_FOUND",
                    Details = ex.SecretName,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Failed to refresh secret: {SecretName}", SanitizeForLog(secretName));
                return StatusCode(500, new ErrorResponse
                {
                    Message = $"Failed to refresh secret: {secretName}",
                    ErrorCode = "SECRET_REFRESH_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [AllowAnonymous]
        [HttpGet("vfhealthcheck")]
        public async Task<IActionResult> VFHealthCheck([FromServices] IVentureFusionService ventureFusionService)
        {
            try
            {
                _aspLogger.LogInformation("VentureFusion service health check");
                var response = await ventureFusionService.CheckHealth();
                var content = await response.Content.ReadAsStringAsync();
                
                return Ok(new
                {
                    status = "Healthy",
                    message = "VentureFusion Service Health check success",
                    statusCode = (int)response.StatusCode,
                    responseContent = content,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (CommunicationException ex)
            {
                _aspLogger.LogError(ex, "VentureFusion communication error during health check, ErrorCode: {ErrorCode}", ex.ErrorCode);
                
                return StatusCode(500, new ErrorResponse
                {
                    Message = "VentureFusion service health check failed",
                    ErrorCode = ex.ErrorCode ?? "VF_HEALTHCHECK_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "VentureFusion service health check failed");
                
                return StatusCode(500, new ErrorResponse
                {
                    Message = "VentureFusion service health check failed",
                    ErrorCode = "VF_HEALTHCHECK_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Get correspondence requests filtered by document type
        /// </summary>
        /// <param name="documentType">The archive document type code to filter by</param>
        /// <returns>List of correspondence requests matching the document type</returns>
        [HttpGet("requests/bydocumenttype/{documentType}")]
        public async Task<IActionResult> GetCorrespondenceRequestsByDocumentType(string documentType)
        {
            try
            {
                _aspLogger.LogInformation("Get correspondence requests by DocumentType requested: " + SanitizeForLog(documentType));

                if (string.IsNullOrWhiteSpace(documentType))
                {
                    _aspLogger.LogWarning("GetCorrespondenceRequestsByDocumentType called with null or empty documentType");
                    return BadRequest(new
                    {
                        success = false,
                        error = "DocumentType is required",
                        timestamp = DateTime.UtcNow
                    });
                }

                // Validate documentType exists in CorrespondenceDocumentType table
                var documentTypeDetails = await _documentTypeService.GetDocumentTypeInfoAsync(documentType);
                if (documentTypeDetails == null)
                {
                    _aspLogger.LogWarning("DocumentType not found in CorrespondenceDocumentType table: " + SanitizeForLog(documentType));
                    return NotFound(new
                    {
                        success = false,
                        error = $"DocumentType '{documentType}' is not found in the CorrespondenceDocumentType table",
                        documentType = documentType,
                        timestamp = DateTime.UtcNow
                    });
                }

                var requests = await _correspondenceRequestService.GetCorrespondenceRequestsByDocumentTypeAsync(documentType);
                var requestList = requests.ToList();

                if (!requestList.Any())
                {
                    _aspLogger.LogInformation("No correspondence requests found for DocumentType: " + SanitizeForLog(documentType));
                    return Ok(new
                    {
                        success = true,
                        count = 0,
                        data = requestList,
                        documentTypeInfo = new
                        {
                            documentType = documentTypeDetails.DocumentType,
                            documentSubType = documentTypeDetails.DocumentSubType,
                            cabinetName = documentTypeDetails.CabinetName
                        },
                        message = $"No correspondence requests found for DocumentType: {documentType}",
                        timestamp = DateTime.UtcNow
                    });
                }

                _aspLogger.LogInformation("Successfully retrieved " + requestList.Count + " correspondence request(s) for DocumentType: " + SanitizeForLog(documentType));
                
                return Ok(new
                {
                    success = true,
                    count = requestList.Count,
                    data = requestList,
                    documentTypeInfo = new
                    {
                        documentType = documentTypeDetails.DocumentType,
                        documentSubType = documentTypeDetails.DocumentSubType,
                        cabinetName = documentTypeDetails.CabinetName
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Error getting correspondence requests for DocumentType: " + SanitizeForLog(documentType));
                return StatusCode(500, new
                {
                    success = false,
                    error = "An error occurred while retrieving correspondence requests by document type",
                    documentType = documentType,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        //// Helper methods
        private object GetCacheContents(IMemoryCache cache)
        {
            try
            {
                if (cache is ClearableMemoryCache clearableCache)
                {
                    var keys = clearableCache.GetKeys().ToList();
                    return new
                    {
                        totalEntries = keys.Count,
                        keys = keys.ToList()
                    };
                }
                return new { message = "Cache inspection not supported" };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        private object TestCacheDirectly(IMemoryCache cache)
        {
            try
            {
                var testKey = "debug-test";
                var testValue = $"test-{DateTime.UtcNow:HHmmss}";
                cache.Set(testKey, testValue, TimeSpan.FromMinutes(1));
                var retrieved = cache.Get<string>(testKey);

                return new
                {
                    success = retrieved == testValue,
                    original = testValue,
                    retrieved = retrieved,
                    match = retrieved == testValue,
                    cacheType = cache.GetType().Name
                };
            }
            catch (Exception ex)
            {
                return new { success = false, error = ex.Message };
            }
        }

        private async Task WarmupCacheAsync(ISecretsCacheService secretsCache, IConfiguration configuration, ILogger logger)
        {
            var secretsToWarmup = new[]
            {
                "AzureServiceBusFQURL", "AzureDbConnectionString",
                "VFClientID", "VFClientSecret", "VFSubscriptionKey", "VFTenantID",
                "VFUri", "VFScopes", "VFTokenEndpoint", "VFSolutionID", "VFCustomerID",
                "NServicebusLicense"
            };

            var successCount = 0;
            var failureCount = 0;

            foreach (var secretName in secretsToWarmup)
            {
                try
                {
                    var configKey = $"SecretNames:{secretName}";
                    var secretKeyName = configuration[configKey];

                    if (!string.IsNullOrEmpty(secretKeyName))
                    {
                        await secretsCache.GetSecretAsync(secretKeyName);
                        successCount++;
                        logger.LogDebug("Successfully warmed up secret: {SecretName}", SanitizeForLog(secretName));
                    }
                    else
                    {
                        logger.LogWarning("Configuration key not found for secret: {SecretName}", SanitizeForLog(secretName));
                        failureCount++;
                    }
                }
                catch (SecretNotFoundException ex)
                {
                    failureCount++;
                    logger.LogWarning(ex, "Secret not found during warmup: {SecretName}", SanitizeForLog(secretName));
                }
                catch (Exception ex)
                {
                    failureCount++;
                    logger.LogWarning(ex, "Failed to cache secret: {SecretName}", SanitizeForLog(secretName));
                }
            }

            logger.LogInformation("Cache warmup completed. Success: {SuccessCount}, Failures: {FailureCount}", successCount, failureCount);
        }

        private object CreateInitialMessage(string handlerName, SequentialProcess process)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(handlerName))
                {
                    _aspLogger.LogError("Handler name is null or empty");
                    throw new ValidationException("Handler name cannot be null or empty",
                        new Dictionary<string, List<string>> { ["HandlerName"] = new List<string> { "Handler name is required" } });
                }

                var originalHandlerName = handlerName;
                var normalized = handlerName.Trim().ToLowerInvariant();

                _aspLogger.LogDebug("Creating initial message - Original: '{Original}', Normalized: '{Normalized}'",
                    originalHandlerName, normalized);

                object message = normalized switch
                {
                    var type when type.Contains("beginrequesthandler") =>
                        new BeginRequestHandlerMessage { Process = process },
                    var type when type.Contains("assigndocumentumidhandler") =>
                        new AssignDocumentumIdHandlerMessage { Process = process },
                    var type when type.Contains("addcorrespondencesbushandler") =>
                        new AddCorrespondenceSBusHandlerMessage { Process = process },
                    var type when type.Contains("addcorrespondencedbhandler") =>
                        new AddCorrespondenceDBHandlerMessage { Process = process },
                    var type when type.Contains("requestcompositionhandler") =>
                        new RequestCompositionHandlerMessage { Process = process },
                    var type when type.Contains("updatecorrespondencedbhandler") =>
                        new UpdateCorrespondenceDBHandlerMessage { Process = process },
                    _ => throw new ValidationException($"Unknown handler type: '{originalHandlerName}'. Available handlers: BeginRequestHandler, AssignDocumentumIdHandler, AddCorrespondenceSBusHandler, AddCorrespondenceDBHandler, RequestCompositionHandler, UpdateCorrespondenceDBHandler",
                        new Dictionary<string, List<string>> { ["HandlerName"] = new List<string> { $"'{originalHandlerName}' is not a valid handler type" } })
                };

                _aspLogger.LogDebug("Successfully created message for handler: {HandlerName}, MessageType: {MessageType}",
                    originalHandlerName, message.GetType().Name);

                return message;
            }
            catch (Exception ex)
            {
                _aspLogger.LogError(ex, "Error in CreateInitialMessage for handler: {HandlerName}", SanitizeForLog(handlerName));
                throw;
            }
        }

        // To fix Veracode flaw "117 Improper Output Neutralization for Logs", ensure that any user-supplied or external data written to logs is properly sanitized or encoded to prevent log injection.

        // Example fix for logging user-supplied data:
        // _aspLogger.LogInformation("Processing request for system: {RequestingSystemName}, DocId: {DocId}, MessageId: {MessageId}",
        //     SanitizeForLog(requestingSystemName), SanitizeForLog(request.Process.DocId ?? "Not assigned"), messageId);

        // Add a helper method to sanitize log input:
        private static string SanitizeForLog(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Remove newlines, tabs, nulls, and other control chars except space
            var sanitized = new string(input
                .Where(c => c == ' ' || (c >= 0x20 && c != 0x7F))
                .ToArray());

            // Truncate to 500 chars to avoid log flooding
            if (sanitized.Length > 500)
                sanitized = sanitized.Substring(0, 500) + "...[truncated]";

            // HTML encode to neutralize special characters
            return System.Net.WebUtility.HtmlEncode(sanitized);
        }

        /// <summary>
        /// Validates if the provided DocId is already in use for another correspondence request.
        /// </summary>
        /// <param name="docId">The document ID to validate.</param>
        /// <returns>True if the DocId is already in use, false otherwise.</returns>
        private async Task<bool> IsDocIdAlreadyInUseAsync(string docId)
        {
            if (string.IsNullOrWhiteSpace(docId))
            {
                return false;
            }

            var existingRequests = await _correspondenceRequestService.GetCorrespondenceRequestByDocIdAsync(docId);
            return existingRequests != null && existingRequests.Any();
        }
    }
}
