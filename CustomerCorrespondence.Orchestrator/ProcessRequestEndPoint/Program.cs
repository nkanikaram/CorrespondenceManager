using Azure;
using Azure.Identity;
using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Application.Services;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.HealthChecks;
using CustomerCorrespondence.Infrastructure.Messaging;
using CustomerCorrespondence.Infrastructure.Persistence;
using CustomerCorrespondence.Infrastructure.Repositories;
using CustomerCorrespondence.Infrastructure.Services;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using NServiceBus.Logging;
using ProcessRequestEndPoint.Handlers;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Text.Json;
using Endpoint = NServiceBus.Endpoint;

var builder = WebApplication.CreateBuilder(args);

// Configure logging first - Enhanced Azure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Enhanced Azure logging configuration
bool appInsightsConfigured = false;
string? appInsightsStatus = null;

if (!builder.Environment.IsDevelopment())
{
    try
    {
        var appInsightsConnectionString = builder.Configuration.GetConnectionString("ApplicationInsights");

        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            builder.Services.AddApplicationInsightsTelemetry(options =>
            {
                options.ConnectionString = appInsightsConnectionString;
                options.DeveloperMode = false;
                options.EnableAdaptiveSampling = true;
            });

            builder.Logging.AddApplicationInsights(
                configureTelemetryConfiguration: (config) => config.ConnectionString = appInsightsConnectionString,
                configureApplicationInsightsLoggerOptions: (options) => { });

            appInsightsConfigured = true;
            appInsightsStatus = "Application Insights configured successfully from appsettings.json";
        }
        else
        {
            appInsightsStatus = "No Application Insights connection string found in appsettings.json";
        }
    }
    catch (Exception ex)
    {
        appInsightsStatus = $"Failed to configure Application Insights: {ex.Message}";
    }
}

// near builder.Services registration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalReact", policy =>
    {
        // Allow common local dev ports for the React front-end (5173, 5174) and CRA default (3000).
        // Add additional localhost ports here if your dev server uses a different port.
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});


// Configure log levels from configuration
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

// Register services and dependencies
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
builder.Services.AddSingleton<KeyVaultService>();
builder.Services.AddSingleton<IKeyVaultService, KeyVaultService>();
builder.Services.AddSingleton<ISecretsCacheService, SecretsCacheService>();
builder.Services.AddSingleton<NServiceBusLicenseProvider>();
builder.Services.AddSingleton<IMemoryCache, ClearableMemoryCache>();
builder.Services.AddSingleton<HttpClient>();

builder.Services.AddHttpClient<IDocumentIdGeneratorService, DocumentIdGeneratorService>();
builder.Services.AddScoped<IVentureFusionService, VentureFusionService>();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IPgpEncryptionService, PgpEncryptionService>();
builder.Services.AddScoped<ICorrespondenceRequestService, CorrespondenceRequestService>();
builder.Services.AddScoped<ICorrespondenceRequestStatusHistoryService, CorrespondenceRequestStatusHistoryService>();
builder.Services.AddScoped<IRoutingConfigurationService, RoutingConfigurationService>();
builder.Services.AddScoped<ICorrespondenceRequestRepository, CorrespondenceRequestRepository>();
builder.Services.AddScoped<ICorrespondenceRequestStatusHistoryRepository, CorrespondenceRequestStatusHistoryRepository>();
builder.Services.AddScoped<ICorrespondenceStatusService, CorrespondenceStatusService>();
builder.Services.AddScoped<IRoutingConfigurationRepository, RoutingConfigurationRepository>();
builder.Services.AddScoped<IDocumentIdsService, DocumentIdsService>();
builder.Services.AddScoped<IDocumentIdsRepository, DocumentIdsRepository>();
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();
builder.Services.AddScoped<IChatHistoryRepository, ChatHistoryRepository>();
builder.Services.AddScoped<ICorrespondenceFilterService, CorrespondenceFilterService>();
builder.Services.AddScoped<IQueryIntentRouter, QueryIntentRouter>();

builder.Services.AddScoped<ICorrespondenceDocumentTypeRepository, CorrespondenceDocumentTypeRepository>();
builder.Services.AddScoped<ICorrespondenceDocumentTypeService, CorrespondenceDocumentTypeService>();

// Configure Solutions options
builder.Services.AddOptions<Solutions>().Configure<IServiceProvider>((solutionOptions, serviceProvider) =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var secretsCache = serviceProvider.GetRequiredService<ISecretsCacheService>();

    try
    {
        var vfCustomerID = configuration["SecretNames:VFCustomerID"];
        var vfUsageID = configuration["SecretNames:VFUsageID"];

        if (string.IsNullOrWhiteSpace(vfCustomerID) || string.IsNullOrWhiteSpace(vfUsageID))
        {
            logger.LogError("Customer ID, or Usage ID configuration is missing. VFCustomerID: {VFCustomerID}, VFUsageID: {VFUsageID}",
                vfCustomerID ?? "null", vfUsageID ?? "null");
            throw new ConfigurationException("Customer ID or Usage ID configuration is missing.", "CONFIG_MISSING");
        }

        var customerIdSecret = secretsCache.GetSecretAsync(vfCustomerID!).GetAwaiter().GetResult();
        var usageIdSecret = secretsCache.GetSecretAsync(vfUsageID!).GetAwaiter().GetResult();

        solutionOptions.customerId = customerIdSecret;

        if (!int.TryParse(usageIdSecret, out var parsedUsageId))
        {
            logger.LogError("VF Usage ID secret value '{UsageIdSecret}' is not a valid integer", usageIdSecret);
            throw new ValidationException($"VF Usage ID secret value '{usageIdSecret}' is not a valid integer.",
                new Dictionary<string, List<string>> { ["VFUsageID"] = new List<string> { "Must be a valid integer" } });
        }
        solutionOptions.usageId = parsedUsageId;

        logger.LogInformation("Solutions options configured successfully. UsageId: {UsageId}", solutionOptions.usageId);
    }
    catch (ConfigurationException)
    {
        throw;
    }
    catch (ValidationException)
    {
        throw;
    }
    catch (SecretNotFoundException ex)
    {
        logger.LogError(ex, "Failed to retrieve required secret for Solutions configuration");
        throw;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected error configuring Solutions options");
        throw new ConfigurationException("Failed to configure Solutions options", "SOLUTIONS_CONFIG_ERROR", ex);
    }
});

