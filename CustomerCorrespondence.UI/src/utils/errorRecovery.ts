/**
 * Smart Error Recovery - Automatic retry logic with exponential backoff
 * Classifies errors and provides recovery suggestions
 */

export interface ErrorClassification {
    isRetryable: boolean;
    category: 'network' | 'validation' | 'server' | 'unknown';
    message: string;
    suggestion?: string;
    originalError?: any;
}

/**
 * Classify an error to determine if it's retryable and provide suggestions
 */
export function classifyError(error: any): ErrorClassification {
    // Network errors (no response at all)
    if (!error.response) {
        return {
            isRetryable: true,
            category: 'network',
            message: error.message || 'Network error — connection failed',
            suggestion: 'Check your internet connection and try again.',
            originalError: error,
        };
    }

    const status = error.response?.status ?? 0;
    const data = error.response?.data ?? {};
    const errorMsg = data?.Message || data?.message || data?.error || error.message || 'An error occurred';

    // Client errors (4xx) - generally not retryable
    if (status >= 400 && status < 500) {
        let suggestion: string | undefined;

        if (status === 400) {
            suggestion = 'Check the request format and try again.';
        } else if (status === 401) {
            suggestion = 'Your session may have expired — please refresh the page.';
        } else if (status === 403) {
            suggestion = 'You may not have permission for this action.';
        } else if (status === 404) {
            suggestion = 'The requested resource was not found — try a different search.';
        }

        return {
            isRetryable: false,
            category: 'validation',
            message: errorMsg,
            suggestion,
            originalError: error,
        };
    }

    // Server errors (5xx) - retryable
    if (status >= 500 && status < 600) {
        return {
            isRetryable: true,
            category: 'server',
            message: errorMsg || `Server error (${status})`,
            suggestion: 'The server is having trouble — retrying automatically...',
            originalError: error,
        };
    }

    // Timeout (0 or no response)
    if (status === 0 || error.code === 'ECONNABORTED') {
        return {
            isRetryable: true,
            category: 'network',
            message: 'Request timeout — connection took too long',
            suggestion: 'Checking connection and retrying...',
            originalError: error,
        };
    }

    // Unknown error
    return {
        isRetryable: true,
        category: 'unknown',
        message: errorMsg,
        suggestion: 'Retrying the operation...',
        originalError: error,
    };
}

/**
 * Execute a function with automatic retry logic and exponential backoff
 * @param fn Function to execute
 * @param maxRetries Maximum number of retries (default: 3)
 * @param initialDelayMs Initial delay in milliseconds (default: 500)
 * @param onRetry Optional callback when a retry occurs
 * @returns Result from the function
 */
export async function retryWithBackoff<T>(
    fn: () => Promise<T>,
    maxRetries = 3,
    initialDelayMs = 500,
    onRetry?: (attempt: number, error: ErrorClassification) => void
): Promise<T> {
    let lastError: any;

    for (let attempt = 0; attempt <= maxRetries; attempt++) {
        try {
            return await fn();
        } catch (err: any) {
            lastError = err;
            const classified = classifyError(err);

            // Don't retry if error is not retryable
            if (!classified.isRetryable || attempt === maxRetries) {
                throw classified;
            }

            // Notify about retry attempt
            if (onRetry) {
                onRetry(attempt + 1, classified);
            }

            // Exponential backoff: 500ms, 1s, 2s, 4s...
            const delayMs = initialDelayMs * Math.pow(2, attempt);

            console.debug(
                `🔄 Retrying (attempt ${attempt + 1}/${maxRetries}) after ${delayMs}ms...`,
                classified.message
            );

            // Wait before retry
            await new Promise(resolve => setTimeout(resolve, delayMs));
        }
    }

    // All retries exhausted
    const classified = classifyError(lastError);
    throw classified;
}

/**
 * Format an error for display to the user
 */
export function formatErrorMessage(error: any): string {
    if (typeof error === 'string') {
        return error;
    }

    if (error.message) {
        return error.message;
    }

    if (error.response?.data?.Message) {
        return error.response.data.Message;
    }

    if (error.response?.data?.message) {
        return error.response.data.message;
    }

    return 'An error occurred — please try again.';
}

/**
 * Get a user-friendly description of what to do based on error type
 */
export function getRecoverySuggestion(error: any): string | undefined {
    if (error.suggestion) {
        return error.suggestion;
    }

    if (error.isRetryable) {
        return 'This operation will be retried automatically.';
    }

    return undefined;
}

/**
 * Hook to track error recovery attempts (useful for telemetry/logging)
 */
export interface ErrorRecoveryMetrics {
    errorType: string;
    retriesAttempted: number;
    successOnRetry: boolean;
    totalTimeMs: number;
}

export function createErrorRecoveryTracker() {
    const metrics: ErrorRecoveryMetrics[] = [];

    return {
        trackRetry: (errorType: string, retryCount: number, success: boolean, durationMs: number) => {
            metrics.push({
                errorType,
                retriesAttempted: retryCount,
                successOnRetry: success,
                totalTimeMs: durationMs,
            });
        },
        getMetrics: () => [...metrics],
        clearMetrics: () => {
            metrics.length = 0;
        },
    };
}
