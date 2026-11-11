# ECCO.CommunicationManager.Exceptions

Custom exception types for ECCO Communication Manager providing domain-specific error handling.

## Overview

This package contains specialized exception classes for the ECCO Communication Manager system:
- **CorrespondenceException** - Base exception for correspondence-related errors with error code support
- **ValidationException** - Exception for validation failures with field-level error tracking
- **SecretNotFoundException** - Exception for Azure Key Vault secret retrieval failures
- **NotFoundException** - Exception for resource/entity not found scenarios
- **DatabaseException** - Exception for database operation failures
- **ConfigurationException** - Exception for configuration issues
- **CommunicationException** - Exception for external service communication failures
- **EncryptionException** - Exception for encryption/decryption operation failures

## Installation
dotnet add package ECCO.CommunicationManager.Exceptions

Or via NuGet Package Manager:
Install-Package ECCO.CommunicationManager.Exceptions

## Features

### CorrespondenceException

Base exception for all correspondence-related errors in the ECCO system. Includes support for error codes to categorize exceptions.

#### Constructors

| Constructor | Description |
|------------|-------------|
| `CorrespondenceException()` | Creates an empty exception |
| `CorrespondenceException(string message)` | Creates exception with message |
| `CorrespondenceException(string message, Exception innerException)` | Creates exception with message and inner exception |
| `CorrespondenceException(string message, string errorCode)` | Creates exception with message and error code |
| `CorrespondenceException(string message, string errorCode, Exception innerException)` | Creates exception with all parameters |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ErrorCode` | `string?` | Error code for categorizing the exception |

#### Usage

```csharp
using ECCO.CommunicationManager.Exceptions;
// Basic usage 
throw new CorrespondenceException("Failed to process correspondence request");
// With inner exception 
	try 
	{ 
		// ... some operation 
	} 
	catch (Exception ex) 
	{ 
		throw new CorrespondenceException("Correspondence processing failed", ex);		
	}
// In service methods 
public async Task<Result> ProcessCorrespondenceAsync(CorrespondenceRequest request) 
{ 
	if (request == null) 
		throw new CorrespondenceException("Correspondence request cannot be null");
	// ... processing logic
}
```

### ValidationException

Exception thrown when input validation fails. Inherits from `CorrespondenceException` and provides field-level validation error tracking.

#### Constructors

| Constructor | Description |
|------------|-------------|
| `ValidationException(string message)` | Creates exception with message and empty validation errors |
| `ValidationException(string message, Dictionary<string, List<string>> validationErrors)` | Creates exception with message and field-specific validation errors |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ErrorCode` | `string` | Always set to "VALIDATION_ERROR" |
| `ValidationErrors` | `Dictionary<string, List<string>>` | Dictionary of validation errors by field name |

#### Usage

```csharp
using ECCO.CommunicationManager.Exceptions;

// Single validation error 
	if (string.IsNullOrEmpty(request.RecipientEmail)) 
	{ 
		throw new ValidationException("Recipient email is required"); 
	}
// With specific field validation 
public void ValidateRequest(CreateCorrespondenceRequest request) 
{ 
	if (request.TemplateId <= 0) 
		throw new ValidationException("Invalid template ID provided");
	if (string.IsNullOrWhiteSpace(request.RecipientName))
		throw new ValidationException("Recipient name cannot be empty");
}
// Catching and handling 
try 
{ 
	ValidateRequest(request); 
} 
catch (ValidationException ex) 
{ 
	return Result.Failure($"Validation failed: {ex.Message}", "VALIDATION_ERROR"); 
}
```

### SecretNotFoundException

Exception thrown when a required secret cannot be found in Azure Key Vault.

#### Usage

```csharp
throw new SecretNotFoundException("The specified secret was not found in Azure Key Vault.");
```

### NotFoundException

Exception thrown when a requested resource or entity is not found. Used for database queries, API calls, and resource lookups that return no results.

#### Constructors