// Configure VentureFusion credentials
builder.Services.AddOptions<VentureFusionServiceCredentials>().Configure<IServiceProvider>((options, serviceProvider) =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var secretsCache = serviceProvider.GetRequiredService<ISecretsCacheService>();

    try
    {
        logger.LogInformation("Configuring VentureFusion service credentials");

        var secretNames = new[]
        {
            ("VFTenantID", "SecretNames:VFTenantID"),
            ("VFClientID", "SecretNames:VFClientID"),
            ("VFClientSecret", "SecretNames:VFClientSecret"),
            ("VFSubscriptionKey", "SecretNames:VFSubscriptionKey"),
            ("VFUri", "SecretNames:VFUri"),
            ("VFScopes", "SecretNames:VFScopes"),
            ("VFTokenEndpoint", "SecretNames:VFTokenEndpoint")
        };

        foreach (var (name, configKey) in secretNames)
        {
            var secretName = configuration[configKey];
            if (string.IsNullOrWhiteSpace(secretName))
            {
                logger.LogError("Configuration key {ConfigKey} is missing or empty", configKey);
                throw new ConfigurationException($"{configKey} configuration is missing.", "CONFIG_MISSING", configKey);
            }
        }

        options.TenantId = secretsCache.GetSecretAsync(configuration["SecretNames:VFTenantID"]!).GetAwaiter().GetResult();
        options.ClientId = secretsCache.GetSecretAsync(configuration["SecretNames:VFClientID"]!).GetAwaiter().GetResult();
        options.ClientSecret = secretsCache.GetSecretAsync(configuration["SecretNames:VFClientSecret"]!).GetAwaiter().GetResult();
        options.ApimSubscriptionKey = secretsCache.GetSecretAsync(configuration["SecretNames:VFSubscriptionKey"]!).GetAwaiter().GetResult();
        options.Scope = secretsCache.GetSecretAsync(configuration["SecretNames:VFScopes"]!).GetAwaiter().GetResult();
        options.TokenEndpoint = secretsCache.GetSecretAsync(configuration["SecretNames:VFTokenEndpoint"]!).GetAwaiter().GetResult();

        var uriString = secretsCache.GetSecretAsync(configuration["SecretNames:VFUri"]!).GetAwaiter().GetResult();
        options.Uri = new Uri(uriString);
        options.grant_type = "client_credentials";

        logger.LogInformation("VentureFusion service credentials configured successfully. TokenEndpoint: {TokenEndpoint}, URI: {Uri}",
            options.TokenEndpoint, options.Uri);
    }
    catch (ConfigurationException)
    {
        throw;
    }
    catch (SecretNotFoundException ex)
    {
        logger.LogError(ex, "Failed to retrieve VentureFusion credential from Key Vault");
        throw;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure VentureFusion credentials");
        throw new ConfigurationException("Failed to configure VentureFusion credentials", "VF_CONFIG_ERROR", ex);
    }
});

// Configure database context - connection string resolved once, reused on every scope creation
string? _cachedMainConnectionString = null;
bool _usingLocalDbFallback = false;
object _mainConnectionStringLock = new();

builder.Services.AddDbContext<CorrespondenceDBContext>((serviceProvider, options) =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

    if (_cachedMainConnectionString == null)
    {
        lock (_mainConnectionStringLock)
        {
            if (_cachedMainConnectionString == null)
            {
                var config = serviceProvider.GetRequiredService<IConfiguration>();
                var env = serviceProvider.GetRequiredService<IHostEnvironment>();
                // Only Development may use the local AZURE_SQL_CONNECTION override — appsettings.json
                // defines it for all environments, but Production/Demo must always use the Key Vault
                // secret so this never accidentally redirects a deployed environment to localdb.
                var localConnectionString = env.IsDevelopment()
                    ? (config.GetConnectionString("AZURE_SQL_CONNECTION") ?? config["AZURE_SQL_CONNECTION"])
                    : null;

                if (!string.IsNullOrWhiteSpace(localConnectionString))
                {
                    // Local development: prefer the plain AZURE_SQL_CONNECTION (e.g. localdb) over
                    // Key Vault + Managed Identity — the same connection used by
                    // CorrespondenceSearchController and the seed tool — even when Key Vault access
                    // to the cloud Dev DB happens to succeed (e.g. developer is signed in to Azure).
                    logger.LogWarning("Using local AZURE_SQL_CONNECTION for CorrespondenceDBContext (local development override)");
                    _cachedMainConnectionString = localConnectionString;
                    _usingLocalDbFallback = true;
                }
                else
                {
                    var secretsCache = serviceProvider.GetRequiredService<ISecretsCacheService>();

                    var dbSecretName = config["SecretNames:AzureDbConnectionString"];
                    if (string.IsNullOrWhiteSpace(dbSecretName))
                        throw new InvalidOperationException("Database connection string configuration is missing.");

                    var baseConnectionString = secretsCache.GetSecretAsync(dbSecretName).GetAwaiter().GetResult();
                    if (string.IsNullOrWhiteSpace(baseConnectionString))
                        throw new InvalidOperationException("Database connection string secret is missing from Key Vault.");

                    var csb = new SqlConnectionStringBuilder(baseConnectionString);
                    csb.Remove("Authentication");
                    csb.Remove("User ID");
                    csb.Remove("Password");

                    _cachedMainConnectionString = csb.ConnectionString;
                }

                logger.LogDebug("Database connection string resolved and cached");
            }
        }
    }

    options.UseSqlServer(_cachedMainConnectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
        sqlOptions.CommandTimeout(120);
    });

    if (!_usingLocalDbFallback)
    {
        var credential = new DefaultAzureCredential();
        options.AddInterceptors(new ManagedIdentityDbConnectionInterceptor(credential, logger));
    }
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Authentication - For Veracode Flaw Fix
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.MetadataAddress = builder.Configuration["Annotations:OIDCDiscoveryURL"] ?? string.Empty;
    options.Events = JwtEventHandler.CreateJwtEvents();
    options.Audience = "S9uareRootLLC:JWT";
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ECCOUsersOrDevelopers", policy =>
        policy.RequireRole(
            builder.Configuration["Annotations:ValidUsers"] ?? string.Empty,
            builder.Configuration["Annotations:ValidDevelopers"] ?? string.Empty
        ));
});

// Correspondence Manager portal: local sign-in, RBAC permissions and ABAC policies (see ProcessRequestEndPoint/Auth).
// Adds the "EccoPortal" JWT scheme alongside the OIDC one above; the default scheme is unchanged.
ProcessRequestEndPoint.Auth.PortalAuthServiceCollectionExtensions.AddPortalAuth(builder);

// PDF of each correspondence request's payload, stored in Blob Storage (see ProcessRequestEndPoint/Pdf).
ProcessRequestEndPoint.Pdf.PdfServiceCollectionExtensions.AddCorrespondencePdfs(builder);

// Record details, recycle bin + retention, letter templates, server-side alerts and live updates (ProcessRequestEndPoint/Realtime).
ProcessRequestEndPoint.Realtime.PortalFeatures.AddPortalFeatures(builder);

