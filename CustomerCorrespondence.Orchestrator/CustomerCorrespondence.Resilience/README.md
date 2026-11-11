# ECCO.CommunicationManager.Resilience

Resilience and fault-tolerance patterns for ECCO Communication Manager using Polly framework.

## Overview

This package provides retry policies and resilience patterns to handle transient failures gracefully:
- **HTTP Retry Policies** - Exponential backoff for HTTP operations with status code handling
- **General Retry Policies** - Configurable retry logic with integrated logging
- **Simple Retry Execution** - Helper method for one-off retry scenarios
- **Polly Integration** - Industry-standard resilience framework for production-grade fault tolerance

## Installation
dotnet add package ECCO.CommunicationManager.Resilience

Or via NuGet Package Manager:
Install-Package ECCO.CommunicationManager.Resilience

## Features

### HTTP Retry Policy with Exponential Backoff

Automatically retry failed HTTP requests with exponential backoff strategy. Handles both HTTP status code failures and `HttpRequestException`.

#### Basic Usage

```csharp
using ECCO.CommunicationManager.Resilience;
// Create an HTTP retry policy (default: 3 retries, 1 second initial delay) var retryPolicy = RetryPolicyHelper.CreateHttpRetryPolicy();
// Use with HttpClient var httpClient = new HttpClient(); var response = await retryPolicy.ExecuteAsync(async () => { return await httpClient.GetAsync("https://api.example.com/data"); });
```

#### Custom Configuration

Configure the retry policy to suit your application's needs.

```csharp
using ECCO.CommunicationManager.Resilience;
using Polly;
using System.Net.Http;

// Create a custom HTTP retry policy
var customRetryPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(
        retryCount: 5, // Number of retries
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)) // Exponential backoff
    );

// Use with HttpClient
var httpClient = new HttpClient();
var response = await customRetryPolicy.ExecuteAsync(async () => {
    return await httpClient.GetAsync("https://api.example.com/data");
};

// Custom retry count and initial delay var retryPolicy = RetryPolicyHelper.CreateHttpRetryPolicy( retryCount: 5,           // Retry up to 5 times initialDelaySeconds: 2   // Start with 2 second delay );
// Delays will be: 2s, 4s, 8s, 16s, 32s (exponential backoff) var response = await retryPolicy.ExecuteAsync(async () => { return await httpClient.PostAsJsonAsync("https://api.example.com/process", data); });

```

### Retry Policy with Logging

Track retry attempts with integrated logging support. Ideal for debugging transient failures and monitoring retry behavior.

#### Usage with ILogger
```csharp
using ECCO.CommunicationManager.Resilience; using Microsoft.Extensions.Logging;
public class ExternalApiService { private readonly ILogger<ExternalApiService> _logger;
public ExternalApiService(ILogger<ExternalApiService> logger)
{
    _logger = logger;
}

public async Task<ApiResponse> CallExternalApiAsync()
{
    // Create retry policy with logging
    var retryPolicy = RetryPolicyHelper.CreateRetryPolicyWithLogging(
        logger: _logger,
        retryCount: 3,
        initialDelaySeconds: 1
    );
    
    return await retryPolicy.ExecuteAsync(async () =>
    {
        // This operation will be retried on failure
        var response = await _httpClient.GetAsync("https://external-api.com/endpoint");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApiResponse>();
    });
}
}
```

**Log Output Example:**

````````

#### Real-World Example: External API Integration

