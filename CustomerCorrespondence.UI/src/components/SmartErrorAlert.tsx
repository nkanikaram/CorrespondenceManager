import React, { useState } from 'react';
import { Alert, Button, Spinner } from 'react-bootstrap';

interface SmartErrorAlertProps {
    error: any | null;
    isRetrying?: boolean;
    onRetry?: () => void;
    onDismiss?: () => void;
    variant?: 'danger' | 'warning';
    className?: string;
    style?: React.CSSProperties;
}

/**
 * SmartErrorAlert - Displays errors with automatic retry suggestions
 * Shows:
 * - Error message
 * - Recovery suggestion (if available)
 * - Retry button (if error is retryable)
 * - Dismiss button
 */
export default function SmartErrorAlert({
    error,
    isRetrying = false,
    onRetry,
    onDismiss,
    variant = 'danger',
    className = '',
    style = {},
}: SmartErrorAlertProps) {
    const [dismissed, setDismissed] = useState(false);

    if (!error || dismissed) {
        return null;
    }

    // Extract message and suggestion
    const message = typeof error === 'string' ? error : error.message || 'An error occurred';
    const suggestion = error.suggestion || error.recoveryHint;
    const isRetryable = error.isRetryable !== false;
    const category = error.category || 'unknown';

    // Determine icon based on error category
    const iconMap: Record<string, string> = {
        network: '📡',
        validation: '⚠️',
        server: '🔧',
        unknown: '❌',
    };
    const icon = iconMap[category] || '❌';

    const handleDismiss = () => {
        setDismissed(true);
        onDismiss?.();
    };

    const handleRetry = () => {
        setDismissed(false);
        onRetry?.();
    };

    return (
        <Alert
            variant={variant}
            className={`py-2 px-3 d-flex justify-content-between align-items-start gap-2 ${className}`}
            style={style}
        >
            <div style={{ flex: 1, minWidth: 0 }}>
                {/* Icon + Message */}
                <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px' }}>
                    <span style={{ fontSize: '14px', flexShrink: 0 }}>{icon}</span>
                    <div style={{ minWidth: 0 }}>
                        <strong style={{ fontSize: '13px', display: 'block', wordBreak: 'break-word' }}>
                            {message}
                        </strong>

                        {/* Suggestion / Recovery Info */}
                        {suggestion && (
                            <div
                                style={{
                                    fontSize: '12px',
                                    marginTop: '4px',
                                    opacity: 0.9,
                                    wordBreak: 'break-word',
                                }}
                            >
                                💡 {suggestion}
                            </div>
                        )}
                    </div>
                </div>
            </div>

            {/* Action Buttons */}
            <div style={{ display: 'flex', gap: '6px', flexShrink: 0 }}>
                {isRetryable && onRetry && (
                    <Button
                        variant="outline-dark"
                        size="sm"
                        onClick={handleRetry}
                        disabled={isRetrying}
                        style={{
                            fontSize: '12px',
                            padding: '2px 8px',
                            height: '24px',
                            lineHeight: '1.2',
                        }}
                    >
                        {isRetrying ? (
                            <>
                                <Spinner animation="border" size="sm" style={{ marginRight: '4px' }} />
                                Retrying...
                            </>
                        ) : (
                            '🔄 Retry'
                        )}
                    </Button>
                )}

                <Button
                    variant="outline-dark"
                    size="sm"
                    onClick={handleDismiss}
                    disabled={isRetrying}
                    style={{
                        fontSize: '12px',
                        padding: '2px 8px',
                        height: '24px',
                        lineHeight: '1.2',
                    }}
                >
                    ✕
                </Button>
            </div>
        </Alert>
    );
}