// Swagger implementation
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ECCO API",
        Version = "v1",
        Description = "An API to start correspondence journey for requested document.",
        Contact = new OpenApiContact
        {
            Name = "CommManager",
            Email = "DS-ECCO@S9uareRootLLC.com",
        },
        License = new OpenApiLicense
        {
            Name = "Swagger Sample License",
        }
    });

    // Set the comments path for the Swagger JSON and UI.
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using Bearer scheme.",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
        {
            new OpenApiSecurityScheme {
                Reference = new OpenApiReference {
                    Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<KeyVaultHealthCheck>("KeyVault")
    .AddCheck<ServiceBusHealthCheck>("AzureServiceBus")
    .AddCheck<AzureSqlHealthCheck>("AzureSqlDatabase",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "database", "sql", "azure" });

// NServiceBus endpoint startup - BEFORE building the app
IEndpointInstance? endpointInstance = null;
builder.Services.AddSingleton<IMessageSession>(sp =>
{
    if (endpointInstance == null)
        throw new ConfigurationException("NServiceBus endpoint not started yet", "NSERVICEBUS_NOT_STARTED");
    return endpointInstance;
});

// Build the app to get the root provider
var app = builder.Build();
var rootProvider = app.Services;

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("=== APPLICATION STARTUP TEST LOG - This should appear in logs ===");
logger.LogError("=== TEST ERROR LOG - This should appear in logs ===");
logger.LogWarning("=== TEST WARNING LOG - This should appear in logs ===");

var configuration = rootProvider.GetRequiredService<IConfiguration>();
var secretsCache = rootProvider.GetRequiredService<ISecretsCacheService>();
var tempLogger = rootProvider.GetRequiredService<ILogger<Program>>();

tempLogger.LogInformation("Starting NServiceBus endpoint configuration");

DiagnoseCacheService(rootProvider, tempLogger);
await DiagnoseAzureIdentityAsync(configuration, builder.Environment, tempLogger);
ValidateConfiguration(configuration, tempLogger);

var endpointConfiguration = new EndpointConfiguration("ProcessRequestEndPoint");
var useAzureBehavior = ShouldUseAzureBehavior(configuration, builder.Environment, tempLogger);

if (!useAzureBehavior)
{
    tempLogger.LogDebug("Using LearningTransport for development environment");
    endpointConfiguration.EnableInstallers();
    endpointConfiguration.UseTransport<LearningTransport>();
}
else
{
    tempLogger.LogInformation("Using AzureServiceBusTransport with AZURE_CLIENT_ID");

    try
    {
        var secretName = configuration["SecretNames:AzureServiceBusFQURL"];
        if (string.IsNullOrWhiteSpace(secretName))
        {
            tempLogger.LogError("Service Bus configuration is missing from appsettings");
            throw new ConfigurationException("Service Bus configuration is missing.", "CONFIG_MISSING", "SecretNames:AzureServiceBusFQURL");
        }

        // Get Service Bus connection string from Key Vault
        string serviceBusConnectionString;
        try
        {
            tempLogger.LogDebug("Retrieving Service Bus connection string from Key Vault");
            serviceBusConnectionString = secretsCache.GetSecretAsync(secretName).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(serviceBusConnectionString))
            {
                tempLogger.LogError("Service Bus connection string is empty or missing from Key Vault");
                throw new SecretNotFoundException(secretName);
            }
            tempLogger.LogInformation("Service Bus connection string retrieved successfully (length: {Length})", serviceBusConnectionString.Length);
        }
        catch (SecretNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDetailedAzureException(ex, "Retrieving Service Bus Connection String", tempLogger);
            throw new SecretNotFoundException(secretName, ex);
        }

        // Use AZURE_CLIENT_ID environment variable directly
        var azureClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(azureClientId))
        {
            tempLogger.LogError("CRITICAL: AZURE_CLIENT_ID environment variable is not set");
            throw new ConfigurationException("AZURE_CLIENT_ID environment variable must be set in Azure App Service configuration.",
                "AZURE_CLIENT_ID_MISSING", "AZURE_CLIENT_ID");
        }

        tempLogger.LogInformation("Using AZURE_CLIENT_ID for Service Bus authentication: {ClientId}", azureClientId);

        var credential = new DefaultAzureCredential();
        try
        {
            var transport = new AzureServiceBusTransport(serviceBusConnectionString, credential);
            transport.MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10);
            transport.TransportTransactionMode = TransportTransactionMode.ReceiveOnly;
            endpointConfiguration.UseTransport(transport);

            // Configure SQL Persistence with Managed Identity
            var dbSecretName = configuration["SecretNames:AzureDbConnectionString"];
            var baseConnectionString = secretsCache.GetSecretAsync(dbSecretName!).GetAwaiter().GetResult();

            var csb = new SqlConnectionStringBuilder(baseConnectionString);
            csb.Remove("Authentication");
            csb.Remove("User ID");
            csb.Remove("Password");

            var persistence = endpointConfiguration.UsePersistence<SqlPersistence>();
            persistence.SqlDialect<SqlDialect.MsSqlServer>();

            persistence.ConnectionBuilder(() =>
            {
                var sqlConnection = new SqlConnection(csb.ConnectionString);
                var token = credential.GetTokenAsync(
                    new Azure.Core.TokenRequestContext(new[] { "https://database.windows.net/.default" })
                ).GetAwaiter().GetResult();

                sqlConnection.AccessToken = token.Token;
                return sqlConnection;
            });

            tempLogger.LogInformation("Azure Service Bus transport configured successfully");
        }
        catch (Exception ex)
        {
            LogDetailedAzureException(ex, "Configuring Azure Service Bus Transport", tempLogger, "Azure Service Bus", serviceBusConnectionString);
            await DiagnoseServiceBusPermissionsAsync(configuration, builder.Environment, serviceBusConnectionString, tempLogger);
            throw new CommunicationException("Failed to configure Azure Service Bus transport", "SERVICEBUS_CONFIG_ERROR", "AzureServiceBus", ex);
        }
    }
    catch (ConfigurationException)
    {
        throw;
    }
    catch (SecretNotFoundException)
    {
        throw;
    }
    catch (CommunicationException)
    {
        throw;
    }
}

endpointConfiguration.AssemblyScanner().ScanAssembliesInNestedDirectories = true;
endpointConfiguration.EnableFeature<NServiceBus.MessageRouting.RoutingSlips.RoutingSlips>();

