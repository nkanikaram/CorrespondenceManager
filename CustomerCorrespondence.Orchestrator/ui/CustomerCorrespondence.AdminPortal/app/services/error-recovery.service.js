(function () {
  'use strict';

  /**
   * Smart error recovery — classifies $http failures into retryable/non-retryable
   * categories with a user-facing suggestion.
   *
   * $http rejections look like { status, data, xhrStatus, ... }: status <= 0 means
   * the request never got a response (network error, timeout or abort).
   */
  angular.module('adminPortal').factory('errorRecovery', errorRecovery);

  function errorRecovery() {
    return {
      classifyError: classifyError,
      messageOf: messageOf
    };

    function serverMessage(data) {
      if (!data || typeof data !== 'object') return typeof data === 'string' && data ? data : null;
      return data.Message || data.message || (typeof data.error === 'string' ? data.error : null) || null;
    }

    /** Best human-readable message for any rejection (classified or raw). */
    function messageOf(error, fallback) {
      if (!error) return fallback || 'Request failed';
      if (typeof error === 'string') return error;
      return serverMessage(error.data) || error.message || fallback || 'Request failed';
    }

    function classifyError(error) {
      error = error || {};
      // Already classified (e.g. by the API service) — don't wrap twice.
      if (error.category && typeof error.isRetryable === 'boolean') return error;

      var status = typeof error.status === 'number' ? error.status : 0;

      // No response at all: network failure, timeout or abort.
      if (status <= 0) {
        if (error.xhrStatus === 'timeout') {
          return {
            isRetryable: true,
            category: 'network',
            message: 'Request timeout — connection took too long',
            suggestion: 'Checking connection and retrying...',
            originalError: error
          };
        }
        return {
          isRetryable: true,
          category: 'network',
          message: error.message || 'Network error — connection failed',
          suggestion: 'Check your internet connection and try again.',
          originalError: error
        };
      }

      var errorMsg = serverMessage(error.data) || error.message || error.statusText || 'An error occurred';

      // Client errors (4xx) — generally not retryable.
      if (status >= 400 && status < 500) {
        var suggestion;
        if (status === 400) suggestion = 'Check the request format and try again.';
        else if (status === 401) suggestion = 'Your session may have expired — please refresh the page.';
        else if (status === 403) suggestion = 'You may not have permission for this action.';
        else if (status === 404) suggestion = 'The requested resource was not found — try a different search.';

        return {
          isRetryable: false,
          category: 'validation',
          message: errorMsg,
          suggestion: suggestion,
          originalError: error
        };
      }

      // Server errors (5xx) — retryable.
      if (status >= 500 && status < 600) {
        return {
          isRetryable: true,
          category: 'server',
          message: errorMsg || ('Server error (' + status + ')'),
          suggestion: 'The server is having trouble — retrying automatically...',
          originalError: error
        };
      }

      return {
        isRetryable: true,
        category: 'unknown',
        message: errorMsg,
        suggestion: 'Retrying the operation...',
        originalError: error
      };
    }
  }
})();
