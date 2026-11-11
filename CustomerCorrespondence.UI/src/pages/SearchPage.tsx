import { useEffect, useRef, useState } from 'react';
import { Form, Button, Spinner, Alert, Toast, ToastContainer, Dropdown, Modal } from 'react-bootstrap';
import { Grid, GridColumn } from '@progress/kendo-react-grid';
import type { GridFilterChangeEvent, GridCustomRowProps } from '@progress/kendo-react-grid';
import { filterBy, orderBy } from '@progress/kendo-data-query';
import type { CompositeFilterDescriptor } from '@progress/kendo-data-query';
import AssistantPopup, { type AssistantPopupHandle } from '../components/AssistantPopup';
import InsertCorrespondenceModal from '../components/InsertCorrespondenceModal';
import AnalyticsPanel from '../components/AnalyticsPanel';
import SmartErrorAlert from '../components/SmartErrorAlert';
import SearchRefinementPanel from '../components/SearchRefinementPanel';
import ThemeToggle from '../components/ThemeToggle';
import ConfirmDialog from '../components/ConfirmDialog';
import { searchCorrespondence, insertCorrespondence, getStatusValues } from '../api/correspondenceApi';
import type { AllAggregates, SearchParams, SendCorrespondenceParams, ExportParams } from '../api/kendoAiAdapter';
import { classifyError } from '../utils/errorRecovery';
import { analyzeSearchQuery, type SearchRefinementResult, type SearchSuggestion } from '../utils/searchRefinement';
import { validatePayload } from '../utils/payloadValidation';

type SearchResultItem = {
    correspondenceId?: string;
    CorrespondenceId?: string;
    docId?: string;
    DocId?: string;
    claimNumber?: string;
    ClaimNumber?: string;
    requestingDomainName?: string;
    RequestingDomainName?: string;
    deliveryMethodCode?: string;
    DeliveryMethodCode?: string;
    requestCreationTS?: string;
    RequestCreationTS?: string;
    latestStatus?: string;
    LatestStatus?: string;
    payloadText?: string;
    PayloadText?: string;
    requestPayload?: string;
    RequestPayload?: string;
    payload?: string;
    Payload?: string;
};

// Single font stack shared by the Request Payload modal's JSON and Text views so both render identically.
const PAYLOAD_FONT = "'Cascadia Code', Consolas, 'Courier New', monospace";

const STATUS_STYLES: Record<string, { rowBg: string }> = {
    completed: { rowBg: 'rgba(78,  201, 148, 0.18)' },
    processing: { rowBg: 'rgba(255, 193,   7, 0.15)' },
    submitted: { rowBg: 'rgba(156, 220, 254, 0.15)' },
    failed: { rowBg: 'rgba(244,  71,  71, 0.18)' },
};

function CreatedCell(props: any) {
    const raw: string = props.dataItem.created ?? '';
    if (!raw) return <td>—</td>;
    const d = new Date(raw);
    if (isNaN(d.getTime())) return <td>{raw}</td>;
    const p = (n: number) => String(n).padStart(2, '0');
    return <td>{`${p(d.getMonth() + 1)}/${p(d.getDate())}/${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`}</td>;
}

function StatusCell(props: any) {
    const status: string = props.dataItem.status ?? '';
    const key = status.toLowerCase();
    return (
        <td>
            <span className={`vsc-badge vsc-badge-${STATUS_STYLES[key] ? key : 'unknown'}`}>
                {status || '—'}
            </span>
        </td>
    );
}

function PayloadCell(props: any) {
    const payload: string = props.dataItem.payload ?? '';
    if (!payload) return <td>—</td>;
    return (
        <td style={{ display: 'flex', gap: 8 }}>
            <button
                onClick={() => props.onViewPayload(payload)}
                style={{
                    background: 'none',
                    border: 'none',
                    color: '#0078d4',
                    cursor: 'pointer',
                    textDecoration: 'underline',
                    padding: 0,
                    fontSize: 'inherit',
                }}
                title="View payload"
            >
                View
            </button>
            {props.onExplainRecord && (
                <button
                    onClick={() => props.onExplainRecord(props.dataItem)}
                    style={{
                        background: 'none',
                        border: 'none',
                        color: '#6f2da8',
                        fontWeight: 600,
                        cursor: 'pointer',
                        textDecoration: 'underline',
                        padding: 0,
                        fontSize: 'inherit',
                    }}
                    title="Ask the AI Assistant to explain this record"
                >
                    Explain
                </button>
            )}
        </td>
    );
}

// Session-local history of aggregate snapshots per distinct filter combination, so repeat
// searches ("show Failed records") can be compared against the last time they were run —
// e.g. "Failed count is up 40% since you last ran this search."
const SNAPSHOT_HISTORY_KEY = 'ecco.ai.searchSnapshots';
type SearchSnapshot = { total: number; failedCount: number; capturedAt: string };

function snapshotSignature(searchParams: Record<string, unknown>): string {
    const relevant = ['freeText', 'correspondenceId', 'docId', 'claimNumber', 'policyNumber', 'customerName', 'customerId', 'latestStatus', 'deliveryMethodCode', 'fromDate', 'toDate'];
    const entries = relevant
        .map(k => [k, searchParams[k]] as const)
        .filter(([, v]) => v !== undefined && v !== null && v !== '');
    if (entries.length === 0) return ''; // unfiltered "show everything" isn't a meaningful comparison point
    return JSON.stringify(entries);
}

function loadSnapshotHistory(): Record<string, SearchSnapshot> {
    try {
        const raw = localStorage.getItem(SNAPSHOT_HISTORY_KEY);
        return raw ? JSON.parse(raw) : {};
    } catch {
        return {};
    }
}

function saveSnapshotHistory(history: Record<string, SearchSnapshot>) {
    try {
        localStorage.setItem(SNAPSHOT_HISTORY_KEY, JSON.stringify(history));
    } catch {
        // ignore storage errors (private browsing, quota, etc.)
    }
}

// Background anomaly scan: periodically checks the last-24h Failed rate across the WHOLE
// dataset (not just whatever the user currently has filtered) against a rolling baseline, so
// risk signals surface even if nobody happens to search for Failed records that day.
// Session-only (runs on a client-side interval while this page is open) — a true always-on
// version would need a backend job, which is out of scope here.
const BACKGROUND_SCAN_KEY = 'ecco.ai.backgroundScanHistory';
const BACKGROUND_SCAN_INTERVAL_MS = 10 * 60 * 1000; // 10 minutes
type BackgroundScanPoint = { failedRate: number; total: number; scannedAt: string };

function loadBackgroundScanHistory(): BackgroundScanPoint[] {
    try {
        const raw = localStorage.getItem(BACKGROUND_SCAN_KEY);
        return raw ? JSON.parse(raw) : [];
    } catch {
        return [];
    }
}

function saveBackgroundScanHistory(points: BackgroundScanPoint[]) {
    try {
        localStorage.setItem(BACKGROUND_SCAN_KEY, JSON.stringify(points.slice(-10)));
    } catch {
        // ignore storage errors (private browsing, quota, etc.)
    }
}

function StatusRow(props: GridCustomRowProps) {
    const status: string = props.dataItem?.status ?? '';
    const bg = STATUS_STYLES[status.toLowerCase()]?.rowBg ?? '';
    return (
        <tr {...props.trProps} style={{ ...props.trProps?.style, backgroundColor: bg }}>
            {props.children}
        </tr>
    );
}

type NormalizedResponse = {
    page: number;
    pageSize: number;
    total: number;
    items: SearchResultItem[];
};

// Extracts a claim number (e.g. "CL-12345") embedded in free-text search input.
function detectClaim(text: string): string | undefined {
    const match = text.match(/\b[A-Z]{2,3}-[0-9]{3,6}\b/i);
    return match ? match[0].toUpperCase() : undefined;
}