// Configure default recoverability (retry logic)
try
{
    if (builder.Environment.IsDevelopment())
    {
        // Local development: cap retries at 3 total (immediate only, no delayed retries) so a
        // consistently-failing message (e.g. downstream Venture Fusion call unreachable locally)
        // doesn't spam the console with repeated retry attempts before moving to the error queue.
        endpointConfiguration.Recoverability()
            .Immediate(immediate => immediate.NumberOfRetries(3))
            .Delayed(delayed => delayed.NumberOfRetries(0));

        endpointConfiguration.Recoverability()
            .CustomPolicy(CustomRecoverabilityPolicyConfig.CustomRecoverabilityPolicy);

        tempLogger.LogInformation("NServiceBus recoverability configured (Development override). ImmediateRetries: 3, DelayedRetries: 0");
    }
    else
    {
        var noOfRetries = secretsCache.GetSecretAsync(configuration["SecretNames:NumberRetries"]!).GetAwaiter().GetResult();
        var noOfDelayedRetries = secretsCache.GetSecretAsync(configuration["SecretNames:NumberDelayedRetries"]!).GetAwaiter().GetResult();
        var delayInSeconds = secretsCache.GetSecretAsync(configuration["SecretNames:DelaySeconds"]!).GetAwaiter().GetResult();
        var failedMsgEndpoint = secretsCache.GetSecretAsync(configuration["SecretNames:FailedMsgEndpoint"]!).GetAwaiter().GetResult();

        // Default failed message retry policy
        endpointConfiguration.Recoverability()
            .Immediate(immediate => immediate.NumberOfRetries(Convert.ToInt32(noOfRetries)))
            .Delayed(delayed => delayed.NumberOfRetries(Convert.ToInt32(noOfDelayedRetries)).TimeIncrease(TimeSpan.FromSeconds(Convert.ToDouble(delayInSeconds))));

        // Configure custom recoverability policy
        endpointConfiguration.Recoverability()
            .CustomPolicy(CustomRecoverabilityPolicyConfig.CustomRecoverabilityPolicy);

        tempLogger.LogInformation("NServiceBus recoverability configured. ImmediateRetries: {ImmediateRetries}, DelayedRetries: {DelayedRetries}",
            noOfRetries, noOfDelayedRetries);
    }
}
catch (SecretNotFoundException ex)
{
    tempLogger.LogError(ex, "Failed to retrieve recoverability configuration secrets");
    throw;
}
catch (Exception ex)
{
    tempLogger.LogError(ex, "Failed to configure recoverability settings");
    throw new ConfigurationException("Failed to configure NServiceBus recoverability", "RECOVERABILITY_CONFIG_ERROR", ex);
}

try
{
    if (builder.Environment.IsDevelopment())
    {
 // In Development, use the trial/community license or skip license validation
        tempLogger.LogWarning("Running in Development mode - using NServiceBus trial license");
        // NServiceBus will automatically use the trial license if no license is configured
 // No need to call endpointConfiguration.License() in development
    }
    else
    {
 tempLogger.LogDebug("Retrieving NServiceBus license from Key Vault");
        var licenseProvider = rootProvider.GetRequiredService<NServiceBusLicenseProvider>();
 var licenseKey = licenseProvider.GetLicenseKeyAsync().GetAwaiter().GetResult();
        if (string.IsNullOrEmpty(licenseKey))
        {
     tempLogger.LogError("NServiceBus license key is empty or missing");
     throw new ConfigurationException("NServiceBus license key is missing.", "LICENSE_MISSING");
        }

        endpointConfiguration.License(licenseKey);
        tempLogger.LogInformation("NServiceBus license configured successfully (length: {Length})", licenseKey.Length);
    }

    endpointConfiguration.SendFailedMessagesTo("eccofailedmessagesqueue");
}
catch (ConfigurationException)
{
    throw;
}
catch (SecretNotFoundException ex)
{
    LogDetailedAzureException(ex, "Retrieving NServiceBus License", tempLogger);
    throw;
}
catch (Exception ex)
{
    LogDetailedAzureException(ex, "Retrieving NServiceBus License", tempLogger);
    throw new ConfigurationException("Failed to configure NServiceBus license", "LICENSE_CONFIG_ERROR", ex);
}

endpointConfiguration.UseSerialization<SystemJsonSerializer>();

