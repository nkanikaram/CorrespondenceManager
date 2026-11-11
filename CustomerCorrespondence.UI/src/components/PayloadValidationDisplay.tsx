import React, { useState } from 'react';
import { Alert, Badge, Form, OverlayTrigger, Tooltip } from 'react-bootstrap';
import type { PayloadValidationResult, FieldValidation } from '../utils/payloadValidation';

interface PayloadValidationDisplayProps {
    validation: PayloadValidationResult | null;
    onApplySuggestion?: (fieldName: string, autoFix: string) => void;
    jsonPayload?: string; // For debug display
}

/**
 * Displays validation results with field-level indicators, summary badge, and suggestions
 */
export default function PayloadValidationDisplay({ validation, onApplySuggestion, jsonPayload }: PayloadValidationDisplayProps) {
    const [showDebug, setShowDebug] = useState(false);

    if (!validation || validation.summary.totalIssues === 0) {
        return null;
    }

    const { errors, warnings, suggestions, summary } = validation;

    // Create a map of field issues for easy lookup
    const fieldIssuesMap = new Map<string, FieldValidation[]>();
    [...errors, ...warnings, ...suggestions].forEach(issue => {
        if (!fieldIssuesMap.has(issue.fieldName)) {
            fieldIssuesMap.set(issue.fieldName, []);
        }
        fieldIssuesMap.get(issue.fieldName)!.push(issue);
    });

    return (
        <div className="mb-3">
            {/* Summary Badge */}
            <div className="d-flex align-items-center gap-2 mb-3">
                <span className="text-muted">Validation:</span>
                {summary.errorCount > 0 && (
                    <Badge bg="danger" className="cursor-help" title={`${summary.errorCount} blocking error${summary.errorCount !== 1 ? 's' : ''}`}>
                        🔴 {summary.errorCount} Error{summary.errorCount !== 1 ? 's' : ''}
                    </Badge>
                )}
                {summary.warningCount > 0 && (
                    <Badge bg="warning" className="cursor-help" title={`${summary.warningCount} optional issue${summary.warningCount !== 1 ? 's' : ''}`}>
                        🟡 {summary.warningCount} Warning{summary.warningCount !== 1 ? 's' : ''}
                    </Badge>
                )}
                {summary.suggestionCount > 0 && (
                    <Badge bg="info" className="cursor-help" title={`${summary.suggestionCount} enhancement suggestion${summary.suggestionCount !== 1 ? 's' : ''}`}>
                        💡 {summary.suggestionCount} Suggestion{summary.suggestionCount !== 1 ? 's' : ''}
                    </Badge>
                )}
            </div>

            {/* Debug Section - Shows extracted fields and payload structure */}
            {summary.errorCount > 0 && (
                <Alert variant="light" className="mb-3 p-2" style={{ borderLeft: '3px solid #888' }}>
                    <div className="d-flex align-items-center gap-2 mb-2">
                        <small className="text-muted">🔍 Debug Info:</small>
                        <button
                            type="button"
                            className="btn btn-sm btn-link p-0"
                            style={{ fontSize: 12 }}
                            onClick={() => setShowDebug(!showDebug)}
                        >
                            {showDebug ? 'Hide' : 'Show'} Details
                        </button>
                    </div>
                    {showDebug && jsonPayload && (
                        <div style={{ fontSize: 11, color: '#aaa', fontFamily: 'monospace', marginTop: 8 }}>
                            <details open>
                                <summary style={{ cursor: 'pointer', marginBottom: 4 }}>Raw Payload Structure</summary>
                                <pre style={{
                                    backgroundColor: '#2a2a2a',
                                    padding: 8,
                                    borderRadius: 3,
                                    overflow: 'auto',
                                    maxHeight: '200px',
                                    fontSize: 10
                                }}>{JSON.stringify(JSON.parse(jsonPayload), null, 2).substring(0, 500)}...</pre>
                            </details>
                            <p style={{ marginTop: 6, marginBottom: 0 }}>
                                Check browser console (F12) for detailed extraction logs with "validatePayload" prefix.
                            </p>
                        </div>
                    )}
                </Alert>
            )}

            {errors.length > 0 && (
                <Alert variant="danger" className="mb-2">
                    <Alert.Heading className="h6 mb-2">⛔ Blocking Issues</Alert.Heading>
                    {errors.map((issue, idx) => (
                        <div key={idx} className="mb-2">
                            <strong>{issue.message}</strong>
                            {issue.suggestion && <p className="text-muted small mb-1">💡 {issue.suggestion}</p>}
                            {issue.autoFix && onApplySuggestion && (
                                <button
                                    type="button"
                                    className="btn btn-sm btn-outline-danger"
                                    onClick={() => onApplySuggestion(issue.fieldName, issue.autoFix!)}
                                >
                                    Apply Fix: {issue.autoFix}
                                </button>
                            )}
                        </div>
                    ))}
                </Alert>
            )}

            {/* Warnings */}
            {warnings.length > 0 && (
                <Alert variant="warning" className="mb-2">
                    <Alert.Heading className="h6 mb-2">⚠️ Optional Issues</Alert.Heading>
                    {warnings.map((issue, idx) => (
                        <div key={idx} className="mb-2">
                            <strong>{issue.message}</strong>
                            {issue.suggestion && <p className="text-muted small mb-1">💡 {issue.suggestion}</p>}
                            {issue.autoFix && onApplySuggestion && (
                                <button
                                    type="button"
                                    className="btn btn-sm btn-outline-warning"
                                    onClick={() => onApplySuggestion(issue.fieldName, issue.autoFix!)}
                                >
                                    Apply Fix: {issue.autoFix}
                                </button>
                            )}
                        </div>
                    ))}
                </Alert>
            )}

            {/* Suggestions */}
            {suggestions.length > 0 && (
                <Alert variant="info" className="mb-2">
                    <Alert.Heading className="h6 mb-2">💡 Enhancement Suggestions</Alert.Heading>
                    {suggestions.map((issue, idx) => (
                        <div key={idx} className="mb-1">
                            <strong>{issue.message}</strong>
                            {issue.suggestion && <p className="text-muted small mb-0">→ {issue.suggestion}</p>}
                        </div>
                    ))}
                </Alert>
            )}
        </div>
    );
}

