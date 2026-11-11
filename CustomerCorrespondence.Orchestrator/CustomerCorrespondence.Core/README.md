# ECCO.CommunicationManager.Core

Core shared library for ECCO Communication Manager providing foundational patterns for error handling and API responses.

## Overview

This package provides essential building blocks for ECCO Communication Manager microservices:
- **Result Pattern** - Functional error handling without exceptions
- **API Response Models** - Standardized response structures

## Installation

dotnet add package ECCO.CommunicationManager.Core

Or via NuGet Package Manager:
Install-Package ECCO.CommunicationManager.Core

## Features

### Result Pattern

Functional approach to error handling that avoids exceptions for business logic failures.

#### Basic Usage

```csharp
// Simple success/failure 
public Result ValidateInput(string input) 
{ 
    if (string.IsNullOrEmpty(input)) 
        return Result.Failure("Input cannot be empty", "VALIDATION_ERROR");
    return Result.Success();
}
// With return value 
public Result<Customer> GetCustomer(int id) 
{ 
    var customer = _repository.FindById(id);
    if (customer == null)
        return Result<Customer>.Failure("Customer not found", "NOT_FOUND");

    return Result<Customer>.Success(customer);
}
// Multiple errors 
public Result ValidateCustomer(Customer customer) 
{ 
    var errors = new List<string>();
    if (string.IsNullOrEmpty(customer.Name))
        errors.Add("Name is required");

    if (string.IsNullOrEmpty(customer.Email))
        errors.Add("Email is required");

    return errors.Any() 
        ? Result.Failure(errors, "VALIDATION_ERROR")
        : Result.Success();
}
// Exception handling 
try 
{ 
    var data = await _externalService.GetDataAsync(); 
    return Result<Data>.Success(data); 
} 
catch (Exception ex) 
{ 
    return Result<Data>.Failure(ex, "EXTERNAL_SERVICE_ERROR"); 
}
// Combining results 
var result1 = ValidateEmail(email); 
var result2 = ValidatePhone(phone); 
var combinedResult = Result.Combine(result1, result2);
```

### API Response Models

Standardized response structures for consistent API communication.

#### BaseResponse<T> Usage

```csharp
// Success response 
var customer = await _service.GetCustomerAsync(id); 
return Ok(BaseResponse<Customer>.CreateSuccess(customer));

// Failure response 
return BadRequest(BaseResponse<Customer>.CreateFailure( "Customer not found", $"No customer exists with ID {id}" ));

// Validation failure 
var validationErrors = new Dictionary<string, List<string>> 
    { { "Email", new List<string> { "Email is required", "Email format is invalid" } }, 
    { "Phone", new List<string> { "Phone number is invalid" } } }; 
    return BadRequest(BaseResponse<Customer>.CreateValidationFailure(validationErrors));

// Exception response 
try 
{ 
    var result = await _service.ProcessAsync(request); 
    return Ok(BaseResponse<ProcessResult>.CreateSuccess(result)); 
} 
catch (Exception ex) 
{ 
    return StatusCode(500, BaseResponse<ProcessResult>.CreateFailure( "An error occurred during processing", ex )); 
}
```


#### ErrorResponse Usage
```csharp
// Simple error 
var error = new ErrorResponse { Message = "Resource not found", ErrorCode = "NOT_FOUND", Details = "The requested customer ID does not exist", Timestamp = DateTime.UtcNow };
// With validation errors 
var error = new ErrorResponse { Message = "Validation failed", ErrorCode = "VALIDATION_ERROR", Timestamp = DateTime.UtcNow, ValidationErrors = new Dictionary<string, List<string>> { { "Name", new List<string> { "Name is required" } } } };
```

## API Reference

### Result Class