// Configure NServiceBus container
endpointConfiguration.RegisterComponents(services =>
{
    services.AddSingleton<IMemoryCache, ClearableMemoryCache>();
    services.AddSingleton<HttpClient>();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddSingleton<KeyVaultService>();
    services.AddSingleton<IKeyVaultService, KeyVaultService>();
    services.AddSingleton<ISecretsCacheService, SecretsCacheService>();

    // Add IHostEnvironment registration for NServiceBus container
    services.AddSingleton<IHostEnvironment>(builder.Environment);

    // Configure VentureFusion credentials for NServiceBus
    services.AddOptions<VentureFusionServiceCredentials>().Configure<IServiceProvider>((options, sp) =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var cache = sp.GetRequiredService<ISecretsCacheService>();

        try
        {
            options.TokenEndpoint = cache.GetSecretAsync(config["SecretNames:VFTokenEndpoint"]!).GetAwaiter().GetResult();
            options.ClientId = cache.GetSecretAsync(config["SecretNames:VFClientID"]!).GetAwaiter().GetResult();
            options.ClientSecret = cache.GetSecretAsync(config["SecretNames:VFClientSecret"]!).GetAwaiter().GetResult();
            options.TenantId = cache.GetSecretAsync(config["SecretNames:VFTenantID"]!).GetAwaiter().GetResult();
            options.ApimSubscriptionKey = cache.GetSecretAsync(config["SecretNames:VFSubscriptionKey"]!).GetAwaiter().GetResult();
            options.Scope = cache.GetSecretAsync(config["SecretNames:VFScopes"]!).GetAwaiter().GetResult();
            options.Uri = new Uri(cache.GetSecretAsync(config["SecretNames:VFUri"]!).GetAwaiter().GetResult());
            options.grant_type = "client_credentials";
        }
        catch (Exception ex) when (builder.Environment.IsDevelopment())
        {
            // Local development: real Venture Fusion credentials aren't configured/reachable.
            // Use syntactically-valid placeholder values so VentureFusionService can still be
            // constructed (its constructor validates these are non-empty) — the actual VF HTTP
            // call is separately skipped in Development inside RequestCompositionHandler.
            var logger = sp.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "VentureFusion credentials unavailable — using local placeholder values (Development)");
            options.TokenEndpoint = "https://localhost/vf-placeholder/token";
            options.ClientId = "local-dev-client-id";
            options.ClientSecret = "local-dev-client-secret";
            options.TenantId = "local-dev-tenant-id";
            options.ApimSubscriptionKey = "local-dev-subscription-key";
            options.Scope = "local-dev-scope";
            options.Uri = new Uri("https://localhost/vf-placeholder");
            options.grant_type = "client_credentials";
        }
        catch (SecretNotFoundException ex)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConfigurationException("Failed to configure VentureFusion credentials for NServiceBus", "VF_NSERVICEBUS_CONFIG_ERROR", ex);
        }
    });

    services.AddOptions<Solutions>().Configure<IServiceProvider>((solutionOptions, sp) =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var cache = sp.GetRequiredService<ISecretsCacheService>();

        try
        {
            solutionOptions.customerId = cache.GetSecretAsync(config["SecretNames:VFCustomerID"]!).GetAwaiter().GetResult();

            var usageIdSecret = cache.GetSecretAsync(config["SecretNames:VFUsageID"]!).GetAwaiter().GetResult();
            if (!int.TryParse(usageIdSecret, out var parsedUsageId))
            {
                throw new ValidationException($"VF Usage ID secret value '{usageIdSecret}' is not a valid integer.",
                    new Dictionary<string, List<string>> { ["VFUsageID"] = new List<string> { "Must be a valid integer" } });
            }
            solutionOptions.usageId = parsedUsageId;
        }
        catch (Exception ex) when (builder.Environment.IsDevelopment())
        {
            // Local development: real Venture Fusion Solutions config isn't available — use placeholders.
            var logger = sp.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "VF Solutions config unavailable — using local placeholder values (Development)");
            solutionOptions.customerId = "local-dev-customer-id";
            solutionOptions.usageId = 0;
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (SecretNotFoundException ex)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConfigurationException("Failed to configure Solutions for NServiceBus", "SOLUTIONS_NSERVICEBUS_CONFIG_ERROR", ex);
        }
    });

    services.AddHttpClient<IDocumentIdGeneratorService, DocumentIdGeneratorService>();
    services.AddScoped<IVentureFusionService, VentureFusionService>();
    services.AddScoped<IEncryptionService, EncryptionService>();
    services.AddScoped<IPgpEncryptionService, PgpEncryptionService>();
    services.AddScoped<ICorrespondenceRequestService, CorrespondenceRequestService>();
    services.AddScoped<ICorrespondenceRequestStatusHistoryService, CorrespondenceRequestStatusHistoryService>();
    services.AddScoped<IRoutingConfigurationService, RoutingConfigurationService>();
    services.AddScoped<ICorrespondenceRequestRepository, CorrespondenceRequestRepository>();
    services.AddScoped<ICorrespondenceRequestStatusHistoryRepository, CorrespondenceRequestStatusHistoryRepository>();
    services.AddScoped<IRoutingConfigurationRepository, RoutingConfigurationRepository>();
    services.AddScoped<IDocumentIdsService, DocumentIdsService>();
    services.AddScoped<IDocumentIdsRepository, DocumentIdsRepository>();

    // Resolve connection string ONCE — not on every scope creation.
    // Local development: prefer the plain AZURE_SQL_CONNECTION (e.g. localdb) over Key Vault +
    // Managed Identity, same override as the ASP.NET Core CorrespondenceDBContext registration above —
    // NServiceBus handlers resolve from this separate container, so this must be mirrored here too.
    var nsbLocalConnectionString = builder.Environment.IsDevelopment()
        ? (configuration.GetConnectionString("AZURE_SQL_CONNECTION") ?? configuration["AZURE_SQL_CONNECTION"])
        : null;

    string nsbFinalConnectionString;
    bool nsbUsingLocalDbFallback;
    DefaultAzureCredential? nsbCredential = null;

    if (!string.IsNullOrWhiteSpace(nsbLocalConnectionString))
    {
        nsbFinalConnectionString = nsbLocalConnectionString;
        nsbUsingLocalDbFallback = true;
    }
    else
    {
        var nsbDbSecretName = configuration["SecretNames:AzureDbConnectionString"];
        if (string.IsNullOrWhiteSpace(nsbDbSecretName))
        {
            throw new InvalidOperationException("Database connection string configuration is missing.");
        }

        var nsbBaseConnectionString = secretsCache.GetSecretAsync(nsbDbSecretName).GetAwaiter().GetResult();
        var nsbConnectionStringBuilder = new SqlConnectionStringBuilder(nsbBaseConnectionString);
        nsbConnectionStringBuilder.Remove("Authentication");
        nsbConnectionStringBuilder.Remove("User ID");
        nsbConnectionStringBuilder.Remove("Password");
        nsbFinalConnectionString = nsbConnectionStringBuilder.ConnectionString;

        var nsbAzureClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(nsbAzureClientId))
        {
            throw new InvalidOperationException("AZURE_CLIENT_ID environment variable must be set.");
        }

        nsbCredential = new DefaultAzureCredential();
        nsbUsingLocalDbFallback = false;
    }

    services.AddDbContext<CorrespondenceDBContext>((sp, options) =>
    {
        var dbLogger = sp.GetRequiredService<ILogger<Program>>();
        options.UseSqlServer(nsbFinalConnectionString);
        if (!nsbUsingLocalDbFallback)
        {
            options.AddInterceptors(new ManagedIdentityDbConnectionInterceptor(nsbCredential!, dbLogger));
        }
    });
    services.AddScoped<NonRecoverableLoggingBehavior>();
});

// Register the pipeline behavior 
endpointConfiguration.Pipeline.Register(
    behavior: typeof(NonRecoverableLoggingBehavior),
    description: "Logs non-recoverable errors to Corr DB");

try
{
    tempLogger.LogDebug("Validating dependencies before starting NServiceBus endpoint");

    if (!builder.Environment.IsDevelopment())
    {
        // Validate dependencies before starting (skip in Development)
 var testSecret = secretsCache.GetSecretAsync(configuration["SecretNames:AzureServiceBusFQURL"]!).GetAwaiter().GetResult();
 tempLogger.LogDebug("Key Vault access verified - Service Bus connection string length: {Length}", testSecret?.Length ?? 0);

        if (useAzureBehavior)
 {
     var serviceBusConnectionStringValidation = secretsCache.GetSecretAsync(configuration["SecretNames:AzureServiceBusFQURL"]!).GetAwaiter().GetResult();
     if (!serviceBusConnectionStringValidation.Contains(".servicebus.windows.net"))
     {
         tempLogger.LogError("Invalid Service Bus connection string format - does not contain '.servicebus.windows.net'");
         throw new InvalidOperationException("Invalid Service Bus connection string format.");
     }
     tempLogger.LogDebug("Service Bus connection string format validated");
 }

 var licenseProviderValidation = rootProvider.GetRequiredService<NServiceBusLicenseProvider>();
        var licenseKeyValidation = licenseProviderValidation.GetLicenseKeyAsync().GetAwaiter().GetResult();
        if (string.IsNullOrEmpty(licenseKeyValidation))
        {
     tempLogger.LogError("NServiceBus license key validation failed - key is empty");
     throw new ConfigurationException("NServiceBus license key is missing.", "LICENSE_MISSING");
        }
    }
    else
    {
        tempLogger.LogDebug("Skipping Azure Key Vault validation in Development mode");
    }

    endpointInstance = await Endpoint.Start(endpointConfiguration);
}
catch (ValidationException ex)
{
    tempLogger.LogError(ex, "Validation failed during NServiceBus endpoint startup");
    throw;
}
catch (ConfigurationException ex)
{
    tempLogger.LogError(ex, "Configuration error during NServiceBus endpoint startup");
    throw;
}
catch (SecretNotFoundException ex)
{
    tempLogger.LogError(ex, "Secret not found during NServiceBus endpoint startup");
    throw;
}
catch (Exception ex)
{
    tempLogger.LogError(ex, "Failed to start NServiceBus endpoint: {ErrorMessage}", ex.Message);
    LogDetailedAzureException(ex, "NServiceBus Endpoint Startup", tempLogger);
    throw new ConfigurationException("Failed to start NServiceBus endpoint", "NSERVICEBUS_STARTUP_ERROR", ex);
}

