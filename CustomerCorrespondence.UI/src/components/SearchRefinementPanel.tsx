import { useState } from 'react';
import { Alert, Badge, Button, Collapse } from 'react-bootstrap';
import type { SearchRefinementResult, SearchSuggestion } from '../utils/searchRefinement';

interface SearchRefinementPanelProps {
    refinementResult: SearchRefinementResult | null;
    onApplySuggestion?: (suggestion: SearchSuggestion) => void;
    onApplyTypoFix?: (corrected: string) => void;
}

/**
 * Displays search refinement suggestions at top of results: a typo fix and/or a couple of
 * one-click structured filters implied by the free-text search.
 */
export default function SearchRefinementPanel({
    refinementResult,
    onApplySuggestion,
    onApplyTypoFix,
}: SearchRefinementPanelProps) {
    const [collapsed, setCollapsed] = useState(false);
    const [dismissedSuggestions, setDismissedSuggestions] = useState<Set<string>>(new Set());

    if (!refinementResult || (refinementResult.suggestions.length === 0 && !refinementResult.typoCorrection)) {
        return null;
    }

    const filterSuggestions = refinementResult.suggestions.filter(s => !dismissedSuggestions.has(s.id));
    const typoFix = refinementResult.typoCorrection;
    const hasContent = typoFix || filterSuggestions.length > 0;

    if (!hasContent) return null;

    const handleDismiss = (id: string) => {
        setDismissedSuggestions(prev => new Set(prev).add(id));
    };

    const handleApply = (suggestion: SearchSuggestion) => {
        onApplySuggestion?.(suggestion);
        handleDismiss(suggestion.id);
    };

    return (
        <div className="mb-3">
            <Alert
                variant="light"
                className="border border-info-subtle rounded-3 py-2 px-3 mb-0"
                style={{ backgroundColor: '#f0f8ff' }}
            >
                <div className="d-flex align-items-center justify-content-between">
                    <div className="d-flex align-items-center gap-2">
                        <span style={{ fontSize: '1.2rem' }}>✨</span>
                        <span className="text-muted small fw-semibold">Search Tips</span>
                        <Badge bg="info" className="ms-2">
                            {(typoFix ? 1 : 0) + filterSuggestions.length} Suggestion{filterSuggestions.length !== 1 ? 's' : ''}
                        </Badge>
                    </div>
                    <Button
                        variant="link"
                        size="sm"
                        onClick={() => setCollapsed(!collapsed)}
                        className="text-decoration-none p-0 text-muted"
                    >
                        {collapsed ? '▼ Show' : '▲ Hide'}
                    </Button>
                </div>

                <Collapse in={!collapsed}>
                    <div className="mt-2 pt-2 border-top border-info-subtle">
                        {typoFix && (
                            <div className={filterSuggestions.length > 0 ? 'mb-3 p-2 bg-white rounded border-start border-4 border-warning' : 'p-2 bg-white rounded border-start border-4 border-warning'}>
                                <div className="d-flex align-items-start justify-content-between gap-2">
                                    <div className="flex-grow-1">
                                        <strong className="d-block mb-1">
                                            🔤 Did you mean: <code className="bg-light px-2 py-1 rounded">{typoFix.corrected}</code>?
                                        </strong>
                                        <small className="text-muted">
                                            We detected a typo: "<code>{typoFix.original}</code>" might be "<code>{typoFix.corrected}</code>"
                                        </small>
                                    </div>
                                    <Button
                                        size="sm"
                                        variant="outline-warning"
                                        onClick={() => {
                                            onApplyTypoFix?.(typoFix.corrected);
                                        }}
                                        className="ms-auto flex-shrink-0"
                                    >
                                        Fix Typo
                                    </Button>
                                </div>
                            </div>
                        )}

                        {filterSuggestions.length > 0 && (
                            <div>
                                <strong className="d-block mb-2 text-muted small">🎯 Try These Filters:</strong>
                                <div className="d-flex flex-wrap gap-2">
                                    {filterSuggestions.slice(0, 3).map(suggestion => (
                                        <div
                                            key={suggestion.id}
                                            className="p-2 bg-white rounded border border-info-subtle"
                                            style={{ flex: '1 1 calc(33% - 8px)', minWidth: '200px' }}
                                        >
                                            <div className="d-flex align-items-start justify-content-between gap-2 mb-2">
                                                <strong className="small">
                                                    {suggestion.icon} {suggestion.title}
                                                </strong>
                                                <Button
                                                    size="sm"
                                                    variant="link"
                                                    className="text-decoration-none p-0 text-muted"
                                                    onClick={() => handleDismiss(suggestion.id)}
                                                    title="Dismiss"
                                                >
                                                    ✕
                                                </Button>
                                            </div>
                                            {suggestion.description && (
                                                <small className="text-muted d-block mb-2">{suggestion.description}</small>
                                            )}
                                            <div className="d-flex gap-2">
                                                <Button
                                                    size="sm"
                                                    variant="outline-info"
                                                    onClick={() => handleApply(suggestion)}
                                                    className="text-nowrap"
                                                >
                                                    Apply Filter
                                                </Button>
                                                <small className="text-muted align-self-center">
                                                    {Math.round(suggestion.confidence * 100)}% match
                                                </small>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            </div>
                        )}
                    </div>
                </Collapse>
            </Alert>
        </div>
    );
}
