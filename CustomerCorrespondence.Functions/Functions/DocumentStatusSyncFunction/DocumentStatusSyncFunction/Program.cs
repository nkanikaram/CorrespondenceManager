using DocumentStatusSyncFunction.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ECMServicesDemo;
using Azure.Messaging.ServiceBus;
using CustomerCorrespondence.Infrastructure.Persistence;
using CustomerCorrespondence.Infrastructure.Services;
using CustomerCorrespondence.Domain.Interfaces;     
using Azure.Identity;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IKeyVaultService, KeyVaultService>();
builder.Services.AddTransient<ECMServiceClient>(sp =>
    new ECMServiceClient(ECMServiceClient.EndpointConfiguration.ECMServicePort));

//var configuration = builder.Configuration;
var keyVaultUrl = Environment.GetEnvironmentVariable("KeyVaultUrl");
Console.WriteLine($"KeyVaultUrl: {keyVaultUrl}");
builder.Services.AddScoped<IECMServiceClient, ECMServiceClientWrapper>();

builder.Services.AddDbContext<CorrespondenceDBContext>((serviceProvider, options) =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<ManagedIdentityDbConnectionInterceptor>>();
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var secretsCache = serviceProvider.GetRequiredService<IKeyVaultService>();

    logger.LogInformation("Configuring database context with AZURE_CLIENT_ID managed identity");

    // Get base connection string from Key Vault
    var dbSecretName = Environment.GetEnvironmentVariable("SecretNames__AzureDbConnectionString");
    if (string.IsNullOrWhiteSpace(dbSecretName))
    {
        logger.LogError("Database connection string configuration is missing");
        throw new InvalidOperationException("Database connection string configuration is missing.");
    }

    var baseConnectionString = secretsCache.GetSecretAsync(dbSecretName).GetAwaiter().GetResult();
    if (string.IsNullOrWhiteSpace(baseConnectionString))
    {
        logger.LogError("Database connection string secret is missing from Key Vault");
        throw new InvalidOperationException("Database connection string secret is missing from Key Vault.");
    }

    // Clean connection string for managed identity
    var connectionStringBuilder = new SqlConnectionStringBuilder(baseConnectionString);
    connectionStringBuilder.Remove("Authentication");
    connectionStringBuilder.Remove("User ID");
    connectionStringBuilder.Remove("Password");

    // Use AZURE_CLIENT_ID environment variable directly
    var azureClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
    if (string.IsNullOrWhiteSpace(azureClientId))
    {
        logger.LogError("CRITICAL: AZURE_CLIENT_ID environment variable is not set");
        throw new InvalidOperationException("AZURE_CLIENT_ID environment variable must be set in Azure App Service configuration.");
    }

    logger.LogInformation("Using AZURE_CLIENT_ID for managed identity: {ClientId}", azureClientId);

    var credential = new DefaultAzureCredential();
    // Configure Entity Framework
    options.UseSqlServer(connectionStringBuilder.ConnectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
        sqlOptions.CommandTimeout(120);
    });

    options.AddInterceptors(new ManagedIdentityDbConnectionInterceptor(credential, logger));
    logger.LogInformation("Database context configured successfully with AZURE_CLIENT_ID managed identity");
});

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Register Service Bus Client using Managed Identity
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("ServiceBusRegistration");
    var serviceBusNamespace = Environment.GetEnvironmentVariable("ServiceBusNamespace");
    
    if (string.IsNullOrWhiteSpace(serviceBusNamespace))
    {
        logger.LogWarning("ServiceBusNamespace is not configured. Service Bus features will be disabled.");
        return null as ServiceBusClient;
    }
    
    try
    {
        logger.LogInformation("Initializing Service Bus client with Managed Identity for namespace: {Namespace}", serviceBusNamespace);
        var credential = new DefaultAzureCredential();
        return new ServiceBusClient(serviceBusNamespace, credential);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to create Service Bus client with Managed Identity. Service Bus features will be disabled.");
        return null as ServiceBusClient;
    }
});

// Register Service Bus Service
builder.Services.AddSingleton<IServicebusService, ServicebusService>();

builder.Build().Run();