if (!string.IsNullOrEmpty(appInsightsStatus))
{
    if (appInsightsConfigured)
    {
        logger.LogInformation(appInsightsStatus);
    }
    else
    {
        logger.LogWarning(appInsightsStatus);
    }
}

// Helper methods with logger
static void DiagnoseCacheService(IServiceProvider serviceProvider, ILogger logger)
{
    try
    {
        logger.LogInformation("Starting cache diagnostics");
        var cache = serviceProvider.GetRequiredService<IMemoryCache>();
        var secretsCache = serviceProvider.GetRequiredService<ISecretsCacheService>();
        var config = serviceProvider.GetRequiredService<IConfiguration>();

        // Basic cache test
        var testKey = "test-key";
        var testValue = "test-value";
        cache.Set(testKey, testValue, TimeSpan.FromMinutes(1));
        var retrievedValue = cache.Get<string>(testKey);

        if (retrievedValue != testValue)
        {
            logger.LogError("Basic cache test failed. Expected: {Expected}, Got: {Actual}", testValue, retrievedValue);
        }

        // Test secrets cache
        var testSecretName = config["SecretNames:VFUsageID"];
        if (!string.IsNullOrEmpty(testSecretName))
        {
            var secret1 = secretsCache.GetSecretAsync(testSecretName).GetAwaiter().GetResult();
            var secret2 = secretsCache.GetSecretAsync(testSecretName).GetAwaiter().GetResult();

            if (secret1 != secret2)
            {
                logger.LogError("Secret cache inconsistency detected for secret: {SecretName}", testSecretName);
            }
        }

        logger.LogInformation("Cache diagnostics completed successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Cache diagnostics failed");
    }
}

static async Task WarmupCacheAsync(ISecretsCacheService secretsCache, IConfiguration configuration, ILogger logger)
{
    var secretsToWarmup = new[]
    {
        "AzureServiceBusFQURL", "AzureDbConnectionString",
        "VFClientID", "VFClientSecret", "VFSubscriptionKey", "VFTenantID",
        "VFUri", "VFScopes", "VFTokenEndpoint", "VFCustomerID",
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
            }
            else
            {
                logger.LogWarning("Configuration key not found for secret: {SecretName}", secretName);
            }
        }
        catch (SecretNotFoundException ex)
        {
            failureCount++;
            logger.LogWarning(ex, "Secret not found during warmup: {SecretName}", secretName);
        }
        catch (Exception ex)
        {
            failureCount++;
            logger.LogDebug(ex, "Failed to cache secret: {SecretName}", secretName);
        }
    }

    logger.LogInformation("Cache warmup completed. Success: {SuccessCount}, Failures: {FailureCount}", successCount, failureCount);
}

static void ValidateConfiguration(IConfiguration configuration, ILogger logger)
{
    logger.LogInformation("Starting configuration validation");

    var requiredSecrets = new[]
    {
        "SecretNames:AzureServiceBusFQURL",
        "SecretNames:AzureDbConnectionString",
        "SecretNames:VFClientID",
        "SecretNames:VFClientSecret",
        "SecretNames:VFSubscriptionKey",
        "SecretNames:VFTenantID",
        "SecretNames:VFUri",
        "SecretNames:VFScopes",
        "SecretNames:VFTokenEndpoint",
        "SecretNames:VFCustomerID",
        "SecretNames:VFUsageID",
    };

    var missingSecrets = requiredSecrets.Where(secretName =>
        string.IsNullOrWhiteSpace(configuration[secretName])).ToList();

    if (missingSecrets.Count > 0)
    {
        logger.LogError("Missing required configuration values: {MissingSecrets}", string.Join(", ", missingSecrets));
        throw new ConfigurationException($"Missing required configuration values: {string.Join(", ", missingSecrets)}", "CONFIG_VALIDATION_FAILED");
    }

    // Validate AZURE_CLIENT_ID environment variable
    var azureClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
    if (string.IsNullOrWhiteSpace(azureClientId))
    {
        logger.LogError("AZURE_CLIENT_ID environment variable is not set");
        throw new ConfigurationException("AZURE_CLIENT_ID environment variable must be configured in Azure App Service.", 
            "AZURE_CLIENT_ID_MISSING", "AZURE_CLIENT_ID");
    }
    logger.LogDebug("Configuration validation passed");
}

static bool IsRunningInAzure()
{
    return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")) ||
           !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"));
}

static bool ShouldUseAzureBehavior(IConfiguration configuration, IWebHostEnvironment environment, ILogger logger)
{
    var forceAzureMode = configuration.GetValue<bool>("ForceAzureMode", false);
    // In development prefer LearningTransport unless ForceAzureMode is explicitly set.
    if (environment.IsDevelopment())
    {
        var resultDev = forceAzureMode;
        logger.LogDebug("Development environment detected. ForceAzureMode={ForceAzureMode}. Azure behavior: {Result}", forceAzureMode, resultDev);
        return resultDev;
    }

    var useAzureBehavior = configuration.GetValue<bool>("UseAzureBehavior", false);
    var inAzure = IsRunningInAzure();
    var notDevelopment = !environment.IsDevelopment();

    var result = forceAzureMode || useAzureBehavior || inAzure || notDevelopment;
    logger.LogDebug("Azure behavior: {Result}", result);

    return result;
}