| Property | Type | Description |
|----------|------|-------------|
| `IsSuccess` | `bool` | Indicates if the operation was successful |
| `IsFailure` | `bool` | Indicates if the operation failed |
| `Error` | `string?` | Single error message |
| `Errors` | `IReadOnlyList<string>` | Collection of error messages |
| `ErrorCode` | `string?` | Error code for categorization |

| Method | Description |
|--------|-------------|
| `Success()` | Creates a successful result |
| `Failure(string error, string? errorCode)` | Creates a failed result with an error |
| `Failure(IEnumerable<string> errors, string? errorCode)` | Creates a failed result with multiple errors |
| `Failure(Exception exception, string? errorCode)` | Creates a failed result from an exception |
| `Combine(params Result[] results)` | Combines multiple results |

### Result<T> Class

Inherits from `Result` and adds:

| Property | Type | Description |
|----------|------|-------------|
| `Value` | `T?` | The returned value if successful |

| Method | Description |
|--------|-------------|
| `Success(T value)` | Creates a successful result with a value |
| `Failure(...)` | Same as Result.Failure methods |

### BaseResponse<T> Class

| Property | Type | Description |
|----------|------|-------------|
| `Success` | `bool` | Indicates if the operation was successful |
| `Data` | `T?` | The data payload |
| `ErrorMessage` | `string?` | Error message if failed |
| `ErrorDetails` | `string?` | Additional error details |
| `ValidationErrors` | `Dictionary<string, List<string>>?` | Validation errors |
| `Timestamp` | `DateTime` | Response timestamp |

| Method | Description |
|--------|-------------|
| `CreateSuccess(T data)` | Creates a successful response |
| `CreateFailure(string errorMessage, string? errorDetails)` | Creates a failure response |
| `CreateValidationFailure(Dictionary<string, List<string>> validationErrors)` | Creates a validation failure response |
| `CreateFailure(string errorMessage, Exception exception)` | Creates a failure response from exception |

### ErrorResponse Class

| Property | Type | Description |
|----------|------|-------------|
| `Message` | `string` | Error message |
| `ErrorCode` | `string?` | Error code |
| `Details` | `string?` | Additional error details |
| `Timestamp` | `DateTime` | Error timestamp |
| `ValidationErrors` | `Dictionary<string, List<string>>?` | Validation errors |

## Best Practices

1. **Use Result Pattern for Business Logic**: Reserve exceptions for truly exceptional cases
2. **Provide Error Codes**: Include error codes for easier categorization and handling
3. **Be Specific with Error Messages**: Help developers understand what went wrong
4. **Combine Validation Results**: Use `Result.Combine()` for multiple validation checks
5. **Consistent API Responses**: Always use `BaseResponse<T>` for API endpoints
6. **Include Timestamps**: Helps with debugging and tracking issues
7. **Validation Errors**: Use the `ValidationErrors` dictionary for field-specific errors

## Dependencies

- **.NET 8.0** - Target framework
- **Zero external dependencies** - Completely standalone package

## Related Packages

- **ECCO.CommunicationManager.Exceptions** - Custom exception types
- **ECCO.CommunicationManager.Resilience** - Retry policies and fault handling
- **ECCO.CommunicationManager.Domain** - Domain entities and business logic
- **ECCO.CommunicationManager.Application** - Application services and orchestration
- **ECCO.CommunicationManager.Infrastructure** - Data access and external integrations

## Version History

### 1.0.0 (Initial Release)
- Result pattern (Result, Result<T>) for functional error handling
- API response models (BaseResponse<T>, ErrorResponse)
- Zero external dependencies
- Full .NET 8 support with nullable reference types
- Extracted from Common library for lightweight core functionality

## 📞 Support

- **Team:** ECCO Development Team
- **Repo:** [Azure DevOps - ECCO-CommunicationManager](https://dev.azure.com/S9uareRootLLC/CorporateServices/_git/ECCO-CommunicationManager)

---

## 📄 License

Internal use for S9uareRoot LLC

---

**Made with ❤️ by the ECCO Team**
