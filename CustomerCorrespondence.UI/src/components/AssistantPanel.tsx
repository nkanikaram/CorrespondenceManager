import { useEffect, useState, useRef, forwardRef, useImperativeHandle } from 'react';
import { Button, Form, Spinner } from 'react-bootstrap';
import { queryKendoAi } from '../api/kendoAiAdapter';
import SmartErrorAlert from './SmartErrorAlert';
import MarkdownAnswer from './MarkdownAnswer';
import MiniBarChart from './MiniBarChart';
import MiniPieChart from './MiniPieChart';
import MiniStackedBarChart from './MiniStackedBarChart';
import MiniSmallMultiples from './MiniSmallMultiples';
import ConfirmDialog from './ConfirmDialog';
import PromptDialog from './PromptDialog';
import { classifyError } from '../utils/errorRecovery';
import type { SearchContext, SearchParams, SendCorrespondenceParams, ExportParams, AiAction, ChatTurn, TokenUsage } from '../api/kendoAiAdapter';
import { listConversations, getConversation, createConversation, appendMessage, deleteConversation } from '../api/chatHistoryApi';
import type { ConversationSummary } from '../api/chatHistoryApi';
import { getAiUsageSummary, type AiUsageSummary } from '../api/correspondenceApi';

type ChartData =
    | { title: string; counts: Record<string, number>; chartType: 'bar' | 'pie' | 'donut' }
    // A two-dimensional breakdown (e.g. delivery method by status) drawn as one stacked bar —
    // the default when no pie/donut was explicitly requested.
    | { title: string; crossTab: Record<string, Record<string, number>>; chartType: 'stacked' }
    // Same cross-tab, but pie/donut was explicitly requested — honored as small multiples (one
    // pie/donut per outer-key group), since a single pie can't hold two values per slice.
    | { title: string; crossTab: Record<string, Record<string, number>>; chartType: 'pie-multi' | 'donut-multi' };

type HistoryItem = {
    q: string;
    a: string;
    isSearch?: boolean;
    isSend?: boolean;
    isUpdate?: boolean;
    isExport?: boolean;
    isClear?: boolean;
    isUndo?: boolean;
    /** Internal-only: a same-turn "did you mean" hint from runAction's 'search' case, folded into
     *  the answer text before the history entry is built — never itself rendered as a field. */
    zeroResultHint?: string;
    isMulti?: boolean;
    sendError?: string;
    /** What the AI extracted for a search action — shown as a small "understood as" explainer. */
    params?: SearchParams;
    /** Present when this turn produced one or more charts (a multi-action turn can ask for several). */
    charts?: ChartData[];
};

// Chat history only stores plain text per message, so chart snapshots ride along as a hidden
// HTML-comment marker appended to the saved answer — invisible in the raw text, stripped back out
// (and parsed) when a conversation is reopened from History, so charts survive a page reload.
const CHART_MARKER_RE = /\n\n<!--charts:(.+)-->$/s;

function encodeChartMarker(answer: string, charts: ChartData[]): string {
    return `${answer}\n\n<!--charts:${JSON.stringify(charts)}-->`;
}

// Plain Levenshtein edit distance — used only to find a close status-name match, not for
// anything performance-sensitive (statusValues is at most a few dozen entries).
function editDistance(a: string, b: string): number {
    const m = a.length, n = b.length;
    const dp: number[][] = Array.from({ length: m + 1 }, (_, i) => [i, ...Array(n).fill(0)]);
    for (let j = 0; j <= n; j++) dp[0][j] = j;
    for (let i = 1; i <= m; i++) {
        for (let j = 1; j <= n; j++) {
            dp[i][j] = a[i - 1] === b[j - 1]
                ? dp[i - 1][j - 1]
                : 1 + Math.min(dp[i - 1][j - 1], dp[i - 1][j], dp[i][j - 1]);
        }
    }
    return dp[m][n];
}

// A zero-token safety net: even though the AI is told the real status list and instructed to
// self-correct before searching, it can still miss — this catches a 0-result status search after
// the fact and suggests the closest real value, entirely client-side.
function closestStatusMatch(attempted: string, known: string[]): string | null {
    const a = attempted.toLowerCase();
    let best: { value: string; dist: number } | null = null;
    for (const k of known) {
        if (k.toLowerCase() === a) return null; // exact match — not the problem, nothing to suggest
        const dist = editDistance(a, k.toLowerCase());
        if (dist <= Math.max(2, Math.ceil(k.length * 0.4)) && (!best || dist < best.dist)) {
            best = { value: k, dist };
        }
    }
    return best?.value ?? null;
}

// After a search that just ran, checks whether it used a status name close to but not exactly
// matching a known real value and returned nothing — the likely cause of a confusing "0 results"
// answer the AI itself had no way to catch (it never sees the actual row count it produced).
// Mirrors kendoAiAdapter.ts's wantsNarrative check — used client-side to detect when a search-only
// turn (no data was loaded yet, so the model correctly deferred the narrative) should be silently
// followed by one more call now that the data it just searched for is actually loaded.
const NARRATIVE_RE = /\b(summar|timeline|history|what happened|recap|walk me through)\b/i;

function zeroResultStatusHint(
    params: SearchParams,
    ctx: SearchContext | null | undefined,
    statusValues: string[] | undefined,
): string | null {
    if (!ctx || ctx.total !== 0 || !statusValues || statusValues.length === 0) return null;
    const attempted = (params.statuses && params.statuses.length > 0) ? params.statuses : (params.status ? [params.status] : []);
    for (const a of attempted) {
        const suggestion = closestStatusMatch(a, statusValues);
        if (suggestion) return `💡 No records matched status "${a}" — did you mean "${suggestion}"?`;
    }
    return null;
}

function decodeChartMarker(content: string): { text: string; charts?: ChartData[] } {
    const match = content.match(CHART_MARKER_RE);
    if (!match) return { text: content };
    try {
        const charts = JSON.parse(match[1]) as ChartData[];
        return { text: content.slice(0, match.index), charts };
    } catch {
        return { text: content };
    }
}

export type AssistantPanelHandle = {
    /** Opens the Ask flow and runs `text` as if the user had typed and sent it. */
    ask: (text: string) => void;
};

// How many prior exchanges to send back to the AI as short-term memory (bounds token usage).
const CONTEXT_TURNS = 3;
// A prior answer only needs to be recognizable in context, not replayed verbatim — long
// answers (tables, timelines) get cut down before being resent as input tokens on later turns.
const CONTEXT_ANSWER_CHAR_LIMIT = 300;

function truncateForContext(answer: string): string {
    if (answer.length <= CONTEXT_ANSWER_CHAR_LIMIT) return answer;
    return answer.slice(0, CONTEXT_ANSWER_CHAR_LIMIT).trim() + '… [truncated for context]';
}

const SAMPLE_QUERIES = [
    'Show records in Processing status',
    'Top 20 most recent completed requests',
    'Show failed records from the last 30 days',
    'How many records are there for each customer?',
    'Find claim CL-12345',
    'Show submitted records',
    'Latest 10 failed requests',
    'Show records delivered by Email',
    'How many records are in each status?',
    'Clear all the filters in the grid',
    'Records from Jan 2024',
    'Records from today',
    'Show failed records from last week and export them',
    'Undo the last change',
    'Show me a chart of records by status',
    'Export just the claim number and status columns',
];

// Capability hints, not data observations — these stay static regardless of what's loaded.
// (Data-driven observations belong in the Insights card instead, which is computed from the results.)
const TIPS = [
    '🗣️ Speak naturally: "show me everything from Payment Guard Advantage last month" works as well as picking filters manually.',
    '✏️ Refine without restating: after a search, say "actually make that Completed instead" — the AI merges it with your current filters.',
    '↩️ Made a wrong turn? Just say "undo" to revert your last filter change.',
    '🔗 Chain requests: "show failed records from last week and export them" runs both actions in one go.',
    '🎤 Click the mic icon to dictate a query instead of typing it.',
    '⭐ Shift-click any chip (favorite or history) to remove it from your favorites.',
    '⌨️ Use ↑ / ↓ in the message box to cycle through your last queries, like a terminal.',
    '📌 Star a question after asking it to pin it under Favorites for next time.',
    '📌 Save the grid\'s current filters as a named "Saved View" to reapply with one click later.',
    '📊 Ask for a chart — "chart the status breakdown" — to see a bar chart instead of a wall of numbers.',
    '🔊 Click the speaker icon on any answer to have it read aloud.',
    '📊 Check the token counter at the bottom of this card — "+ New chat" resets the conversation history that gets resent on every message.',
];

const FAVORITES_KEY = 'ecco.ai.favoriteQueries';
const SAVED_VIEWS_KEY = 'ecco.ai.savedViews';

type SavedView = { name: string; params: SearchParams };

function loadSavedViews(): SavedView[] {
    try {
        const raw = localStorage.getItem(SAVED_VIEWS_KEY);
        return raw ? JSON.parse(raw) : [];
    } catch {
        return [];
    }
}

function saveSavedViews(views: SavedView[]) {
    try {
        localStorage.setItem(SAVED_VIEWS_KEY, JSON.stringify(views));
    } catch {
        // ignore storage errors (private browsing, quota, etc.)
    }
}

// Cumulative token usage across all AI calls this browser has ever made, so users can see the
// real cost driver (system prompt + context, resent on every call) rather than guessing.
const TOKEN_USAGE_KEY = 'ecco.ai.tokenUsageTotals';
type TokenTotals = { inputTokens: number; outputTokens: number; calls: number };
const EMPTY_TOTALS: TokenTotals = { inputTokens: 0, outputTokens: 0, calls: 0 };

function loadTokenTotals(): TokenTotals {
    try {
        const raw = localStorage.getItem(TOKEN_USAGE_KEY);
        return raw ? { ...EMPTY_TOTALS, ...JSON.parse(raw) } : EMPTY_TOTALS;
    } catch {
        return EMPTY_TOTALS;
    }
}

function saveTokenTotals(totals: TokenTotals) {
    try {
        localStorage.setItem(TOKEN_USAGE_KEY, JSON.stringify(totals));
    } catch {
        // ignore storage errors (private browsing, quota, etc.)
    }
}