/**
 * Field-level validation indicator component
 */
interface FieldValidationIndicatorProps {
    fieldName: string;
    validation: PayloadValidationResult | null;
}

export function FieldValidationIndicator({ fieldName, validation }: FieldValidationIndicatorProps) {
    if (!validation) return null;

    const allIssues = [...validation.errors, ...validation.warnings, ...validation.suggestions];
    const fieldIssues = allIssues.filter(i => i.fieldName === fieldName);

    if (fieldIssues.length === 0) return null;

    const hasError = fieldIssues.some(i => i.level === 'error');
    const hasWarning = fieldIssues.some(i => i.level === 'warning');
    const hasSuggestion = fieldIssues.some(i => i.level === 'info');

    // Determine icon and color based on severity
    let icon = '';
    let bgClass = '';

    if (hasError) {
        icon = '🔴';
        bgClass = 'bg-danger-subtle';
    } else if (hasWarning) {
        icon = '🟡';
        bgClass = 'bg-warning-subtle';
    } else if (hasSuggestion) {
        icon = '💡';
        bgClass = 'bg-info-subtle';
    }

    const content = (
        <Tooltip id={`tooltip-${fieldName}`}>
            <div className="text-start">
                {fieldIssues.map((issue, idx) => (
                    <div key={idx} className="mb-1">
                        <strong>{issue.message}</strong>
                        {issue.suggestion && <p className="text-muted small mb-0">→ {issue.suggestion}</p>}
                    </div>
                ))}
            </div>
        </Tooltip>
    );

    return (
        <OverlayTrigger placement="right" overlay={content}>
            <span className={`badge rounded-pill ${bgClass} cursor-help ms-1`} style={{ fontSize: '0.9rem' }}>
                {icon}
            </span>
        </OverlayTrigger>
    );
}

/**
 * Form group with built-in validation indicator
 */
interface ValidatedFormGroupProps extends React.PropsWithChildren {
    fieldName: string;
    label: string;
    validation: PayloadValidationResult | null;
    required?: boolean;
    className?: string;
}

export function ValidatedFormGroup({ fieldName, label, validation, required, className, children }: ValidatedFormGroupProps) {
    const allIssues = validation ? [...validation.errors, ...validation.warnings, ...validation.suggestions] : [];
    const fieldIssues = allIssues.filter(i => i.fieldName === fieldName);
    const hasError = fieldIssues.some(i => i.level === 'error');

    return (
        <Form.Group className={className}>
            <Form.Label>
                {label}
                {required && <span className="text-danger ms-1">*</span>}
                <FieldValidationIndicator fieldName={fieldName} validation={validation} />
            </Form.Label>
            {children}
            {fieldIssues.length > 0 && (
                <Form.Text className={hasError ? 'text-danger' : 'text-warning'}>
                    {fieldIssues[0].message}
                </Form.Text>
            )}
        </Form.Group>
    );
}