static string ExtractServiceBusNamespace(string connectionString, ILogger logger)
{
    try
    {
        var parts = connectionString.Split(';');
        var endpointPart = parts.FirstOrDefault(p => p.StartsWith("Endpoint="));
        if (endpointPart != null)
        {
            var endpoint = endpointPart.Substring("Endpoint=".Length);
            var uri = new Uri(endpoint);
            var namespaceName = uri.Host.Split('.')[0];
            logger.LogDebug("Extracted Service Bus namespace: {Namespace}", namespaceName);
            return namespaceName;
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not extract namespace from connection string");
    }
    return "unknown-namespace";
}

static async Task AnalyzeCurrentIdentityAsync(Azure.Core.TokenCredential credential, ILogger logger)
{
    try
    {
        logger.LogDebug("Starting current identity analysis");
        var token = await credential.GetTokenAsync(
            new Azure.Core.TokenRequestContext(new[] { "https://management.azure.net/.default" }),
            CancellationToken.None);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);
        var oid = jwt.Claims.FirstOrDefault(x => x.Type == "oid")?.Value ?? "Not found";

        if (oid != "Not found")
        {
            logger.LogInformation("Identity Object ID for role assignments: {ObjectId}", oid);
        }
        else
        {
            logger.LogWarning("Could not determine Object ID from token");
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not analyze current identity");
    }
}

static async Task DiagnoseServiceBusPermissionsAsync(IConfiguration configuration, IWebHostEnvironment environment, string serviceBusConnectionString, ILogger logger)
{
    try
    {
        logger.LogInformation("Starting Service Bus permissions diagnostics");
        var ns = ExtractServiceBusNamespace(serviceBusConnectionString, logger);
        var credential = new DefaultAzureCredential();
        await AnalyzeCurrentIdentityAsync(credential, logger);

        logger.LogError("Service Bus namespace: {Namespace}.servicebus.windows.net - Missing permissions: Manage + EntityWrite. Grant 'Azure Service Bus Data Owner' role", ns);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Service Bus diagnostics failed");
    }
}

static async Task DiagnoseAzureIdentityAsync(IConfiguration configuration, IWebHostEnvironment environment, ILogger logger)
{
    try
    {
        logger.LogInformation("Starting Azure identity diagnostics");
        var credential = new DefaultAzureCredential();
        await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(new[] { "https://vault.azure.net/.default" }), CancellationToken.None);
        await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(new[] { "https://servicebus.azure.net/.default" }), CancellationToken.None);
        logger.LogInformation("Azure identity diagnostics completed successfully");
    }
    catch (AuthenticationFailedException ex)
    {
        logger.LogError(ex, "Authentication failed during identity diagnostics");
        await TryCredentialsAsync(configuration, environment, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Identity diagnostics failed");
    }
}

static async Task TryCredentialsAsync(IConfiguration configuration, IWebHostEnvironment environment, ILogger logger)
{
    logger.LogInformation("Testing various credential types");
    var useAzureBehavior = ShouldUseAzureBehavior(configuration, environment, logger);

    Dictionary<string, Func<Azure.Core.TokenCredential>> credentialTypes = useAzureBehavior
        ? new Dictionary<string, Func<Azure.Core.TokenCredential>>
        {
            ["ManagedIdentity"] = () => new ManagedIdentityCredential(),
            ["Environment"] = () => new EnvironmentCredential()
        }
        : new Dictionary<string, Func<Azure.Core.TokenCredential>>
        {
            ["ManagedIdentity"] = () => new ManagedIdentityCredential(),
            ["VisualStudio"] = () => new VisualStudioCredential(),
            ["AzureCLI"] = () => new AzureCliCredential(),
            ["VisualStudioCode"] = () => new VisualStudioCodeCredential(),
            ["Environment"] = () => new EnvironmentCredential()
        };

    foreach (var kvp in credentialTypes)
    {
        try
        {
            var credential = kvp.Value();
            await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(new[] { "https://vault.azure.net/.default" }), CancellationToken.None);
            logger.LogInformation("Credential type {CredentialType} works successfully", kvp.Key);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Credential type {CredentialType} failed", kvp.Key);
        }
    }
}

static void LogDetailedAzureException(Exception ex, string operation, ILogger logger, string resourceType = "", string serviceBusConnectionString = "")
{
    logger.LogError(ex, "Azure operation failed: {Operation} - {ExceptionType}", operation, ex.GetType().Name);

    if (ex.Message.Contains("Authorization failed") && ex.Message.Contains("Manage,EntityWrite"))
    {
        var namespaceName = ExtractServiceBusNamespace(serviceBusConnectionString, logger);
        logger.LogError("SERVICE BUS AUTHORIZATION ERROR: {Namespace}.servicebus.windows.net - Missing: Manage + EntityWrite. Grant 'Azure Service Bus Data Owner' role", namespaceName);
        return;
    }

    if (ex is RequestFailedException azEx)
    {
        logger.LogError("Azure Error Code: {ErrorCode}, HTTP Status: {Status}", azEx.ErrorCode, azEx.Status);
    }

    // Log ECCO exceptions with error codes
    if (ex is CorrespondenceException corrEx)
    {
        logger.LogError("ECCO Error Code: {ErrorCode}", corrEx.ErrorCode);
    }

    // Log inner exceptions
    var innerEx = ex.InnerException;
    var depth = 1;
    while (innerEx != null && depth <= 3)
    {
        logger.LogError("Inner Exception {Depth}: {ExceptionType} - {Message}", depth, innerEx.GetType().Name, innerEx.Message);
        innerEx = innerEx.InnerException;
        depth++;
    }
}

// Cache warmup
try
{
    using var scope = app.Services.CreateScope();
    var mainSecretsCache = scope.ServiceProvider.GetRequiredService<ISecretsCacheService>();
    var mainConfiguration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var mainLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await WarmupCacheAsync(mainSecretsCache, mainConfiguration, mainLogger);
}
catch (SecretNotFoundException ex)
{
    logger.LogError(ex, "Secret not found during cache warmup");
}
catch (Exception ex)
{
    logger.LogError(ex, "Cache warmup failed during application startup");
}

var logging = LogManager.Use<DefaultFactory>();
logging.Level(NServiceBus.Logging.LogLevel.Info);

if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var vfService = scope.ServiceProvider.GetRequiredService<IVentureFusionService>();
        logger.LogInformation("VentureFusion service created successfully in development environment");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "VentureFusion service creation failed in development environment - this may be expected");
    }

    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    logger.LogInformation("Application starting in production mode - Swagger UI disabled");
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Graceful shutdown
if (endpointInstance != null)
{
    var lifetime = app.Lifetime;
    lifetime.ApplicationStopping.Register(() =>
    {
        try
        {
            logger.LogInformation("Application shutdown requested - stopping NServiceBus endpoint");
            endpointInstance.Stop().GetAwaiter().GetResult();
            logger.LogInformation("NServiceBus endpoint stopped successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to stop NServiceBus endpoint gracefully");
        }
    });
}

// Health checks endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            results = report.Entries.Select(e => new
            {
                key = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds,
                error = e.Value.Exception?.Message,
                errorCode = (e.Value.Exception as CorrespondenceException)?.ErrorCode,
                data = e.Value.Data.Count > 0 ? e.Value.Data : null,
                tags = e.Value.Tags?.ToArray()
            }).ToArray(),
            environment = new
            {
                machineName = Environment.MachineName,
                osVersion = Environment.OSVersion.ToString(),
                dotNetVersion = Environment.Version.ToString(),
                processorCount = Environment.ProcessorCount,
                workingSet = Environment.WorkingSet,
                forceAzureMode = context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("ForceAzureMode"),
                useAzureBehavior = context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("UseAzureBehavior")
            }
        };

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        await context.Response.WriteAsync(json);
    }
});

