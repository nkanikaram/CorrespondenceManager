# ECCO.CommunicationManager.Infrastructure

> Infrastructure layer for ECCO Communication Manager - containing data access, external service integrations, messaging, health checks, and infrastructure implementations.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Entity Framework Core](https://img.shields.io/badge/EF%20Core-9.0.11-blue.svg)](https://docs.microsoft.com/ef/core/)
[![Azure](https://img.shields.io/badge/Azure-Services-0078D4.svg)](https://azure.microsoft.com/)

---

## 📦 Package Information

**Package Name:** `ECCO.CommunicationManager.Infrastructure`  
**Version:** 1.0.0  
**Target Framework:** .NET 8.0

---

## 🎯 What's This For?

This package contains the **infrastructure layer** for the ECCO Correspondence system:

- ✅ **Data Access** - EF Core repositories and database context
- ✅ **External Services** - VentureFusion API, Azure Key Vault, Service Bus
- ✅ **Messaging** - NServiceBus routing and message forwarding
- ✅ **Security** - PGP encryption, secrets management, caching
- ✅ **Health Checks** - Azure SQL, Key Vault, Service Bus monitoring
- ✅ **Infrastructure Utilities** - Document ID generation, filtering services

---

**Key Principle**: Infrastructure layer **implements** all interfaces defined by Application and Domain layers.

---

## 📚 What's Included?

### 🗄️ Data Access

#### **CorrespondenceDBContext**
Entity Framework Core database context managing all correspondence data.

```csharp
public class CorrespondenceDBContext : DbContext 
{ 
    public DbSet<CorrespondenceRequest> CorrespondenceRequests { get; set; } 
    public DbSet<CorrespondenceRequestStatusHistory> CorrespondenceRequestStatusHistories { get; set; } 
    public DbSet<CorrespondenceStatusCode> CorrespondenceStatusCodes { get; set; } 
    public DbSet<RoutingConfiguration> RoutingConfigurations { get; set; } 
    public DbSet<NonRecoverableErrorLog> NonRecoverableErrorLogs { get; set; } 
    public DbSet<CorrespondenceDeliveryMethod> CorrespondenceDeliveryMethods { get; set; } 
    public DbSet<DocumentIdEntity> DocumentIds { get; set; } 
}
```

**Features:**
- EF Core 9.0.11 with SQL Server
- Fluent API configuration for all entities
- Cascade delete for status histories
- Optimized indexes for performance
- Encrypted binary data storage (PolicyNumber, RequestPayload)
- Managed Identity authentication

**Entity Configurations:**
- `CorrespondenceRequest` - Primary correspondence entity with encrypted fields
- `CorrespondenceRequestStatusHistory` - Composite key with cascade delete
- `RoutingConfiguration` - Simplified route storage with computed property
- `DocumentIdEntity` - Document ID pool management
- `NonRecoverableErrorLog` - Error tracking for failed messages

---

### 📦 Repositories

All repositories implement Domain interfaces with EF Core.

#### **CorrespondenceRequestRepository**

```csharp
public class CorrespondenceRequestRepository : ICorrespondenceRequestRepository 
{ 
    Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByCorrIdAsync(Guid correspondenceId); 
    Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByDocIdAsync(string docId); 
    Task AddCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest); 
    Task UpdateCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest); 
    Task DeleteCorrespondenceRequestForCorrIdAsync(Guid correspondenceId); 
    Task DeleteCorrespondenceRequestForDocIdAsync(string docId); 
    Task<Guid> GetCorrespondenceIdByDocIdAsync(string docId); 
    Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByPolicyNumberHashAsync(byte[] hash); 
    Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByClaimNumberAsync(string claimNumber); 
}
```
**Features:**
- Async operations with EF Core
- Log injection prevention (sanitized error logging)
- Encrypted policy number search via hash
- Include navigation properties (StatusHistories)

#### **CorrespondenceRequestStatusHistoryRepository**

```csharp
public class CorrespondenceRequestStatusHistoryRepository : ICorrespondenceRequestStatusHistoryRepository 
{ 
    Task<IEnumerable<CorrespondenceRequestStatusHistory>> GetAllByCorrespondenceIdAsync(Guid id); 
    Task<CorrespondenceRequestStatusHistory?> GetStatusHistoryByCorrIdSeqNumAsync(Guid id, int seq); 
    Task<int> GetMaxSequenceNumber(Guid correspondenceId); Task<string> GetStatusCodeByStatus(string statusName); 
    Task AddCorrespondenceRequestStatusHistoryAsync(CorrespondenceRequestStatusHistory history); 
    Task UpdateCorrespondenceRequesStatusHistorytAsync(CorrespondenceRequestStatusHistory history); 
    Task DeleteCorrespondenceRequestStatusHistoryAsync(Guid correspondenceId); 
    Task<string?> GetStatusDescriptionByStatusCode(string statusCode); 
}
```

#### **RoutingConfigurationRepository**

```csharp
public class RoutingConfigurationRepository : IRoutingConfigurationRepository 
{ 
    Task<RoutingConfiguration?> GetRoutingConfigurationByIdAsync(int id); 
    Task<RoutingConfiguration?> GetRoutingConfigurationByRouteNameAsync(string routeName); 
    Task<RoutingConfiguration?> GetRoutingConfigurationByCustomerNameAsync(string customerName); 
    Task AddRoutingConfigurationAsync(RoutingConfiguration routingConfiguration); 
    Task UpdateRoutingConfigurationAsync(RoutingConfiguration routingConfiguration); 
}
```

**Features:**
- Simplified routing with semicolon-separated route data
- CustomerName index for fast lookups
- Route deserialization via computed property

#### **DocumentIdsRepository**

```csharp
public class DocumentIdsRepository : IDocumentIdsRepository 
{ 
    Task AddDocumentIdAsync(string documentId); 
    Task<string?> GetAndUpdateDocumentIdAsync(); 
    Task<int> GetNewDocumentIdCountAsync(); 
    Task RevertDocumentIdAsync(string documentId); 
}
```

**Features:**
- Atomic document ID assignment
- Status tracking (New → Used)
- Rollback support for failed operations
- Concurrent access handling

---

### 🔐 Security Services

#### **PgpEncryptionService**
BouncyCastle-based PGP encryption for sensitive data.

```csharp
public class PgpEncryptionService 
{ 
    Task<byte[]> EncryptAsync(string plainText); 
    Task<string> DecryptAsync(byte[] encryptedData); 
}
```

**Features:**
- PGP public key encryption
- BouncyCastle.Cryptography 2.6.1
- Secure key management via Key Vault
- Base64 encoding for encrypted output

#### **EncryptionService**
Implements `IEncryptionService` with both PGP and hashing capabilities.

```csharp
public class EncryptionService : IEncryptionService 
{ 
    Task<string> EncryptAsync(string plainText); 
    Task<string> DecryptAsync(string encryptedText); 
    byte[] GenerateSearchHash(string plainText); 
}
```

**Features:**
- Wraps PgpEncryptionService
- SHA-256 hash generation for searches
- Deterministic hashing for database lookups

#### **KeyVaultService**
Azure Key Vault integration with Managed Identity.
Note: Currently doesn't implement `IKeyVaultService` interface from Common package.
Should be refactored to implement `CustomerCorrespondence.Common.Interfaces.IKeyVaultService`.

```csharp
public class KeyVaultService 
{ 
    Task<string?> GetSecretAsync(string secretName); 
}
```

**Features:**
- Azure.Security.KeyVault.Secrets 4.8.0
- DefaultAzureCredential with Managed Identity support
- Configurable via `KeyVault:Url` and `KeyVault:ManagedIdentityClientId`
- Graceful handling of missing secrets

#### **SecretsCacheService**
In-memory caching for Key Vault secrets with TTL.
Note: Implements `ISecretsCacheService` from both Domain and Common packages 
(interface duplication issue to be resolved).

```csharp
public class SecretsCacheService : ISecretsCacheService 
{ 
    Task<string> GetSecretAsync(string secretName); 
    bool IsSecretCached(string secretName); 
    string? GetCachedSecret(string secretName); 
    void ClearCache(); 
    void RemoveSecret(string secretName); 
    IEnumerable<string> GetCachedSecretNames(); 
    void SetSecret(string secretName, string value, TimeSpan duration); 
}
```

**Features:**
- Thread-safe concurrent dictionary
- Automatic expiration (default 1 hour)
- Lazy loading from Key Vault
- Cache management endpoints
- Reduces Key Vault API calls

---

### 🌐 External Services

#### **VentureFusionService**
Integration with VentureFusion document composition API.

```csharp
public class VentureFusionService : IVentureFusionService 
{ 
    Task<HttpResponseMessage> CheckHealth(); 
    Task<HttpResponseMessage> PostDataRecords(SequentialProcess request); 
    Task<VentureFusionBearerToken> GetBearerToken(); 
    void SetBearerTokenOnVentureFusionClient(VentureFusionBearerToken token); 
    DataRecord MapCorrespondenceRequestToVentureFusionRequest(SequentialProcess request); 
}
```

**Features:**
- OAuth 2.0 bearer token authentication
- Health check endpoint monitoring
- SequentialProcess to DataRecord mapping
- Recipient address formatting
- Error handling and logging
- HTTP client configuration with retry policies

#### **VentureFusionServiceFactory**
Factory for creating VentureFusion service instances with proper configuration.

```csharp
public class VentureFusionServiceFactory 
{ 
    VentureFusionService CreateService(VentureFusionServiceCredentials credentials); 
}
```

**Features:**
- Credential-based service instantiation
- HTTP client configuration
- APIM subscription key management

#### **CorrespondenceFilterService**
Advanced correspondence filtering and search.

```csharp
public class CorrespondenceFilterService : ICorrespondenceFilterService 
{ 
    Task<IEnumerable<CorrespondenceFilterResponse>> GetFilteredCorrespondenceAsync( CorrespondenceFilterRequest request); 
}
```

**Features:**
- Multi-criteria filtering (system, delivery method, status, date range)
- Encrypted policy number search (hash-based)
- Date parsing with validation
- Status history inclusion
- Comprehensive error logging

---

### 📨 Messaging

#### **RoutingSlipForwarder**
NServiceBus routing slip pattern implementation for sequential message processing.

```csharp
public static class RoutingSlipForwarder 
{ 
    Task ForwardToNextStepIfAny<T>(IMessageHandlerContext context, T message); 
}
```

**Features:**
- Sequential workflow orchestration
- Route header management (`RoutingSlip-Route`, `RoutingSlip-Index`)
- Automatic forwarding to next handler
- Loop prevention
- Comprehensive logging for debugging
- JSON-based route serialization

**Headers:**
- `RoutingSlip-Route` - JSON array of endpoint names
- `RoutingSlip-Index` - Current position in route
- `RoutingSlip-OriginatingId` - Original message ID

#### **NServiceBusLicenseProvider**
Manages NServiceBus license from Azure Key Vault.

```csharp
public class NServiceBusLicenseProvider 
{ 
    Task ProvideLicenseAsync(SecretClient secretClient); 
}
```

**Features:**
- Retrieves license from Key Vault
- Sets license for NServiceBus runtime
- Supports enterprise deployments

---

### 🏥 Health Checks

#### **AzureSqlHealthCheck**
Monitors Azure SQL Database connectivity.

```csharp
public class AzureSqlHealthCheck : IHealthCheck 
{ 
    Task<HealthCheckResult> CheckHealthAsync( HealthCheckContext context, CancellationToken cancellationToken); 
}
```

**Features:**
- Managed Identity authentication
- Token-based SQL connection
- Connection string validation
- Simple SELECT query test
- Detailed logging
- Integration with ASP.NET Core health checks

#### **KeyVaultHealthCheck**
Monitors Azure Key Vault accessibility.

```csharp
public class KeyVaultHealthCheck : IHealthCheck 
{ 
    Task<HealthCheckResult> CheckHealthAsync( HealthCheckContext context, CancellationToken cancellationToken); 
}
```

**Features:**
- Validates Key Vault URL configuration
- Tests secret retrieval
- Handles permission issues gracefully
- Managed Identity integration

#### **ServiceBusHealthCheck**
Monitors Azure Service Bus connectivity.

```csharp
public class ServiceBusHealthCheck : IHealthCheck 
{ 
    Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken); 
}
```

**Features:**
- Connection string validation
- Queue/topic connectivity test
- Managed Identity authentication
- Namespace-level health verification

---

### 🛠️ Utilities

#### **DocumentIdGeneratorService**
Generates unique 19-digit document IDs.

```csharp
public class DocumentIdGeneratorService : IDocumentIdGeneratorService 
{ 
    Task<string?> GenerateDocIdAsync(); 
}
```

**Features:**
- Retrieves IDs from pre-seeded pool
- Atomic ID assignment via repository
- Returns null when pool exhausted
- Thread-safe operations

---

## 🚀 Usage Examples

### Configure Infrastructure Services

```csharp
using ECCO.CommunicationManager.Infrastructure.Persistence; 
using ECCO.CommunicationManager.Infrastructure.Services; 
using ECCO.CommunicationManager.Infrastructure.Repositories;

// Database context with Managed Identity 
builder.Services.AddDbContext<CorrespondenceDBContext>(options => { 
    var connectionString = await secretsCache.GetSecretAsync("AzureDbConnectionString"); 
    var sqlBuilder = new SqlConnectionStringBuilder(connectionString); 
    sqlBuilder.Remove("Authentication"); 
    sqlBuilder.Remove("User ID"); 
    sqlBuilder.Remove("Password");
    options.UseSqlServer(
        sqlBuilder.ConnectionString,
        sqlOptions => sqlOptions.EnableRetryOnFailure()
    );
});

// Repositories 
builder.Services.AddScoped<ICorrespondenceRequestRepository, CorrespondenceRequestRepository>(); 
builder.Services.AddScoped<IRoutingConfigurationRepository, RoutingConfigurationRepository>(); 
builder.Services.AddScoped<IDocumentIdsRepository, DocumentIdsRepository>();

// Infrastructure services 
builder.Services.AddSingleton<ISecretsCacheService, SecretsCacheService>(); 
builder.Services.AddScoped<IEncryptionService, EncryptionService>(); 
builder.Services.AddScoped<IVentureFusionService, VentureFusionService>(); 
builder.Services.AddScoped<IDocumentIdGeneratorService, DocumentIdGeneratorService>(); 
builder.Services.AddScoped<ICorrespondenceFilterService, CorrespondenceFilterService>();

// Health checks 
builder.Services.AddHealthChecks()
                .AddCheck<AzureSqlHealthCheck>("azuresql")
                .AddCheck<KeyVaultHealthCheck>("keyvault")
                .AddCheck<ServiceBusHealthCheck>("servicebus");
```

### Use Repositories

```csharp
// Get correspondence by DocId 
var correspondence = await _correspondenceRequestRepository.GetCorrespondenceRequestByDocIdAsync("1234567890123456789");

// Search by encrypted policy number 
var policyHash = _encryptionService.GenerateSearchHash(policyNumber); 
var results = await _correspondenceRequestRepository.GetCorrespondenceRequestByPolicyNumberHashAsync(policyHash);

// Add new correspondence 
var request = new CorrespondenceRequest { 
        CorrespondenceId = Guid.NewGuid(), 
        DeliveryMethodCode = "EMAIL", 
        PolicyNumber = await _encryptionService.EncryptAsync(policyNumber) 
        }; 
await _correspondenceRequestRepository.AddCorrespondenceRequestAsync(request);
```

### Use VentureFusion Service

```csharp
var ventureFusionService = _serviceProvider.GetRequiredService<IVentureFusionService>();

// Check health 
var healthResponse = await ventureFusionService.CheckHealth(); 
if (healthResponse.IsSuccessStatusCode) 
{ 
    Console.WriteLine("VentureFusion API is healthy"); 
}

// Post data records 
var process = new SequentialProcess { RequestingSystemName = "PolicyManagement", DeliveryMethod = "MAIL", // ... other properties };
var response = await ventureFusionService.PostDataRecords(process);
```

### Use Routing Slip Forwarder

```csharp
using CustomerCorrespondence.Infrastructure.Messaging;
public class MyHandler : IHandleMessages<MyMessage> 
{ 
    public async Task Handle(MyMessage message, IMessageHandlerContext context) 
    { 
        // Process the message 
        Console.WriteLine($"Processing message: {message.DocId}");
        // Forward to next step in routing slip
        await RoutingSlipForwarder.ForwardToNextStepIfAny(context, message);
    }
}
```

### Use Secrets Cache

```csharp
// Get secret (with automatic caching) 
var connectionString = await _secretsCache.GetSecretAsync("MyConnectionString");

// Check if cached 
if (_secretsCache.IsSecretCached("MyConnectionString")) 
{ 
    var cached = _secretsCache.GetCachedSecret("MyConnectionString"); 
}

// Manual cache management 
_secretsCache.SetSecret("MySecret", "value", TimeSpan.FromHours(2)); 
_secretsCache.RemoveSecret("MySecret"); _secretsCache.ClearCache();
```

---

## 📦 Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| .NET | 8.0 | Target framework |
| Microsoft.EntityFrameworkCore | 9.0.11 | ORM framework |
| Microsoft.EntityFrameworkCore.SqlServer | 9.0.11 | SQL Server provider |
| Azure.Identity | 1.17.1 | Managed Identity authentication |
| Azure.Security.KeyVault.Secrets | 4.8.0 | Key Vault integration |
| Azure.Messaging.ServiceBus | 7.20.1 | Service Bus client |
| BouncyCastle.Cryptography | 2.6.1 | PGP encryption |
| Microsoft.Extensions.Diagnostics.HealthChecks | 10.0.0 | Health monitoring |

---

## 🔗 Related Packages

| Package | Purpose | Relationship |
|---------|---------|--------------|
| `ECCO.CommunicationManager.Domain` | Domain entities, interfaces | Implemented by Infrastructure |
| `ECCO.CommunicationManager.Application` | Application services, interfaces | Implemented by Infrastructure |
| `ECCO.CommunicationManager.Common` | Shared utilities, interfaces, constants | Used by Infrastructure |

---

## ⚠️ Known Issues

### Interface Duplication
- `ISecretsCacheService` is defined in both `Domain.Interfaces` and `Common.Interfaces`
- This should be consolidated to `Common.Interfaces` only
- Infrastructure currently implements the Domain version

### Missing Interface Implementation
- `KeyVaultService` should implement `IKeyVaultService` from Common package
- Currently implemented as a standalone class

---

## 🔐 Security Features

### Managed Identity Authentication
All Azure services use Managed Identity:
- **Azure SQL** - Token-based authentication
- **Azure Key Vault** - DefaultAzureCredential
- **Azure Service Bus** - Managed Identity connection

### Data Encryption
- **PGP Encryption** - Policy numbers encrypted at rest
- **Hash-Based Search** - SHA-256 hashes for encrypted field lookups
- **Secure Transport** - HTTPS for all external API calls

### Log Injection Prevention
All user inputs are sanitized before logging:

```csharp
string safeDocId = System.Net.WebUtility.HtmlEncode(docId ?? string.Empty); 
Console.Error.WriteLine($"Error processing DocId {safeDocId}");
```

---

## 🎯 Best Practices

### DO ✅

```csharp
// Use dependency injection public class MyRepository : IMyRepository 
{ 
    private readonly CorrespondenceDBContext _dbContext;
    public MyRepository(CorrespondenceDBContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }
}

// Always use async operations 
var results = await _dbContext.CorrespondenceRequests.Where(r => r.DocId == docId).ToListAsync();

// Sanitize inputs before logging 
var safeInput = System.Net.WebUtility.HtmlEncode(userInput); 
_logger.LogError($"Failed to process: {safeInput}");

// Use Managed Identity 
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = azureClientId });
```

### DON'T ❌
```csharp
// Don't hardcode connection strings 
var connStr = "Server=...;Password=..."; // ❌

// Don't expose domain logic in infrastructure 
public class MyRepository 
{ 
    public async Task ProcessBusinessRule() { } // ❌ Business logic belongs in Domain/Application 
}

// Don't log sensitive data 
_logger.LogInformation($"Policy: {policyNumber}"); // ❌ PII exposure

// Don't use synchronous I/O 
var result = _dbContext.CorrespondenceRequests.ToList(); // ❌ Blocking call
```

---

## 📋 Configuration

### appsettings.json
```json
{ 
    "KeyVault": { 
        "Url": "https://your-keyvault.vault.azure.net/", 
        "ManagedIdentityClientId": "your-managed-identity-client-id" 
        }, 
    "SecretNames": { 
        "AzureDbConnectionString": "AzureDbConnectionString", 
        "NServiceBusLicense": "NServiceBusLicense", 
        "PgpPublicKey": "PgpPublicKey" 
        }, 
    "ConnectionStrings": { 
        "ServiceBus": "Endpoint=sb://your-namespace.servicebus.windows.net/;..." 
        } 
}
```

### Environment Variables
Managed Identity
AZURE_CLIENT_ID=your-managed-identity-client-id

Optional overrides
AZURE_TENANT_ID=your-tenant-id AZURE_CLIENT_SECRET=your-client-secret (for development only)

---

## 📝 Version History

### 1.0.0 (Initial Release)
- ✅ EF Core 9.0 repositories with Managed Identity
- ✅ CorrespondenceDBContext with all entity configurations
- ✅ VentureFusion API integration
- ✅ PGP encryption service (BouncyCastle)
- ✅ Azure Key Vault integration with caching
- ✅ NServiceBus routing slip forwarder
- ✅ Health checks (SQL, Key Vault, Service Bus)
- ✅ Document ID generator service
- ✅ Correspondence filter service
- ✅ Log injection prevention
- ✅ Hash-based encrypted field searches

---

## 📞 Support

- **Team:** ECCO Development Team
- **Repo:** [Azure DevOps - ECCO-CommunicationManager](https://dev.azure.com/S9uareRootLLC/CorporateServices/_git/ECCO-CommunicationManager)

---

## 📄 License

Internal use for S9uareRoot LLC

---

**Made with ❤️ by the ECCO Team**