```csharp
using ECCO.CommunicationManager.Resilience;
using Microsoft.Extensions.Logging;

public class WeatherService
{
    private readonly ILogger<WeatherService> _logger;
    private readonly HttpClient _httpClient;

    public WeatherService(ILogger<WeatherService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<WeatherForecast> GetForecastAsync()
    {
        // Define the fallback response in case of failure
        WeatherForecast fallbackResponse = new WeatherForecast
        {
            TemperatureC = 0,
            Summary = "Unable to retrieve weather data"
        };

        // Create a Polly fallback policy
        var fallbackPolicy = Policy<WeatherForecast>
            .Handle<HttpRequestException>()
            .FallbackAsync(fallbackResponse, async (exception, liveRetryCount) =>
            {
                // Log the fallback execution
                _logger.LogWarning($"Fallback executed: {exception.Message}");
                await Task.CompletedTask;
            });

        // Combine fallback policy with retry policy
        var combinedPolicy = fallbackPolicy.WrapAsync(RetryPolicyHelper.CreateRetryPolicyWithLogging(
            logger: _logger,
            retryCount: 3,
            initialDelaySeconds: 1
        ));

        // Execute the HTTP request with the combined policy
        return await combinedPolicy.ExecuteAsync(async () =>
        {
            var response = await _httpClient.GetAsync("https://api.weather.com/forecast/today");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<WeatherForecast>();
        });
    }
}

````````

#### Log Output Example

When retries occur, you'll see log entries like:

warn: ExternalApiService[0] Retry 1 after 1s delay due to: The HTTP response message was not successful. warn: ExternalApiService[0] Retry 2 after 2s delay due to: The HTTP response message was not successful. warn: ExternalApiService[0] Retry 3 after 4s delay due to: The HTTP response message was not successful.


### Database Operations with Logging

Leverage retry policies for database operations with the same ease as HTTP calls. Ensure data consistency and reliability in communication with backend services.

#### Example: Entity Framework Core with SQL Server
```csharp
using ECCO.CommunicationManager.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public class DataService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<DataService> _logger;

    public DataService(ApplicationDbContext dbContext, ILogger<DataService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SaveDataAsync(MyEntity entity)
    {
        // Create a retry policy for database operations
        var retryPolicy = RetryPolicyHelper.CreateRetryPolicyWithLogging(
            logger: _logger,
            retryCount: 5,
            initialDelaySeconds: 1
        );

        await retryPolicy.ExecuteAsync(async () =>
        {
            // Add and save the entity to the database
            await _dbContext.AddAsync(entity);
            await _dbContext.SaveChangesAsync();
        });
    }
}

```

#### Log Output Example

For logging database operations, the output will be similar to HTTP operations, prefixed with the logger's category, e.g., `DataService[0] Retry 1 after 1s delay due to: ...`.


### Simple Retry Execution Helper

For one-off scenarios where you don't need to reuse a policy, use the `ExecuteWithRetryAsync` helper method.

#### Basic Usage

```csharp
using ECCO.CommunicationManager.Resilience;

