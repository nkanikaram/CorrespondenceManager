# CustomerCorrespondence.Domain

> Core domain layer for ECCO Communication Manager - containing entities, value objects, message contracts, and domain interfaces.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![NServiceBus](https://img.shields.io/badge/NServiceBus-8.2.6-blue.svg)](https://particular.net/nservicebus)

---

## 📦 Package Information

**Package Name:** `ECCO.CommunicationManager.Domain`  
**Version:** 1.0.0  
**Target Framework:** .NET 8.0

---

## 🎯 What's This For?

This package contains the **core domain model** for the ECCO Correspondence system:

- ✅ **Domain Entities** - CorrespondenceRequest, RoutingConfiguration, Solutions, etc.
- ✅ **Value Objects** - SequentialProcess, RecipientAddress, MailDeliveryInfo, CompositionInfo
- ✅ **NServiceBus Messages** - Command contracts for distributed messaging
- ✅ **Domain Interfaces** - Repository and service contracts
- ✅ **API Response Models** - VentureFusionApiResponse

---

### 📄 Core Entities

#### **CorrespondenceRequest**
Main correspondence entity with encrypted policy information.

```csharp
public class CorrespondenceRequest 
{ 
    public Guid CorrespondenceId { get; set; } 
    public string? DocId { get; set; } 
    public string? RequestingDomainName { get; set; } 
    public required string DeliveryMethodCode { get; set; } 
    public string? VersionCode { get; set; } 
    public string? ArchiveDocumentTypeCode { get; set; } 
    public string? ArchiveDocumentSubTypeCode { get; set; } 
    public byte[]? PolicyNumber { get; set; } // PGP encrypted 
    public byte[]? PolicyNumberHash { get; set; } // SHA-256 hash for searches 
    public string? ClaimNumber { get; set; } 
    public string? CustomerId { get; set; } 
    public byte[]? RequestPayload { get; set; } 
    public DateTime RequestCreationTS { get; set; } 
    public ICollection<CorrespondenceRequestStatusHistory> StatusHistories { get; set; }

    public void Validate() 
    { 
        /* Validates DeliveryMethodCode */ 
    }
}
```

#### **CorrespondenceRequestStatusHistory**
Tracks status changes over time.

```csharp
public class CorrespondenceRequestStatusHistory 
{ 
    public Guid CorrespondenceRequestStatusHistoryId { get; set; } 
    public Guid CorrespondenceId { get; set; } 
    public int HistorySequenceNum { get; set; } 
    [Required] 
    public string? StatusCode { get; set; } 
    public string? StatusUpdatedBy { get; set; } 
    public DateTime StatusUpdateTS { get; set; } 
}
```

#### **RoutingConfiguration**
Defines routing rules for correspondence processing.

```csharp
public class RoutingConfiguration 
{ 
    public int Id { get; set; } 
    public string? RouteName { get; set; } 
    public string? CustomerName { get; set; } 
    public string? CorrespondenceMethod { get; set; }
    // Store as semicolon-separated values
    public string RouteData { get; set; } = string.Empty;

    // Computed property that converts to/from simple format
    public List<string> Route
    {
        get => string.IsNullOrEmpty(RouteData)
            ? new List<string>()
            : RouteData.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        set => RouteData = string.Join(";", value);
    }
}
```

#### **Solutions**
Solution configuration entity.

```csharp
public class Solutions 
{ 
    public string? solutionsId { get; set; } 
    public string? solutionName { get; set; } 
    public string? description { get; set; } 
    public int dataRetention { get; set; } 
    public string? customerId { get; set; } 
    public int usageId { get; set; } 
    public string? welcomeKitSolutionsId { get; set; } 
    public string? welcomeLetterSolutionsId { get; set; } 
}   
```

#### **CorrespondenceDeliveryMethod**
Defines available delivery methods.

```csharp
public class CorrespondenceDeliveryMethod 
{ 
    public Guid DeliveryMethodId { get; set; } 
    public required string DeliveryMethodCode { get; set; } 
    public required string DeliveryMethodName { get; set; }
    public void Validate() { /* Validates required fields */ }
}
```

#### **CorrespondenceStatusCode**
Defines available status codes and descriptions.

```csharp
public class CorrespondenceStatusCode 
{ 
    public Guid StatusId { get; set; } 
    public required string StatusCode { get; set; } 
    public required string StatusDescription { get; set; }
    public void Validate() { /* Validates required fields */ }
}
```

---

### 💎 Value Objects

#### **SequentialProcess**
Core value object representing a correspondence processing workflow with validation and sanitization.

```csharp
public class SequentialProcess 
{ 
    public string? DocId { get; set; }
    [Required]
    public required string RequestingSystemName { get; set; }

    public string? ECCOTrackingPolicyNumber { get; set; }
    public string? ECCOTrackingClaimNumber { get; set; }
    public string? ECCOTrackingCustomerId { get; set; }

    [Required]
    public required string DeliveryMethod { get; set; }

    public string? DocumentDescription { get; set; }

    [Required]
    public required MailDeliveryInfo MailDeliveryInfo { get; set; }

    [Required]
    public required CompositionInfo CompositionInfo { get; set; }

    public VentureFusionApiResponse? VentureFusionResponse { get; set; }
    public Dictionary<string, object>? AdditionalData { get; set; }

    // Validation methods
    public void Validate() { /* Basic validation */ }
    public void ValidateNoSpecialCharacters() { /* Security validation */ }
    public void TrimAllStringFields() { /* Data sanitization */ }
}
```

#### **MailDeliveryInfo**
Contains mailing address and recipient information.

```csharp
public class MailDeliveryInfo 
{ 
    [StringLength(60)] public string? RecipientFullName { get; set; }
    [StringLength(30)]
    public string? RecipientFirstName { get; set; }

    [StringLength(30)]
    public string? RecipientLastName { get; set; }

    [Required]
    public RecipientAddress? RecipientAddress { get; set; }

    public bool? NonUSAddressFlag { get; set; }
    public bool? BadAddressFlag { get; set; }

    public void ValidateNoSpecialCharacters() { }
    public void TrimAllStringFields() { }
}
```

#### **RecipientAddress**
Physical address information with validation.

```csharp   
public class RecipientAddress 
{ 
    [StringLength(35)] 
    public string? RecipientAddressLine1 { get; set; }
    [StringLength(35)]
    public string? RecipientAddressLine2 { get; set; }

    [StringLength(35)]
    public string? RecipientAddressLine3 { get; set; }

    [StringLength(100)]
    public string? RecipientAddressCity { get; set; }

    [StringLength(40)]
    public string? RecipientAddressST { get; set; }

    [StringLength(20)]
    public string? RecipientAddressZIP { get; set; }

    [StringLength(60)]
    public string? RecipientCountry { get; set; }

    public void ValidateNoSpecialCharacters() { }
    public void TrimAllStringFields() { }
}
```

#### **CompositionInfo**
Document composition metadata.

```csharp
public class CompositionInfo 
{ 
    [Required] 
    public string? SolutionsId { get; set; }
    [Required]
    public string? VersionCode { get; set; }

    [StringLength(250)]
    public string? CompositionSchemaId { get; set; }

    public void ValidateNoSpecialCharacters() { }
    public void TrimAllStringFields() { }
}
```

#### **ArchiveInfo**
Archive configuration (currently minimal).

public class ArchiveInfo { // Reserved for future archive metadata }

#### **VentureFusionApiResponse**
Response model for VentureFusion API calls.

```csharp
public class VentureFusionApiResponse 
{ 
    public System.Net.HttpStatusCode StatusCode { get; set; } 
    public string? ResponseContent { get; set; } 
    public DateTime ResponseTimestamp { get; set; } 
    public bool IsSuccessful { get; set; } 
    public string? ErrorMessage { get; set; } 
    public Dictionary<string, string>? ResponseHeaders { get; set; } 
}   
```

---

### 🔐 Security & Credentials

#### **VentureFusionServiceCredentials**
Credentials for external VentureFusion service.

```csharp
public class VentureFusionServiceCredentials 
{ 
    public string? TenantId { get; set; } 
    public string? ClientId { get; set; } 
    public string? ClientSecret { get; set; } 
    public string? ApimSubscriptionKey { get; set; } 
    public Uri? Uri { get; set; } 
    public string? Scope { get; set; } 
    public string? TokenEndpoint { get; set; } 
    public string? grant_type { get; set; }
public string GetEncodedAuthenticationString()
    => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
}
```

#### **VentureFusionBearerToken**
OAuth bearer token response.

```csharp
public class VentureFusionBearerToken 
{ 
    public string? token_type { get; set; } 
    public int expires_in { get; set; } 
    public int ext_expires_in { get; set; } 
    public string? access_token { get; set; } 
}
```

---

### 📨 NServiceBus Message Contracts

All messages implement NServiceBus `ICommand` for distributed messaging.

#### **BeginRequestHandlerMessage**
Initiates the correspondence processing workflow.

```csharp
public class BeginRequestHandlerMessage : ICommand 
{ 
    public required SequentialProcess Process { get; set; }
    public BeginRequestHandlerMessage() { }
    public BeginRequestHandlerMessage(SequentialProcess process)
        => Process = process ?? throw new ArgumentNullException(nameof(process));
}
```

#### **RequestCompositionHandlerMessage**
Triggers document composition process.

```csharp
public class RequestCompositionHandlerMessage : ICommand 
{ 
    public required SequentialProcess Process { get; set; }
    public RequestCompositionHandlerMessage() { }
    public RequestCompositionHandlerMessage(SequentialProcess process)
        => Process = process ?? throw new ArgumentNullException(nameof(process));
}
```

#### **AssignDocumentumIdHandlerMessage**
Assigns a unique document ID.

```csharp
public class AssignDocumentumIdHandlerMessage : ICommand 
{ 
    public required SequentialProcess Process { get; set; }
    public AssignDocumentumIdHandlerMessage() { }
    public AssignDocumentumIdHandlerMessage(SequentialProcess process)
        => Process = process ?? throw new ArgumentNullException(nameof(process));
}
```           

#### **AddCorrespondenceSBusHandlerMessage**
Publishes correspondence to Service Bus.

```csharp
public class AddCorrespondenceSBusHandlerMessage : ICommand 
{ 
    public required SequentialProcess Process { get; set; }
    public AddCorrespondenceSBusHandlerMessage() { }
    public AddCorrespondenceSBusHandlerMessage(SequentialProcess process)
        => Process = process ?? throw new ArgumentNullException(nameof(process));
}
```

#### **AddCorrespondenceDBHandlerMessage**
Adds correspondence to database.

```csharp
public class AddCorrespondenceDBHandlerMessage : ICommand 
{ 
    public required SequentialProcess Process { get; set; }
    public AddCorrespondenceDBHandlerMessage() { }
    public AddCorrespondenceDBHandlerMessage(SequentialProcess process)
        => Process = process ?? throw new ArgumentNullException(nameof(process));
}
```

#### **UpdateCorrespondenceDBHandlerMessage**
Updates correspondence in database.

```csharp
public class UpdateCorrespondenceDBHandlerMessage : ICommand 
{ 
    public required SequentialProcess Process { get; set; }
    public UpdateCorrespondenceDBHandlerMessage() { }
    public UpdateCorrespondenceDBHandlerMessage(SequentialProcess process)
        => Process = process ?? throw new ArgumentNullException(nameof(process));
}
```

#### **UpdateArchivedStatusDBHandlerMessage**
Updates archive status in database.

```csharp
public class UpdateArchivedStatusDBHandlerMessage : ICommand 
{ 
    public required SequentialProcess Process { get; set; }
    public UpdateArchivedStatusDBHandlerMessage() { }
    public UpdateArchivedStatusDBHandlerMessage(SequentialProcess process)
        => Process = process ?? throw new ArgumentNullException(nameof(process));
}
```

#### **SequentialProcessRequest**
Message wrapper for routing (implements NServiceBus `IMessage`).

```csharp
public class SequentialProcessRequest : IMessage 
{ 
    public required SequentialProcess Process { get; set; } 
    public List<string> Route { get; set; } = new List<string>();
    public void Validate() { /* Validates Process is not null */ }
}
```

---

### 🔌 Domain Interfaces

Contracts that infrastructure layer implements.

#### **ICorrespondenceRequestRepository**
Repository for correspondence requests.

```csharp
public interface ICorrespondenceRequestRepository 
{ 
    Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByCorrIdAsync(Guid CorrespondenceId); 
    Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByDocIdAsync(string docId); 
    Task AddCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest); 
    Task UpdateCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest); 
    Task DeleteCorrespondenceRequestForCorrIdAsync(Guid correspondenceId); 
    Task DeleteCorrespondenceRequestForDocIdAsync(string docId); 
    Task<Guid> GetCorrespondenceIdByDocIdAsync(string docId); 
    Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByPolicyNumberHashAsync(byte[] policyNumberHash); 
    Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByClaimNumberAsync(string claimNumber); 
}
```

#### **ICorrespondenceRequestStatusHistoryRepository**
Repository for status history tracking.

```csharp
public interface ICorrespondenceRequestStatusHistoryRepository 
{ 
    Task<IEnumerable<CorrespondenceRequestStatusHistory>> GetAllByCorrespondenceIdAsync(Guid correspondenceId); 
    Task<CorrespondenceRequestStatusHistory?> GetStatusHistoryByCorrIdSeqNumAsync(Guid correspondenceId, int historySequenceNum); 
    Task<int> GetMaxSequenceNumber(Guid correspondenceId); 
    Task<string> GetStatusCodeByStatus(string statusName); 
    Task AddCorrespondenceRequestStatusHistoryAsync(CorrespondenceRequestStatusHistory correspondenceRequestStatusHistory); 
    Task UpdateCorrespondenceRequesStatusHistorytAsync(CorrespondenceRequestStatusHistory correspondenceRequestStatusHistory); 
    Task DeleteCorrespondenceRequestStatusHistoryAsync(Guid correspondenceId); 
    Task<string?> GetStatusDescriptionByStatusCode(string statusCode); 
}
```

#### **IRoutingConfigurationRepository**
Repository for routing configurations.

```csharp
public interface IRoutingConfigurationRepository 
{ 
    Task<RoutingConfiguration?> GetRoutingConfigurationByIdAsync(int id); 
    Task<RoutingConfiguration?> GetRoutingConfigurationByRouteNameAsync(string routeName); 
    Task<RoutingConfiguration?> GetRoutingConfigurationByCustomerNameAsync(string customerName); 
    Task AddRoutingConfigurationAsync(RoutingConfiguration routingConfiguration); 
    Task UpdateRoutingConfigurationAsync(RoutingConfiguration routingConfiguration); 
}
```

#### **IDocumentIdsRepository**
Repository for managing document ID sequences.

```csharp
public interface IDocumentIdsRepository 
{ 
    Task AddDocumentIdAsync(string documentId); 
    Task<string?> GetAndUpdateDocumentIdAsync(); 
    Task<int> GetNewDocumentIdCountAsync(); 
    Task RevertDocumentIdAsync(string documentId); 
}
```

---

### 📋 DTOs (Data Transfer Objects)

#### **CorrespondenceFilterRequest**
Request DTO for filtering correspondence.

[Include actual structure from Domain\DTOs\CorrespondenceFilterRequest.cs]

#### **CorrespondenceFilterResponse**
Response DTO with filtered correspondence data.

[Include actual structure from Domain\DTOs\CorrespondenceFilterResponse.cs]

#### **CorrespondenceStatusResponse**
Comprehensive status information response.

[Include actual structure from Domain\DTOs\CorrespondenceStatusResponse.cs]

---

### 🛡️ Helper Classes

#### **ProcessRequestHelper**
Internal static helper for security validation.

```csharp
internal static class ProcessRequestHelper 
{ 
    // Matches characters that can be weaponised for injection attacks 
    internal static readonly Regex ForbiddenCharsRegex = new Regex(@"[<>{}[];|\^~`]", RegexOptions.Compiled);

    internal static void CheckField(string? value, string fieldName)
    {
        if (value != null && ForbiddenCharsRegex.IsMatch(value))
            throw new ValidationException(
                $"{fieldName} contains invalid special characters. " +
                @"Forbidden characters: < > { } [ ] ; | \ ^ ~ `");
    }
}
```

**Forbidden Characters:** `< > { } [ ] ; | \ ^ ~ `` `

These characters are blocked to prevent injection attacks.

---

## 🚀 Usage Examples

### Working with Entities

```csharp
using CustomerCorrespondence.Domain.Entities;
// Create correspondence request var request = new CorrespondenceRequest { CorrespondenceId = Guid.NewGuid(), DeliveryMethodCode = "EMAIL", RequestingDomainName = "PolicyManagement", CustomerId = "CUST-12345", RequestCreationTS = DateTime.UtcNow, StatusHistories = new List<CorrespondenceRequestStatusHistory>() };
// Validate try { request.Validate(); // Throws if invalid } catch (InvalidOperationException ex) { // Handle validation error }
```

### Creating a Sequential Process

```csharp
using CustomerCorrespondence.Domain.Entities;
var process = new SequentialProcess { RequestingSystemName = "PolicyManagement", DeliveryMethod = "EMAIL", DocumentDescription = "Policy Welcome Letter", MailDeliveryInfo = new MailDeliveryInfo { RecipientFullName = "John Doe", RecipientAddress = new RecipientAddress { RecipientAddressLine1 = "123 Main St", RecipientAddressCity = "Madison", RecipientAddressST = "WI", RecipientAddressZIP = "53703" } }, CompositionInfo = new CompositionInfo { SolutionsId = "SOL-001", VersionCode = "V1" } };
// Validate and sanitize process.Validate(); process.ValidateNoSpecialCharacters(); process.TrimAllStringFields();
```

### Sending NServiceBus Messages

```csharp
using CustomerCorrespondence.Domain.Entities.Messages;
var message = new BeginRequestHandlerMessage(process); await endpoint.Send(message);
```

### Implementing Domain Interfaces

```csharp
using CustomerCorrespondence.Domain.Interfaces; 
using CustomerCorrespondence.Domain.Entities;
public class CorrespondenceRequestRepository : ICorrespondenceRequestRepository 
{ 
    private readonly DbContext _context;
    public async Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByCorrIdAsync(Guid id)
    {
        return await _context.CorrespondenceRequests
            .Include(r => r.StatusHistories)
            .Where(r => r.CorrespondenceId == id)
            .ToListAsync();
    }