export default function SearchPage() {
    const [freeText, setFreeText] = useState('');
    const [results, setResults] = useState<NormalizedResponse | null>(null);
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(25);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<any | null>(null);
    const [isRetrying, setIsRetrying] = useState(false);
    const [sortBy, setSortBy] = useState<string | null>(null);
    const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc');
    const [filter, setFilter] = useState<CompositeFilterDescriptor>({ logic: 'and', filters: [] });
    const [aiAggregates, setAiAggregates] = useState<AllAggregates | null>(null);
    const [aiDataLoaded, setAiDataLoaded] = useState(false);
    const [aiFilterLabel, setAiFilterLabel] = useState<string | null>(null);
    const [activeAiParams, setActiveAiParams] = useState<SearchParams | null>(null);
    const [aiInsights, setAiInsights] = useState<string[]>([]);
    // Findings from the periodic whole-dataset background scan (feature independent of the
    // user's current search) — merged with aiInsights when handed to the AI Assistant.
    const [backgroundAlerts, setBackgroundAlerts] = useState<string[]>([]);
    // Stack of filter states prior to each AI-driven search/clear, so "undo" can pop back one step.
    const aiParamsUndoStackRef = useRef<(SearchParams | null)[]>([]);
    const assistantPopupRef = useRef<AssistantPopupHandle>(null);
    // Bumped on every search; lets a slow, superseded fetchAllForAI call detect it's stale and
    // discard its results instead of overwriting aggregates/insights from a newer search.
    const aiFetchIdRef = useRef(0);
    const [showAnalytics, setShowAnalytics] = useState(false);
    const [showInsert, setShowInsert] = useState(false);
    const [aiGeneratedPayload, setAiGeneratedPayload] = useState<string | null>(null);
    const [aiPayloadContext, setAiPayloadContext] = useState<string | null>(null);
    // The params behind the currently open AI draft, if any — lets "update_correspondence_request" patch it.
    const [aiDraftParams, setAiDraftParams] = useState<SendCorrespondenceParams | null>(null);
    const [toast, setToast] = useState<{ variant: 'success' | 'danger'; message: string } | null>(null);
    const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
    const [exporting, setExporting] = useState(false);
    const [resubmitting, setResubmitting] = useState(false);
    // Pending rows awaiting user confirmation in the Kendo ConfirmDialog before resubmitting.
    const [resubmitConfirm, setResubmitConfirm] = useState<SearchResultItem[] | null>(null);
    // Reserved space alongside the docked AI Assistant — 0 when it's floating/closed, so the
    // page reflows back to its default width the moment it's undocked.
    const [assistantDockWidth, setAssistantDockWidth] = useState(0);
    const [showPayloadModal, setShowPayloadModal] = useState(false);
    const [payloadViewMode, setPayloadViewMode] = useState<'json' | 'text'>('json');
    const [payloadDecrypted, setPayloadDecrypted] = useState<string | null>(null);
    const [isEncryptedPayload, setIsEncryptedPayload] = useState(false);
    const [refinementResult, setRefinementResult] = useState<SearchRefinementResult | null>(null);

    function toggleSelect(id: string) {
        setSelectedIds(prev => {
            const next = new Set(prev);
            if (next.has(id)) next.delete(id); else next.add(id);
            return next;
        });
    }

    function handleViewPayload(payload: string) {
        const { decrypted, isEncrypted } = tryDecrypt(payload);
        setPayloadDecrypted(decrypted);
        setIsEncryptedPayload(isEncrypted);
        setPayloadViewMode('json');
        setShowPayloadModal(true);
    }

    // "Explain" button on a grid row — hands the record's key fields to the AI Assistant as a
    // ready-made question instead of making the user retype/describe the record themselves.
    // For Failed records, also runs the same validation used before submit against the record's
    // stored payload, so the AI can cite a concrete likely cause instead of guessing generically.
    function handleExplainRecord(item: Record<string, unknown>) {
        const summary = [
            item.id ? `Correspondence ID: ${item.id}` : null,
            item.docId ? `Doc ID: ${item.docId}` : null,
            item.claimNumber ? `Claim: ${item.claimNumber}` : null,
            item.customer ? `Customer: ${item.customer}` : null,
            item.delivery ? `Delivery: ${item.delivery}` : null,
            item.created ? `Created: ${item.created}` : null,
            item.status ? `Status: ${item.status}` : null,
        ].filter(Boolean).join(', ');

        let rootCauseHint = '';
        if (String(item.status ?? '').toLowerCase() === 'failed' && item.payload) {
            try {
                const parsed = JSON.parse(item.payload as string);
                const validation = validatePayload(parsed);
                const issues = [...validation.errors, ...validation.warnings].map(v => v.message);
                if (issues.length > 0) {
                    rootCauseHint = ` Automated validation of this record's payload found these issues, which are the likely cause(s) of failure: ${issues.join('; ')}.`;
                }
            } catch {
                // Payload wasn't parseable JSON (e.g. still PGP-encrypted) — explain without a root-cause hint.
            }
        }

        assistantPopupRef.current?.openAndAsk(
            `Explain this correspondence record in plain English: ${summary}.${rootCauseHint} ` +
            `What does its status mean, and is there anything about it that looks unusual?`
        );
    }

    function tryFormatJson(text: string): { formatted: string; isValid: boolean } {
        try {
            const parsed = JSON.parse(text);
            return { formatted: JSON.stringify(parsed, null, 2), isValid: true };
        } catch {
            return { formatted: text, isValid: false };
        }
    }

    function renderPayloadAsTable(payload: string | null) {
        if (!payload) {
            return <div style={{ padding: '12px', color: '#d4d4d4' }}>—</div>;
        }

        try {
            const parsed = JSON.parse(payload);
            const rows: { key: string; value: string }[] = [];

            const flattenObject = (obj: any, prefix = '') => {
                if (obj === null || obj === undefined) {
                    return;
                }
                if (typeof obj !== 'object') {
                    rows.push({ key: prefix || 'value', value: String(obj) });
                    return;
                }
                if (Array.isArray(obj)) {
                    obj.forEach((item, idx) => {
                        const key = prefix ? `${prefix}[${idx}]` : `[${idx}]`;
                        if (typeof item === 'object' && item !== null) {
                            flattenObject(item, key);
                        } else {
                            rows.push({ key, value: String(item) });
                        }
                    });
                    return;
                }
                Object.keys(obj).forEach(k => {
                    const newKey = prefix ? `${prefix}.${k}` : k;
                    const val = obj[k];
                    if (typeof val === 'object' && val !== null && !Array.isArray(val)) {
                        flattenObject(val, newKey);
                    } else if (Array.isArray(val)) {
                        flattenObject(val, newKey);
                    } else {
                        rows.push({ key: newKey, value: String(val ?? '') });
                    }
                });
            };

            flattenObject(parsed);

            return (
                <table
                    style={{
                        width: '100%',
                        borderCollapse: 'collapse',
                        fontSize: '12px',
                        fontFamily: PAYLOAD_FONT,
                        background: '#2d2d2d',
                        color: '#d4d4d4',
                    }}
                >
                    <thead>
                        <tr style={{ borderBottom: '2px solid #555', background: '#1e1e1e' }}>
                            <th
                                style={{
                                    padding: '10px',
                                    textAlign: 'left',
                                    fontWeight: 'bold',
                                    color: '#4ec9b0',
                                    borderRight: '1px solid #555',
                                }}
                            >
                                Key
                            </th>
                            <th
                                style={{
                                    padding: '10px',
                                    textAlign: 'left',
                                    fontWeight: 'bold',
                                    color: '#4ec9b0',
                                }}
                            >
                                Value
                            </th>
                        </tr>
                    </thead>
                    <tbody>
                        {rows.map((row, idx) => (
                            <tr key={idx} style={{ borderBottom: '1px solid #3c3c3c' }}>
                                <td
                                    style={{
                                        padding: '8px 10px',
                                        verticalAlign: 'top',
                                        color: '#ce9178',
                                        borderRight: '1px solid #3c3c3c',
                                        maxWidth: '300px',
                                        wordBreak: 'break-word' as const,
                                        fontWeight: '600',
                                    }}
                                >
                                    {row.key}
                                </td>
                                <td
                                    style={{
                                        padding: '8px 10px',
                                        verticalAlign: 'top',
                                        color: '#d4d4d4',
                                        maxWidth: '500px',
                                        wordBreak: 'break-word' as const,
                                    }}
                                >
                                    {row.value}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            );
        } catch {
            // If JSON parsing fails, show as plain text
            return (
                <pre
                    style={{
                        background: '#2d2d2d',
                        color: '#d4d4d4',
                        padding: '12px',
                        overflow: 'auto',
                        margin: 0,
                        fontSize: '12px',
                        fontFamily: PAYLOAD_FONT,
                        whiteSpace: 'pre-wrap',
                        wordWrap: 'break-word' as const,
                    }}
                >
                    {payload || '—'}
                </pre>
            );
        }
    }

    // Simple base64 decryption (for encrypted payloads)
    // If payload looks encrypted, attempts base64 decode
    function tryDecrypt(payload: string): { decrypted: string; isEncrypted: boolean } {
        if (!payload) {
            return { decrypted: payload, isEncrypted: false };
        }

        // Backend handles AES-GCM decryption via IncludePayload: true
        // Just detect if payload looks encrypted (Base64) or is already decrypted (JSON/PGP)
        if (payload.includes('BEGIN PGP MESSAGE') || payload.startsWith('{') || payload.startsWith('[')) {
            return { decrypted: payload, isEncrypted: false };
        }

        // Check if it looks like Base64-encoded data (encryption)
        if (/^[A-Za-z0-9+/=]+$/.test(payload.trim())) {
            return { decrypted: payload, isEncrypted: true };
        }

        return { decrypted: payload, isEncrypted: false };
    }

    // AES-GCM decryption is handled by backend via IncludePayload: true in search request
    // Backend returns decrypted payload in RequestPayload field

    function renderPayloadJson(payload: string | null) {
        if (!payload) return '—';
        const { formatted, isValid } = tryFormatJson(payload);
        return (
            <pre
                style={{
                    background: '#2d2d2d',
                    color: isValid ? '#ce9178' : '#d4d4d4',
                    padding: '12px',
                    borderRadius: '4px',
                    overflow: 'auto',
                    maxHeight: '400px',
                    fontSize: '12px',
                    fontFamily: 'Courier New, monospace',
                    margin: 0,
                    border: isValid ? '1px solid #3c3c3c' : '1px solid #c41e3a',
                }}
            >
                {formatted}
            </pre>
        );
    }

    function SelectCell({ dataItem }: any) {
        const id = dataItem.id ?? dataItem.correspondenceId ?? dataItem.CorrespondenceId;
        return (
            <td onClick={e => e.stopPropagation()} style={{ paddingLeft: '8px', paddingRight: '8px' }}>
                <input
                    type="checkbox"
                    checked={selectedIds.has(id)}
                    onChange={() => toggleSelect(id)}
                    style={{ cursor: 'pointer', width: '18px', height: '18px' }}
                />
            </td>
        );
    }

    // Returns the freshly-computed aggregate counts once fully loaded (or null when skipped/
    // unavailable) — awaited only by callers that specifically need up-to-date data right after
    // this search (e.g. the AI chaining a chart onto the same turn). The grid itself renders and
    // the loading spinner clears well before this resolves; ordinary callers just don't await it.
    async function doSearch(
        p: number,
        size: number = pageSize,
        aiParams?: SearchParams,
        manualSort?: { by: string; dir: 'asc' | 'desc' },
        skipAnalytics = false
    ): Promise<AggregateCounts | null> {
        let aggregatesPromise: Promise<AggregateCounts | null> = Promise.resolve(null);
        setLoading(true);
        setError(null);
        if (!aiParams) {
            setAiFilterLabel(null);
            setActiveAiParams(null);
            setFilter({ logic: 'and', filters: [] });
        }

        // AI sort takes priority; manual sort (from column click) comes next; grid state last.
        const effectiveSortBy = aiParams?.sortBy ?? manualSort?.by ?? sortBy;
        const effectiveSortDir = aiParams?.sortDirection ?? manualSort?.dir ?? sortDirection;

        // When AI drives the search, use only AI-provided fields (never fall back to stale UI state).
        const searchText = aiParams ? (aiParams.freeText ?? '') : freeText;
        const claim = aiParams?.claimNumber ?? detectClaim(searchText);
        const requestBody: any = {
            freeText: claim ? undefined : (searchText || undefined),
            page: p,
            pageSize: size,
            sortBy: effectiveSortBy ?? undefined,
            sortDirection: effectiveSortBy ? (effectiveSortDir ?? 'desc') : undefined,
            includePayload: true,
        };
        if (claim) requestBody.claimNumber = claim;
        if (aiParams?.policyNumber) requestBody.policyNumber = aiParams.policyNumber;
        if (aiParams?.correspondenceId) requestBody.correspondenceId = aiParams.correspondenceId;
        if (aiParams?.docId) requestBody.docId = aiParams.docId;
        if (aiParams?.customer) requestBody.customerName = aiParams.customer;
        if (aiParams?.customerId) requestBody.customerId = aiParams.customerId;
        if (aiParams?.statuses && aiParams.statuses.length > 0) requestBody.latestStatuses = aiParams.statuses;
        else if (aiParams?.status) requestBody.latestStatus = aiParams.status;
        if (aiParams?.delivery) requestBody.deliveryMethodCode = aiParams.delivery;
        if (aiParams?.fromDate) requestBody.fromDate = aiParams.fromDate;
        if (aiParams?.toDate) requestBody.toDate = aiParams.toDate;

        // helpful debug log to verify what is being sent to the API
        // check browser devtools Network tab to see the actual request/response
        // this helps determine whether the backend expects a different property name or format
        // eslint-disable-next-line no-console
        console.debug('search request body', requestBody);

        try {
            const resp = await searchCorrespondence(requestBody);

            // Debug: log raw response to see what fields are available
            // eslint-disable-next-line no-console
            const firstItem = resp.items?.[0] ?? resp.Items?.[0];
            console.debug('API Response (first item):', firstItem);
            if (firstItem) {
                console.debug('🔍 Available fields in response:', Object.keys(firstItem));
                console.debug('🔍 Full first item JSON:', JSON.stringify(firstItem, null, 2));

                // Look for any field containing 'payload'
                const payloadFields = Object.keys(firstItem).filter(k => k.toLowerCase().includes('payload'));
                if (payloadFields.length > 0) {
                    console.debug('✓ Found payload fields:', payloadFields);
                    payloadFields.forEach(field => {
                        const value = (firstItem as any)[field];
                        const preview = typeof value === 'string' ? value.substring(0, 150) : value;
                        console.debug(`  ${field}:`, preview);
                        if (typeof value === 'string' && value.includes('BEGIN PGP')) {
                            console.warn(`  ⚠️ ${field} contains PGP encrypted data - backend decryption key may not be configured`);
                        }
                    });
                } else {
                    console.warn('❌ No payload fields found in response');
                }
            }

            // normalize server response (camelCase or PascalCase)
            const rawTotal = resp.total ?? resp.Total ?? 0;
            const normalized: NormalizedResponse = {
                page: resp.page ?? resp.Page ?? 1,
                pageSize: resp.pageSize ?? resp.PageSize ?? pageSize,
                // For top-N queries cap total at the requested limit so the pager
                // shows "1–N of N" instead of "1–N of <all records>".
                total: aiParams?.pageSize ? Math.min(rawTotal, aiParams.pageSize) : rawTotal,
                items: resp.items ?? resp.Items ?? []
            };

            // also prepare grid-friendly rows with normalized fields
            const gridItems = (normalized.items || []).map(it => ({
                ...it,
                id: it.correspondenceId ?? it.CorrespondenceId,
                docId: it.docId ?? it.DocId,
                claimNumber: it.claimNumber ?? it.ClaimNumber,
                customer: it.requestingDomainName ?? it.RequestingDomainName,
                delivery: it.deliveryMethodCode ?? it.DeliveryMethodCode,
                created: it.requestCreationTS ?? it.RequestCreationTS,
                status: it.latestStatus ?? it.LatestStatus,
                payload: it.RequestPayload ?? it.requestPayload ?? it.payloadText ?? it.PayloadText ?? it.payload ?? it.Payload ?? '',
            }));

            setResults({ ...normalized, items: gridItems });
            setPage(normalized.page);
            setSelectedIds(new Set());

            // Analyze search for refinement suggestions
            const searchText = aiParams ? (aiParams.freeText ?? '') : freeText;
            const refinement = analyzeSearchQuery(requestBody, searchText);
            setRefinementResult(refinement);

            if (!skipAnalytics) {
                setAiAggregates(null);
                setAiDataLoaded(false);
                setAiInsights([]);
                const fetchId = ++aiFetchIdRef.current;
                aggregatesPromise = fetchAllForAI(requestBody, normalized.total, fetchId);
            }
        } catch (err: any) {
            // Smart error classification and recovery suggestions
            console.error('search error', err);
            const classified = classifyError(err);
            setError(classified);
            setResults(null);
        } finally {
            setLoading(false);
        }
        return aggregatesPromise;
    }

    // Retry handler for search errors with exponential backoff
    async function handleSearchRetry() {
        if (!error || !error.isRetryable) return;

        setIsRetrying(true);
        try {
            // Retry the last search (page, pageSize, and other params are in state)
            await doSearch(page, pageSize, activeAiParams ?? undefined);
        } finally {
            setIsRetrying(false);
        }
    }

    // Load all correspondence requests on initial page load
    useEffect(() => {
        doSearch(1, pageSize);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    // The real, authoritative status values — given to the AI assistant so it can validate a
    // status name (or suggest the closest match on a zero-result search) instead of guessing.
    const [statusValues, setStatusValues] = useState<string[]>([]);
    useEffect(() => {
        getStatusValues().then(setStatusValues).catch(() => setStatusValues([]));
    }, []);

    // Background anomaly scan — independent of whatever the user has searched for, so a spike
    // in Failed records surfaces even if nobody happens to search for Failed that day.
    useEffect(() => {
        let cancelled = false;

        async function scan() {
            try {
                const since = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
                const resp = await searchCorrespondence({ fromDate: since, page: 1, pageSize: 1000 });
                if (cancelled) return;
                const items: any[] = resp.items ?? resp.Items ?? [];
                const total = resp.total ?? resp.Total ?? items.length;
                if (total === 0) return;

                const failedCount = items.filter(it => String(it.latestStatus ?? it.LatestStatus ?? '').toLowerCase() === 'failed').length;
                const failedRate = failedCount / Math.max(items.length, 1);

                const history = loadBackgroundScanHistory();
                if (history.length >= 2) {
                    const baseline = history.reduce((sum, p) => sum + p.failedRate, 0) / history.length;
                    if (baseline > 0 && failedRate >= baseline * 1.5 && failedRate - baseline >= 0.1) {
                        const pct = Math.round(failedRate * 100);
                        const basePct = Math.round(baseline * 100);
                        const alert = `🚨 Failed rate across all recent records is ${pct}% — well above the ${basePct}% baseline from earlier scans today.`;
                        setBackgroundAlerts(prev => (prev.includes(alert) ? prev : [alert, ...prev].slice(0, 3)));
                        try {
                            if ('Notification' in window && Notification.permission === 'granted') {
                                new Notification('ECCO Correspondence Manager', { body: alert });
                            }
                        } catch {
                            // best-effort only
                        }
                    }
                }

                saveBackgroundScanHistory([...history, { failedRate, total, scannedAt: new Date().toISOString() }]);
            } catch {
                // Best-effort background check — never surface this as a user-facing error.
            }
        }

        const firstRun = window.setTimeout(scan, 15_000); // let the initial page load settle first
        const interval = window.setInterval(scan, BACKGROUND_SCAN_INTERVAL_MS);
        return () => {
            cancelled = true;
            window.clearTimeout(firstRun);
            window.clearInterval(interval);
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    // Add select-all checkbox to grid header
    useEffect(() => {
        const headerRow = document.querySelector('.k-grid-header th') as HTMLElement | null;
        if (!headerRow) return;

        let checkbox = headerRow.querySelector('input[type="checkbox"]') as HTMLInputElement | null;
        if (!checkbox) {
            checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.style.cursor = 'pointer';
            checkbox.style.width = '18px';
            checkbox.style.height = '18px';
            checkbox.title = 'Select all items on this page';
            headerRow.innerHTML = '';
            headerRow.style.textAlign = 'center';
            headerRow.style.paddingLeft = '8px';
            headerRow.style.paddingRight = '8px';
            headerRow.appendChild(checkbox);
        }

        // Re-evaluate against the current page's items every time (not just on first mount)
        const pageIds = results ? results.items.map(item => ((item as any).id ?? item.correspondenceId ?? item.CorrespondenceId) as string) : [];
        checkbox.checked = pageIds.length > 0 && pageIds.every(id => selectedIds.has(id));

        // Rebind (not addEventListener) so the handler always closes over the current page's data
        checkbox.onchange = () => {
            setSelectedIds(prev => {
                const next = new Set(prev);
                if (checkbox!.checked) {
                    pageIds.forEach(id => next.add(id));
                } else {
                    pageIds.forEach(id => next.delete(id));
                }
                return next;
            });
        };
    }, [results, selectedIds]);

    // Builds the correspondence payload JSON + human-readable context from AI-extracted params.
    // Shared by the "new draft" and "patch the open draft" flows so they stay in sync.
    function buildCorrespondencePayload(params: SendCorrespondenceParams, extraContextLines: string[] = []) {
        const payload = {
            Process: {
                DocId: '',
                RequestingSystemName: params.requestingSystemName ?? '',
                ECCOTrackingClaimNumber: params.claimNumber ?? '',
                ECCOTrackingCustomerId: params.customerId ?? '',
                ECCOTrackingPolicyNumber: params.policyNumber ?? '',
                DeliveryMethod: params.deliveryMethod ?? 'Mail',
                DocumentDescription: params.documentDescription ?? '',
                MailDeliveryInfo: {
                    RecipientFullName: params.recipientFullName ?? '',
                    RecipientFirstName: params.recipientFirstName ?? '',
                    RecipientLastName: params.recipientLastName ?? '',
                    RecipientAddress: {
                        RecipientAddressLine1: params.addressLine1 ?? '',
                        RecipientAddressLine2: null,
                        RecipientAddressLine3: null,
                        RecipientAddressCity: params.city ?? '',
                        RecipientAddressST: params.state ?? '',
                        RecipientAddressZIP: params.zip ?? '',
                        RecipientCountry: params.country ?? 'US',
                    },
                    NonUSAddressFlag: false,
                    BadAddressFlag: false,
                },
                CompositionInfo: {
                    VersionCode: params.versionCode ?? '',
                    CompositionSchemaId: '',
                    SolutionsId: params.solutionsId ?? '',
                },
                AdditionalData: {},
            },
        };

        // Build human-readable context showing what AI extracted
        const context = [
            ...extraContextLines,
            params.documentDescription && `📋 Document: ${params.documentDescription}`,
            params.recipientFullName && `👤 Recipient: ${params.recipientFullName}`,
            params.claimNumber && `🎫 Claim: ${params.claimNumber}`,
            params.policyNumber && `📄 Policy: ${params.policyNumber}`,
            (params.addressLine1 || params.city || params.state) && `📍 Address: ${[params.addressLine1, params.city, params.state, params.zip].filter(Boolean).join(', ')}`,
            params.deliveryMethod && `📮 Delivery: ${params.deliveryMethod}`,
        ]
            .filter(Boolean)
            .join('\n');

        // Validate before opening the form — surfaces missing/malformed fields immediately
        // instead of letting the user find out only after they hit submit.
        const validation = validatePayload(payload);
        const issueLines = [
            ...validation.errors.map(v => `❌ ${v.message}`),
            ...validation.warnings.map(v => `⚠️ ${v.message}`),
        ];
        const contextWithValidation = issueLines.length > 0
            ? `${context || 'AI generated correspondence request'}\n\n${issueLines.join('\n')}`
            : (context || 'AI generated correspondence request');

        return { payload, contextWithValidation, validation };
    }

    // Checks for a likely duplicate — another request for the same claim/policy submitted
    // recently — so the AI can warn the user before they accidentally send a second one.
    async function checkForDuplicate(params: SendCorrespondenceParams): Promise<string | null> {
        if (!params.claimNumber && !params.policyNumber) return null;
        try {
            const sevenDaysAgo = new Date(Date.now() - 7 * 86_400_000).toISOString().slice(0, 10);
            const resp = await searchCorrespondence({
                claimNumber: params.claimNumber || undefined,
                policyNumber: params.claimNumber ? undefined : params.policyNumber || undefined,
                fromDate: sevenDaysAgo,
                page: 1,
                pageSize: 1,
            });
            const total = resp.total ?? resp.Total ?? 0;
            if (total > 0) {
                const target = params.claimNumber ? `claim ${params.claimNumber}` : `policy ${params.policyNumber}`;
                return `⚠️ Possible duplicate: ${total} existing request${total === 1 ? '' : 's'} for ${target} already submitted in the last 7 days.`;
            }
        } catch {
            // Best-effort check — never block sending because the duplicate lookup itself failed.
        }
        return null;
    }

    async function handleAiSendCorrespondence(params: SendCorrespondenceParams): Promise<string> {
        const dupWarning = await checkForDuplicate(params);
        const { payload, contextWithValidation, validation } = buildCorrespondencePayload(params, dupWarning ? [dupWarning] : []);

        // Open modal with AI-generated payload for review
        setAiDraftParams(params);
        setAiGeneratedPayload(JSON.stringify(payload, null, 2));
        setAiPayloadContext(contextWithValidation);
        setShowInsert(true);

        const dupNote = dupWarning ? ` ${dupWarning}` : '';
        if (validation.errors.length > 0) {
            return `Correspondence payload generated, but ${validation.errors.length} field${validation.errors.length === 1 ? '' : 's'} need${validation.errors.length === 1 ? 's' : ''} attention before you can submit — see the highlighted issues in the form.${dupNote}`;
        }
        return `Correspondence payload generated. Review and modify the details in the form, then submit.${dupNote}`;
    }

    // Patches the currently open AI-generated draft with just the fields the user corrected,
    // instead of making them re-describe the whole request from scratch.
    async function handleAiUpdateCorrespondence(partial: Partial<SendCorrespondenceParams>): Promise<string> {
        const merged: SendCorrespondenceParams = { ...(aiDraftParams ?? {}), ...partial };
        const { payload, contextWithValidation, validation } = buildCorrespondencePayload(merged);

        setAiDraftParams(merged);
        setAiGeneratedPayload(JSON.stringify(payload, null, 2));
        setAiPayloadContext(contextWithValidation);
        setShowInsert(true);

        const changed = Object.keys(partial).join(', ') || 'the draft';
        if (validation.errors.length > 0) {
            return `Updated ${changed}. ${validation.errors.length} field${validation.errors.length === 1 ? '' : 's'} still need${validation.errors.length === 1 ? 's' : ''} attention before you can submit.`;
        }
        return `Updated ${changed}. Review the changes in the form, then submit.`;
    }

    // Applies an AI-driven filter state (or null to clear). Shared by search, clear-filters, and undo
    // so all three push/pop the same undo stack instead of duplicating the apply logic three times.
    async function applyAiParams(params: SearchParams | null, opts: { pushUndo: boolean } = { pushUndo: true }): Promise<AggregateCounts | null> {
        if (opts.pushUndo) {
            // Cap at 10 steps back — plenty for "undo" while bounding memory.
            aiParamsUndoStackRef.current = [...aiParamsUndoStackRef.current.slice(-9), activeAiParams];
        }

        if (params === null) {
            setFreeText('');
            setError(null);
            setAiAggregates(null);
            setAiFilterLabel(null);
            setActiveAiParams(null);
            setFilter({ logic: 'and', filters: [] });
            return doSearch(1, pageSize, {});
        }

        // Only put text-searchable values in the search box
        const display = params.claimNumber ?? params.policyNumber ?? params.correspondenceId ?? params.docId ?? params.freeText ?? params.customer ?? '';
        setFreeText(display);

        // Build human-readable AI filter badge
        const parts: string[] = [];
        if (params.pageSize) parts.push(`Top ${params.pageSize}`);
        if (params.statuses && params.statuses.length > 0) parts.push(`Status in [${params.statuses.join(', ')}]`);
        else if (params.status) parts.push(`Status = "${params.status}"`);
        if (params.claimNumber) parts.push(`Claim = "${params.claimNumber}"`);
        if (params.policyNumber) parts.push(`Policy = "${params.policyNumber}"`);
        if (params.correspondenceId) parts.push(`CorrespondenceId = "${params.correspondenceId}"`);
        if (params.docId) parts.push(`DocId = "${params.docId}"`);
        if (params.customer) parts.push(`Customer = "${params.customer}"`);
        if (params.customerId) parts.push(`CustomerId = "${params.customerId}"`);
        if (params.delivery) parts.push(`Delivery = "${params.delivery}"`);
        if (params.fromDate) parts.push(`From ${params.fromDate}`);
        if (params.toDate) parts.push(`To ${params.toDate}`);
        if (params.freeText) parts.push(`"${params.freeText}"`);
        if (params.sortBy) parts.push(`Sort: ${params.sortBy} ${params.sortDirection ?? 'desc'}`);
        setAiFilterLabel(parts.length ? parts.join(' | ') : null);
        setActiveAiParams(params);

        // Use AI-specified page size for "top N" queries; update pager to match
        const effectiveSize = params.pageSize ?? pageSize;
        if (params.pageSize) setPageSize(params.pageSize);

        // Sync grid sort state so the column header shows the sort arrow
        if (params.sortBy) {
            setSortBy(params.sortBy);
            setSortDirection(params.sortDirection ?? 'desc');
        }

        const searchPromise = doSearch(1, effectiveSize, params);

        if (params.statuses && params.statuses.length > 0) {
            setFilter({ logic: 'or', filters: params.statuses.map(s => ({ field: 'status', operator: 'eq', value: s })) });
        } else if (params.status) {
            setFilter({ logic: 'and', filters: [{ field: 'status', operator: 'eq', value: params.status }] });
        } else {
            setFilter({ logic: 'and', filters: [] });
        }

        return searchPromise;
    }

    function handleAiClearFilters() {
        applyAiParams(null);
    }

    async function handleAiSearch(params: SearchParams): Promise<void> {
        await applyAiParams(params);
    }

    function handleAiUndo() {
        const stack = aiParamsUndoStackRef.current;
        if (stack.length === 0) {
            setToast({ variant: 'danger', message: 'Nothing to undo' });
            return;
        }
        const previous = stack[stack.length - 1];
        aiParamsUndoStackRef.current = stack.slice(0, -1);
        applyAiParams(previous, { pushUndo: false });
    }

    // Deterministic, client-side observations about the loaded dataset — no AI call needed.
    // Surfaced proactively in the assistant panel so users see risk signals without having to ask.
    // Rounds to a whole-percent share of `part`/`whole`, clamped to [0, 100] so a mismatched or
    // stale count can never render as something absurd like "5070%".
    function shareOf(part: number, whole: number): number {
        if (whole <= 0) return 0;
        return Math.max(0, Math.min(100, Math.round((part / whole) * 100)));
    }

    function buildAiInsights(
        totalCount: number,
        statusCounts: Record<string, number>,
        customerCounts: Record<string, number>,
        deliveryCounts: Record<string, number>,
        ageBuckets: { bucket: string; count: number }[],
    ): string[] {
        const insights: string[] = [];
        if (totalCount === 0) return insights;

        const oldProcessing = ageBuckets.find(b => b.bucket === '>7 Days')?.count ?? 0;
        if (oldProcessing > 0) {
            insights.push(`⚠️ ${oldProcessing} record${oldProcessing === 1 ? ' has' : 's have'} been Processing for over 7 days — possible SLA risk.`);
        }

        const failed = statusCounts['Failed'] ?? 0;
        const failedPct = shareOf(failed, totalCount);
        if (failed > 0 && failedPct >= 10) {
            insights.push(`🔻 ${failed} of ${totalCount} records are Failed (${failedPct}%) — higher than typical.`);
        }

        const customerEntries = Object.entries(customerCounts);
        if (customerEntries.length > 1) {
            const [topCustomer, topCount] = customerEntries.sort((a, b) => b[1] - a[1])[0];
            const share = shareOf(topCount, totalCount);
            if (share >= 50) {
                insights.push(`📊 "${topCustomer}" is ${topCount} of ${totalCount} loaded records (${share}%).`);
            }
        }

        const deliveryEntries = Object.entries(deliveryCounts);
        if (deliveryEntries.length > 1) {
            const [topMethod, topCount] = deliveryEntries.sort((a, b) => b[1] - a[1])[0];
            const share = shareOf(topCount, totalCount);
            if (share >= 80) {
                insights.push(`📮 ${topMethod} is used for ${topCount} of ${totalCount} deliveries (${share}%) — other methods are barely used.`);
            }
        }

        return insights.slice(0, 4);
    }

    type AggregateCounts = { statusCounts: Record<string, number>; customerCounts: Record<string, number>; deliveryCounts: Record<string, number> };

    // Returns the computed counts (in addition to setting state) so a caller that specifically
    // needs the up-to-date breakdown right away — e.g. the AI chaining a chart onto this same
    // search — can await it instead of reading possibly-stale state/props.
    async function fetchAllForAI(searchParams: any, total: number, fetchId: number): Promise<AggregateCounts | null> {
        const isStale = () => fetchId !== aiFetchIdRef.current;

        if (total === 0) { setAiInsights([]); setAiDataLoaded(true); return null; }

        // The data has inconsistent casing for delivery method ("Mail" vs "MAIL") — normalize so
        // charts/aggregates group them as one category instead of splitting into near-duplicate rows.
        const normalizeDelivery = (raw: string) =>
            raw.length > 0 ? raw[0].toUpperCase() + raw.slice(1).toLowerCase() : raw;

        const normalize = (it: any) => ({
            status: it.latestStatus ?? it.LatestStatus ?? 'Unknown',
            customer: it.requestingDomainName ?? it.RequestingDomainName ?? 'Unknown',
            delivery: normalizeDelivery(it.deliveryMethodCode ?? it.DeliveryMethodCode ?? 'Unknown'),
            created: it.requestCreationTS ?? it.RequestCreationTS ?? null as string | null,
        });

        try {
            // Strip sort — order doesn't affect aggregates and avoids backend sort overhead.
            const { sortBy: _sb, sortDirection: _sd, ...baseParams } = searchParams;

            // Page 1 reveals the backend's actual page-size cap.
            const firstResp = await searchCorrespondence({ ...baseParams, page: 1, pageSize: 2000 });
            const firstBatch = (firstResp.items ?? firstResp.Items ?? []).map(normalize);

            const batchSize = Math.max(firstBatch.length, 1);
            const totalPages = Math.ceil(total / batchSize);
            const allItems = [...firstBatch];

            // Fetch remaining pages in parallel groups of 10 to stay within API concurrency limits.
            const PARALLEL = 10;
            for (let p = 2; p <= totalPages; p += PARALLEL) {
                const pageNums = Array.from(
                    { length: Math.min(PARALLEL, totalPages - p + 1) },
                    (_, i) => p + i,
                );
                const responses = await Promise.all(
                    pageNums.map(page => searchCorrespondence({ ...baseParams, page, pageSize: batchSize })),
                );
                for (const resp of responses) {
                    (resp.items ?? resp.Items ?? []).forEach((it: any) => allItems.push(normalize(it)));
                }
            }

            const tally = (field: 'status' | 'customer' | 'delivery') =>
                allItems.reduce((acc: Record<string, number>, item) => {
                    const k = String(item[field]);
                    acc[k] = (acc[k] ?? 0) + 1;
                    return acc;
                }, {});

            // For dimensions already constrained by the filter the count is exactly `total`
            // (every fetched record matches the filter value by definition).
            const statusCounts = baseParams.latestStatus ? { [baseParams.latestStatus]: total } : tally('status');
            const customerCounts = baseParams.customerName ? { [baseParams.customerName]: total } : tally('customer');
            const deliveryCounts = baseParams.deliveryMethodCode ? { [normalizeDelivery(baseParams.deliveryMethodCode)]: total } : tally('delivery');

            // Delivery × Status cross-tab (always from sample — filter-known optimisation doesn't apply here)
            const deliveryStatusCounts: Record<string, Record<string, number>> = {};
            allItems.forEach(it => {
                if (!deliveryStatusCounts[it.delivery]) deliveryStatusCounts[it.delivery] = {};
                deliveryStatusCounts[it.delivery][it.status] = (deliveryStatusCounts[it.delivery][it.status] ?? 0) + 1;
            });

            // Volume by day
            const dateGroups: Record<string, number> = {};
            allItems.forEach(it => {
                if (it.created) {
                    const date = new Date(it.created).toISOString().split('T')[0];
                    dateGroups[date] = (dateGroups[date] ?? 0) + 1;
                }
            });
            const volumeByDay = Object.entries(dateGroups)
                .map(([date, count]) => ({ date, count }))
                .sort((a, b) => a.date.localeCompare(b.date));

            // Processing age buckets — SLA risk proxy
            const now = Date.now();
            const ageBuckets: Record<string, number> = { 'Today': 0, '1–3 Days': 0, '3–7 Days': 0, '>7 Days': 0 };
            allItems.forEach(it => {
                if (it.status.toLowerCase() === 'processing' && it.created) {
                    const ageDays = (now - new Date(it.created).getTime()) / 86_400_000;
                    if (ageDays < 1) ageBuckets['Today']++;
                    else if (ageDays < 3) ageBuckets['1–3 Days']++;
                    else if (ageDays < 7) ageBuckets['3–7 Days']++;
                    else ageBuckets['>7 Days']++;
                }
            });
            const processingAgeBuckets = Object.entries(ageBuckets).map(([bucket, count]) => ({ bucket, count }));

            // A newer search may have started (and reset its own aiFetchIdRef) while these requests
            // were in flight — discard this result rather than clobbering the newer search's state.
            if (isStale()) return null;

            setAiAggregates({
                total,
                statusCounts,
                customerCounts,
                deliveryCounts,
                deliveryStatusCounts,
                volumeByDay,
                processingAgeBuckets,
                recordsFetched: allItems.length,
            });
            const insights = buildAiInsights(total, statusCounts, customerCounts, deliveryCounts, processingAgeBuckets);

            // Compare against the last time this exact filter combination was searched, if ever.
            const sig = snapshotSignature(searchParams);
            if (sig) {
                const snapshots = loadSnapshotHistory();
                const previous = snapshots[sig];
                const failedCount = statusCounts['Failed'] ?? 0;
                if (previous) {
                    const totalDelta = total - previous.total;
                    const failedDelta = failedCount - previous.failedCount;
                    if (previous.total > 0 && Math.abs(totalDelta) / previous.total >= 0.2) {
                        const dir = totalDelta > 0 ? 'up' : 'down';
                        const pct = Math.round((Math.abs(totalDelta) / previous.total) * 100);
                        insights.push(`📈 Result count is ${dir} ${pct}% vs. the last time you ran this search (was ${previous.total}, now ${total}).`);
                    }
                    if (previous.failedCount > 0 && Math.abs(failedDelta) / previous.failedCount >= 0.2) {
                        const dir = failedDelta > 0 ? 'up' : 'down';
                        const pct = Math.round((Math.abs(failedDelta) / previous.failedCount) * 100);
                        insights.push(`📈 Failed count is ${dir} ${pct}% vs. last time (was ${previous.failedCount}, now ${failedCount}).`);
                    } else if (previous.failedCount === 0 && failedCount > 0) {
                        insights.push(`📈 This search had 0 Failed records last time — now has ${failedCount}.`);
                    }
                }
                snapshots[sig] = { total, failedCount, capturedAt: new Date().toISOString() };
                saveSnapshotHistory(snapshots);
            }

            setAiInsights(insights.slice(0, 5));
            return { statusCounts, customerCounts, deliveryCounts };
        } catch {
            if (isStale()) return null;
            setAiAggregates(null); // clear stale data from a previous search
            setAiInsights([]);
            return null;
        } finally {
            if (!isStale()) setAiDataLoaded(true);
        }
    }

    // Kendo Grid handles paging; keep totalPages only if needed later

    // sorting is handled via Kendo Grid events

    // columns are rendered manually below

    // react-table instance is not rendered directly here (we use columns + data for server-side rendering)

    // The search API's field naming/casing for the payload isn't fixed (varies by endpoint/version),
    // so scan the raw item for any key that looks like a payload field instead of guessing one exact name.
    function findPayload(r: Record<string, unknown>): string {
        for (const key of Object.keys(r)) {
            if (/^payload/i.test(key) || /payloadtext$/i.test(key)) {
                const v = r[key];
                if (v != null && v !== '') return String(v);
            }
        }
        return '';
    }

    // Canonical export columns — keyed the same way the AI's export_results.columns argument names them,
    // so a request like "export just claim number and status" can filter/reorder without a translation layer.
    const CSV_COLUMNS: { key: string; header: string; get: (r: SearchResultItem) => unknown }[] = [
        { key: 'id', header: 'Id', get: r => r.correspondenceId ?? r.CorrespondenceId },
        { key: 'docId', header: 'DocId', get: r => r.docId ?? r.DocId },
        { key: 'claimNumber', header: 'ClaimNumber', get: r => r.claimNumber ?? r.ClaimNumber },
        { key: 'customer', header: 'Customer', get: r => r.requestingDomainName ?? r.RequestingDomainName },
        { key: 'delivery', header: 'Delivery', get: r => r.deliveryMethodCode ?? r.DeliveryMethodCode },
        { key: 'created', header: 'Created', get: r => r.requestCreationTS ?? r.RequestCreationTS },
        { key: 'status', header: 'Status', get: r => r.latestStatus ?? r.LatestStatus },
        { key: 'payload', header: 'Payload', get: r => r.payloadText ?? r.PayloadText ?? findPayload(r as Record<string, unknown>) },
    ];

    function toCsvRow(r: SearchResultItem, columns?: string[]) {
        const cols = columns && columns.length > 0
            ? CSV_COLUMNS.filter(c => columns.includes(c.key))
            : CSV_COLUMNS;
        const row: Record<string, unknown> = {};
        for (const c of cols) row[c.header] = c.get(r);
        return row;
    }

    function xmlEscape(v: unknown) {
        return String(v ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // Cell background per Status value, mirroring the grid's row-highlight colors (STATUS_STYLES).
    const STATUS_EXCEL_FILL: Record<string, string> = {
        completed: '#D6F5E8',
        processing: '#FFF3CD',
        submitted: '#DCF1FE',
        failed: '#FBDADA',
    };

    // Builds an Excel-native SpreadsheetML (.xls) workbook — no third-party library required.
    // (The npm `xlsx`/SheetJS package has unpatched high-severity CVEs, so we avoid it.)
    function downloadExcel(rows: Record<string, unknown>[], filename: string) {
        if (!rows.length) return;
        const headers = Object.keys(rows[0]);
        const statusColIdx = headers.findIndex(h => h.toLowerCase() === 'status');

        const headerRow = `<Row ss:Height="20">${headers
            .map(h => `<Cell ss:StyleID="Header"><Data ss:Type="String">${xmlEscape(h)}</Data></Cell>`)
            .join('')}</Row>`;

        const dataRows = rows.map((r, i) => {
            const statusVal = statusColIdx >= 0 ? String(r[headers[statusColIdx]] ?? '').toLowerCase() : '';
            const styleId = STATUS_EXCEL_FILL[statusVal] ? `Status_${statusVal}` : (i % 2 === 1 ? 'RowAlt' : 'Row');
            return `<Row>${headers
                .map(h => `<Cell ss:StyleID="${styleId}"><Data ss:Type="String">${xmlEscape(r[h])}</Data></Cell>`)
                .join('')}</Row>`;
        }).join('');

        const colWidths = headers
            .map(h => `<Column ss:Width="${Math.min(Math.max(h.length * 8, 70), 220)}"/>`)
            .join('');

        const statusStyles = Object.entries(STATUS_EXCEL_FILL)
            .map(([key, color]) => `<Style ss:ID="Status_${key}">
   <Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Weight="1" ss:Color="#D0D0D0"/></Borders>
   <Interior ss:Color="${color}" ss:Pattern="Solid"/>
  </Style>`).join('\n  ');

        const xml = `<?xml version="1.0"?>
<?mso-application progid="Excel.Sheet"?>
<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet"
 xmlns:o="urn:schemas-microsoft-com:office:office"
 xmlns:x="urn:schemas-microsoft-com:office:excel"
 xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
 <Styles>
  <Style ss:ID="Header">
   <Font ss:Bold="1" ss:Color="#FFFFFF" ss:Size="10"/>
   <Interior ss:Color="#0078D4" ss:Pattern="Solid"/>
   <Alignment ss:Vertical="Center"/>
   <Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Weight="1" ss:Color="#005A9E"/></Borders>
  </Style>
  <Style ss:ID="Row">
   <Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Weight="1" ss:Color="#D0D0D0"/></Borders>
  </Style>
  <Style ss:ID="RowAlt">
   <Interior ss:Color="#F5F5F5" ss:Pattern="Solid"/>
   <Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Weight="1" ss:Color="#D0D0D0"/></Borders>
  </Style>
  ${statusStyles}
 </Styles>
 <Worksheet ss:Name="Export">
  <Table>
   ${colWidths}
   ${headerRow}
   ${dataRows}
  </Table>
  <WorksheetOptions xmlns="urn:schemas-microsoft-com:office:excel">
   <FreezePanes/><FrozenNoSplit/><SplitHorizontal>1</SplitHorizontal><TopRowBottomPane>1</TopRowBottomPane>
   <ActivePane>2</ActivePane>
  </WorksheetOptions>
  <AutoFilter x:Range="R1C1:R1C${headers.length}" xmlns="urn:schemas-microsoft-com:office:excel"/>
 </Worksheet>
</Workbook>`;
        const blob = new Blob([xml], { type: 'application/vnd.ms-excel;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(url);
    }

    function exportCurrentPage(columns?: string[]) {
        if (!results) return;
        downloadExcel(results.items.map(r => toCsvRow(r, columns)), 'export-page.xls');
    }

    function exportSelected(columns?: string[]) {
        if (!results || selectedIds.size === 0) return;
        const rows = results.items.filter(r => selectedIds.has((r.correspondenceId ?? r.CorrespondenceId) as string));
        downloadExcel(rows.map(r => toCsvRow(r, columns)), 'export-selected.xls');
    }

    async function exportAll(columns?: string[]) {
        if (!results) return;
        setExporting(true);
        try {
            const claim = activeAiParams?.claimNumber ?? detectClaim(freeText);
            const baseParams: any = {
                freeText: claim ? undefined : (activeAiParams ? (activeAiParams.freeText ?? undefined) : (freeText || undefined)),
                claimNumber: claim,
                policyNumber: activeAiParams?.policyNumber,
                correspondenceId: activeAiParams?.correspondenceId,
                docId: activeAiParams?.docId,
                customerName: activeAiParams?.customer,
                customerId: activeAiParams?.customerId,
                latestStatus: activeAiParams?.status,
                deliveryMethodCode: activeAiParams?.delivery,
                fromDate: activeAiParams?.fromDate,
                toDate: activeAiParams?.toDate,
            };
            const firstResp = await searchCorrespondence({ ...baseParams, page: 1, pageSize: 2000 });
            const firstBatch: SearchResultItem[] = firstResp.items ?? firstResp.Items ?? [];
            const rawTotal = firstResp.total ?? firstResp.Total ?? firstBatch.length;
            const total = activeAiParams?.pageSize ? Math.min(rawTotal, activeAiParams.pageSize) : rawTotal;

            const batchSize = Math.max(firstBatch.length, 1);
            const totalPages = Math.ceil(total / batchSize);
            const allItems = [...firstBatch];

            const PARALLEL = 10;
            for (let p = 2; p <= totalPages; p += PARALLEL) {
                const pageNums = Array.from({ length: Math.min(PARALLEL, totalPages - p + 1) }, (_, i) => p + i);
                const responses = await Promise.all(pageNums.map(page => searchCorrespondence({ ...baseParams, page, pageSize: batchSize })));
                for (const resp of responses) {
                    (resp.items ?? resp.Items ?? []).forEach((it: SearchResultItem) => allItems.push(it));
                }
            }

            downloadExcel(allItems.slice(0, total).map(r => toCsvRow(r, columns)), 'export-all.xls');
        } catch (err: any) {
            console.error('export all failed', err);
            setToast({ variant: 'danger', message: err?.message ?? 'Export failed' });
        } finally {
            setExporting(false);
        }
    }

    function handleAiExport(params: ExportParams) {
        if (params.scope === 'all') exportAll(params.columns);
        else if (params.scope === 'selected') exportSelected(params.columns);
        else exportCurrentPage(params.columns);
    }

    // Bulk-resubmits the checked rows' ORIGINAL stored payload. This is a real write action
    // (each row is POSTed again via the same endpoint the manual "Insert" form uses), so it always
    // requires the user to have manually selected the rows AND to confirm the count before anything
    // is sent — the AI can find/filter candidates via search_grid, but never triggers this itself.
    // Confirmation is a Kendo Dialog (see resubmitConfirm state) rather than window.confirm.
    function handleBulkResubmit() {
        if (!results || selectedIds.size === 0) return;
        const rows = results.items.filter(r => selectedIds.has((r.correspondenceId ?? r.CorrespondenceId) as string));
        const withPayload = rows.filter(r => !!(r as any).payload) as SearchResultItem[];
        if (withPayload.length === 0) {
            setToast({ variant: 'danger', message: 'None of the selected records have a storable payload to resubmit.' });
            return;
        }
        setResubmitConfirm(withPayload);
    }

    async function executeBulkResubmit() {
        const withPayload = resubmitConfirm;
        setResubmitConfirm(null);
        if (!withPayload || withPayload.length === 0) return;

        setResubmitting(true);
        let succeeded = 0;
        let failed = 0;
        for (const r of withPayload) {
            try {
                const parsed = JSON.parse((r as any).payload as string);
                await insertCorrespondence(parsed);
                succeeded++;
            } catch {
                failed++;
            }
        }
        setResubmitting(false);
        setToast({
            variant: failed === 0 ? 'success' : 'danger',
            message: `Resubmitted ${succeeded} record${succeeded === 1 ? '' : 's'}${failed ? `, ${failed} failed` : ''}.`,
        });
        if (succeeded > 0) setTimeout(() => doSearch(page, pageSize, activeAiParams ?? undefined), 1500);
    }

    return (
        <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', marginRight: assistantDockWidth, transition: 'margin-right 0.15s ease', background: 'var(--vsc-editor)', color: 'var(--vsc-text)', overflow: 'hidden' }}>

            {/* ── Header ── */}
            <header style={{ flexShrink: 0, background: 'var(--vsc-titlebar)', color: 'var(--vsc-text)', padding: '8px 20px', borderBottom: '1px solid var(--vsc-border)', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                    <span
                        role="img"
                        aria-label="Dolphin"
                        style={{
                            fontSize: 24,
                            lineHeight: 1,
                            display: 'inline-flex',
                            alignItems: 'center',
                            filter: 'drop-shadow(0 1px 1px rgba(0,0,0,0.25))',
                        }}
                    >
                        🐬
                    </span>
                    <div style={{ display: 'flex', flexDirection: 'column', lineHeight: 1.1 }}>
                        <span style={{ fontSize: 18, fontWeight: 700, letterSpacing: 8.0, color: '#0078d4' }}>ECCO</span>
                        <h4 style={{ margin: 0, fontSize: 15, fontWeight: 600, letterSpacing: 0.3 }}>Correspondence Manager</h4>
                    </div>
                </div>
                <ThemeToggle />
            </header>

            {/* ── Toolbar ── */}
            <div style={{ flexShrink: 0, padding: '8px 16px', borderBottom: '1px solid var(--vsc-border)' }}>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                    <Form.Control
                        style={{ flex: 1 }}
                        placeholder="Search by DocId, claim number..."
                        value={freeText}
                        onChange={e => setFreeText(e.target.value)}
                        onKeyDown={e => { if (e.key === 'Enter') { doSearch(1); e.preventDefault(); } }}
                    />
                    <Button onClick={() => doSearch(1)} disabled={loading} variant="primary" style={{ whiteSpace: 'nowrap' }}>
                        {loading ? <Spinner animation="border" size="sm" /> : 'Search'}
                    </Button>
                    <Button
                        onClick={() => {
                            setFreeText('');
                            setError(null);
                            setAiAggregates(null);
                            setAiFilterLabel(null);
                            setActiveAiParams(null);
                            setFilter({ logic: 'and', filters: [] });
                            // Pass an empty aiParams object so doSearch ignores the (still-stale)
                            // freeText closure value and reloads the full, unfiltered dataset.
                            doSearch(1, pageSize, {});
                        }}
                        disabled={loading}
                        variant="secondary"
                    >
                        Clear
                    </Button>
                    <Dropdown>
                        <Dropdown.Toggle variant="outline-secondary" disabled={!results || exporting} id="export-dropdown">
                            {exporting ? <Spinner animation="border" size="sm" /> : 'Export to Excel'}
                        </Dropdown.Toggle>
                        <Dropdown.Menu>
                            <Dropdown.Item onClick={() => exportCurrentPage()} disabled={!results}>
                                Export current page ({results?.items.length ?? 0})
                            </Dropdown.Item>
                            <Dropdown.Item onClick={() => exportSelected()} disabled={selectedIds.size === 0}>
                                Export selected ({selectedIds.size})
                            </Dropdown.Item>
                            <Dropdown.Item onClick={() => exportAll()} disabled={!results}>
                                Export all matching ({results?.total ?? 0})
                            </Dropdown.Item>
                        </Dropdown.Menu>
                    </Dropdown>
                    <Button
                        onClick={handleBulkResubmit}
                        disabled={selectedIds.size === 0 || resubmitting}
                        variant="warning"
                        title="Resubmit the checked rows' original payload as new correspondence requests"
                        style={{ whiteSpace: 'nowrap', color: '#3d2c00', fontWeight: 600 }}
                    >
                        {resubmitting ? <Spinner animation="border" size="sm" /> : `🔁 Resubmit Selected (${selectedIds.size})`}
                    </Button>
                    <Button
                        onClick={() => setShowAnalytics(v => !v)}
                        variant={showAnalytics ? 'primary' : 'outline-secondary'}
                        style={{ whiteSpace: 'nowrap' }}
                    >
                        📊 Analytics
                    </Button>
                    <Button onClick={() => setShowInsert(true)} variant="outline-primary" style={{ whiteSpace: 'nowrap' }}>
                        + Insert
                    </Button>
                </div>
                {detectClaim(freeText) && (
                    <div style={{ marginTop: 4, fontSize: 12, color: '#0078d4' }}>
                        Claim filter: <strong>{detectClaim(freeText)}</strong>
                    </div>
                )}
                {error && (
                    <SmartErrorAlert
                        error={error}
                        isRetrying={isRetrying}
                        onRetry={error?.isRetryable ? handleSearchRetry : undefined}
                        onDismiss={() => setError(null)}
                        variant="danger"
                        className="mt-2 mb-0"
                        style={{ fontSize: 12 }}
                    />
                )}
                {results && (
                    <div style={{ marginTop: 4, fontSize: 12, color: '#555' }}>
                        Showing {results.items.length} of {results.total} records
                    </div>
                )}
            </div>

            {/* ── Analytics Panel ── */}
            {showAnalytics && (
                <AnalyticsPanel
                    aggregates={aiAggregates}
                    loading={results !== null && !aiDataLoaded}
                    filterLabel={aiFilterLabel}
                />
            )}

            {/* ── Content: grid ── */}
            <div style={{ flex: 1, minHeight: 0, display: 'flex', gap: 10, padding: '10px 16px', overflow: 'hidden' }}>

                {/* Grid */}
                <div style={{ flex: 1, minWidth: 0, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
                    {aiFilterLabel && (
                        <div style={{ flexShrink: 0, marginBottom: 6, display: 'flex', alignItems: 'center', gap: 8 }}>
                            <span style={{ fontSize: 12, background: '#e8f4ff', color: '#0078d4', border: '1px solid #b3d7ff', borderRadius: 12, padding: '2px 10px' }}>
                                🤖 AI Filter: {aiFilterLabel}
                            </span>
                            <button
                                onClick={() => { setAiFilterLabel(null); setActiveAiParams(null); setFilter({ logic: 'and', filters: [] }); }}
                                style={{ background: 'none', border: 'none', cursor: 'pointer', fontSize: 14, color: '#888', lineHeight: 1 }}
                                title="Clear AI filter"
                            >×</button>
                        </div>
                    )}

                    {/* Search Refinement Panel */}
                    {results && refinementResult && (
                        <div style={{ flexShrink: 0, marginBottom: 10 }}>
                            <SearchRefinementPanel
                                refinementResult={refinementResult}
                                onApplySuggestion={(suggestion: SearchSuggestion) => {
                                    if (suggestion.query) {
                                        const updatedParams = { ...suggestion.query };
                                        doSearch(1, pageSize, updatedParams as SearchParams);
                                    }
                                }}
                                onApplyTypoFix={(corrected: string) => {
                                    setFreeText(corrected);
                                    doSearch(1, pageSize);
                                }}
                            />
                        </div>
                    )}

                    {results ? (
                        <Grid
                            style={{ height: '100%', fontSize: '13px' }}
                            data={(() => {
                                if (activeAiParams?.pageSize != null) {
                                    // Top-N: all records are in memory — sort, filter, then slice to the current page.
                                    const sorted = orderBy(filterBy(results.items, filter), sortBy ? [{ field: sortBy, dir: sortDirection }] : []);
                                    return sorted.slice((page - 1) * pageSize, page * pageSize);
                                }
                                return filterBy(results.items, filter);
                            })()}
                            skip={(page - 1) * pageSize}
                            take={pageSize}
                            total={results.total}
                            pageable={(() => {
                                const topN = activeAiParams?.pageSize;
                                if (topN != null && topN <= 25) return false;          // all fit on one page — no pager
                                const sizes = topN != null
                                    ? ([10, 25, 50, 100] as number[]).filter(s => s <= topN)   // only sizes ≤ N
                                    : [10, 25, 50, 100];
                                return { buttonCount: 5, info: true, pageSizes: sizes };
                            })()}
                            sortable
                            filterable
                            resizable
                            reorderable
                            rows={{ data: StatusRow }}
                            filter={filter}
                            onFilterChange={(e: GridFilterChangeEvent) => setFilter(e.filter)}
                            sort={[sortBy ? { field: sortBy, dir: sortDirection } : undefined].filter(Boolean) as any}
                            onPageChange={(e) => {
                                const newSize = e.page.take || pageSize;
                                const newPage = Math.floor((e.page.skip || 0) / newSize) + 1;
                                setPageSize(newSize);
                                setPage(newPage);
                                if (activeAiParams?.pageSize != null) {
                                    // Top-N: all records are already in memory.
                                    // Just updating page/pageSize state re-slices the data — no server call needed.
                                    return;
                                }
                                // Normal search: server-side paging, preserve any active AI filter.
                                // skipAnalytics=true — dataset hasn't changed, only the page offset.
                                const params = activeAiParams
                                    ? { ...activeAiParams, pageSize: undefined }
                                    : undefined;
                                doSearch(newPage, newSize, params, undefined, true);
                            }}
                            onSortChange={(e) => {
                                const s = e.sort?.[0];
                                const newBy = s?.field ?? null;
                                const newDir = (s?.dir as 'asc' | 'desc') ?? 'asc';
                                setSortBy(newBy);
                                if (newBy) setSortDirection(newDir);
                                // Top-N mode: re-order the existing rows client-side only.
                                // Going back to the server would return a different set of N records.
                                if (activeAiParams?.pageSize != null) return;
                                // Normal mode: server-side sort, preserve any active AI filter criteria.
                                // skipAnalytics=true — same dataset, just reordered.
                                const params = activeAiParams
                                    ? { ...activeAiParams, pageSize: undefined, sortBy: undefined, sortDirection: undefined }
                                    : undefined;
                                doSearch(1, pageSize, params, { by: newBy, dir: newDir }, true);
                            }}
                            selectable
                            onSelectionChange={(e) => console.debug('row selected', e.dataItem)}
                        >
                            <GridColumn field="select" title=" " width="40px" sortable={false} filterable={false} cells={{ data: SelectCell }} />
                            <GridColumn field="id" title="Correspondence Id" />
                            <GridColumn field="docId" title="Doc Id" />
                            <GridColumn field="claimNumber" title="Claim Number" />
                            <GridColumn field="customer" title="Customer" />
                            <GridColumn field="delivery" title="Delivery" />
                            <GridColumn field="created" title="Created" cells={{ data: CreatedCell }} />
                            <GridColumn field="status" title="Status" cells={{ data: StatusCell }} />
                            <GridColumn
                                field="payloadText"
                                title="Request Payload"
                                width="140px"
                                sortable={false}
                                filterable={false}
                                cells={{ data: (props: any) => <PayloadCell {...props} onViewPayload={handleViewPayload} onExplainRecord={handleExplainRecord} /> }}
                            />
                        </Grid>
                    ) : (
                        <div style={{ color: '#858585', padding: 32, textAlign: 'center', fontSize: 13 }}>
                            Enter a search term above and press Search.
                        </div>
                    )}
                </div>
            </div>

            {/* AI Assistant — floating popup */}
            <AssistantPopup
                ref={assistantPopupRef}
                onQuery={(q) => console.debug('assistant query', q)}
                onSearch={handleAiSearch}
                onSendCorrespondence={handleAiSendCorrespondence}
                onUpdateCorrespondence={handleAiUpdateCorrespondence}
                onExport={handleAiExport}
                onClearFilters={handleAiClearFilters}
                onUndo={handleAiUndo}
                onDockChange={(docked, width) => setAssistantDockWidth(docked ? width : 0)}
                statusValues={statusValues}
                activeParams={activeAiParams}
                activeDraft={aiDraftParams}
                insights={[...backgroundAlerts, ...aiInsights]}
                searchContext={results ? {
                    total: results.total,
                    page: results.page,
                    pageSize: results.pageSize,
                    items: results.items as Record<string, unknown>[],
                    pageStatusCounts: (results.items as any[]).reduce((acc, item) => {
                        const s = String(item.status ?? 'Unknown');
                        acc[s] = (acc[s] ?? 0) + 1;
                        return acc;
                    }, {} as Record<string, number>),
                    allAggregates: aiAggregates,
                    allDataLoaded: aiDataLoaded,
                } : null}
            />

            {/* ── Footer ── */}
            <footer style={{ flexShrink: 0, background: '#3c3c3c', color: '#858585', padding: '5px 20px', borderTop: '1px solid #555', fontSize: 11, textAlign: 'center' }}>
                © {new Date().getFullYear()} Correspondence Manager
            </footer>

            <ConfirmDialog
                show={!!resubmitConfirm}
                title="Resubmit Records"
                danger
                message={
                    resubmitConfirm
                        ? `Resubmit ${resubmitConfirm.length} record${resubmitConfirm.length === 1 ? '' : 's'}? Each one's original request payload will be re-sent as a new correspondence request.`
                        : ''
                }
                confirmLabel="Resubmit"
                onConfirm={executeBulkResubmit}
                onCancel={() => setResubmitConfirm(null)}
            />

            <InsertCorrespondenceModal
                show={showInsert}
                aiPayload={aiGeneratedPayload}
                aiContext={aiPayloadContext}
                onHide={() => {
                    setShowInsert(false);
                    setAiGeneratedPayload(null);
                    setAiPayloadContext(null);
                    setAiDraftParams(null);
                }}
                onSuccess={(message) => {
                    setShowInsert(false);
                    setAiGeneratedPayload(null);
                    setAiPayloadContext(null);
                    setAiDraftParams(null);
                    setToast({ variant: 'success', message });
                    // Insert is processed asynchronously (NServiceBus) — the record isn't in the
                    // DB immediately after the 202 response, so wait briefly before refreshing.
                    // Jump to page 1 so the new record (newest by default sort) is visible.
                    setTimeout(() => doSearch(1, pageSize), 3000);
                }}
                onError={(message) => setToast({ variant: 'danger', message })}
            />

            {/* ── Payload Viewer Modal ── */}
            <Modal
                show={showPayloadModal}
                onHide={() => setShowPayloadModal(false)}
                size="lg"
                backdrop="static"
                centered
                style={{ zIndex: 2001 }}
            >
                <Modal.Header closeButton style={{ background: '#3c3c3c', color: '#d4d4d4', borderColor: '#555' }}>
                    <Modal.Title>Request Payload</Modal.Title>
                </Modal.Header>
                <Modal.Body style={{ background: '#1e1e1e', color: '#d4d4d4', padding: '20px' }}>
                    {isEncryptedPayload && (
                        <Alert variant="info" style={{ marginBottom: '16px' }}>
                            <strong>✓ Encrypted payload detected and decrypted</strong>
                        </Alert>
                    )}
                    <div style={{ display: 'flex', gap: '8px', marginBottom: '16px' }}>
                        <Button
                            variant={payloadViewMode === 'json' ? 'primary' : 'outline-secondary'}
                            size="sm"
                            onClick={() => setPayloadViewMode('json')}
                        >
                            JSON Format
                        </Button>
                        <Button
                            variant={payloadViewMode === 'text' ? 'primary' : 'outline-secondary'}
                            size="sm"
                            onClick={() => setPayloadViewMode('text')}
                        >
                            Text Format
                        </Button>
                    </div>
                    <div style={{ maxHeight: '500px', overflow: 'auto', borderRadius: '4px', border: '1px solid #3c3c3c' }}>
                        {payloadViewMode === 'json' ? (
                            renderPayloadJson(payloadDecrypted)
                        ) : (
                            renderPayloadAsTable(payloadDecrypted)
                        )}
                    </div>
                </Modal.Body>
                <Modal.Footer style={{ background: '#3c3c3c', borderColor: '#555' }}>
                    <Button variant="secondary" onClick={() => setShowPayloadModal(false)}>
                        Close
                    </Button>
                    <Button
                        variant="outline-primary"
                        onClick={() => {
                            if (payloadDecrypted) {
                                navigator.clipboard.writeText(payloadDecrypted);
                                setToast({ variant: 'success', message: 'Payload copied to clipboard' });
                            }
                        }}
                    >
                        Copy to Clipboard
                    </Button>
                </Modal.Footer>
            </Modal>

            <ToastContainer position="top-end" className="p-3" style={{ zIndex: 2000 }}>
                <Toast
                    show={!!toast}
                    onClose={() => setToast(null)}
                    delay={6000}
                    autohide
                    bg={toast?.variant}
                >
                    <Toast.Header closeButton>
                        <strong className="me-auto">
                            {toast?.variant === 'success' ? 'Correspondence Inserted' : 'Insert Failed'}
                        </strong>
                    </Toast.Header>
                    <Toast.Body className={toast?.variant === 'danger' ? 'text-white' : undefined}>
                        {toast?.message}
                    </Toast.Body>
                </Toast>
            </ToastContainer>
        </div>
    );
}