function formatTokenCount(n: number): string {
    return n >= 1000 ? `${(n / 1000).toFixed(1)}k` : String(n);
}

// How many times a Saved View has been clicked to reapply filters — each one is an AI call that
// was skipped entirely, so this directly measures the tokens saved by the feature.
const SAVED_VIEW_USES_KEY = 'ecco.ai.savedViewUses';

function loadSavedViewUses(): number {
    try {
        const raw = localStorage.getItem(SAVED_VIEW_USES_KEY);
        const n = raw ? parseInt(raw, 10) : 0;
        return Number.isFinite(n) ? n : 0;
    } catch {
        return 0;
    }
}

function saveSavedViewUses(n: number) {
    try {
        localStorage.setItem(SAVED_VIEW_USES_KEY, String(n));
    } catch {
        // ignore storage errors (private browsing, quota, etc.)
    }
}

// Fallback used only until we have real measured data (tokenTotals.calls === 0) — a rough
// estimate of a typical call's cost (system prompt + tool schemas + loaded-data context).
const ESTIMATED_TOKENS_PER_AI_CALL = 5490;

// Turns a SearchParams object into a short, human-readable summary —
// used both for the "Understood as" explainer and saved-view labels.
function describeParams(params: SearchParams | null | undefined): string {
    if (!params) return 'all records';
    const parts: string[] = [];
    if (params.status) parts.push(`Status = "${params.status}"`);
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
    if (params.pageSize) parts.push(`Top ${params.pageSize}`);
    if (params.sortBy) parts.push(`Sort: ${params.sortBy} ${params.sortDirection ?? 'desc'}`);
    return parts.length ? parts.join(', ') : 'all records';
}

const TABLE_ROW_RE = /^\s*\|.*\|\s*$/;
const SEPARATOR_ROW_RE = /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/;

function splitCellsForSpeech(line: string): string[] {
    return line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(c => c.trim());
}