| Constructor | Description |
|------------|-------------|
| `NotFoundException(string message, string errorCode)` | Creates exception with message and error code |
| `NotFoundException(string message, string errorCode, Exception innerException)` | Creates exception with inner exception |
| `NotFoundException(string message, string errorCode, string resourceType, string identifier)` | Creates exception with resource details |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ResourceType` | `string?` | The type of resource that was not found |
| `Identifier` | `string?` | The identifier used to search for the resource |
| `ErrorCode` | `string` | Error code for categorization |

#### Usage

```csharp
// Basic usage 
throw new NotFoundException("The requested correspondence entity was not found.");
// With error code 
throw new NotFoundException("The specified template was not found.", "TEMPLATE_NOT_FOUND");
// With resource details 
throw new NotFoundException("The user was not found.", "USER_NOT_FOUND", "User", "john.doe@example.com");
```

### DatabaseException

Exception thrown when database operations fail. Used for SQL errors, connection issues, constraint violations, and data access errors.

#### Constructors

| Constructor | Description |
|------------|-------------|
| `DatabaseException(string message)` | Creates exception with message |
| `DatabaseException(string message, string errorCode)` | Creates exception with message and error code |
| `DatabaseException(string message, string errorCode, Exception innerException)` | Creates exception with inner exception |
| `DatabaseException(string message, string errorCode, string operation, string tableName, Exception innerException)` | Creates exception with full details |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `SqlErrorCode` | `int?` | The SQL error code, if available |
| `Operation` | `string?` | The database operation that failed (Insert, Update, Delete, Query) |
| `TableName` | `string?` | The table or entity involved in the operation |
| `ErrorCode` | `string` | Error code for categorization |

#### Usage

```csharp
// Basic usage 
throw new DatabaseException("An error occurred while accessing the database.");

```
With error code 
```csharp
throw new DatabaseException("Unique constraint violated.", "DB_UNIQUE_CONSTRAINT");
```

With full details 
```csharp
throw new DatabaseException("Error updating record.", "DB_UPDATE_ERROR", "Update", "Correspondence", ex);
```

### ConfigurationException

Exception thrown when configuration is missing, invalid, or cannot be loaded. Used for appsettings.json issues, environment variables, and configuration validation.

#### Constructors

| Constructor | Description |
|------------|-------------|
| `ConfigurationException(string message, string errorCode)` | Creates exception with message and error code |
| `ConfigurationException(string message, string errorCode, string configurationKey)` | Creates exception with specific configuration key |
| `ConfigurationException(string message, string errorCode, Exception innerException)` | Creates exception with inner exception |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ConfigurationKey` | `string?` | The configuration key that is missing or invalid |
| `Section` | `string?` | The configuration section involved |
| `ErrorCode` | `string` | Error code for categorization |

#### Usage

```csharp
throw new ConfigurationException("Invalid application configuration detected.");
```

### CommunicationException

Exception thrown when communication with external services fails. Used for HTTP communication, API calls, Service Bus, and other network/messaging operations.

#### Constructors

| Constructor | Description |
|------------|-------------|
| `CommunicationException(string message)` | Creates exception with message |
| `CommunicationException(string message, string errorCode)` | Creates exception with message and error code |
| `CommunicationException(string message, string errorCode, Exception innerException)` | Creates exception with inner exception |
| `CommunicationException(string message, string errorCode, string serviceName, Exception innerException)` | Creates exception with service name |
| `CommunicationException(string message, string errorCode, string serviceName, int httpStatusCode, Exception innerException)` | Creates exception with HTTP status code |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `HttpStatusCode` | `int?` | The HTTP status code associated with the failure |
| `ServiceName` | `string?` | The service or endpoint that failed |
| `ErrorCode` | `string` | Error code for categorization |

#### Usage

```csharp
throw new CommunicationException("Failed to communicate with the email service.");
```

### EncryptionException

Exception thrown when encryption or decryption operations fail. Used for AES encryption, PGP encryption, hashing, and key management errors.

#### Constructors

| Constructor | Description |
|------------|-------------|
| `EncryptionException(string message, string errorCode)` | Creates exception with message and error code |
| `EncryptionException(string message, string errorCode, Exception innerException)` | Creates exception with inner exception |
| `EncryptionException(string message, string errorCode, string encryptionType, string operation, Exception innerException)` | Creates exception with encryption details |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `EncryptionType` | `string?` | The type of encryption operation (AES, PGP, SHA256, etc.) |
| `Operation` | `string?` | The operation being performed (Encrypt, Decrypt, Hash) |
| `ErrorCode` | `string` | Error code for categorization |

#### Usage

```csharp
throw new EncryptionException("Error encrypting the data.");
```

## Best Practices

1. **Use Specific Exceptions**: Choose the most specific exception type for your scenario:
   - `ValidationException` for input validation failures
   - `NotFoundException` for missing resources/entities
   - `DatabaseException` for data access errors
   - `ConfigurationException` for configuration issues
   - `SecretNotFoundException` for Key Vault problems
   - `CommunicationException` for external service failures
   - `EncryptionException` for cryptographic operations
   - `CorrespondenceException` for general business logic errors

2. **Include Error Codes**: Always provide error codes for better error categorization and client handling

3. **Preserve Inner Exceptions**: When catching and re-throwing, include the original exception as inner exception

4. **Add Context Details**: Use specific properties like `ResourceType`, `Identifier`, `Operation`, `TableName`, `ServiceName`, `EncryptionType`, etc.

5. **Log Before Throwing**: Log exception details before throwing for better troubleshootability

6. **Catch Specifically**: Catch specific exception types rather than base `Exception` when possible

