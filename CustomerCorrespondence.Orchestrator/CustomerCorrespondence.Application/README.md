# ECCO.CommunicationManager.Application

> Application layer for ECCO Communication Manager - containing application services, use cases, business logic orchestration, and service interfaces.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![AutoMapper](https://img.shields.io/badge/AutoMapper-16.1.1-blue.svg)](https://automapper.org/)

---

## 📦 Package Information

**Package Name:** `ECCO.CommunicationManager.Application`  
**Version:** 1.0.0  
**Target Framework:** .NET 8.0

---

## 🎯 What's This For?

This package contains the **application layer** for the ECCO Correspondence system:

- ✅ **Application Services** - Business logic orchestration and use case implementation
- ✅ **Service Interfaces** - Contracts for infrastructure implementations
- ✅ **DTOs** - Data transfer objects for API communication
- ✅ **Business Logic** - Coordination between domain and infrastructure layers
- ✅ **AutoMapper Integration** - Object-to-object mapping profiles

**Key Principle**: Application layer **orchestrates** domain logic and **defines contracts** that infrastructure implements.

---

## 📚 What's Included?

### 🔧 Application Services

Application services implement business use cases by coordinating domain entities and infrastructure services.

#### **CorrespondenceRequestService**
Manages CRUD operations for correspondence requests.

```csharp
public class CorrespondenceRequestService : ICorrespondenceRequestService 
{ 
    Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByCorrIdAsync(Guid corrId); 
    Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByDocIdAsync(string docId); 
    Task AddCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest); 
    Task UpdateCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest); 
    Task DeleteCorrespondenceRequestAsync(Guid correspondenceId); 
}
```

**Features:**
- Retrieval by CorrespondenceId or DocId
- Create, update, and delete operations
- Comprehensive logging for all operations
- Exception handling and error propagation

#### **CorrespondenceStatusService**
Provides correspondence status tracking and querying capabilities.

```csharp
public class CorrespondenceStatusService : ICorrespondenceStatusService 
{ 
    Task<CorrespondenceStatusResponse?> GetCorrespondenceStatusByDocIdAsync(string docId); 
    Task<CorrespondenceStatusResponse?> GetCorrespondenceStatusByCorrespondenceIdAsync(Guid correspondenceId); 
    Task<IEnumerable<CorrespondenceStatusResponse>> GetCorrespondenceStatusByPolicyNumberAsync(string policyNumber); 
    Task<IEnumerable<CorrespondenceStatusResponse>> GetCorrespondenceStatusByClaimNumberAsync(string claimNumber); 
}
```
**Features:**
- Multi-criteria status lookup (DocId, CorrespondenceId, PolicyNumber, ClaimNumber)
- Encrypted policy number search using hash-based lookup
- Complete status history tracking
- Processing information (VentureFusion integration status)
- Log injection prevention (sanitized logging)

#### **RoutingConfigurationService**
Manages routing configurations for correspondence processing.

```csharp
public class RoutingConfigurationService : IRoutingConfigurationService 
{ 
    Task<RoutingConfiguration> GetRoutingConfigurationByIdAsync(int id); 
    Task<RoutingConfiguration?> GetRoutingConfigurationByRouteNameAsync(string routeName); 
    Task<RoutingConfiguration?> GetRoutingConfigurationByCustomerNameAsync(string customerName); 
    Task AddRoutingConfigurationAsync(RoutingConfiguration routingConfiguration); 
    Task UpdateRoutingConfigurationAsync(RoutingConfiguration routingConfiguration); 
}
```
**Features:**
- Lookup by Id, RouteName, or CustomerName
- Create and update routing rules
- Route data management

#### **DocumentIdsService**
Manages unique document ID generation and lifecycle.

```csharp
public class DocumentIdsService : IDocumentIdsService 
{ 
    Task AddDocumentIdAsync(string documentId); 
    Task<string?> GetAndUpdateDocumentIdAsync(); 
    Task<int> GetNewDocumentIdCountAsync(); 
    Task RevertDocumentIdAsync(string documentId); 
}
```

**Features:**
- Sequential document ID assignment
- ID pool management
- Rollback support for failed operations
- Available ID count tracking

#### **CorrespondenceRequestStatusHistoryService**
Manages status history tracking for correspondence.

```csharp
public class CorrespondenceRequestStatusHistoryService : ICorrespondenceRequestStatusHistoryService 
{ 
    Task<IEnumerable<CorrespondenceRequestStatusHistory?>> GetAllByCorrespondenceIdAsync(Guid correspondenceId); 
    Task<CorrespondenceRequestStatusHistory?> GetByHistorySequenceNumAsync(Guid correspondenceId, int historySequenceNum); 
    Task AddCorrespondenceRequestStatusHistoryAsync(CorrespondenceRequestStatusHistory history); 
    Task UpdateCorrespondenceRequesStatusHistorytAsync(CorrespondenceRequestStatusHistory history); 
    Task DeleteCorrespondenceRequestStatusHistoryAsync(Guid correspondenceId); 
    Task LogNonRecoverableErrorAsync(IncomingMessage messageId, string errorMessage, string stackTrace); 
}
```

**Features:**
- Complete status history retrieval
- Sequence-based history lookup
- Error logging for non-recoverable failures
- NServiceBus message tracking

---

### 🔌 Service Interfaces

Interfaces that infrastructure layer must implement.

#### **IVentureFusionService**
External VentureFusion API integration.
```csharp
public interface IVentureFusionService 
{ 
    Task<HttpResponseMessage> CheckHealth(); 
    Task<HttpResponseMessage> PostDataRecords(SequentialProcess request); 
    Task<VentureFusionBearerToken> GetBearerToken(); 
    void SetBearerTokenOnVentureFusionClient(VentureFusionBearerToken token); 
    DataRecord MapCorrespondenceRequestToVentureFusionRequest(SequentialProcess request); 
}
```

**Purpose:**
- Health check monitoring
- Document composition requests
- OAuth token management
- Request mapping/transformation

#### **ICorrespondenceFilterService**
Advanced correspondence filtering and search.
```csharp
public interface ICorrespondenceFilterService 
{ 
    Task<IEnumerable<CorrespondenceFilterResponse>> GetFilteredCorrespondenceAsync( CorrespondenceFilterRequest request); 
}
```

**Purpose:**
- Multi-criteria correspondence search
- Date range filtering
- Status-based filtering
- Policy number search (encrypted)

#### **IEncryptionService**
Data encryption and hashing for sensitive information.

```csharp
public interface IEncryptionService 
{ 
    Task<string> EncryptAsync(string plainText); 
    Task<string> DecryptAsync(string encryptedText); 
    byte[] GenerateSearchHash(string plainText); 
}
```

**Purpose:**
- PGP encryption for policy numbers
- Decryption of sensitive data
- SHA-256 hash generation for searches

#### **IDocumentIdGeneratorService**
Document ID generation service.

```csharp
public interface IDocumentIdGeneratorService { Task<string?> GenerateDocIdAsync(); }
```

**Purpose:**
- Generate unique 19-digit document IDs
- Ensure ID uniqueness across the system

---

### 📋 DTOs (Data Transfer Objects)

DTOs in the Domain layer used by Application services.

#### **CorrespondenceFilterRequest**
Request DTO for filtering correspondence.

```csharp
public class CorrespondenceFilterRequest { [Required] public string RequestingSystemName { get; set; }
public string? DocumentDescription { get; set; }
public string? DeliveryMethod { get; set; }
public string? CorrespondenceRequestStatusCode { get; set; }

[Required]
public string CreateBeginDate { get; set; } // Format: MM/dd/yyyy

public string? CreateEndDate { get; set; }
public string? PolicyNumber { get; set; }
}
```

#### **CorrespondenceFilterResponse**
Response DTO with filtered correspondence data.

```csharp
public class CorrespondenceFilterResponse 
{ 
    public Guid CorrespondenceId { get; set; } 
    public string? DocId { get; set; } 
    public string? RequestingDomainName { get; set; } 
    public string? DeliveryMethodCode { get; set; } 
    public string? PolicyNumber { get; set; } 
    public string? CustomerId { get; set; } 
    public DateTime RequestCreationTS { get; set; } 
    public ICollection<CorrespondenceRequestStatusHistory> StatusHistories { get; set; } 
}
```

#### **CorrespondenceStatusResponse**
Comprehensive status information response.
```csharp
public class CorrespondenceStatusResponse 
{ 
    public string? DocId { get; set; } 
    public Guid CorrespondenceId { get; set; } 
    public string? RequestingSystemName { get; set; } 
    public string? PolicyNumber { get; set; } 
    public string? ClaimNumber { get; set; } 
    public string? CustomerId { get; set; } 
    public string? DeliveryMethod { get; set; } 
    public DateTime RequestCreationDate { get; set; } 
    public CurrentStatusInfo CurrentStatus { get; set; } 
    public List<StatusHistoryInfo> StatusHistory { get; set; } 
    public ProcessingInfo? ProcessingInfo { get; set; } 
}
```

**Supporting Classes:**
- `CurrentStatusInfo` - Current status details
- `StatusHistoryInfo` - Historical status entries
- `ProcessingInfo` - VentureFusion processing metadata

---

## 🚀 Usage Examples

### Using Application Services

#### Get Correspondence Status

```csharp
using ECCO.CommunicationManager.Application.Interfaces;
public class StatusController : ControllerBase 
{ 
    private readonly ICorrespondenceStatusService _statusService;
    public StatusController(ICorrespondenceStatusService statusService)
    {
        _statusService = statusService;
    }

    [HttpGet("status/{docId}")]
    public async Task<ActionResult<CorrespondenceStatusResponse>> GetStatus(string docId)
    {
        var status = await _statusService.GetCorrespondenceStatusByDocIdAsync(docId);
    
        if (status == null)
            return NotFound($"No correspondence found for DocId: {docId}");
        
        return Ok(status);
    }
}
```

#### Search Correspondence
```csharp
var filterRequest = new CorrespondenceFilterRequest 
{ 
    RequestingSystemName = "PolicyManagement", 
    DeliveryMethod = "EMAIL", 
    CreateBeginDate = "01/01/2026", 
    CreateEndDate = "01/31/2026", 
    CorrespondenceRequestStatusCode = "COMPLETED" 
};
var results = await _filterService.GetFilteredCorrespondenceAsync(filterRequest);
```

#### Generate Document ID
```csharp
var docId = await _documentIdGeneratorService.GenerateDocIdAsync(); 
if (docId == null) { 
    // No available IDs - handle error 
    throw new InvalidOperationException("No document IDs available"); 
}
// Use the generated 19-digit ID 
Console.WriteLine($"Generated DocId: {docId}");
```

#### Update Correspondence
```csharp
var request = await _correspondenceRequestService.GetCorrespondenceRequestByDocIdAsync(docId);
if (request != null) 
{ 
    request.DeliveryMethodCode = "MAIL"; 
    await _correspondenceRequestService.UpdateCorrespondenceRequestAsync(request); 
}
```

---

## 🔐 Security Features

### Encrypted Policy Number Search
The `CorrespondenceStatusService` uses hash-based lookups for policy numbers:
```csharp
// Generate hash for database lookup (same algorithm as storage) 
var policyNumberHash = _encryptionService.GenerateSearchHash(policyNumber);
// Use hash-based lookup instead of plaintext 
var correspondenceRequests = await _requestRepo.GetCorrespondenceRequestByPolicyNumberHashAsync(policyNumberHash);
```

### Log Injection Prevention
All services sanitize input before logging:

```csharp
// Neutralize output for logs to prevent log injection 
var safeDocId = System.Net.WebUtility.HtmlEncode( docId.Replace("\r", string.Empty).Replace("\n", string.Empty) ); 
_logger.LogInformation($"Getting correspondence status for DocId: {safeDocId}");
```

---

## 📦 Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| .NET | 8.0 | Target framework |
| AutoMapper | 16.1.1 | Object-to-object mapping |
| Microsoft.Extensions.Logging.Abstractions | 10.0.1 | Logging abstractions |
| ECCO.CommunicationManager.Domain | 1.0.0 | Domain entities and interfaces |

---

## 🔗 Related Packages

| Package | Purpose | Relationship |
|---------|---------|--------------|
| `ECCO.CommunicationManager.Domain` | Domain entities, value objects, messages | Referenced by Application |
| `ECCO.CommunicationManager.Infrastructure` | EF Core, repositories, external APIs | Implements Application interfaces |
| `ECCO.CommunicationManager.Common` | Shared utilities, Result pattern | Independent utility library |

---

## 🎯 Best Practices

### DO ✅

```csharp
// Use dependency injection 
public class MyService : IMyService 
{ 
    private readonly ICorrespondenceRequestRepository _repo; 
    private readonly ILogger<MyService> _logger;

    public MyService(
        ICorrespondenceRequestRepository repo,
        ILogger<MyService> logger)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}

// Log all operations 
_logger.LogInformation("Fetching correspondence for DocId: {DocId}", docId);

// Sanitize inputs before logging 
var safeInput = System.Net.WebUtility.HtmlEncode(userInput); 
_logger.LogInformation($"Processing request: {safeInput}");

// Handle exceptions appropriately 
try 
{ 
    await _repo.AddCorrespondenceRequestAsync(request); 
} 
catch (Exception ex) 
{ 
    _logger.LogError(ex, "Error adding correspondence"); 
    throw; // Re-throw for caller to handle 
}
```

### DON'T ❌
```csharp
// Don't access infrastructure directly 
using Microsoft.EntityFrameworkCore; // ❌ In Application layer

// Don't include business logic in DTOs 
public class CorrespondenceFilterRequest 
{ 
    public void ProcessRequest() { } // ❌ DTOs are data-only 
}

// Don't log sensitive data without sanitization 
_logger.LogInformation($"Policy: {policyNumber}"); // ❌ Logs PII

// Don't swallow exceptions 
catch (Exception) 
{ 
    return null; // ❌ Silent failure 
}
```

---

## 📋 Service Registration

In your `Program.cs` or `Startup.cs`:
```csharp
// Register application services 
builder.Services.AddScoped<ICorrespondenceRequestService, CorrespondenceRequestService>(); 
builder.Services.AddScoped<ICorrespondenceStatusService, CorrespondenceStatusService>(); 
builder.Services.AddScoped<IRoutingConfigurationService, RoutingConfigurationService>(); 
builder.Services.AddScoped<IDocumentIdsService, DocumentIdsService>(); 
builder.Services.AddScoped<ICorrespondenceRequestStatusHistoryService, CorrespondenceRequestStatusHistoryService>();

// Infrastructure services (implemented in Infrastructure layer) 
builder.Services.AddScoped<IVentureFusionService, VentureFusionService>(); 
builder.Services.AddScoped<ICorrespondenceFilterService, CorrespondenceFilterService>(); 
builder.Services.AddScoped<IEncryptionService, EncryptionService>(); 
builder.Services.AddScoped<IDocumentIdGeneratorService, DocumentIdGeneratorService>();

// Add AutoMapper 
builder.Services.AddAutoMapper(typeof(ApplicationAssemblyMarker));
```

---

## 📝 Version History

### 1.0.0 (Initial Release)
- ✅ Application services (CorrespondenceRequestService, RoutingConfigurationService, DocumentIdsService)
- ✅ Status tracking service (CorrespondenceStatusService)
- ✅ Service interfaces (IVentureFusionService, ICorrespondenceFilterService, IEncryptionService)
- ✅ DTOs for filtering and status responses
- ✅ AutoMapper integration
- ✅ Comprehensive logging
- ✅ Security features (log injection prevention, encrypted search)


---

## 📞 Support

- **Team:** ECCO Development Team
- **Repo:** [Azure DevOps - ECCO-CommunicationManager](https://dev.azure.com/S9uareRootLLC/CorporateServices/_git/ECCO-CommunicationManager)

---

## 📄 License

Internal use for S9uareRoot LLC

---

**Made with ❤️ by the ECCO Team**