// Strips the markdown punctuation an advisor would never actually say out loud, and swaps a
// couple of symbols for the words a person would use when speaking ("%" → "percent").
function speakableLine(line: string): string {
    return line
        .replace(/^#{1,6}\s*/, '')
        .replace(/\*\*(.*?)\*\*/g, '$1')
        .replace(/__(.*?)__/g, '$1')
        .replace(/(?<!\*)\*(?!\*)(.*?)(?<!\*)\*(?!\*)/g, '$1')
        .replace(/^[-*]\s+/, '')
        .replace(/`/g, '')
        .replace(/%/g, ' percent')
        .replace(/&/g, ' and ')
        .trim();
}

// Turns a small markdown table into a spoken sentence per row ("Payment Guard Advantage: 15,159")
// instead of reading pipe characters, or a one-line summary if the table is too long to recite.
function tableToSpeech(headers: string[], rows: string[][]): string {
    if (rows.length === 0) return '';
    if (rows.length > 6) {
        const top = rows[0];
        return `a table with ${rows.length} rows — the top one is ${top.join(', ')}`;
    }
    const keyCol = headers.length > 1 ? 0 : -1;
    return rows
        .map(row => keyCol >= 0 ? `${row[keyCol]}: ${row.filter((_, i) => i !== keyCol).join(', ')}` : row.join(', '))
        .join('. ');
}

// Best-effort natural-sounding version of an answer for text-to-speech — strips markdown
// punctuation that would otherwise be read aloud literally ("asterisk asterisk"), turns bullet
// lists into a flowing sentence, and summarizes tables as spoken rows instead of pipe characters.
function toSpeechText(text: string): string {
    const lines = text.split('\n');
    const out: string[] = [];
    let i = 0;
    let bulletBuf: string[] = [];

    const flushBullets = () => {
        if (bulletBuf.length === 0) return;
        if (bulletBuf.length === 1) {
            out.push(bulletBuf[0]);
        } else {
            out.push(bulletBuf.slice(0, -1).join(', ') + ', and ' + bulletBuf[bulletBuf.length - 1]);
        }
        bulletBuf = [];
    };

    while (i < lines.length) {
        const line = lines[i];
        if (TABLE_ROW_RE.test(line) && lines[i + 1] !== undefined && SEPARATOR_ROW_RE.test(lines[i + 1])) {
            flushBullets();
            const headers = splitCellsForSpeech(line).map(speakableLine);
            let j = i + 2;
            const rows: string[][] = [];
            while (j < lines.length && TABLE_ROW_RE.test(lines[j])) {
                rows.push(splitCellsForSpeech(lines[j]).map(speakableLine));
                j++;
            }
            const spoken = tableToSpeech(headers, rows);
            if (spoken) out.push(spoken);
            i = j;
            continue;
        }
        const trimmed = line.trim();
        if (/^[-*]\s+/.test(trimmed)) {
            const cleaned = speakableLine(trimmed);
            if (cleaned) bulletBuf.push(cleaned);
        } else {
            flushBullets();
            const cleaned = speakableLine(trimmed);
            if (cleaned) out.push(cleaned);
        }
        i++;
    }
    flushBullets();
    // Join with natural pauses rather than robotic hard stops on every line break.
    return out.join('. ').replace(/\.\.+/g, '.');
}

let cachedVoices: SpeechSynthesisVoice[] | null = null;

export type VoiceGender = 'male' | 'female';
const VOICE_PREF_KEY = 'ecco.ai.voiceGender';

function loadVoicePref(): VoiceGender {
    try {
        const raw = localStorage.getItem(VOICE_PREF_KEY);
        return raw === 'female' ? 'female' : 'male';
    } catch {
        return 'male';
    }
}

function saveVoicePref(v: VoiceGender) {
    try {
        localStorage.setItem(VOICE_PREF_KEY, v);
    } catch {
        // ignore storage errors (private browsing, quota, etc.)
    }
}

// The Web Speech API doesn't expose a voice's gender — browsers ship a fixed roster of named
// system voices and there's no property for it, so we match against the well-known names each
// platform actually ships (Windows/Edge "Microsoft ...", Chrome's "Google ...", macOS/iOS voices).
const FEMALE_VOICE_HINTS = [
    'female', 'zira', 'susan', 'samantha', 'victoria', 'aria', 'jenny', 'emma', 'ava', 'allison',
    'karen', 'moira', 'tessa', 'fiona', 'kate', 'serena', 'salli', 'joanna', 'kendra', 'ivy',
    'google us english', 'google uk english female', 'hazel', 'catherine', 'michelle',
];
const MALE_VOICE_HINTS = [
    'male', 'david', 'mark', 'guy', 'daniel', 'alex', 'fred', 'ryan', 'christopher', 'george',
    'james', 'eric', 'matthew', 'brian', 'google uk english male', 'tom', 'oliver',
];

function matchesGender(name: string, gender: VoiceGender): boolean {
    const lower = name.toLowerCase();
    const hints = gender === 'female' ? FEMALE_VOICE_HINTS : MALE_VOICE_HINTS;
    return hints.some(h => lower.includes(h));
}

// The default voice on most systems reads flatly; prefer a "Natural"/"Neural" en-US voice
// (Edge/Chrome ship these) so the read-out sounds like a person, not a screen reader — and
// within that, prefer one matching the user's chosen gender when the browser offers a choice.
function pickNaturalVoice(gender: VoiceGender): SpeechSynthesisVoice | null {
    if (!('speechSynthesis' in window)) return null;
    const voices = cachedVoices ?? window.speechSynthesis.getVoices();
    if (voices.length > 0) cachedVoices = voices;
    if (!voices.length) return null;

    const enVoices = voices.filter(v => v.lang?.toLowerCase().startsWith('en'));
    const pool = enVoices.length > 0 ? enVoices : voices;

    const genderMatched = pool.filter(v => matchesGender(v.name, gender));
    const naturalInGender = genderMatched.find(v => /natural|neural/i.test(v.name));
    if (naturalInGender) return naturalInGender;
    if (genderMatched.length > 0) return genderMatched[0];

    // No voice we can confidently label as the requested gender — fall back to any natural voice.
    const natural = pool.find(v => /natural|neural/i.test(v.name));
    if (natural) return natural;

    const usLocal = pool.find(v => v.lang?.toLowerCase() === 'en-us' && v.localService);
    if (usLocal) return usLocal;

    return pool[0] ?? null;
}

// Web Speech API isn't in the standard TS DOM lib — declare the minimal shape we use.
type SpeechRecognitionResultLike = { transcript: string };
type SpeechRecognitionEventLike = { results: ArrayLike<ArrayLike<SpeechRecognitionResultLike>> };
type SpeechRecognitionLike = {
    lang: string;
    continuous: boolean;
    interimResults: boolean;
    start: () => void;
    stop: () => void;
    onresult: ((e: SpeechRecognitionEventLike) => void) | null;
    onerror: ((e: { error: string }) => void) | null;
    onend: (() => void) | null;
};

function getSpeechRecognitionCtor(): (new () => SpeechRecognitionLike) | null {
    const w = window as any;
    return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null;
}

function loadFavorites(): string[] {
    try {
        const raw = localStorage.getItem(FAVORITES_KEY);
        return raw ? JSON.parse(raw) : [];
    } catch {
        return [];
    }
}

function saveFavorites(favs: string[]) {
    try {
        localStorage.setItem(FAVORITES_KEY, JSON.stringify(favs));
    } catch {
        // ignore storage errors (private browsing, quota, etc.)
    }
}

// A single consistent "card" shape (border + background + chevron header) used for every
// collapsible tile in the Ask tab (Insights, Tip, Favorites, Saved Views, Try asking), so they
// all read as the same kind of thing instead of some being bordered boxes and others bare text.
function CollapsibleCard({
    title,
    count,
    headerColor = '#d4d4d4',
    cardStyle,
    defaultOpen = true,
    headerExtra,
    children,
}: {
    title: string;
    count?: number;
    headerColor?: string;
    cardStyle?: React.CSSProperties;
    defaultOpen?: boolean;
    /** Extra control rendered at the right end of the header row, e.g. an action button. */
    headerExtra?: React.ReactNode;
    children: React.ReactNode;
}) {
    const [open, setOpen] = useState(defaultOpen);
    return (
        <div
            style={{
                marginBottom: 8,
                flexShrink: 0,
                border: '1px solid #3c3c3c',
                borderRadius: 4,
                background: '#2a2b2d',
                padding: 8,
                ...cardStyle,
            }}
        >
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 6 }}>
                <button
                    type="button"
                    onClick={() => setOpen(o => !o)}
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 4,
                        flex: 1,
                        minWidth: 0,
                        textAlign: 'left',
                        fontSize: 11,
                        color: headerColor,
                        fontWeight: 600,
                        background: 'none',
                        border: 'none',
                        padding: 0,
                        cursor: 'pointer',
                    }}
                >
                    <span style={{ display: 'inline-block', flexShrink: 0, transform: open ? 'rotate(90deg)' : 'none', transition: 'transform 0.1s' }}>▸</span>
                    <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {title}{count !== undefined ? ` (${count})` : ''}
                    </span>
                </button>
                {headerExtra}
            </div>
            {open && <div style={{ marginTop: 6 }}>{children}</div>}
        </div>
    );
}

type Props = {
    onQuery?: (q: string) => void;
    /** May return a Promise that resolves once the search (and its aggregate breakdown) has fully
     *  loaded — awaited when a chart is chained onto the same AI turn, so the chart reads fresh
     *  data instead of whatever was loaded before this search started. */
    onSearch?: (params: SearchParams) => void | Promise<void>;
    onSendCorrespondence?: (params: SendCorrespondenceParams) => Promise<string>;
    onUpdateCorrespondence?: (params: Partial<SendCorrespondenceParams>) => Promise<string>;
    onExport?: (params: ExportParams) => void;
    onClearFilters?: () => void;
    onUndo?: () => void;
    searchContext?: SearchContext | null;
    /** The real status values present in the data — lets the AI validate/suggest instead of guessing. */
    statusValues?: string[];
    /** The filters currently applied to the grid — lets the AI merge partial corrections instead of resetting. */
    activeParams?: SearchParams | null;
    /** The correspondence draft currently open for review, if any — lets the AI patch it conversationally. */
    activeDraft?: SendCorrespondenceParams | null;
    /** Deterministic, client-computed observations about the loaded dataset (e.g. SLA risk, skewed breakdowns). */
    insights?: string[];
};

const AssistantPanel = forwardRef<AssistantPanelHandle, Props>(function AssistantPanel(
    { onQuery, onSearch, onSendCorrespondence, onUpdateCorrespondence, onExport, onClearFilters, onUndo, searchContext, statusValues, activeParams, activeDraft, insights },
    ref,
) {
    const [text, setText] = useState('');
    const [history, setHistory] = useState<HistoryItem[]>([]);
    const [activeTab, setActiveTab] = useState<'ask' | 'conversation' | 'history'>('ask');
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<any | null>(null);
    const [isRetrying, setIsRetrying] = useState(false);
    const [favorites, setFavorites] = useState<string[]>(() => loadFavorites());
    const [savedViews, setSavedViews] = useState<SavedView[]>(() => loadSavedViews());
    const [showAllTips, setShowAllTips] = useState(false);
    const [tipOfTheMoment] = useState(() => TIPS[Math.floor(Math.random() * TIPS.length)]);
    const [listening, setListening] = useState(false);
    const [voiceSupported] = useState(() => getSpeechRecognitionCtor() != null);
    const [voiceOutputSupported] = useState(() => typeof window !== 'undefined' && 'speechSynthesis' in window);
    const [speakingIndex, setSpeakingIndex] = useState<number | null>(null);
    const [voiceGender, setVoiceGender] = useState<VoiceGender>(() => loadVoicePref());
    const [tokenTotals, setTokenTotals] = useState<TokenTotals>(() => loadTokenTotals());
    const [savedViewUses, setSavedViewUses] = useState<number>(() => loadSavedViewUses());
    const [deleteConfirmId, setDeleteConfirmId] = useState<string | null>(null);
    const [deleteAllConfirm, setDeleteAllConfirm] = useState(false);
    const [deletingAll, setDeletingAll] = useState(false);
    const [selectedConversationIds, setSelectedConversationIds] = useState<Set<string>>(new Set());
    const [deleteSelectedConfirm, setDeleteSelectedConfirm] = useState(false);
    const [deletingSelected, setDeletingSelected] = useState(false);
    // Set whenever persistTurn fails to save a Q&A to chat history — surfaced in the UI instead
    // of only logging to the console, since a silent save failure looks like "History isn't
    // updating" with no indication anything went wrong.
    const [persistFailed, setPersistFailed] = useState(false);
    const [showSaveViewPrompt, setShowSaveViewPrompt] = useState(false);
    const [lastCallUsage, setLastCallUsage] = useState<TokenUsage | null>(null);
    const [showTokenDetail, setShowTokenDetail] = useState(false);
    // Org-wide usage, from AiUsageLog — the server-side counterpart to tokenTotals above, which is
    // only this browser's private estimate. Refetched after every call so it stays current.
    const [orgUsage, setOrgUsage] = useState<AiUsageSummary | null>(null);
    useEffect(() => { getAiUsageSummary().then(setOrgUsage).catch(() => setOrgUsage(null)); }, []);
    // A long-running send() (e.g. awaiting a chained search before building its chart) keeps its
    // own closed-over `searchContext` for its whole lifetime, even after props update — reading
    // through this ref instead picks up the latest value once the awaited search resolves.
    const searchContextRef = useRef(searchContext);
    useEffect(() => { searchContextRef.current = searchContext; }, [searchContext]);
    const recognitionRef = useRef<SpeechRecognitionLike | null>(null);

    // Terminal-style query history navigation (Up/Down arrow keys)
    const queryHistoryRef = useRef<string[]>([]);
    const historyIdxRef = useRef(-1);   // -1 = composing new query
    const draftRef = useRef('');        // saved draft when navigating away from it
    const lastQueryRef = useRef<string>('');  // track last query for retry
    const lastContextRef = useRef<SearchContext | null>(null);  // track last context for retry

    // Persisted chat history (conversations saved server-side)
    const [conversations, setConversations] = useState<ConversationSummary[]>([]);
    const [activeConversationId, setActiveConversationId] = useState<string | null>(null);
    const activeConversationIdRef = useRef<string | null>(null);
    // The stored `title` is truncated (older conversations were cut to 40 chars) — the History
    // list shows the full original first message instead, fetched once per conversation and cached.
    const [conversationPreviews, setConversationPreviews] = useState<Record<string, string>>({});
    const fetchedPreviewIdsRef = useRef<Set<string>>(new Set());

    useEffect(() => { saveFavorites(favorites); }, [favorites]);
    useEffect(() => { saveSavedViews(savedViews); }, [savedViews]);
    useEffect(() => { saveVoicePref(voiceGender); }, [voiceGender]);

    useEffect(() => {
        return () => { recognitionRef.current?.stop(); };
    }, []);

    useEffect(() => {
        return () => { try { window.speechSynthesis?.cancel(); } catch { /* ignore */ } };
    }, []);

    // Voice list often loads asynchronously the first time — warm the cache so the first
    // "read aloud" click already has the full list to pick a natural-sounding voice from.
    useEffect(() => {
        if (!voiceOutputSupported) return;
        const synth = window.speechSynthesis;
        const onVoicesChanged = () => { cachedVoices = synth.getVoices(); };
        onVoicesChanged();
        synth.addEventListener('voiceschanged', onVoicesChanged);
        return () => synth.removeEventListener('voiceschanged', onVoicesChanged);
    }, [voiceOutputSupported]);

    useEffect(() => {
        refreshConversations();
    }, []);

    async function refreshConversations() {
        try {
            const list = await listConversations();
            setConversations(list);

            // Only fetch previews for conversations we haven't already resolved — avoids
            // re-fetching all of them every time a new turn triggers a refresh.
            const toFetch = list.filter(c => !fetchedPreviewIdsRef.current.has(c.conversationId));
            if (toFetch.length === 0) return;
            toFetch.forEach(c => fetchedPreviewIdsRef.current.add(c.conversationId));

            const entries = await Promise.all(toFetch.map(async c => {
                try {
                    const detail = await getConversation(c.conversationId);
                    const firstUser = detail.messages.find(m => m.role === 'user');
                    return [c.conversationId, firstUser?.content ?? c.title ?? ''] as const;
                } catch {
                    fetchedPreviewIdsRef.current.delete(c.conversationId); // allow retry on next refresh
                    return [c.conversationId, c.title ?? ''] as const;
                }
            }));
            setConversationPreviews(prev => ({ ...prev, ...Object.fromEntries(entries) }));
        } catch (err) {
            // Best-effort — history list is a convenience, never blocks the assistant itself.
            console.warn('[AI] Failed to load chat history list:', err);
        }
    }

    async function openConversation(conversationId: string) {
        try {
            const convo = await getConversation(conversationId);
            const items: HistoryItem[] = [];
            for (let i = 0; i < convo.messages.length; i++) {
                const m = convo.messages[i];
                if (m.role !== 'user') continue;
                const reply = convo.messages[i + 1]?.role === 'assistant' ? convo.messages[i + 1].content : '';
                const { text, charts } = decodeChartMarker(reply);
                items.unshift({ q: m.content, a: text, charts });
            }
            setHistory(items);
            setActiveConversationId(conversationId);
            activeConversationIdRef.current = conversationId;
            setActiveTab('conversation');
        } catch (err) {
            console.warn('[AI] Failed to load conversation:', err);
        }
    }

    function newChat() {
        stopSpeaking();
        setHistory([]);
        setActiveConversationId(null);
        activeConversationIdRef.current = null;
        setActiveTab('ask');
    }

    function handleDeleteConversation(conversationId: string, e: React.MouseEvent) {
        e.stopPropagation();
        setDeleteConfirmId(conversationId);
    }

    async function executeDeleteConversation() {
        const conversationId = deleteConfirmId;
        setDeleteConfirmId(null);
        if (!conversationId) return;
        try {
            await deleteConversation(conversationId);
            setConversations(list => list.filter(c => c.conversationId !== conversationId));
            if (activeConversationIdRef.current === conversationId) {
                newChat();
            }
        } catch (err) {
            console.warn('[AI] Failed to delete conversation:', err);
        }
    }

    // Shared by "Delete All" and "Delete Selected" — deletes each id, removes the successful
    // ones from the list, and returns which ids failed so callers can leave those in place.
    async function bulkDeleteConversations(ids: string[]): Promise<Set<string>> {
        const results = await Promise.allSettled(ids.map(id => deleteConversation(id)));
        const failedIds = new Set(ids.filter((_, i) => results[i].status === 'rejected'));
        setConversations(list => list.filter(c => failedIds.has(c.conversationId)));
        if (activeConversationIdRef.current && ids.includes(activeConversationIdRef.current) && !failedIds.has(activeConversationIdRef.current)) {
            newChat();
        }
        if (failedIds.size > 0) {
            console.warn(`[AI] Failed to delete ${failedIds.size} conversation(s)`);
        }
        return failedIds;
    }

    async function executeDeleteAllConversations() {
        setDeleteAllConfirm(false);
        const ids = conversations.map(c => c.conversationId);
        if (ids.length === 0) return;
        setDeletingAll(true);
        try {
            const failedIds = await bulkDeleteConversations(ids);
            setSelectedConversationIds(sel => new Set([...sel].filter(id => failedIds.has(id))));
        } finally {
            setDeletingAll(false);
        }
    }

    function toggleConversationSelected(conversationId: string) {
        setSelectedConversationIds(sel => {
            const next = new Set(sel);
            if (next.has(conversationId)) next.delete(conversationId);
            else next.add(conversationId);
            return next;
        });
    }

    async function executeDeleteSelectedConversations() {
        setDeleteSelectedConfirm(false);
        const ids = [...selectedConversationIds];
        if (ids.length === 0) return;
        setDeletingSelected(true);
        try {
            const failedIds = await bulkDeleteConversations(ids);
            setSelectedConversationIds(failedIds);
        } finally {
            setDeletingSelected(false);
        }
    }

    async function persistTurn(question: string, answer: string, charts?: ChartData[]) {
        try {
            let conversationId = activeConversationIdRef.current;
            if (!conversationId) {
                const title = question.length > 100 ? question.slice(0, 100) + '…' : question;
                const created = await createConversation(title);
                conversationId = created.conversationId;
                activeConversationIdRef.current = conversationId;
                setActiveConversationId(conversationId);
            }
            await appendMessage(conversationId, 'user', question);
            await appendMessage(conversationId, 'assistant', charts && charts.length > 0 ? encodeChartMarker(answer, charts) : answer);
            setPersistFailed(false);
            refreshConversations();
        } catch (err) {
            // Persistence is best-effort — never let a save failure affect the live chat — but
            // flag it so the user can tell "History" is missing this turn instead of it silently
            // vanishing (the previous behavior only logged to the browser console).
            console.warn('[AI] Failed to persist chat turn:', err);
            setPersistFailed(true);
        }
    }

    function togglelistening() {
        if (listening) {
            recognitionRef.current?.stop();
            return;
        }
        const Ctor = getSpeechRecognitionCtor();
        if (!Ctor) return;
        const recognition = new Ctor();
        recognition.lang = 'en-US';
        recognition.continuous = false;
        recognition.interimResults = false;
        recognition.onresult = (e) => {
            const transcript = e.results[e.results.length - 1]?.[0]?.transcript ?? '';
            if (transcript.trim()) setText(prev => (prev ? prev + ' ' : '') + transcript.trim());
        };
        recognition.onerror = () => setListening(false);
        recognition.onend = () => setListening(false);
        recognitionRef.current = recognition;
        setListening(true);
        recognition.start();
    }

    function toggleFavorite(q: string) {
        setFavorites(f => f.includes(q) ? f.filter(x => x !== q) : [...f, q]);
    }

    function saveCurrentView() {
        if (!activeParams || Object.keys(activeParams).length === 0) return;
        setShowSaveViewPrompt(true);
    }

    function confirmSaveCurrentView(name: string) {
        setShowSaveViewPrompt(false);
        if (!activeParams) return;
        setSavedViews(v => [...v.filter(x => x.name !== name), { name, params: activeParams }]);
    }

    function removeSavedView(name: string) {
        setSavedViews(v => v.filter(x => x.name !== name));
    }

    function applySavedView(view: SavedView) {
        onSearch?.(view.params);
        setHistory(h => [{ q: `Applied saved view "${view.name}"`, a: `Showing: ${describeParams(view.params)}`, isSearch: true, params: view.params }, ...h]);
        setActiveTab('conversation');
        // No AI call was made for this — track it so the token-savings comparison stays accurate.
        setSavedViewUses(n => {
            const next = n + 1;
            saveSavedViewUses(next);
            return next;
        });
    }

    // Best-effort reminder — fires only while this browser tab stays open (setTimeout, not a real
    // schedule). A true "email me a daily digest" needs a backend cron + mail sender; this is the
    // honest subset achievable client-side, and is labeled as such in the UI.
    function scheduleReminder(ms: number, label: string) {
        const summary = insights && insights.length > 0
            ? insights[0]
            : hasData ? `${searchContext?.allAggregates?.total ?? searchContext?.total ?? 0} records loaded` : 'no data loaded yet';
        const fire = () => {
            try {
                if ('Notification' in window && Notification.permission === 'granted') {
                    new Notification('ECCO Correspondence Manager', { body: `Reminder: ${summary}` });
                    return;
                }
            } catch {
                // Notification API unavailable/blocked — fall through to the in-panel reminder below.
            }
            setHistory(h => [{ q: '', a: `🔔 Reminder (${label} later): ${summary}` }, ...h]);
        };
        if ('Notification' in window && Notification.permission === 'default') {
            Notification.requestPermission().catch(() => { });
        }
        window.setTimeout(fire, ms);
        setHistory(h => [{ q: '', a: `Okay — I'll remind you in ${label} (while this tab stays open).` }, ...h]);
    }

    function stopSpeaking() {
        try {
            window.speechSynthesis?.cancel();
        } catch {
            // ignore
        }
        setSpeakingIndex(null);
    }

    function speak(idx: number, text: string) {
        try {
            const synth = window.speechSynthesis;
            if (!synth) return;
            synth.cancel();

            // Clicking the speaker again on the item already playing just stops it.
            if (speakingIndex === idx) {
                setSpeakingIndex(null);
                return;
            }

            const utter = new SpeechSynthesisUtterance(toSpeechText(text));
            const voice = pickNaturalVoice(voiceGender);
            if (voice) utter.voice = voice;
            // Slightly slower and warmer than the 1.0/1.0 default — reads like someone
            // explaining something to you rather than a screen reader rushing through text.
            utter.rate = 0.95;
            utter.pitch = 1.03;
            utter.onend = () => setSpeakingIndex(current => (current === idx ? null : current));
            utter.onerror = () => setSpeakingIndex(current => (current === idx ? null : current));
            setSpeakingIndex(idx);
            synth.speak(utter);
        } catch {
            // speech synthesis isn't available in every browser/context — fail silently
            setSpeakingIndex(null);
        }
    }

    // Snapshots the relevant breakdown for a chart request from whatever aggregate data is currently loaded.
    function buildChartData(metric: 'status' | 'customer' | 'delivery' | 'deliveryByStatus', chartType: 'bar' | 'pie' | 'donut' = 'bar'): ChartData {
        // Read via the ref, not the destructured prop — a chart chained after an awaited search
        // (see runAction's 'search' case) needs the value as of NOW, not as of when this whole
        // send() call started, which a closed-over prop would otherwise still be pinned to.
        const ctx = searchContextRef.current;
        const agg = ctx?.allAggregates;
        if (metric === 'deliveryByStatus') {
            // A pie/donut was explicitly requested — honor it as small multiples (one pie/donut
            // per delivery method) instead of silently substituting a stacked bar.
            if (chartType === 'pie' || chartType === 'donut') {
                return {
                    title: 'Delivery Method by Status',
                    crossTab: agg?.deliveryStatusCounts ?? {},
                    chartType: chartType === 'pie' ? 'pie-multi' : 'donut-multi',
                };
            }
            return { title: 'Delivery Method by Status', crossTab: agg?.deliveryStatusCounts ?? {}, chartType: 'stacked' };
        }
        const titles: Record<typeof metric, string> = {
            status: 'Records by Status',
            customer: 'Records by Customer',
            delivery: 'Records by Delivery Method',
        };
        const counts =
            metric === 'status' ? (agg?.statusCounts ?? ctx?.pageStatusCounts ?? {})
                : metric === 'customer' ? (agg?.customerCounts ?? {})
                    : (agg?.deliveryCounts ?? {});
        return { title: titles[metric], counts, chartType };
    }

    function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
        if (e.key === 'Enter' && !e.shiftKey) {
            send();
            e.preventDefault();
            return;
        }

        const hist = queryHistoryRef.current;

        if (e.key === 'ArrowUp' && hist.length > 0) {
            // Only intercept when cursor is still on the first line
            const beforeCursor = text.substring(0, e.currentTarget.selectionStart);
            if (beforeCursor.includes('\n')) return;
            e.preventDefault();
            if (historyIdxRef.current === -1) {
                draftRef.current = text;
                historyIdxRef.current = hist.length - 1;
            } else if (historyIdxRef.current > 0) {
                historyIdxRef.current--;
            }
            setText(hist[historyIdxRef.current]);
        }

        if (e.key === 'ArrowDown' && historyIdxRef.current !== -1) {
            e.preventDefault();
            if (historyIdxRef.current < hist.length - 1) {
                historyIdxRef.current++;
                setText(hist[historyIdxRef.current]);
            } else {
                historyIdxRef.current = -1;
                setText(draftRef.current);
            }
        }
    }

    // Executes one resolved tool call — shared by the single-action path and multi-action chaining.
    async function runAction(action: AiAction): Promise<Partial<HistoryItem>> {
        switch (action.type) {
            case 'search': {
                // Awaited so a chart chained after this in the same multi-action turn (see
                // buildChartData) reads the freshly-loaded breakdown, not stale prior data.
                await onSearch?.(action.params);
                const hint = zeroResultStatusHint(action.params, searchContextRef.current, statusValues);
                return { isSearch: true, params: action.params, zeroResultHint: hint ?? undefined };
            }
            case 'export':
                onExport?.(action.params);
                return { isExport: true };
            case 'clear_filters':
                onClearFilters?.();
                return { isClear: true };
            case 'undo':
                onUndo?.();
                return { isUndo: true };
            case 'chart':
                return { charts: [buildChartData(action.params.metric, action.params.chartType)] };
            case 'send_correspondence':
                try {
                    await onSendCorrespondence?.(action.params);
                    return { isSend: true };
                } catch (err: any) {
                    const msg = err?.response?.data?.Message ?? err?.message ?? 'Request failed';
                    return { isSend: true, sendError: String(msg) };
                }
            case 'update_correspondence':
                try {
                    await onUpdateCorrespondence?.(action.params);
                    return { isUpdate: true };
                } catch (err: any) {
                    const msg = err?.response?.data?.Message ?? err?.message ?? 'Request failed';
                    return { isUpdate: true, sendError: String(msg) };
                }
        }
    }

    async function send(override?: string) {
        const source = override ?? text;
        if (!source.trim() || loading) return;
        const q = source.trim();
        // Push to history (deduplicate — move to end if already present)
        queryHistoryRef.current = [...queryHistoryRef.current.filter(h => h !== q), q];
        historyIdxRef.current = -1;
        draftRef.current = '';
        setText('');
        setLoading(true);
        setError(null);

        // Store for retry
        lastQueryRef.current = q;
        lastContextRef.current = searchContext ?? null;

        // Give the AI short-term memory of the last few exchanges (chronological order) so
        // follow-ups like "narrow that further" or "undo" have something to refer to. Answers are
        // truncated — the model needs enough to know what it said, not a verbatim table replay
        // re-billed as input tokens on every subsequent turn.
        const contextTurns: ChatTurn[] = [];
        for (const h of history.slice(0, CONTEXT_TURNS).slice().reverse()) {
            if (h.q) contextTurns.push({ role: 'user', content: h.q });
            if (h.a) contextTurns.push({ role: 'assistant', content: truncateForContext(h.a) });
        }

        onQuery?.(q);

        try {
            const result = await queryKendoAi(q, searchContext, {
                history: contextTurns,
                activeParams,
                activeDraft,
                statusValues,
                onUsage: usage => {
                    setLastCallUsage(usage);
                    setTokenTotals(prev => {
                        const next = {
                            inputTokens: prev.inputTokens + usage.inputTokens,
                            outputTokens: prev.outputTokens + usage.outputTokens,
                            calls: prev.calls + 1,
                        };
                        saveTokenTotals(next);
                        return next;
                    });
                    getAiUsageSummary().then(setOrgUsage).catch(() => {});
                },
            });
            // Tracked alongside the history entry so persistTurn can save it too — chat history
            // only stores text, so chart snapshots ride along as a marker in the saved answer.
            let turnCharts: ChartData[] | undefined;
            // Overridden below when a search-only turn is silently followed by a second call for
            // the narrative it couldn't write yet — persistTurn uses this instead of result.text.
            let persistedAnswer = result.text;
            if (result.type === 'search') {
                await onSearch?.(result.params);
                const hint = zeroResultStatusHint(result.params, searchContextRef.current, statusValues);
                let answerText = hint ? `${result.text}\n\n${hint}` : result.text;

                // The model deferred the narrative because the records weren't loaded when it
                // decided to search (see kendoAiAdapter.ts rule 12) — now that onSearch has
                // resolved, searchContextRef has them, so ask the exact same question again,
                // silently, as a second real call. No new "Q:" turn — the narrative replaces this
                // turn's placeholder answer in place.
                if (!hint && NARRATIVE_RE.test(q) && (searchContextRef.current?.total ?? 0) > 0) {
                    try {
                        const follow = await queryKendoAi(q, searchContextRef.current, {
                            history: contextTurns,
                            activeParams,
                            activeDraft,
                            statusValues,
                            onUsage: usage => {
                                setLastCallUsage(usage);
                                setTokenTotals(prev => {
                                    const next = {
                                        inputTokens: prev.inputTokens + usage.inputTokens,
                                        outputTokens: prev.outputTokens + usage.outputTokens,
                                        calls: prev.calls + 1,
                                    };
                                    saveTokenTotals(next);
                                    return next;
                                });
                                getAiUsageSummary().then(setOrgUsage).catch(() => {});
                            },
                        });
                        if (follow.type === 'text') answerText = follow.text;
                    } catch (followErr) {
                        // Best-effort — the search itself already succeeded and is shown either
                        // way, so a failed follow-up just leaves the original placeholder text.
                        console.warn('[AI] Narrative follow-up failed:', followErr);
                    }
                }

                persistedAnswer = answerText;
                setHistory(h => [{ q, a: answerText, isSearch: true, params: result.params }, ...h]);
            } else if (result.type === 'export') {
                onExport?.(result.params);
                setHistory(h => [{ q, a: result.text, isExport: true }, ...h]);
            } else if (result.type === 'clear_filters') {
                onClearFilters?.();
                setHistory(h => [{ q, a: result.text, isClear: true }, ...h]);
            } else if (result.type === 'undo') {
                onUndo?.();
                setHistory(h => [{ q, a: result.text, isUndo: true }, ...h]);
            } else if (result.type === 'chart') {
                turnCharts = [buildChartData(result.params.metric, result.params.chartType)];
                setHistory(h => [{ q, a: result.text, charts: turnCharts }, ...h]);
            } else if (result.type === 'multi') {
                const combined: Partial<HistoryItem> = { isMulti: true };
                const charts: ChartData[] = [];
                let multiHint: string | undefined;
                for (const action of result.actions) {
                    const r = await runAction(action);
                    if (r.charts) charts.push(...r.charts);
                    if (r.zeroResultHint) multiHint = r.zeroResultHint;
                    Object.assign(combined, Object.fromEntries(Object.entries(r).filter(([k, v]) => v !== undefined && k !== 'charts' && k !== 'zeroResultHint')));
                }
                if (charts.length > 0) {
                    combined.charts = charts;
                    turnCharts = charts;
                }
                setHistory(h => [{ q, a: multiHint ? `${result.text}\n\n${multiHint}` : result.text, ...combined }, ...h]);
            } else if (result.type === 'send_correspondence') {
                try {
                    const confirmation = await onSendCorrespondence?.(result.params);
                    setHistory(h => [{ q, a: result.text, isSend: true, sendError: undefined }, ...h]);
                    if (confirmation) setHistory(h => [{ q: '', a: confirmation, isSend: true }, ...h.slice(1)]);
                } catch (sendErr: any) {
                    const msg = sendErr?.response?.data?.Message ?? sendErr?.message ?? 'Request failed';
                    setHistory(h => [{ q, a: result.text, isSend: true, sendError: String(msg) }, ...h]);
                }
            } else if (result.type === 'update_correspondence') {
                try {
                    const confirmation = await onUpdateCorrespondence?.(result.params);
                    setHistory(h => [{ q, a: result.text, isUpdate: true, sendError: undefined }, ...h]);
                    if (confirmation) setHistory(h => [{ q: '', a: confirmation, isUpdate: true }, ...h.slice(1)]);
                } catch (updateErr: any) {
                    const msg = updateErr?.response?.data?.Message ?? updateErr?.message ?? 'Request failed';
                    setHistory(h => [{ q, a: result.text, isUpdate: true, sendError: String(msg) }, ...h]);
                }
            } else {
                setHistory(h => [{ q, a: result.text }, ...h]);
            }
            persistTurn(q, persistedAnswer, turnCharts);
        } catch (err: any) {
            console.error('[AI] queryKendoAi threw:', err);
            const classified = classifyError(err);
            setError(classified);
        } finally {
            setLoading(false);
        }
    }

    useImperativeHandle(ref, () => ({
        ask: (askText: string) => { send(askText); },
    }));

    // Retry handler for AI query errors with exponential backoff
    async function handleAiRetry() {
        if (!error || !error.isRetryable) return;

        setIsRetrying(true);
        try {
            // Retry the last query with the saved context
            await send(lastQueryRef.current);
        } finally {
            setIsRetrying(false);
        }
    }

    function runSample(q: string) {
        setText(q);
        send(q);
    }

    const hasData = (searchContext?.items?.length ?? 0) > 0;
    const allLoaded = searchContext?.allDataLoaded && searchContext?.allAggregates != null;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', height: '100%', border: '1px solid #3c3c3c', borderRadius: 6, padding: 12, background: '#252526', color: '#d4d4d4', boxSizing: 'border-box' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8, flexShrink: 0 }}>
                <h6 style={{ color: '#d4d4d4', margin: 0 }}>Ask ECCO, the Dolphin 🐬</h6>
                <div style={{ display: 'flex', gap: 6 }}>
                    <button
                        type="button"
                        onClick={newChat}
                        title="Start a new chat"
                        disabled={loading}
                        style={{ fontSize: 11, background: '#2d2d2d', color: '#d4d4d4', border: '1px solid #3c3c3c', borderRadius: 4, padding: '2px 8px', cursor: loading ? 'default' : 'pointer' }}
                    >
                        + New chat
                    </button>
                </div>
            </div>
            <div style={{ display: 'flex', gap: 4, marginBottom: 8, flexShrink: 0, borderBottom: '1px solid #3c3c3c' }}>
                <button
                    type="button"
                    onClick={() => setActiveTab('ask')}
                    style={{
                        fontSize: 12,
                        background: 'transparent',
                        color: activeTab === 'ask' ? '#fff' : '#858585',
                        border: 'none',
                        borderBottom: activeTab === 'ask' ? '2px solid #0078d4' : '2px solid transparent',
                        padding: '6px 10px',
                        cursor: 'pointer',
                        fontWeight: activeTab === 'ask' ? 600 : 400,
                    }}
                >
                    Ask
                </button>
                <button
                    type="button"
                    onClick={() => setActiveTab('conversation')}
                    style={{
                        fontSize: 12,
                        background: 'transparent',
                        color: activeTab === 'conversation' ? '#fff' : '#858585',
                        border: 'none',
                        borderBottom: activeTab === 'conversation' ? '2px solid #0078d4' : '2px solid transparent',
                        padding: '6px 10px',
                        cursor: 'pointer',
                        fontWeight: activeTab === 'conversation' ? 600 : 400,
                    }}
                >
                    Conversation{history.length > 0 ? ` (${history.length})` : ''}
                </button>
                <button
                    type="button"
                    onClick={() => setActiveTab('history')}
                    style={{
                        fontSize: 12,
                        background: 'transparent',
                        color: activeTab === 'history' ? '#fff' : '#858585',
                        border: 'none',
                        borderBottom: activeTab === 'history' ? '2px solid #0078d4' : '2px solid transparent',
                        padding: '6px 10px',
                        cursor: 'pointer',
                        fontWeight: activeTab === 'history' ? 600 : 400,
                    }}
                >
                    History{conversations.length > 0 ? ` (${conversations.length})` : ''}
                </button>
            </div>
            {activeTab === 'ask' && (
                <div style={{ flex: 1, minHeight: 0, overflowY: 'auto', marginBottom: 4 }}>
                    {!hasData && (
                        <div style={{ fontSize: 12, color: '#858585', marginBottom: 8 }}>
                            Run a search to load data, then ask questions about the results.
                        </div>
                    )}
                    {hasData && allLoaded && (
                        <div style={{ fontSize: 11, color: '#4ec994', marginBottom: 8 }}>
                            All {searchContext!.allAggregates!.total} records loaded for analysis
                        </div>
                    )}
                    {hasData && !allLoaded && (
                        <div style={{ fontSize: 11, color: '#cca700', marginBottom: 8 }}>
                            <Spinner animation="border" size="sm" style={{ width: 10, height: 10, marginRight: 4 }} />
                            Loading full dataset for AI…
                        </div>
                    )}
                    {insights && insights.length > 0 && (
                        <CollapsibleCard
                            title="💡 Insights"
                            headerColor="#e8c15a"
                            cardStyle={{ border: '1px solid #4a3f1f', background: 'rgba(232, 193, 90, 0.08)' }}
                        >
                            {insights.map((line, i) => (
                                <div key={i} style={{ fontSize: 12, color: '#d4d4d4', marginBottom: i < insights.length - 1 ? 4 : 0 }}>
                                    {line}
                                </div>
                            ))}
                        </CollapsibleCard>
                    )}
                    <CollapsibleCard
                        title="✨ Tip"
                        headerColor="#4aa3f0"
                        cardStyle={{ border: '1px solid #2d3f4a', background: 'rgba(74, 163, 240, 0.06)' }}
                    >
                        <div style={{ fontSize: 12, color: '#d4d4d4' }}>{tipOfTheMoment}</div>
                        <button
                            type="button"
                            onClick={() => setShowAllTips(s => !s)}
                            style={{ fontSize: 11, color: '#858585', background: 'none', border: 'none', padding: 0, marginTop: 6, cursor: 'pointer' }}
                        >
                            {showAllTips ? 'Hide all tips' : `See all tips (${TIPS.length})`}
                        </button>
                        {showAllTips && (
                            <div style={{ marginTop: 6, borderTop: '1px solid #2d3f4a', paddingTop: 6 }}>
                                {TIPS.map((tip, i) => (
                                    <div key={i} style={{ fontSize: 12, color: '#d4d4d4', marginBottom: i < TIPS.length - 1 ? 6 : 0 }}>
                                        {tip}
                                    </div>
                                ))}
                            </div>
                        )}
                    </CollapsibleCard>
                    <CollapsibleCard title="⚙️ Settings" defaultOpen={false}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                            <span style={{ fontSize: 11, color: '#858585' }} title="Fires only while this browser tab stays open — not a true scheduled email digest.">
                                🔔 Remind me:
                            </span>
                            {[{ label: '15 min', ms: 15 * 60 * 1000 }, { label: '1 hr', ms: 60 * 60 * 1000 }, { label: '4 hrs', ms: 4 * 60 * 60 * 1000 }].map(opt => (
                                <button
                                    key={opt.label}
                                    type="button"
                                    onClick={() => scheduleReminder(opt.ms, opt.label)}
                                    style={{ fontSize: 11, background: '#2d2d2d', color: '#4aa3f0', border: '1px solid #2d3f4a', borderRadius: 10, padding: '2px 8px', cursor: 'pointer' }}
                                >
                                    {opt.label}
                                </button>
                            ))}
                        </div>
                        {voiceOutputSupported && (
                            <div style={{ marginTop: 10, paddingTop: 8, borderTop: '1px solid #2d3f4a', display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                                <span style={{ fontSize: 11, color: '#858585' }}>🎙️ Read-aloud voice:</span>
                                {(['male', 'female'] as const).map(g => (
                                    <button
                                        key={g}
                                        type="button"
                                        onClick={() => setVoiceGender(g)}
                                        style={{
                                            fontSize: 11,
                                            background: voiceGender === g ? '#0078d4' : '#2d2d2d',
                                            color: voiceGender === g ? '#fff' : '#4aa3f0',
                                            border: '1px solid #2d3f4a',
                                            borderRadius: 10,
                                            padding: '2px 10px',
                                            cursor: 'pointer',
                                            fontWeight: voiceGender === g ? 600 : 400,
                                        }}
                                    >
                                        {g === 'male' ? 'Male' : 'Female'}
                                    </button>
                                ))}
                                <span
                                    style={{ fontSize: 10, color: '#666' }}
                                    title="Browsers don't allow web pages to record and clone a person's own voice for text-to-speech — that needs a paid cloud voice-cloning service (e.g. ElevenLabs, Azure Custom Neural Voice) wired into the backend. Ask if you'd like that scoped as a separate feature."
                                >
                                    (no "my voice" option — browser limitation ⓘ)
                                </span>
                            </div>
                        )}
                    </CollapsibleCard>
                    <CollapsibleCard title="📊 Token Usage" defaultOpen={false}>
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 6, flexWrap: 'wrap' }}>
                            <span style={{ fontSize: 11, color: '#858585' }}>
                                Last call: {lastCallUsage ? `${formatTokenCount(lastCallUsage.inputTokens)} in / ${formatTokenCount(lastCallUsage.outputTokens)} out` : '—'}
                                {' · '}all-time: {formatTokenCount(tokenTotals.inputTokens + tokenTotals.outputTokens)} across {tokenTotals.calls} call{tokenTotals.calls === 1 ? '' : 's'}
                            </span>
                            <div style={{ display: 'flex', gap: 8 }}>
                                <button
                                    type="button"
                                    onClick={() => setShowTokenDetail(s => !s)}
                                    style={{ fontSize: 11, color: '#4aa3f0', background: 'none', border: 'none', padding: 0, cursor: 'pointer' }}
                                >
                                    {showTokenDetail ? 'Hide tips' : 'How to reduce'}
                                </button>
                                <button
                                    type="button"
                                    onClick={() => { setTokenTotals(EMPTY_TOTALS); saveTokenTotals(EMPTY_TOTALS); setLastCallUsage(null); setSavedViewUses(0); saveSavedViewUses(0); }}
                                    title="Reset the all-time counter stored in this browser"
                                    style={{ fontSize: 11, color: '#858585', background: 'none', border: 'none', padding: 0, cursor: 'pointer' }}
                                >
                                    Reset
                                </button>
                            </div>
                        </div>
                        {orgUsage && (orgUsage.claudeCalls > 0 || orgUsage.routerHits > 0) && (
                            <div style={{ marginTop: 6, fontSize: 11, color: '#4ec994' }}>
                                🌐 Org-wide (all users, since tracking began): {formatTokenCount(orgUsage.inputTokens + orgUsage.outputTokens)} tokens
                                across {orgUsage.claudeCalls} AI call{orgUsage.claudeCalls === 1 ? '' : 's'}
                                {orgUsage.routerHits > 0 ? ` + ${orgUsage.routerHits} free router hit${orgUsage.routerHits === 1 ? '' : 's'}` : ''}
                            </div>
                        )}
                        {showTokenDetail && (
                            <ul style={{ fontSize: 11, color: '#d4d4d4', margin: '6px 0 0', paddingLeft: 16 }}>
                                <li>The full system prompt + your loaded data breakdown is resent on <em>every</em> message — the biggest lever is asking fewer, more specific questions rather than iterating broadly.</li>
                                <li>Start "+ New chat" when you're done with a topic — conversation history compounds input tokens on every turn it's kept.</li>
                                <li>Narrow your search (add filters) before asking a question — a smaller loaded dataset means a smaller breakdown sent to the model.</li>
                                <li>Breakdowns are already capped to the top 12 values per field, and old answers are trimmed to ~300 characters once they scroll out of recent context.</li>
                            </ul>
                        )}
                        {(() => {
                            const avgPerCall = tokenTotals.calls > 0
                                ? Math.round((tokenTotals.inputTokens + tokenTotals.outputTokens) / tokenTotals.calls)
                                : ESTIMATED_TOKENS_PER_AI_CALL;
                            const actualTotal = tokenTotals.inputTokens + tokenTotals.outputTokens;
                            const tokensSaved = savedViewUses * avgPerCall;
                            const withoutSavedViews = actualTotal + tokensSaved;
                            const actualPct = withoutSavedViews > 0 ? Math.max(4, (actualTotal / withoutSavedViews) * 100) : 100;

                            if (savedViewUses === 0) {
                                return savedViews.length > 0 ? (
                                    <div style={{ marginTop: 10, paddingTop: 8, borderTop: '1px solid #2d3f4a', fontSize: 11, color: '#858585' }}>
                                        📌 You have Saved Views but haven't reused one yet — each click skips an AI call entirely
                                        (~{formatTokenCount(avgPerCall)} tokens saved per reuse, based on {tokenTotals.calls > 0 ? 'your average call so far' : 'a typical call'}).
                                    </div>
                                ) : null;
                            }

                            return (
                                <div style={{ marginTop: 10, paddingTop: 8, borderTop: '1px solid #2d3f4a' }}>
                                    <div style={{ fontSize: 11, color: '#4ec994', fontWeight: 600, marginBottom: 6 }}>
                                        📌 Saved Views impact
                                    </div>
                                    <div style={{ fontSize: 11, color: '#d4d4d4', marginBottom: 8 }}>
                                        {savedViewUses} reuse{savedViewUses === 1 ? '' : 's'} × ~{formatTokenCount(avgPerCall)} tokens/call skipped ={' '}
                                        <strong style={{ color: '#4ec994' }}>~{formatTokenCount(tokensSaved)} tokens saved</strong>
                                    </div>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
                                        <span style={{ width: 128, flexShrink: 0, fontSize: 10, color: '#858585', textAlign: 'right' }}>Without Saved Views</span>
                                        <div style={{ flex: 1, height: 12, background: '#2d2d2d', borderRadius: 3, overflow: 'hidden' }}>
                                            <div style={{ width: '100%', height: '100%', background: '#cca700' }} />
                                        </div>
                                        <span style={{ width: 46, flexShrink: 0, fontSize: 10, color: '#d4d4d4' }}>{formatTokenCount(withoutSavedViews)}</span>
                                    </div>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                                        <span style={{ width: 128, flexShrink: 0, fontSize: 10, color: '#858585', textAlign: 'right' }}>With Saved Views (actual)</span>
                                        <div style={{ flex: 1, height: 12, background: '#2d2d2d', borderRadius: 3, overflow: 'hidden' }}>
                                            <div style={{ width: `${actualPct}%`, height: '100%', background: '#4ec994' }} />
                                        </div>
                                        <span style={{ width: 46, flexShrink: 0, fontSize: 10, color: '#d4d4d4' }}>{formatTokenCount(actualTotal)}</span>
                                    </div>
                                </div>
                            );
                        })()}
                    </CollapsibleCard>
                    {(savedViews.length > 0 || (activeParams && Object.keys(activeParams).length > 0)) && (
                        <CollapsibleCard
                            title="📌 Saved Views"
                            count={savedViews.length > 0 ? savedViews.length : undefined}
                            defaultOpen={false}
                            headerExtra={activeParams && Object.keys(activeParams).length > 0 && (
                                <button
                                    type="button"
                                    onClick={saveCurrentView}
                                    title="Save the grid's current filters as a named view"
                                    style={{ fontSize: 11, color: '#4aa3f0', background: 'none', border: 'none', padding: 0, cursor: 'pointer', flexShrink: 0 }}
                                >
                                    + Save current filters
                                </button>
                            )}
                        >
                            {savedViews.length > 0 ? (
                                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                                    {savedViews.map(v => (
                                        <button
                                            key={v.name}
                                            type="button"
                                            onClick={() => applySavedView(v)}
                                            disabled={loading}
                                            title={`${describeParams(v.params)} — shift-click to remove`}
                                            onClickCapture={e => { if ((e as React.MouseEvent).shiftKey) { e.preventDefault(); e.stopPropagation(); removeSavedView(v.name); } }}
                                            style={{
                                                fontSize: 11,
                                                background: '#2d2d2d',
                                                color: '#4aa3f0',
                                                border: '1px solid #2d3f4a',
                                                borderRadius: 12,
                                                padding: '3px 10px',
                                                cursor: loading ? 'default' : 'pointer',
                                            }}
                                        >
                                            📌 {v.name}
                                        </button>
                                    ))}
                                </div>
                            ) : (
                                <div style={{ fontSize: 11, color: '#666' }}>No saved views yet — save your current filters above.</div>
                            )}
                        </CollapsibleCard>
                    )}
                    {favorites.length > 0 && (
                        <CollapsibleCard title="⭐ Favorites" count={favorites.length} defaultOpen={false}>
                            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                                {favorites.map(q => (
                                    <span
                                        key={q}
                                        style={{
                                            display: 'inline-flex',
                                            alignItems: 'center',
                                            fontSize: 11,
                                            background: '#2d2d2d',
                                            border: '1px solid #4a3f1f',
                                            borderRadius: 12,
                                            overflow: 'hidden',
                                        }}
                                    >
                                        <button
                                            type="button"
                                            onClick={() => runSample(q)}
                                            disabled={loading}
                                            title="Click to run"
                                            style={{
                                                fontSize: 11,
                                                background: 'none',
                                                color: '#e8c15a',
                                                border: 'none',
                                                padding: '3px 4px 3px 10px',
                                                cursor: loading ? 'default' : 'pointer',
                                            }}
                                        >
                                            {q}
                                        </button>
                                        <button
                                            type="button"
                                            onClick={() => toggleFavorite(q)}
                                            title="Remove from favorites"
                                            aria-label={`Remove "${q}" from favorites`}
                                            style={{
                                                display: 'flex',
                                                alignItems: 'center',
                                                justifyContent: 'center',
                                                background: 'none',
                                                color: '#8a7a4a',
                                                border: 'none',
                                                padding: '3px 8px 3px 4px',
                                                cursor: 'pointer',
                                                fontSize: 13,
                                                lineHeight: 1,
                                            }}
                                        >
                                            ×
                                        </button>
                                    </span>
                                ))}
                            </div>
                        </CollapsibleCard>
                    )}
                    <CollapsibleCard title="Try asking" count={SAMPLE_QUERIES.length} defaultOpen={false}>
                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                            {SAMPLE_QUERIES.map(q => (
                                <button
                                    key={q}
                                    type="button"
                                    onClick={() => runSample(q)}
                                    disabled={loading}
                                    style={{
                                        fontSize: 11,
                                        background: '#2d2d2d',
                                        color: '#9cdcfe',
                                        border: '1px solid #3c3c3c',
                                        borderRadius: 12,
                                        padding: '3px 10px',
                                        cursor: loading ? 'default' : 'pointer',
                                    }}
                                >
                                    {q}
                                </button>
                            ))}
                        </div>
                    </CollapsibleCard>
                </div>
            )}
            {activeTab === 'conversation' && (
                <div style={{ flex: 1, minHeight: 0, overflowY: 'auto', marginBottom: 4 }}>
                    {history.length === 0 && (
                        <div style={{ fontSize: 12, color: '#858585', padding: '8px 0' }}>
                            No questions asked yet — switch to the Ask tab to get started.
                        </div>
                    )}
                    {history.map((h, idx) => (
                        <div key={idx} style={{ marginBottom: 10, borderBottom: '1px solid #3c3c3c', paddingBottom: 8 }}>
                            <div style={{ fontSize: 12, color: '#9cdcfe', marginBottom: 4, display: 'flex', justifyContent: 'space-between', gap: 6 }}>
                                <span><strong>Q:</strong> {h.q}</span>
                                {h.q && (
                                    <button
                                        type="button"
                                        onClick={() => toggleFavorite(h.q)}
                                        title={favorites.includes(h.q) ? 'Remove from favorites' : 'Save to favorites'}
                                        style={{ background: 'none', border: 'none', cursor: 'pointer', color: favorites.includes(h.q) ? '#e8c15a' : '#555', flexShrink: 0 }}
                                    >
                                        ★
                                    </button>
                                )}
                            </div>
                            {h.isSearch && (
                                <div style={{ fontSize: 11, color: '#4ec994', marginBottom: 4 }}>
                                    ↳ Grid search triggered
                                </div>
                            )}
                            {h.isSearch && h.params && (
                                <div style={{ fontSize: 11, color: '#858585', marginBottom: 4 }}>
                                    🔍 Understood as: {describeParams(h.params)}
                                </div>
                            )}
                            {h.isExport && (
                                <div style={{ fontSize: 11, color: '#4ec994', marginBottom: 4 }}>
                                    ↳ Excel export triggered
                                </div>
                            )}
                            {h.isClear && (
                                <div style={{ fontSize: 11, color: '#4ec994', marginBottom: 4 }}>
                                    ↳ Filters cleared
                                </div>
                            )}
                            {h.isUndo && (
                                <div style={{ fontSize: 11, color: '#4ec994', marginBottom: 4 }}>
                                    ↳ Reverted to previous filters
                                </div>
                            )}
                            {h.isMulti && (
                                <div style={{ fontSize: 11, color: '#4ec994', marginBottom: 4 }}>
                                    ↳ Multiple actions performed
                                </div>
                            )}
                            {h.isSend && !h.sendError && (
                                <div style={{ fontSize: 11, color: '#4ec994', marginBottom: 4 }}>
                                    ✓ Correspondence request submitted
                                </div>
                            )}
                            {h.isSend && h.sendError && (
                                <div style={{ fontSize: 11, color: '#f44747', marginBottom: 4 }}>
                                    ✗ Submit failed: {h.sendError}
                                </div>
                            )}
                            {h.isUpdate && !h.sendError && (
                                <div style={{ fontSize: 11, color: '#4ec994', marginBottom: 4 }}>
                                    ✎ Draft updated
                                </div>
                            )}
                            {h.isUpdate && h.sendError && (
                                <div style={{ fontSize: 11, color: '#f44747', marginBottom: 4 }}>
                                    ✗ Update failed: {h.sendError}
                                </div>
                            )}
                            {h.charts?.map((c, i) => (
                                c.chartType === 'stacked'
                                    ? <MiniStackedBarChart key={i} title={c.title} crossTab={c.crossTab} />
                                    : c.chartType === 'pie-multi' || c.chartType === 'donut-multi'
                                        ? <MiniSmallMultiples key={i} title={c.title} crossTab={c.crossTab} variant={c.chartType === 'pie-multi' ? 'pie' : 'donut'} />
                                        : c.chartType === 'bar'
                                            ? <MiniBarChart key={i} title={c.title} counts={c.counts} />
                                            : <MiniPieChart key={i} title={c.title} counts={c.counts} variant={c.chartType} />
                            ))}
                            <div style={{ fontSize: 13, color: '#d4d4d4', display: 'flex', gap: 6, alignItems: 'flex-start' }}>
                                <strong style={{ flexShrink: 0 }}>A:</strong>
                                <div style={{ flex: 1, minWidth: 0 }}>
                                    <MarkdownAnswer text={h.a} />
                                </div>
                                {voiceOutputSupported && (
                                    <button
                                        type="button"
                                        onClick={() => speak(idx, h.a)}
                                        title={speakingIndex === idx ? 'Stop reading' : 'Read this answer aloud'}
                                        style={{
                                            flexShrink: 0,
                                            background: 'none',
                                            border: 'none',
                                            cursor: 'pointer',
                                            color: speakingIndex === idx ? '#f44747' : '#858585',
                                            fontSize: 13,
                                            animation: speakingIndex === idx ? 'pulse 1.2s infinite' : undefined,
                                        }}
                                    >
                                        {speakingIndex === idx ? '⏹' : '🔊'}
                                    </button>
                                )}
                            </div>
                        </div>
                    ))}
                </div>
            )}
            {activeTab === 'history' && (
                <div style={{ flex: 1, minHeight: 0, overflowY: 'auto', marginBottom: 4 }}>
                    {persistFailed && (
                        <div style={{ fontSize: 11, color: '#e8c15a', background: 'rgba(232, 193, 90, 0.1)', border: '1px solid #4a3f1f', borderRadius: 4, padding: 8, marginBottom: 8 }}>
                            ⚠️ Your last question/answer couldn't be saved to History (connection or server issue) — it's still visible in the Conversation tab this session, but won't be listed here after you leave. Later questions will keep trying to save normally.
                        </div>
                    )}
                    {conversations.length === 0 && (
                        <div style={{ fontSize: 12, color: '#858585', padding: '8px 0' }}>
                            No saved conversations yet.
                        </div>
                    )}
                    {conversations.length > 0 && (
                        <div style={{ display: 'flex', gap: 6, marginBottom: 8, flexWrap: 'wrap' }}>
                            <button
                                type="button"
                                onClick={() => setDeleteSelectedConfirm(true)}
                                disabled={deletingSelected || selectedConversationIds.size === 0}
                                className="ai-history-delete-btn"
                                style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: 4,
                                    fontSize: 11,
                                    background: 'none',
                                    border: '1px solid #3c3c3c',
                                    borderRadius: 4,
                                    padding: '4px 8px',
                                    cursor: (deletingSelected || selectedConversationIds.size === 0) ? 'default' : 'pointer',
                                    opacity: (deletingSelected || selectedConversationIds.size === 0) ? 0.5 : 1,
                                }}
                            >
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                    <polyline points="3 6 5 6 21 6" />
                                    <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
                                </svg>
                                {deletingSelected ? 'Deleting…' : `Delete Selected (${selectedConversationIds.size})`}
                            </button>
                            <button
                                type="button"
                                onClick={() => setDeleteAllConfirm(true)}
                                disabled={deletingAll}
                                className="ai-history-delete-btn"
                                style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: 4,
                                    fontSize: 11,
                                    background: 'none',
                                    border: '1px solid #3c3c3c',
                                    borderRadius: 4,
                                    padding: '4px 8px',
                                    cursor: deletingAll ? 'default' : 'pointer',
                                    opacity: deletingAll ? 0.6 : 1,
                                }}
                            >
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                    <polyline points="3 6 5 6 21 6" />
                                    <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
                                </svg>
                                {deletingAll ? 'Deleting…' : `Delete All (${conversations.length})`}
                            </button>
                        </div>
                    )}
                    {conversations.map(c => {
                        const fullText = conversationPreviews[c.conversationId] ?? c.title ?? '(untitled)';
                        return (
                        <div
                            key={c.conversationId}
                            style={{
                                display: 'flex',
                                alignItems: 'flex-start',
                                gap: 6,
                                marginBottom: 4,
                                borderRadius: 4,
                                background: c.conversationId === activeConversationId ? '#2d2d2d' : 'transparent',
                                border: '1px solid #3c3c3c',
                            }}
                        >
                            <input
                                type="checkbox"
                                checked={selectedConversationIds.has(c.conversationId)}
                                onChange={() => toggleConversationSelected(c.conversationId)}
                                onClick={e => e.stopPropagation()}
                                style={{ margin: '10px 0 0 8px', flexShrink: 0, cursor: 'pointer' }}
                            />
                            <button
                                type="button"
                                onClick={() => openConversation(c.conversationId)}
                                style={{
                                    flex: 1,
                                    minWidth: 0,
                                    display: 'block',
                                    textAlign: 'left',
                                    fontSize: 12,
                                    padding: '8px',
                                    background: 'none',
                                    color: '#d4d4d4',
                                    border: 'none',
                                    cursor: 'pointer',
                                    whiteSpace: 'normal',
                                    wordBreak: 'break-word',
                                }}
                            >
                                {fullText}
                            </button>
                            <button
                                type="button"
                                onClick={e => handleDeleteConversation(c.conversationId, e)}
                                title="Delete this conversation"
                                aria-label="Delete conversation"
                                className="ai-history-delete-btn"
                                style={{
                                    flexShrink: 0,
                                    margin: '6px 8px 0 0',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    width: 26,
                                    height: 26,
                                    borderRadius: 4,
                                    background: 'none',
                                    border: 'none',
                                    cursor: 'pointer',
                                }}
                            >
                                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                    <polyline points="3 6 5 6 21 6" />
                                    <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
                                    <line x1="10" y1="11" x2="10" y2="17" />
                                    <line x1="14" y1="11" x2="14" y2="17" />
                                </svg>
                            </button>
                        </div>
                        );
                    })}
                </div>
            )}
            <div style={{ position: 'relative', flexShrink: 0 }}>
                <Form.Control
                    as="textarea"
                    rows={4}
                    value={text}
                    onChange={e => setText(e.target.value)}
                    onKeyDown={handleKeyDown}
                    placeholder={listening ? 'Listening…' : 'Ask about these results… (Enter to send, Shift+Enter for newline)'}
                    disabled={loading}
                    style={{ paddingRight: 34, fontSize: 13 }}
                />
                {voiceSupported && (
                    <button
                        type="button"
                        onClick={togglelistening}
                        disabled={loading}
                        title={listening ? 'Stop listening' : 'Speak your query'}
                        aria-label={listening ? 'Stop voice input' : 'Start voice input'}
                        style={{
                            position: 'absolute',
                            right: 6,
                            top: 6,
                            width: 24,
                            height: 24,
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            border: 'none',
                            borderRadius: '50%',
                            background: listening ? '#c1443c' : '#3c3c3c',
                            cursor: loading ? 'default' : 'pointer',
                            animation: listening ? 'pulse 1.2s infinite' : undefined,
                        }}
                    >
                        {listening ? (
                            <svg width="12" height="12" viewBox="0 0 24 24" fill="#fff">
                                <rect x="5" y="5" width="14" height="14" rx="2" />
                            </svg>
                        ) : (
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                <path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z" fill="#fff" stroke="none" />
                                <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                                <line x1="12" y1="19" x2="12" y2="23" />
                                <line x1="8" y1="23" x2="16" y2="23" />
                            </svg>
                        )}
                    </button>
                )}
            </div>
            <style>{`
                @keyframes pulse { 0%, 100% { box-shadow: 0 0 0 0 rgba(193,68,60,0.6); } 50% { box-shadow: 0 0 0 6px rgba(193,68,60,0); } }
                .ai-history-delete-btn { color: #9a9a9a; transition: color 0.12s ease, background 0.12s ease; }
                .ai-history-delete-btn:hover, .ai-history-delete-btn:focus-visible { color: #f44747; background: rgba(244, 71, 71, 0.12); }
            `}</style>
            <div className="d-flex gap-2 mt-2" style={{ flexShrink: 0 }}>
                <Button size="sm" onClick={() => send()} disabled={loading || !text}>
                    {loading ? <Spinner animation="border" size="sm" /> : 'Send'}
                </Button>
                <Button size="sm" variant="outline-secondary" onClick={() => { setText(''); setError(null); }} disabled={loading}>
                    Clear
                </Button>
                <Button
                    size="sm"
                    variant="outline-warning"
                    onClick={() => text.trim() && toggleFavorite(text.trim())}
                    disabled={loading || !text.trim()}
                    title="Save this query to favorites"
                >
                    ⭐ Save
                </Button>
            </div>
            {error && (
                <SmartErrorAlert
                    error={error}
                    isRetrying={isRetrying}
                    onRetry={error?.isRetryable ? handleAiRetry : undefined}
                    onDismiss={() => setError(null)}
                    variant="danger"
                    className="mt-2 mb-0"
                    style={{ fontSize: 12, flexShrink: 0 }}
                />
            )}
            <ConfirmDialog
                show={!!deleteConfirmId}
                title="Delete Conversation"
                danger
                message="Delete this saved conversation? This cannot be undone."
                confirmLabel="Delete"
                onConfirm={executeDeleteConversation}
                onCancel={() => setDeleteConfirmId(null)}
            />
            <ConfirmDialog
                show={deleteAllConfirm}
                title="Delete All Conversations"
                danger
                message={`Delete all ${conversations.length} saved conversation${conversations.length === 1 ? '' : 's'}? This cannot be undone.`}
                confirmLabel="Delete All"
                onConfirm={executeDeleteAllConversations}
                onCancel={() => setDeleteAllConfirm(false)}
            />
            <ConfirmDialog
                show={deleteSelectedConfirm}
                title="Delete Selected Conversations"
                danger
                message={`Delete ${selectedConversationIds.size} selected conversation${selectedConversationIds.size === 1 ? '' : 's'}? This cannot be undone.`}
                confirmLabel="Delete Selected"
                onConfirm={executeDeleteSelectedConversations}
                onCancel={() => setDeleteSelectedConfirm(false)}
            />
            <PromptDialog
                show={showSaveViewPrompt}
                title="Save View"
                message="Name this view:"
                defaultValue={(() => {
                    const suggested = describeParams(activeParams);
                    return suggested.length > 40 ? suggested.slice(0, 40) + '…' : suggested;
                })()}
                confirmLabel="Save"
                onSubmit={confirmSaveCurrentView}
                onCancel={() => setShowSaveViewPrompt(false)}
            />
        </div>
    );
});

export default AssistantPanel;