// Execute a simple retry
var result = await RetryPolicyHelper.ExecuteWithRetryAsync(async () =>
{
    // Operation that may fail
    return await httpClient.GetAsync("https://api.example.com/data");
});
```

### Benefits of Using Polly with ECCO Communication Manager

- **Unified Approach**: Consistent resilience strategies across HTTP and database operations.
- **Customizable Policies**: Tailor retry and circuit breaker policies to your application's needs.
- **Seamless Integration**: Works out of the box with ASP.NET Core's logging and dependency injection.

### Getting Help

- **Documentation**: Visit the [Polly GitHub repository](https://github.com/App-vNext/Polly) for in-depth documentation and advanced usage.
- **Community**: Join the [Polly Gitter channel](https://gitter.im/App-vNext/Polly) to discuss and get help from the community.

## API Reference

### RetryPolicyHelper Class

Static helper class providing factory methods for creating retry policies.

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `CreateHttpRetryPolicy(int retryCount, int initialDelaySeconds)` | `AsyncRetryPolicy<HttpResponseMessage>` | Creates an HTTP retry policy with exponential backoff |
| `CreateRetryPolicyWithLogging(ILogger logger, int retryCount, int initialDelaySeconds)` | `AsyncRetryPolicy` | Creates a retry policy with logging support |
| `ExecuteWithRetryAsync<T>(Func<Task<T>> operation, int retryCount, int initialDelaySeconds)` | `Task<T>` | Executes an operation with retry logic |

#### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `retryCount` | `int` | `3` | Number of retry attempts |
| `initialDelaySeconds` | `int` | `1` | Initial delay in seconds (doubles with each retry) |
| `logger` | `ILogger` | N/A | Logger instance for tracking retries |
| `operation` | `Func<Task<T>>` | N/A | The async operation to execute with retry |

### Exponential Backoff Formula

The retry delay follows an exponential backoff pattern:

- `initialDelaySeconds = 1`: Delays are 1s, 2s, 4s, 8s, 16s...
- `initialDelaySeconds = 2`: Delays are 2s, 4s, 8s, 16s, 32s...
- `initialDelaySeconds = 5`: Delays are 5s, 10s, 20s, 40s, 80s...

### Dependency Injection Setup

```csharp
using CustomerCorrespondence.Resilience; using Microsoft.Extensions.DependencyInjection;
public class Startup { public void ConfigureServices(IServiceCollection services) { // Register HttpClient with retry policy services.AddHttpClient<IVentureFusionService, VentureFusionService>() .AddPolicyHandler((serviceProvider, request) => RetryPolicyHelper.CreateHttpRetryPolicy( retryCount: 3, initialDelaySeconds: 1 ));
    // Register other services
    services.AddScoped<ICorrespondenceService, CorrespondenceService>();
}
}
```

### Service Implementation Pattern

```csharp
public class CorrespondenceService : ICorrespondenceService { private readonly ILogger<CorrespondenceService> _logger; private readonly AsyncRetryPolicy _retryPolicy;
public CorrespondenceService(ILogger<CorrespondenceService> logger)
{
    _logger = logger;
    
    // Initialize retry policy once
    _retryPolicy = RetryPolicyHelper.CreateRetryPolicyWithLogging(
        logger: _logger,
        retryCount: 3,
        initialDelaySeconds: 1
    );
}

public async Task<Result> ProcessAsync(CorrespondenceRequest request)
{
    try
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            // Your business logic here
            await ProcessRequestAsync(request);
            return Result.Success();
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to process correspondence after retries");
        return Result.Failure("Processing failed", "PROCESSING_ERROR");
    }
}
}
```

### Multiple Retry Policies Pattern

```csharp
public class MultiServiceClient { private readonly AsyncRetryPolicy<HttpResponseMessage> _fastRetryPolicy; private readonly AsyncRetryPolicy<HttpResponseMessage> _slowRetryPolicy; private readonly HttpClient _httpClient;
public MultiServiceClient(HttpClient httpClient)
{
    _httpClient = httpClient;
    
    // Fast retry for critical operations
    _fastRetryPolicy = RetryPolicyHelper.CreateHttpRetryPolicy(
        retryCount: 5,
        initialDelaySeconds: 1
    );
    
    // Slower retry for less critical operations
    _slowRetryPolicy = RetryPolicyHelper.CreateHttpRetryPolicy(
        retryCount: 3,
        initialDelaySeconds: 5
    );
}

public async Task<Response> CriticalOperationAsync()
{
    var response = await _fastRetryPolicy.ExecuteAsync(
        async () => await _httpClient.GetAsync("/critical")
    );
    return await response.Content.ReadFromJsonAsync<Response>();
}

public async Task<Response> NonCriticalOperationAsync()
{
    var response = await _slowRetryPolicy.ExecuteAsync(
        async () => await _httpClient.GetAsync("/non-critical")
    );
    return await response.Content.ReadFromJsonAsync<Response>();
}
}
```

## Best Practices

1. **Choose Appropriate Retry Counts**: 
   - Critical operations: 3-5 retries
   - Non-critical operations: 1-3 retries
   - Avoid excessive retries that can cause cascading failures

2. **Set Reasonable Initial Delays**:
   - Fast operations (< 1s): 1-2 seconds initial delay
   - Slow operations (> 5s): 2-5 seconds initial delay
   - Consider the operation's typical duration

3. **Use Logging for Visibility**:
   - Always use `CreateRetryPolicyWithLogging` for production scenarios
   - Monitor retry patterns to identify systemic issues
   - Alert on excessive retries

4. **Handle Final Failures Gracefully**:
   - Always catch exceptions after retries exhaust
   - Convert to Result pattern or appropriate error response
   - Log final failures at ERROR level

5. **Combine with Circuit Breaker** (Future Enhancement):
   - Use circuit breaker for cascading failure prevention
   - Polly supports combining retry with circuit breaker policies

6. **Consider Idempotency**:
   - Ensure operations can be safely retried
   - Use unique identifiers to prevent duplicate processing
   - Design APIs with idempotency in mind

7. **Monitor and Adjust**:
   - Track retry metrics and success rates
   - Adjust retry counts and delays based on actual failure patterns
   - Different services may need different retry strategies

8. **Avoid Retry for Non-Transient Errors**:
   - Don't retry validation errors (400 Bad Request)
   - Don't retry authentication errors (401 Unauthorized)
   - Don't retry not found errors (404 Not Found)
   - Only retry transient failures (5xx, timeouts, network issues)

## Common Scenarios

### Scenario 1: HTTP API Call to External Service

```csharp
var retryPolicy = RetryPolicyHelper.CreateHttpRetryPolicy(retryCount: 3, initialDelaySeconds: 1); var response = await retryPolicy.ExecuteAsync( async () => await _httpClient.GetAsync("https://external-api.com/data") );
```

**When to use:** External API calls that may experience transient network issues or temporary service unavailability.

### Scenario 2: Database Operations with Deadlocks

```csharp
var retryPolicy = RetryPolicyHelper.CreateRetryPolicyWithLogging(_logger, retryCount: 3, initialDelaySeconds: 2); await retryPolicy.ExecuteAsync(async () => await _context.SaveChangesAsync());
```

**When to use:** Database operations that may encounter deadlocks or connection pool exhaustion.

### Scenario 3: Azure Service Bus Message Sending

```csharp
await RetryPolicyHelper.ExecuteWithRetryAsync( async () => await _serviceBusSender.SendMessageAsync(message), retryCount: 5, initialDelaySeconds: 1 );
```

**When to use:** Sending messages to Azure Service Bus where transient faults may occur, such as timeout or quota exceeding.

#### Example: Sending Messages with Retry Policy

```csharp
using ECCO.CommunicationManager.Resilience;
using Microsoft.Azure.ServiceBus;
using System.Text;
using System.Threading.Tasks;

