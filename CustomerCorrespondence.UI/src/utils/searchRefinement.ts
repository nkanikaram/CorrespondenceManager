/**
 * Search Refinement
 * Two things only, both deterministic and immediately useful: catch obvious typos, and
 * suggest a real structured filter when the free-text search implies one.
 *
 * This used to also track search history, "frequent filters," and "related searches" — all
 * removed. They added complexity without a payoff: the history/frequent-filters data was
 * computed and stored but never actually displayed anywhere, and "related searches" suggested
 * free-text words (like "status" or "queue") that don't literally appear in the data, so
 * clicking them just returned confusing, empty-looking results. Fewer moving parts, and the
 * parts that remain actually do something visible.
 */

export type RefinementType = 'typo' | 'filter';

export interface SearchSuggestion {
    id: string;
    type: RefinementType;
    title: string;
    description?: string;
    query?: Record<string, any>; // Updated search params
    icon: string;
    confidence: number; // 0-1
}

export interface SearchRefinementResult {
    originalQuery: Record<string, any>;
    suggestions: SearchSuggestion[];
    typoCorrection?: {
        original: string;
        corrected: string;
        field: string;
    };
}

// Common typos and corrections
const COMMON_TYPOS: Record<string, string[]> = {
    'cliam': ['claim', 'claimed'],
    'clam': ['claim'],
    'polic': ['policy'],
    'ploicy': ['policy'],
    'recipent': ['recipient'],
    'recipiant': ['recipient'],
    'adress': ['address'],
    'adres': ['address'],
    'procesing': ['processing'],
    'proccessing': ['processing'],
    'submited': ['submitted'],
    'completd': ['completed'],
    'faild': ['failed'],
    'pended': ['pending'],
    'delivry': ['delivery'],
    'cusotmer': ['customer'],
    'custmer': ['customer'],
    'requet': ['request'],
    'requuest': ['request'],
};

/**
 * Analyzes query for typos and suggests corrections
 */
export function detectTypos(queryText: string): { field: string; original: string; corrected: string } | null {
    if (!queryText) return null;

    const words = queryText.toLowerCase().split(/\s+/);
    for (const word of words) {
        if (COMMON_TYPOS[word]) {
            return {
                field: 'query',
                original: word,
                corrected: COMMON_TYPOS[word][0],
            };
        }
    }
    return null;
}

/**
 * Suggests structured filters based on keywords in the free-text search — the query object
 * here uses the frontend's SearchParams field names (status, delivery, sortBy: "requestCreationTS"),
 * matching exactly what doSearch() reads when applying a suggestion.
 */
export function suggestFilters(query: Record<string, any>, searchText?: string): SearchSuggestion[] {
    const suggestions: SearchSuggestion[] = [];
    const text = (searchText || '').toLowerCase();

    // Every suggestion below is meant to REPLACE the literal free-text search with a structured
    // filter — so the old freeText value is always cleared first. Leaving it in place would AND
    // "status = Completed" together with "text literally contains 'completed submission'", and
    // since that literal phrase rarely matches anything, the structured filter would silently
    // return zero results even though it "worked."
    const base = { ...query, freeText: undefined };

    if (text.includes('failed') || text.includes('error')) {
        suggestions.push({
            id: 'filter-failed',
            type: 'filter',
            title: 'Show Failed Requests',
            description: 'Filter by Failed status with recent first',
            query: { ...base, status: 'Failed', sortBy: 'requestCreationTS', sortDirection: 'desc' },
            icon: '⚠️',
            confidence: 0.95,
        });
    }

    if (text.includes('processing') || text.includes('pending') || text.includes('progress')) {
        suggestions.push({
            id: 'filter-processing',
            type: 'filter',
            title: 'Show Processing Requests',
            description: 'View requests currently being processed',
            query: { ...base, status: 'Processing', sortBy: 'requestCreationTS', sortDirection: 'desc' },
            icon: '⏳',
            confidence: 0.9,
        });
    }

    // Note: "processed" (past tense = done) means Completed here, NOT Processing (present
    // tense = still ongoing) — those two are opposite states in this domain, so this keyword
    // deliberately does NOT also trigger the Processing suggestion above.
    if (text.includes('completed') || text.includes('delivered') || text.includes('processed')) {
        suggestions.push({
            id: 'filter-completed',
            type: 'filter',
            title: 'Show Completed Requests',
            description: 'View successfully delivered correspondence',
            query: { ...base, status: 'Completed', sortBy: 'requestCreationTS', sortDirection: 'desc' },
            icon: '✅',
            confidence: 0.9,
        });
    }

    if (text.includes('today') || text.includes('today\'s')) {
        const today = new Date().toISOString().split('T')[0];
        suggestions.push({
            id: 'filter-today',
            type: 'filter',
            title: `Requests from Today (${today})`,
            query: { ...base, fromDate: today, toDate: today },
            icon: '📅',
            confidence: 0.95,
        });
    }

    if (text.includes('last 7') || text.includes('past week')) {
        const to = new Date();
        const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1000);
        suggestions.push({
            id: 'filter-week',
            type: 'filter',
            title: 'Requests from Last 7 Days',
            query: { ...base, fromDate: from.toISOString().split('T')[0], toDate: to.toISOString().split('T')[0] },
            icon: '📊',
            confidence: 0.9,
        });
    }

    if (text.includes('email') && !text.includes('delivery')) {
        suggestions.push({
            id: 'filter-email',
            type: 'filter',
            title: 'Email Delivery Only',
            description: 'Show requests sent via email',
            query: { ...base, delivery: 'Email' },
            icon: '📧',
            confidence: 0.85,
        });
    }

    if (text.includes('mail') && !text.includes('email')) {
        suggestions.push({
            id: 'filter-mail',
            type: 'filter',
            title: 'Physical Mail Delivery',
            description: 'Show requests sent via postal mail',
            query: { ...base, delivery: 'Mail' },
            icon: '📮',
            confidence: 0.85,
        });
    }

    return suggestions;
}

/**
 * Main refinement analysis function
 */
export function analyzeSearchQuery(query: Record<string, any>, searchText: string = ''): SearchRefinementResult {
    const typoCorrection = detectTypos(searchText);
    const suggestions = suggestFilters(query, searchText).sort((a, b) => b.confidence - a.confidence);

    return {
        originalQuery: query,
        suggestions,
        typoCorrection: typoCorrection ?? undefined,
    };
}