app.MapHealthChecks("/health/detailed", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var detailedResult = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checkedAt = DateTime.UtcNow,
            results = report.Entries.Select(e => new
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
                environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName,
                forceAzureMode = context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("ForceAzureMode"),
                useAzureBehavior = context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("UseAzureBehavior"),
                keyVaultUrl = context.RequestServices.GetRequiredService<IConfiguration>()["KeyVault:Url"]
            }
        };

        var json = JsonSerializer.Serialize(detailedResult, new JsonSerializerOptions { WriteIndented = true });
        await context.Response.WriteAsync(json);
    }
});

// Debug endpoints
app.MapGet("/debug/cache", (IMemoryCache cache, ISecretsCacheService secretsCache) =>
{
    var cacheInfo = new
    {
        timestamp = DateTime.UtcNow,
        cacheType = cache.GetType().Name,
        secretsCacheType = secretsCache.GetType().Name,
        testResult = TestCacheDirectly(cache),
        cacheContents = GetCacheContents(cache)
    };
    return Results.Json(cacheInfo, new JsonSerializerOptions { WriteIndented = true });
});

app.MapGet("/debug/cache/secrets-by-config", (ISecretsCacheService secretsCache, IConfiguration configuration) =>
{
    var secretMappings = new Dictionary<string, object>();
    var secretNames = new[]
    {
        "AzureServiceBusFQURL", "AzureDbConnectionString",
        "VFClientID", "VFClientSecret", "VFSubscriptionKey", "VFTenantID",
        "VFUri", "VFScopes", "VFTokenEndpoint", "VFUsageID", "VFCustomerID"
    };

    foreach (var secretName in secretNames)
    {
        var configKey = $"SecretNames:{secretName}";
        var secretKeyName = configuration[configKey];
        secretMappings[secretName] = new
        {
            configKey = configKey,
            secretKeyName = secretKeyName,
            cacheKey = $"secret:{secretKeyName}",
            isInCache = secretsCache.IsSecretCached(secretKeyName ?? ""),
            configExists = !string.IsNullOrEmpty(secretKeyName)
        };
    }

    return Results.Json(new { timestamp = DateTime.UtcNow, secretMappings }, new JsonSerializerOptions { WriteIndented = true });
});

app.MapPost("/debug/cache/warmup", async (ISecretsCacheService secretsCache, IConfiguration configuration, ILogger<Program> debugLogger) =>
{
    try
    {
        await WarmupCacheAsync(secretsCache, configuration, debugLogger);
        return Results.Json(new { success = true, message = "Cache warmup completed", timestamp = DateTime.UtcNow });
    }
    catch (SecretNotFoundException ex)
    {
        debugLogger.LogError(ex, "Secret not found during cache warmup");
        return Results.Json(new ErrorResponse
        {
            Message = "Secret not found during cache warmup",
            ErrorCode = ex.ErrorCode ?? "SECRET_NOT_FOUND",
            Details = ex.SecretName,
            Timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        debugLogger.LogError(ex, "Debug cache warmup failed");
        return Results.Json(new ErrorResponse
        {
            Message = ex.Message,
            ErrorCode = "CACHE_WARMUP_ERROR",
            Timestamp = DateTime.UtcNow
        });
    }
});

app.MapPost("/debug/cache/clear", (IMemoryCache cache, ISecretsCacheService secretsCache, ILogger<Program> debugLogger) =>
{
    try
    {
        if (cache is ClearableMemoryCache clearableCache)
        {
            var keysBefore = clearableCache.GetKeys().ToList();
            clearableCache.Clear();
            debugLogger.LogInformation("Cache cleared successfully. Removed {Count} entries", keysBefore.Count);
            return Results.Json(new
            {
                success = true,
                message = "Cache cleared successfully",
                entriesBeforeClear = keysBefore.Count,
                clearedKeys = keysBefore
            });
        }
        else
        {
            secretsCache.ClearCache();
            debugLogger.LogInformation("Cache cleared using SecretsCacheService.ClearCache()");
            return Results.Json(new { success = true, message = "Cache cleared using SecretsCacheService.ClearCache()" });
        }
    }
    catch (Exception ex)
    {
        debugLogger.LogError(ex, "Failed to clear cache");
        return Results.Json(new ErrorResponse
        {
            Message = ex.Message,
            ErrorCode = "CACHE_CLEAR_ERROR",
            Timestamp = DateTime.UtcNow
        });
    }
});

app.MapDelete("/debug/cache/{secretName}", (string secretName, ISecretsCacheService secretsCache, ILogger<Program> debugLogger) =>
{
    try
    {
        secretsCache.RemoveSecret(secretName);
        debugLogger.LogInformation("Secret {SecretName} removed from cache", secretName);
        return Results.Json(new { success = true, message = $"Secret '{secretName}' removed from cache" });
    }
    catch (Exception ex)
    {
        debugLogger.LogError(ex, "Failed to remove secret {SecretName} from cache", secretName);
        return Results.Json(new ErrorResponse
        {
            Message = $"Failed to remove secret '{secretName}' from cache",
            ErrorCode = "CACHE_REMOVE_ERROR",
            Details = ex.Message,
            Timestamp = DateTime.UtcNow
        });
    }
});

// Helper methods
static object GetCacheContents(IMemoryCache cache)
{
    try
    {
        if (cache is ClearableMemoryCache clearableCache)
        {
            var keys = clearableCache.GetKeys().ToList();
            return new
            {
                totalEntries = keys.Count,
                keys = keys.Take(10).ToList(),
                hasMore = keys.Count > 10
            };
        }
        return new { message = "Cache inspection not supported with current IMemoryCache implementation" };
    }
    catch (Exception ex)
    {
        return new { error = ex.Message, type = ex.GetType().Name };
    }
}

static object TestCacheDirectly(IMemoryCache cache)
{
    try
    {
        var testKey = "debug-test";
        var testValue = $"test-{DateTime.UtcNow:HHmmss}";
        cache.Set(testKey, testValue, TimeSpan.FromMinutes(1));
        var retrievedValue = cache.Get<string>(testKey);

        return new
        {
            success = retrievedValue == testValue,
            original = testValue,
            retrieved = retrievedValue,
            match = retrievedValue == testValue,
            cacheType = cache.GetType().Name
        };
    }
    catch (Exception ex)
    {
        return new { success = false, error = ex.Message, type = ex.GetType().Name };
    }
}

logger.LogInformation("Application configured and starting. Environment: {Environment}, UseAzureBehavior: {UseAzureBehavior}",
    app.Environment.EnvironmentName,
    app.Configuration.GetValue<bool>("UseAzureBehavior", false));

// later, after app is built and before MapControllers / app.Run():
app.UseCors("AllowLocalReact");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

ProcessRequestEndPoint.Realtime.PortalFeatures.MapPortalFeatures(app); // portal live updates hub

app.Run();