    // Implement other methods...
}
```

---

## 🏛️ Domain-Driven Design (DDD)

This package follows DDD principles:

### Entities
- Have identity (CorrespondenceId, Id)
- Can change over time
- Have lifecycle and behavior
- Examples: `CorrespondenceRequest`, `RoutingConfiguration`

### Value Objects
- Immutable or conceptually immutable
- Defined by their properties, not identity
- Examples: `SequentialProcess`, `RecipientAddress`, `MailDeliveryInfo`

### Repository Interfaces
- Define data access contracts
- Infrastructure implements these contracts
- Domain doesn't depend on infrastructure

### Validation & Security
- `ValidateNoSpecialCharacters()` - Prevents injection attacks
- `TrimAllStringFields()` - Sanitizes input data
- `ProcessRequestHelper` - Centralized security validation

---

## 📦 Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| .NET | 8.0 | Target framework |
| NServiceBus | 8.2.6 | Message contracts (ICommand, IMessage) |

**Note**: This package has minimal dependencies by design - it's the **pure domain model**.

---

## 🔗 Related Packages

| Package | Purpose | Relationship |
|---------|---------|--------------|
| `CustomerCorrespondence.Common` | Shared utilities, Result pattern, constants | Independent utility library |
| `CustomerCorrespondence.Application` | Application services, use cases | Depends on Domain |
| `CustomerCorrespondence.Infrastructure` | EF Core, repositories, external APIs | Implements Domain interfaces |

---

## 🎯 Best Practices

### DO ✅

```csharp
// Keep entities focused on business logic 
public class CorrespondenceRequest 
{ 
    public void Validate() 
    { 
        if (string.IsNullOrWhiteSpace(DeliveryMethodCode)) 
            throw new InvalidOperationException("DeliveryMethodCode required"); 
    }
}