public class QueueService
{
    private readonly IQueueClient _queueClient;
    private readonly ILogger<QueueService> _logger;

    public QueueService(IQueueClient queueClient, ILogger<QueueService> logger)
    {
        _queueClient = queueClient;
        _logger = logger;
    }

    public async Task SendMessageAsync(string messageBody)
    {
        // Create a retry policy for sending messages
        var retryPolicy = RetryPolicyHelper.CreateRetryPolicyWithLogging(
            logger: _logger,
            retryCount: 3,
            initialDelaySeconds: 1
        );

        // Execute the send operation with retry policy
        await retryPolicy.ExecuteAsync(async () =>
        {
            var message = new Message(Encoding.UTF8.GetBytes(messageBody));
            await _queueClient.SendAsync(message);
        });
    }
}

```

#### Log Output Example

Logs will indicate the retry attempts for sending the message, similar to previous examples.


### Scenario 4: File I/O Operations

**When to use:** File operations that may fail due to temporary locks or network-mounted drives.

```csharp
var content = await RetryPolicyHelper.ExecuteWithRetryAsync( async () => await File.ReadAllTextAsync(path), retryCount: 3, initialDelaySeconds: 1 );
```
## Dependencies

- **.NET 8.0** - Target framework
- **Polly 8.x** - Resilience and transient-fault-handling library
- **Microsoft.Extensions.Logging.Abstractions** - For logging support

## Related Packages

- **ECCO.CommunicationManager.Core** - Result pattern and API response models
- **ECCO.CommunicationManager.Exceptions** - Custom exception types
- **ECCO.CommunicationManager.Infrastructure** - Data access and external integrations
- **ECCO.CommunicationManager.Application** - Application services and orchestration

## Version History

### 1.0.0 (Initial Release)
- `CreateHttpRetryPolicy` for HTTP operations with exponential backoff
- `CreateRetryPolicyWithLogging` for operations with logging support
- `ExecuteWithRetryAsync` helper for simple retry scenarios
- Exponential backoff retry strategy
- Full .NET 8 support
- Integration with Polly resilience framework

## License

Copyright © 2024-2026 S9uareRoot LLC. All rights reserved.

Licensed under the MIT License.

## Support

For issues, questions, or contributions, please contact the ECCO Development Team or visit the [project repository](https://dev.azure.com/S9uareRootLLC/CorporateServices/_git/ECCO-CommunicationManager)