7. **Convert to Result Pattern**: In application layer, catch exceptions and convert to `Result` or `BaseResponse<T>` for API responses

8. **Validate Configuration Early**: Throw `ConfigurationException` during startup to fail fast

9. **Include Diagnostic Information**: Populate properties like `SqlErrorCode`, `HttpStatusCode` for easier debugging

10. **Document Error Codes**: Maintain a consistent error code taxonomy across your application

## Error Codes Reference

### Common CorrespondenceException Codes
- `TEMPLATE_NOT_FOUND` - Requested template doesn't exist
- `INVALID_REQUEST` - Request data is invalid
- `PROCESSING_ERROR` - General processing failure

### ValidationException Codes
- `VALIDATION_ERROR` - Automatic error code for all validation exceptions

### NotFoundException Codes
- `CORRESPONDENCE_NOT_FOUND` - Correspondence request not found
- `DOCUMENT_NOT_FOUND` - Document not found
- `TEMPLATE_NOT_FOUND` - Template not found
- `RESOURCE_NOT_FOUND` - Generic resource not found

### DatabaseException Codes
- `DATABASE_SAVE_ERROR` - Failed to save changes
- `DATABASE_INSERT_ERROR` - Insert operation failed
- `DATABASE_UPDATE_ERROR` - Update operation failed
- `DATABASE_DELETE_ERROR` - Delete operation failed
- `DATABASE_CONNECTION_ERROR` - Connection failure
- `SQL_ERROR` - SQL execution error
- `FK_CONSTRAINT_VIOLATION` - Foreign key constraint violation
- `UNIQUE_CONSTRAINT_VIOLATION` - Unique constraint violation
- `DEADLOCK_ERROR` - Database deadlock detected

### ConfigurationException Codes
- `MISSING_CONFIGURATION` - Required configuration key missing
- `INVALID_CONFIGURATION` - Configuration value is invalid
- `MISSING_CONFIGURATION_SECTION` - Configuration section missing
- `CONFIGURATION_BINDING_ERROR` - Failed to bind configuration

### CommunicationException Codes
- `HTTP_REQUEST_FAILED` - HTTP request failed
- `EXTERNAL_API_ERROR` - External API returned error
- `VENTUREFUSION_ERROR` - VentureFusion API communication failed
- `SERVICE_BUS_ERROR` - Service Bus operation failed
- `GRPC_ERROR` - gRPC call failed
- `REQUEST_TIMEOUT` - Request timed out
- `NETWORK_ERROR` - Network connectivity issue

### EncryptionException Codes
- `ENCRYPTION_FAILED` - Encryption operation failed
- `DECRYPTION_FAILED` - Decryption operation failed
- `PGP_ENCRYPTION_FAILED` - PGP encryption failed
- `HASH_FAILED` - Hashing operation failed
- `KEY_RETRIEVAL_FAILED` - Failed to retrieve encryption key
- `INVALID_KEY_FORMAT` - Encryption key format is invalid
- `KEY_EXPIRED` - Encryption key has expired

### SecretNotFoundException Codes
- `SECRET_NOT_FOUND` - Secret doesn't exist in Key Vault (404)
- `SECRET_ACCESS_DENIED` - Insufficient permissions (403)
- `SECRET_THROTTLED` - Rate limit exceeded (429)
- `SECRET_RETRIEVAL_ERROR` - General retrieval failure

## Dependencies

- **.NET 8.0** - Target framework
- **Zero external dependencies** - Completely standalone package

## Related Packages

- **ECCO.CommunicationManager.Core** - Result pattern and API response models
- **ECCO.CommunicationManager.Domain** - Domain entities and business logic
- **ECCO.CommunicationManager.Application** - Application services and orchestration
- **ECCO.CommunicationManager.Infrastructure** - Data access and external integrations

## Version History

### 1.0.1 (Current Release)
- Added `CommunicationException` for external service communication failures
- Added `EncryptionException` for encryption/decryption operation failures
- Added `NotFoundException` for resource/entity not found scenarios
- Added `DatabaseException` for database operation failures
- Added `ConfigurationException` for configuration issues
- Enhanced error code taxonomy with comprehensive categorization
- Additional diagnostic properties for all exception types

### 1.0.0 (Initial Release)
- CorrespondenceException base class with error code support
- ValidationException with field-level error tracking
- SecretNotFoundException for Azure Key Vault failures
- Full .NET 8 support with nullable reference types
- Extracted from Common library for focused exception handling

## License

Copyright © 2024-2026 S9uareRoot LLC. All rights reserved.

Licensed under the MIT License.

## Support

For issues, questions, or contributions, please contact the ECCO Development Team or visit the [project repository](https://dev.azure.com/S9uareRootLLC/CorporateServices/_git/ECCO-CommunicationManager)