// Define clear contracts 
public interface ICorrespondenceRequestRepository 
{ 
    Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByCorrIdAsync(Guid id); 
}
// Always validate and sanitize user input 
process.Validate(); 
process.ValidateNoSpecialCharacters(); 
process.TrimAllStringFields();
```

### DON'T ❌

```csharp
// Don't add infrastructure concerns to entities 
public class CorrespondenceRequest 
{ 
    public void SaveToDatabase() { } // ❌ Infrastructure concern 
}

// Don't reference infrastructure packages 
using Microsoft.EntityFrameworkCore; // ❌ In Domain layer

// Don't skip security validation 
var process = new SequentialProcess { ... }; 
await endpoint.Send(new BeginRequestHandlerMessage(process)); // ❌ Not validated
```

---

## 🔒 Security Features

### Input Validation
All value objects include `ValidateNoSpecialCharacters()` to prevent:
- SQL Injection
- Command Injection
- XML/JSON Injection

### Forbidden Characters
The following characters are blocked: `< > { } [ ] ; | \ ^ ~ `` `

### PGP Encryption
`CorrespondenceRequest.PolicyNumber` is stored as PGP-encrypted bytes with SHA-256 hash for searches.

---

## 📝 Version History

### 1.0.0 (Current Release)
- ✅ Core domain entities (CorrespondenceRequest, RoutingConfiguration, Solutions)
- ✅ Value objects (SequentialProcess, RecipientAddress, MailDeliveryInfo, CompositionInfo)
- ✅ NServiceBus message contracts (ICommand, IMessage implementations)
- ✅ Domain interfaces for repositories
- ✅ Security validation helpers (ProcessRequestHelper)
- ✅ VentureFusion API integration models
- ✅ Input sanitization and validation methods

---

## 📖 Additional Documentation

## 📞 Support

- **Team:** ECCO Development Team
- **Repo:** [Azure DevOps - ECCO-CommunicationManager](https://dev.azure.com/S9uareRootLLC/CorporateServices/_git/ECCO-CommunicationManager)

---

## 📄 License

Internal use for S9uareRoot LLC

---

**Made with ❤️ by the ECCO Team**